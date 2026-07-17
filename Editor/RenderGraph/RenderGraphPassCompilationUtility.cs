using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphPassCompilationUtility
    {
        internal static List<RenderGraphPassDefinition> OrderPassDefinitions(IReadOnlyList<RenderGraphPassDefinition> passDefinitions)
        {
            if (passDefinitions == null || passDefinitions.Count <= 1)
                return ClonePassDefinitions(passDefinitions);

            var orderedIndices = GetOrderedPassIndices(passDefinitions);
            return OrderPassDefinitions(passDefinitions, orderedIndices);
        }

        internal static List<int> GetOrderedPassIndices(IReadOnlyList<RenderGraphPassDefinition> passDefinitions)
        {
            if (passDefinitions == null || passDefinitions.Count == 0)
                return new List<int>();

            if (passDefinitions.Count == 1)
                return new List<int> { 0 };

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

            return TopologicalSort(dependencies);
        }

        internal static List<RenderGraphPassDefinition> OrderPassDefinitions(
            IReadOnlyList<RenderGraphPassDefinition> passDefinitions,
            IReadOnlyList<int> orderedIndices)
        {
            if (passDefinitions == null || orderedIndices == null)
                return new List<RenderGraphPassDefinition>();

            var orderedDefinitions = new List<RenderGraphPassDefinition>(orderedIndices.Count);
            var oldToNewIndex = new Dictionary<int, int>(orderedIndices.Count);

            for (var newIndex = 0; newIndex < orderedIndices.Count; newIndex++)
            {
                var oldIndex = orderedIndices[newIndex];
                oldToNewIndex[oldIndex] = newIndex;
                orderedDefinitions.Add(ClonePassDefinition(passDefinitions[oldIndex]));
            }

            for (var i = 0; i < orderedDefinitions.Count; i++)
            {
                var bindings = orderedDefinitions[i].ResourceBindings;
                for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    var binding = bindings[bindingIndex];
                    if (binding == null || binding.SourceKind != RenderGraphPassBindingSourceKind.PassField)
                        continue;

                    if (oldToNewIndex.TryGetValue(binding.SourcePassIndex, out var newSourceIndex))
                        binding.SourcePassIndex = newSourceIndex;
                }
            }

            return orderedDefinitions;
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

                    if (binding.SourcePassIndex >= 0 && binding.SourcePassIndex < passDefinitions.Count && binding.SourcePassIndex != passIndex)
                        result.Add(binding.SourcePassIndex);

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

                        if (RenderPassPortUtility.CanWrite(otherAccess))
                            result.Add(otherPassIndex);
                    }
                }
            }

            return result;
        }

        private static List<int> TopologicalSort(IReadOnlyList<HashSet<int>> dependencies)
        {
            var count = dependencies.Count;
            var indegree = new int[count];
            var dependents = new List<int>[count];
            for (var i = 0; i < count; i++)
            {
                dependents[i] = new List<int>();
                indegree[i] = dependencies[i].Count;
            }

            for (var i = 0; i < count; i++)
            {
                foreach (var dependency in dependencies[i])
                {
                    if (dependency < 0 || dependency >= count)
                        continue;

                    dependents[dependency].Add(i);
                }
            }

            var queue = new List<int>(count);
            for (var i = 0; i < count; i++)
            {
                if (indegree[i] == 0)
                    queue.Add(i);
            }

            var ordered = new List<int>(count);
            var queued = new bool[count];
            for (var i = 0; i < queue.Count; i++)
                queued[queue[i]] = true;

            while (queue.Count > 0)
            {
                var current = queue[0];
                queue.RemoveAt(0);
                ordered.Add(current);

                foreach (var dependent in dependents[current].OrderBy(index => index))
                {
                    indegree[dependent]--;
                    if (indegree[dependent] == 0 && !queued[dependent])
                    {
                        queue.Add(dependent);
                        queued[dependent] = true;
                    }
                }
            }

            if (ordered.Count == count)
                return ordered;

            Debug.LogWarning("[VividRP] Cyclic pass dependencies detected while compiling RenderGraph. Falling back to original order for unresolved passes.");
            for (var i = 0; i < count; i++)
            {
                if (!ordered.Contains(i))
                    ordered.Add(i);
            }

            return ordered;
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

        private static bool TryGetFieldAccess(IReadOnlyDictionary<string, AccessFlags> fieldAccessMap, string fieldName, out AccessFlags access)
        {
            access = default;
            return !string.IsNullOrEmpty(fieldName)
                && fieldAccessMap != null
                && fieldAccessMap.TryGetValue(fieldName, out access);
        }

        private static List<RenderGraphPassDefinition> ClonePassDefinitions(IReadOnlyList<RenderGraphPassDefinition> passDefinitions)
        {
            if (passDefinitions == null)
                return new List<RenderGraphPassDefinition>();

            var result = new List<RenderGraphPassDefinition>(passDefinitions.Count);
            for (var i = 0; i < passDefinitions.Count; i++)
            {
                result.Add(ClonePassDefinition(passDefinitions[i]));
            }

            return result;
        }

        private static RenderGraphPassDefinition ClonePassDefinition(RenderGraphPassDefinition source)
        {
            var clone = new RenderGraphPassDefinition
            {
                PassType = source?.PassType,
                PassName = source?.PassName,
                EnableAsyncCompute = source?.EnableAsyncCompute ?? false,
            };

            if (source?.RenderListDescParameters != null)
            {
                for (var i = 0; i < source.RenderListDescParameters.Count; i++)
                {
                    var parameter = source.RenderListDescParameters[i];
                    if (parameter == null)
                    {
                        clone.RenderListDescParameters.Add(null);
                        continue;
                    }

                    clone.RenderListDescParameters.Add(new RenderGraphPassRenderListDescParameter
                    {
                        FieldName = parameter.FieldName,
                        Value = parameter.Value != null ? parameter.Value.Clone() : null,
                    });
                }
            }

            if (source?.FloatParameters != null)
            {
                for (var i = 0; i < source.FloatParameters.Count; i++)
                {
                    var parameter = source.FloatParameters[i];
                    if (parameter == null)
                    {
                        clone.FloatParameters.Add(null);
                        continue;
                    }

                    clone.FloatParameters.Add(new RenderGraphPassFloatParameter
                    {
                        FieldName = parameter.FieldName,
                        Value = parameter.Value,
                    });
                }
            }

            if (source?.EnumParameters != null)
            {
                for (var i = 0; i < source.EnumParameters.Count; i++)
                {
                    var parameter = source.EnumParameters[i];
                    if (parameter == null)
                    {
                        clone.EnumParameters.Add(null);
                        continue;
                    }

                    clone.EnumParameters.Add(new RenderGraphPassEnumParameter
                    {
                        FieldName = parameter.FieldName,
                        Value = parameter.Value,
                    });
                }
            }

            if (source?.ResourceBindings == null)
                return clone;

            for (var i = 0; i < source.ResourceBindings.Count; i++)
            {
                var binding = source.ResourceBindings[i];
                if (binding == null)
                {
                    clone.ResourceBindings.Add(null);
                    continue;
                }

                clone.ResourceBindings.Add(new RenderGraphPassResourceBinding
                {
                    FieldName = binding.FieldName,
                    ResourceKind = binding.ResourceKind,
                    ResourceIndex = binding.ResourceIndex,
                    ResourceBindingVariant = binding.ResourceBindingVariant,
                    SourceKind = binding.SourceKind,
                    ConnectionKind = binding.ConnectionKind,
                    SourcePassIndex = binding.SourcePassIndex,
                    SourceFieldName = binding.SourceFieldName,
                });
            }

            return clone;
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
