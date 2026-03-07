using System;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class PreviewNodeData : RenderGraphNodeData
    {
        internal const string TextureInputPortName = "Texture";

        private const string PreviewOptionName = "Preview";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<TexturePreviewValue>(PreviewOptionName)
                .WithDisplayName("Texture Preview")
                .WithDefaultValue(new TexturePreviewValue());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<RenderGraphTexture>(TextureInputPortName)
                .WithDisplayName("Texture")
                .Build();
        }

        internal TexturePreviewValue GetPreviewValue()
        {
            var option = GetNodeOptionByName(PreviewOptionName);
            option.TryGetValue<TexturePreviewValue>(out var previewValue);
            return previewValue ?? new TexturePreviewValue();
        }

        internal Texture GetPreviewTexture()
        {
            return GetPreviewValue().Texture;
        }

        internal void RefreshPreviewConnectionMetadata()
        {
            var previewValue = GetPreviewValue();
            if (previewValue == null)
                return;

            if (TryGetConnectedPassOutput(out var passType, out var fieldName))
            {
                previewValue.SetConnectedPassOutput(passType, fieldName);
            }
            else if (HasConnectedTextureInput())
            {
                previewValue.SetConnectedTextureInput();
            }
            else
            {
                previewValue.ClearConnectionMetadata();
            }
        }

        internal bool HasConnectedTextureInput()
        {
            return GetInputPortByName(TextureInputPortName)?.IsConnected == true;
        }

        internal bool TryGetConnectedPassOutput(out Type passType, out string fieldName)
        {
            passType = null;
            fieldName = null;

            var inputPort = GetInputPortByName(TextureInputPortName);
            var connectedPort = inputPort?.FirstConnectedPort;
            if (connectedPort?.GetNode() is not RenderPassNodeData sourcePassNode)
                return false;

            passType = sourcePassNode.GetPassType();
            if (passType == null)
                return false;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(passType))
            {
                if (field.FieldType != typeof(RenderGraphTexture))
                    continue;

                var attr = field.GetCustomAttribute<RenderGraphResource>();
                var outputPortName = RenderPassPortUtility.GetOutputPortName(field.Name, attr.Access);
                if (string.IsNullOrEmpty(outputPortName))
                    continue;

                if (ReferenceEquals(sourcePassNode.GetOutputPortByName(outputPortName), connectedPort))
                {
                    fieldName = field.Name;
                    return true;
                }
            }

            passType = null;
            return false;
        }
    }
}
