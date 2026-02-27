using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(RasterPassNodeData))]
    public class RasterPassNodeView : RenderGraphNodeView
    {
        public RasterPassNodeView(RasterPassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new Color(0.2f, 0.4f, 0.8f));

            data.EnsureBakedDescriptor();

            var logicLabel = new Label($"Logic: {GetShortTypeName(data.PassLogicTypeName)}");

            var useCameraSize = new Toggle("Use Camera Size") { value = data.UseCameraResolution };
            var outputSize = new Vector2IntField("Output Size") { value = data.OutputResolution };
            outputSize.SetEnabled(!data.UseCameraResolution);

            useCameraSize.RegisterValueChangedCallback(evt =>
            {
                data.UseCameraResolution = evt.newValue;
                outputSize.SetEnabled(!evt.newValue);
            });

            outputSize.RegisterValueChangedCallback(evt => data.OutputResolution = evt.newValue);

            var colorFormat = new EnumField("Color Format", data.OutputColorFormat);
            colorFormat.RegisterValueChangedCallback(evt =>
                data.OutputColorFormat = (UnityEngine.Experimental.Rendering.GraphicsFormat)evt.newValue);

            var clearColorBuffer = new Toggle("Clear Color") { value = data.ClearColorBuffer };
            var clearColor = new ColorField("Clear Color") { value = data.ClearColor };
            clearColor.SetEnabled(data.ClearColorBuffer);

            clearColorBuffer.RegisterValueChangedCallback(evt =>
            {
                data.ClearColorBuffer = evt.newValue;
                clearColor.SetEnabled(evt.newValue);
            });

            clearColor.RegisterValueChangedCallback(evt => data.ClearColor = evt.newValue);

            var depthBits = new EnumField("Depth Bits", data.OutputDepthBits);
            depthBits.RegisterValueChangedCallback(evt =>
                data.OutputDepthBits = (UnityEngine.Rendering.DepthBits)evt.newValue);

            var clearDepth = new Toggle("Clear Depth") { value = data.ClearDepthBuffer };
            clearDepth.RegisterValueChangedCallback(evt => data.ClearDepthBuffer = evt.newValue);

            var info = new HelpBox(BuildSummary(data), HelpBoxMessageType.Info);

            extensionContainer.Add(logicLabel);
            extensionContainer.Add(useCameraSize);
            extensionContainer.Add(outputSize);
            extensionContainer.Add(colorFormat);
            extensionContainer.Add(clearColorBuffer);
            extensionContainer.Add(clearColor);
            extensionContainer.Add(depthBits);
            extensionContainer.Add(clearDepth);
            extensionContainer.Add(info);
            RefreshExpandedState();
        }

        private static string BuildSummary(RasterPassNodeData data)
        {
            var baked = data.BakedPass;
            int colorCount = baked?.ColorAttachments?.Length ?? 0;
            bool hasDepth = baked != null && baked.DepthAttachment.IsDefined;
            int rendererListCount = baked?.RendererLists?.Length ?? 0;

            return $"MRT: {colorCount}/8  Depth: {(hasDepth ? "Yes" : "No")}  RendererList Inputs: {rendererListCount}";
        }

        private static string GetShortTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return "DefaultRasterPassLogic";

            var type = Type.GetType(assemblyQualifiedName);
            return type?.Name ?? "DefaultRasterPassLogic";
        }
    }
}
