using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal class RenderPassNodeData : RenderGraphNodeData
    {
        private const string PassScriptOptionName = "PassScript";
        private const string AsyncComputeOptionName = "AsyncCompute";
        private static readonly Dictionary<Type, MonoScript> s_passScriptCache = new Dictionary<Type, MonoScript>();

        protected virtual string RegisteredPassTypeName => null;

        internal bool UsesPassScriptSelection => string.IsNullOrEmpty(RegisteredPassTypeName);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            var passType = ResolvePassTypeForOptions();

            if (!UsesPassScriptSelection)
            {
                if (ShouldDefineAsyncComputeOption(passType))
                    AddAsyncComputeOption(context);
                return;
            }

            context.AddOption<MonoScript>(PassScriptOptionName)
                .WithDisplayName("Pass Script")
                .Delayed();

            if (ShouldDefineAsyncComputeOption(passType))
                AddAsyncComputeOption(context);
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            var passType = GetPassType();
            if (passType == null)
                return;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();

                if (field.FieldType == typeof(RenderGraphTexture))
                {
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphTexture>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphTexture>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphBuffer))
                {
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphBuffer>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphBuffer>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphRenderList))
                {
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphRenderList>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphRenderList>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
            }
        }

        internal Type GetPassType()
        {
            if (!UsesPassScriptSelection)
                return ResolveType(RegisteredPassTypeName);

            var option = GetNodeOptionByName(PassScriptOptionName);
            if (option == null)
                return null;

            option.TryGetValue<MonoScript>(out var script);
            return script != null ? script.GetClass() : null;
        }

        internal bool HasAsyncComputeOption()
        {
            return GetNodeOptionByName(AsyncComputeOptionName) != null;
        }

        internal bool GetEnableAsyncCompute()
        {
            var option = GetNodeOptionByName(AsyncComputeOptionName);
            if (option == null || !option.TryGetValue<bool>(out var enableAsyncCompute))
                return false;

            return enableAsyncCompute;
        }

        internal string GetRegisteredPassTypeName()
        {
            return RegisteredPassTypeName;
        }

        internal bool TryGetPassScript(out MonoScript script)
        {
            if (UsesPassScriptSelection)
            {
                var option = GetNodeOptionByName(PassScriptOptionName);
                if (option != null && option.TryGetValue(out script) && script != null)
                    return true;
            }

            return TryResolvePassScript(GetPassType(), out script);
        }

        private static Type ResolveType(string assemblyQualifiedOrFullName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedOrFullName))
                return null;

            var type = Type.GetType(assemblyQualifiedOrFullName, throwOnError: false);
            if (type != null)
                return type;

            var fullName = assemblyQualifiedOrFullName;
            var separatorIndex = assemblyQualifiedOrFullName.IndexOf(',');
            if (separatorIndex >= 0)
                fullName = assemblyQualifiedOrFullName.Substring(0, separatorIndex);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool TryResolvePassScript(Type passType, out MonoScript script)
        {
            script = null;
            if (passType == null)
                return false;

            if (s_passScriptCache.TryGetValue(passType, out script))
                return script != null;

            var guids = AssetDatabase.FindAssets($"{passType.Name} t:MonoScript");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (candidate != null && candidate.GetClass() == passType)
                {
                    script = candidate;
                    break;
                }
            }

            s_passScriptCache[passType] = script;
            return script != null;
        }

        private Type ResolvePassTypeForOptions()
        {
            return UsesPassScriptSelection
                ? ResolvePassTypeFromOption(PassScriptOptionName)
                : ResolveType(RegisteredPassTypeName);
        }

        private Type ResolvePassTypeFromOption(string optionName)
        {
            var option = GetNodeOptionByName(optionName);
            if (option == null || !option.TryGetValue<MonoScript>(out var script) || script == null)
                return null;

            return script.GetClass();
        }

        private bool ShouldDefineAsyncComputeOption(Type passType)
        {
            return RenderGraphPassExecutionUtility.SupportsAsyncCompute(passType)
                || HasAsyncComputeOption();
        }

        private static void AddAsyncComputeOption(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(AsyncComputeOptionName)
                .WithDisplayName("Async Compute")
                .WithDefaultValue(false);
        }
    }
}
