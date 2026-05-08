using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class RenderGraphPassCullingUtility
    {
        internal static List<int> GetLivePassIndices(IReadOnlyList<RenderGraphPassDefinition> passDefinitions)
        {
            if (passDefinitions == null || passDefinitions.Count == 0)
                return new List<int>();

            var passTypes = new Type[passDefinitions.Count];
            var fieldAccessMaps = new Dictionary<string, AccessFlags>[passDefinitions.Count];
            for (var i = 0; i < passDefinitions.Count; i++)
            {
                passTypes[i] = ResolveType(passDefinitions[i]?.PassType);
                fieldAccessMaps[i] = BuildFieldAccessMap(passTypes[i]);
            }

            var dependencies = new List<HashSet<int>>(passDefinitions.Count);
            for (var i = 0; i < passDefinitions.Count; i++)
            {
                dependencies.Add(CollectDependencies(i, passDefinitions, passTypes, fieldAccessMaps));
            }

            var live = new bool[passDefinitions.Count];
            var stack = new Stack<int>();
            for (var i = 0; i < passDefinitions.Count; i++)
            {
                if (!IsLiveRoot(passDefinitions[i], passTypes[i]))
                    continue;

                live[i] = true;
                stack.Push(i);
            }

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var dependency in dependencies[current])
                {
                    if (dependency < 0 || dependency >= live.Length || live[dependency])
                        continue;

                    live[dependency] = true;
                    stack.Push(dependency);
                }
            }

            var liveIndices = new List<int>(passDefinitions.Count);
            for (var i = 0; i < live.Length; i++)
            {
                if (live[i])
                    liveIndices.Add(i);
            }

            return liveIndices;
        }

        private static bool IsLiveRoot(
            RenderGraphPassDefinition passDefinition,
            Type passType)
        {
            if (passType == null)
                return true;

            if (typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(passType))
                return true;

            if (typeof(IRenderGraphSideEffectPass).IsAssignableFrom(passType))
                return true;

            var hasWritableResource = false;
            var hasTransientWritableResource = false;
            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null || !CanWrite(attr.Access))
                    continue;

                if (RenderGraphPassReflectionUtility.IsDeclaredTransientResourceField(field))
                {
                    hasTransientWritableResource = true;
                    continue;
                }

                hasWritableResource = true;
            }

            if (!hasWritableResource)
                return !hasTransientWritableResource;

            return HasHistoryCurrentWrite(passDefinition, passType);
        }

        private static bool HasHistoryCurrentWrite(RenderGraphPassDefinition passDefinition, Type passType)
        {
            if (passDefinition?.ResourceBindings == null || passType == null)
                return false;

            foreach (var binding in passDefinition.ResourceBindings)
            {
                if (binding == null
                    || binding.ResourceBindingVariant != RenderGraphResourceBindingVariant.HistoryCurrent
                    || string.IsNullOrEmpty(binding.FieldName))
                {
                    continue;
                }

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, binding.FieldName);
                var attr = field?.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                if (RenderGraphPassReflectionUtility.IsDeclaredTransientResourceField(field))
                    continue;

                var effectiveAccess = RenderGraphPassBindingUtility.ResolveEffectiveAccess(binding, attr.Access);
                if (CanWrite(effectiveAccess))
                    return true;
            }

            return false;
        }

        private static HashSet<int> CollectDependencies(
            int passIndex,
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            IReadOnlyList<Type> passTypes,
            IReadOnlyList<Dictionary<string, AccessFlags>> fieldAccessMaps)
        {
            var result = new HashSet<int>();
            var passDefinition = passDefinitions[passIndex];
            if (passDefinition?.ResourceBindings == null)
                return result;

            foreach (var binding in passDefinition.ResourceBindings)
            {
                if (binding == null)
                    continue;

                if (binding.SourceKind == RenderGraphPassBindingSourceKind.PassField)
                {
                    var sourcePassType = binding.SourcePassIndex >= 0 && binding.SourcePassIndex < passTypes.Count
                        ? passTypes[binding.SourcePassIndex]
                        : null;
                    var sourceField = RenderGraphPassReflectionUtility.GetInstanceField(sourcePassType, binding.SourceFieldName);
                    if (RenderGraphPassReflectionUtility.IsDeclaredTransientResourceField(sourceField))
                        continue;

                    if (binding.SourcePassIndex >= 0
                        && binding.SourcePassIndex < passDefinitions.Count
                        && binding.SourcePassIndex != passIndex)
                    {
                        result.Add(binding.SourcePassIndex);
                    }

                    continue;
                }

                if (!TryGetFieldAccess(fieldAccessMaps[passIndex], binding.FieldName, out var access))
                    continue;

                if (!RenderGraphPassBindingUtility.ConsumesExistingState(binding, access))
                    continue;

                if (binding.ResourceBindingVariant == RenderGraphResourceBindingVariant.HistoryPrevious)
                    continue;

                for (var otherPassIndex = 0; otherPassIndex < passDefinitions.Count; otherPassIndex++)
                {
                    if (otherPassIndex == passIndex)
                        continue;

                    var otherPassDefinition = passDefinitions[otherPassIndex];
                    if (otherPassDefinition?.ResourceBindings == null)
                        continue;

                    foreach (var otherBinding in otherPassDefinition.ResourceBindings)
                    {
                        if (otherBinding == null || otherBinding.SourceKind != RenderGraphPassBindingSourceKind.Resource)
                            continue;

                        if (otherBinding.ResourceKind != binding.ResourceKind
                            || otherBinding.ResourceIndex != binding.ResourceIndex
                            || otherBinding.ResourceBindingVariant != binding.ResourceBindingVariant)
                        {
                            continue;
                        }

                        if (!TryGetFieldAccess(fieldAccessMaps[otherPassIndex], otherBinding.FieldName, out var otherAccess))
                            continue;

                        if (CanWrite(otherAccess))
                            result.Add(otherPassIndex);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, AccessFlags> BuildFieldAccessMap(Type passType)
        {
            var result = new Dictionary<string, AccessFlags>(StringComparer.Ordinal);
            if (passType == null)
                return result;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                if (RenderGraphPassReflectionUtility.IsDeclaredTransientResourceField(field))
                    continue;

                var attr = field.GetCustomAttribute<RenderGraphResource>();
                result[field.Name] = attr.Access;
            }

            return result;
        }

        private static bool TryGetFieldAccess(
            IReadOnlyDictionary<string, AccessFlags> fieldAccessMap,
            string fieldName,
            out AccessFlags access)
        {
            access = default;
            return !string.IsNullOrEmpty(fieldName)
                && fieldAccessMap != null
                && fieldAccessMap.TryGetValue(fieldName, out access);
        }

        private static bool CanWrite(AccessFlags access)
        {
            return (access & AccessFlags.Write) != 0;
        }

        private static Type ResolveType(string assemblyQualifiedOrFullName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedOrFullName))
                return null;

            var type = Type.GetType(assemblyQualifiedOrFullName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(assemblyQualifiedOrFullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
