using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly MethodInfo s_AddOptionMethodDefinition = typeof(IOptionDefinitionContext)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(method =>
                method.Name == "AddOption"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string));
        private static readonly MethodInfo s_TryGetEnumParameterValueMethodDefinition = typeof(RenderPassNodeData)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(method =>
                method.Name == nameof(TryGetEnumParameterValue)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2
                && method.GetParameters()[0].ParameterType == typeof(string));
        private static readonly Dictionary<Type, MonoScript> s_passScriptCache = new Dictionary<Type, MonoScript>();

        protected virtual string RegisteredPassTypeName => null;

        internal bool UsesPassScriptSelection => string.IsNullOrEmpty(RegisteredPassTypeName);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            var passType = ResolvePassTypeForOptions();

            if (!UsesPassScriptSelection)
            {
                AddPassOwnedOverrideOptions(context, passType);
                AddFloatParameterOptions(context, passType);
                AddEnumParameterOptions(context, passType);
                if (ShouldDefineAsyncComputeOption(passType))
                    AddAsyncComputeOption(context);
                return;
            }

            context.AddOption<MonoScript>(PassScriptOptionName)
                .WithDisplayName("Pass Script")
                .Delayed();

            AddPassOwnedOverrideOptions(context, passType);
            AddFloatParameterOptions(context, passType);
            AddEnumParameterOptions(context, passType);

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
                var inputPortName = GetInputPortName(field, attr);
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access, attr.BindingMode);

                if (field.FieldType == typeof(RenderGraphTexture))
                {
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphTexture>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphTexture>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphBuffer))
                {
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphBuffer>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphBuffer>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphRenderList))
                {
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphRenderList>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphRenderList>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetOutputPortDisplayAccess(attr.Access)))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphAccelerationStructure))
                {
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphAccelerationStructure>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, RenderPassPortUtility.GetInputPortDisplayAccess(attr.Access)))
                            .Build();
                    }

                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphAccelerationStructure>(outputPortName)
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

        internal void PopulateFloatParameters(RenderGraphPassDefinition passDefinition)
        {
            if (passDefinition == null)
                return;

            var passType = GetPassType();
            if (passType == null)
                return;

            foreach (var field in RenderGraphPassFloatParameterUtility.EnumerateSerializableFloatFields(passType))
            {
                var option = GetNodeOptionByName(RenderGraphPassFloatParameterUtility.GetOptionName(field.Name));
                if (option == null || !option.TryGetValue<float>(out var value))
                    continue;

                passDefinition.FloatParameters.Add(new RenderGraphPassFloatParameter
                {
                    FieldName = field.Name,
                    Value = value,
                });
            }
        }

        internal void PopulateEnumParameters(RenderGraphPassDefinition passDefinition)
        {
            if (passDefinition == null)
                return;

            var passType = GetPassType();
            if (passType == null)
                return;

            foreach (var field in RenderGraphPassEnumParameterUtility.EnumerateSerializableEnumFields(passType))
            {
                var method = s_TryGetEnumParameterValueMethodDefinition.MakeGenericMethod(field.FieldType);
                var args = new[] { (object)field.Name, Activator.CreateInstance(field.FieldType) };
                if (method.Invoke(this, args) is not bool success || !success || args[1] == null)
                    continue;

                passDefinition.EnumParameters.Add(new RenderGraphPassEnumParameter
                {
                    FieldName = field.Name,
                    Value = Convert.ToInt32(args[1]),
                });
            }
        }

        internal bool TryGetFloatParameterValue(string fieldName, out float value)
        {
            value = default;

            var option = GetNodeOptionByName(RenderGraphPassFloatParameterUtility.GetOptionName(fieldName));
            return option != null && option.TryGetValue<float>(out value);
        }

        internal bool TryGetEnumParameterValue<TEnum>(string fieldName, out TEnum value)
            where TEnum : struct, Enum
        {
            value = default;

            var option = GetNodeOptionByName(RenderGraphPassEnumParameterUtility.GetOptionName(fieldName));
            return option != null && option.TryGetValue(out value);
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

        internal string GetInputPortName(FieldInfo field, RenderGraphResource attr)
        {
            if (field == null || attr == null)
                return null;

            return RenderPassPortUtility.GetInputPortName(
                field.Name,
                attr.Access,
                attr.BindingMode,
                GetPassOwnedResourceOverrideEnabled(field, attr));
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

        protected virtual bool GetPassOwnedResourceOverrideEnabled(FieldInfo field, RenderGraphResource attr)
        {
            if (field == null || !RenderPassPortUtility.SupportsExternalOverride(attr))
                return false;

            var option = GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(field.Name));
            return option != null
                   && option.TryGetValue<bool>(out var overrideEnabled)
                   && overrideEnabled;
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

        private void AddPassOwnedOverrideOptions(IOptionDefinitionContext context, Type passType)
        {
            if (context == null || passType == null)
                return;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (!RenderPassPortUtility.SupportsExternalOverride(attr))
                    continue;

                // Write-only resources never have input ports, so override is meaningless.
                if (RenderPassPortUtility.CanWrite(attr.Access) && !RenderPassPortUtility.CanRead(attr.Access))
                    continue;

                context.AddOption<bool>(RenderPassPortUtility.GetOverrideOptionName(field.Name))
                    .WithDisplayName(RenderPassPortUtility.BuildOverrideOptionDisplayName(field, attr))
                    .WithDefaultValue(false);
            }
        }

        private static void AddFloatParameterOptions(IOptionDefinitionContext context, Type passType)
        {
            if (context == null || passType == null)
                return;

            foreach (var field in RenderGraphPassFloatParameterUtility.EnumerateSerializableFloatFields(passType))
            {
                context.AddOption<float>(RenderGraphPassFloatParameterUtility.GetOptionName(field.Name))
                    .WithDisplayName(BuildFloatParameterDisplayName(field))
                    .WithDefaultValue(RenderGraphPassFloatParameterUtility.GetDefaultValue(passType, field));
            }
        }

        private static void AddEnumParameterOptions(IOptionDefinitionContext context, Type passType)
        {
            if (context == null || passType == null)
                return;

            foreach (var field in RenderGraphPassEnumParameterUtility.EnumerateSerializableEnumFields(passType))
            {
                var optionBuilder = AddEnumOption(context, field);
                if (optionBuilder == null)
                    continue;

                InvokeOptionBuilderMethod(optionBuilder, "WithDisplayName", BuildEnumParameterDisplayName(field));

                var defaultValue = RenderGraphPassEnumParameterUtility.GetDefaultValue(passType, field);
                if (defaultValue != null)
                    InvokeOptionBuilderMethod(optionBuilder, "WithDefaultValue", defaultValue);
            }
        }

        private static void AddAsyncComputeOption(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(AsyncComputeOptionName)
                .WithDisplayName("Async Compute")
                .WithDefaultValue(false);
        }

        private static string BuildFloatParameterDisplayName(FieldInfo field)
        {
            var fieldName = field?.Name;
            if (string.IsNullOrEmpty(fieldName))
                return "Float";

            if (fieldName.StartsWith("m_", StringComparison.Ordinal))
                fieldName = fieldName.Substring(2);

            return ObjectNames.NicifyVariableName(fieldName);
        }

        private static string BuildEnumParameterDisplayName(FieldInfo field)
        {
            return BuildFloatParameterDisplayName(field);
        }

        private static object AddEnumOption(IOptionDefinitionContext context, FieldInfo field)
        {
            if (context == null || field == null || !field.FieldType.IsEnum)
                return null;

            var method = s_AddOptionMethodDefinition.MakeGenericMethod(field.FieldType);
            return method.Invoke(context, new object[] { RenderGraphPassEnumParameterUtility.GetOptionName(field.Name) });
        }

        private static void InvokeOptionBuilderMethod(object optionBuilder, string methodName, object value)
        {
            if (optionBuilder == null || string.IsNullOrEmpty(methodName) || value == null)
                return;

            var method = optionBuilder.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                {
                    if (candidate.Name != methodName)
                        return false;

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(value);
                });
            method?.Invoke(optionBuilder, new[] { value });
        }

    }
}
