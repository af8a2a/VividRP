using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(RendererFilterNodeData))]
    public class RendererFilterNodeView : RenderGraphNodeView
    {
        public RendererFilterNodeView(RendererFilterNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new Color(0.55f, 0.3f, 0.7f));

            var layerMask = new LayerMaskField("Layer Mask") { value = data.Settings.LayerMask };
            layerMask.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.LayerMask = evt.newValue;
                data.Settings = settings;
            });

            var queueMin = new IntegerField("Queue Min") { value = data.Settings.RenderQueueMin };
            queueMin.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.RenderQueueMin = Mathf.Clamp(evt.newValue, 0, 5000);
                settings.RenderQueueMax = Mathf.Max(settings.RenderQueueMin, settings.RenderQueueMax);
                data.Settings = settings;
            });

            var queueMax = new IntegerField("Queue Max") { value = data.Settings.RenderQueueMax };
            queueMax.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.RenderQueueMax = Mathf.Clamp(evt.newValue, settings.RenderQueueMin, 5000);
                data.Settings = settings;
            });

            var sorting = new EnumField("Sorting", data.Settings.SortingCriteria);
            sorting.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.SortingCriteria = (UnityEngine.Rendering.SortingCriteria)evt.newValue;
                data.Settings = settings;
            });

            var requireDepth = new Toggle("Require Depth") { value = data.Settings.RequireDepthBuffer };
            requireDepth.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.RequireDepthBuffer = evt.newValue;
                data.Settings = settings;
            });

            var shaderPasses = new TextField("Shader Passes")
            {
                value = string.Join(", ", data.Settings.ShaderPassNames ?? Array.Empty<string>())
            };

            shaderPasses.RegisterValueChangedCallback(evt =>
            {
                var settings = data.Settings;
                settings.ShaderPassNames = evt.newValue
                    .Split(',')
                    .Select(pass => pass.Trim())
                    .Where(pass => !string.IsNullOrEmpty(pass))
                    .ToArray();
                data.Settings = settings;
            });

            extensionContainer.Add(layerMask);
            extensionContainer.Add(queueMin);
            extensionContainer.Add(queueMax);
            extensionContainer.Add(sorting);
            extensionContainer.Add(requireDepth);
            extensionContainer.Add(shaderPasses);
            RefreshExpandedState();
        }
    }
}
