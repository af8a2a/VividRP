using System;
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

        protected virtual string RegisteredPassTypeName => null;

        internal bool UsesPassScriptSelection => string.IsNullOrEmpty(RegisteredPassTypeName);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            if (!UsesPassScriptSelection)
                return;

            context.AddOption<MonoScript>(PassScriptOptionName)
                .WithDisplayName("Pass Script")
                .Delayed();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            var passType = GetPassType();
            if (passType == null)
                return;

            var fields = passType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                if (field.FieldType == typeof(RenderGraphTexture))
                {
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphTexture>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, attr.Access & ~AccessFlags.Write))
                            .Build();
                    }

                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphTexture>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, attr.Access & ~AccessFlags.Read))
                            .Build();
                    }
                }
                else if (field.FieldType == typeof(RenderGraphBuffer))
                {
                    var inputPortName = RenderPassPortUtility.GetInputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(inputPortName))
                    {
                        context.AddInputPort<RenderGraphBuffer>(inputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, attr.Access & ~AccessFlags.Write))
                            .Build();
                    }

                    var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                    if (!string.IsNullOrEmpty(outputPortName))
                    {
                        context.AddOutputPort<RenderGraphBuffer>(outputPortName)
                            .WithDisplayName(RenderPassPortUtility.BuildPortDisplayName(field, attr, attr.Access & ~AccessFlags.Read))
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
            option.TryGetValue<MonoScript>(out var script);
            return script != null ? script.GetClass() : null;
        }

        internal string GetRegisteredPassTypeName()
        {
            return RegisteredPassTypeName;
        }

        internal bool TryGetPassScript(out MonoScript script)
        {
            if (!UsesPassScriptSelection)
            {
                script = null;
                return false;
            }

            var option = GetNodeOptionByName(PassScriptOptionName);
            return option.TryGetValue(out script);
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
    }
}
