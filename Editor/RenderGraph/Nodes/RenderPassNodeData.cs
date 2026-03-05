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
                    context.AddInputPort<RenderGraphTexture>(field.Name)
                        .WithDisplayName(BuildResourcePortDisplayName(field, attr))
                        .Build();
                }
                else if (field.FieldType == typeof(RenderGraphBuffer))
                {
                    context.AddInputPort<RenderGraphBuffer>(field.Name)
                        .WithDisplayName(BuildResourcePortDisplayName(field, attr))
                        .Build();
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

        private static string BuildResourcePortDisplayName(FieldInfo field, RenderGraphResource attr)
        {
            var displayName = string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name;

            var accessLabel = AccessFlagsToShortName(attr.Access);
            var attachmentLabel = attr.IsDepthAttachment
                ? "Depth"
                : attr.AttachmentIndex >= 0
                    ? $"A{attr.AttachmentIndex}"
                    : null;

            if (!string.IsNullOrEmpty(attachmentLabel))
                return $"{displayName} ({accessLabel}, {attachmentLabel})";

            return $"{displayName} ({accessLabel})";
        }

        private static string AccessFlagsToShortName(AccessFlags access)
        {
            var canRead = (access & AccessFlags.Read) != 0;
            var canWrite = (access & AccessFlags.Write) != 0;

            if (canRead && canWrite) return "RW";
            if (canRead) return "R";
            if (canWrite) return "W";

            return access.ToString();
        }
    }
}
