using System;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class RenderPassNodeData : RenderGraphNodeData
    {
        private const string PassScriptOptionName = "PassScript";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
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
            var option = GetNodeOptionByName(PassScriptOptionName);
            option.TryGetValue<MonoScript>(out var script);
            return script != null ? script.GetClass() : null;
        }

        internal bool TryGetPassScript(out MonoScript script)
        {
            var option = GetNodeOptionByName(PassScriptOptionName);
            return option.TryGetValue(out script);
        }
    }
}
