using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [ResourceNode("Renderer Filter")]
    public class RendererFilterNodeData : ResourceNodeData
    {
        public RendererFilterSettings Settings = RendererFilterSettings.CreateDefault();

        public RendererFilterNodeData()
        {
            NodeName = "Renderer Filter";
            AddPort("Renderer List Out", PortType.RendererList, false, AccessFlags.Read, ResourceIntent.Read);
        }

        public override ResourceSlot CreateResource(ResourceCreationContext context)
        {
            Settings.EnsureDefaults();

            var sortingSettings = new SortingSettings(context.Camera)
            {
                criteria = Settings.SortingCriteria
            };

            var shaderPasses = Settings.ShaderPassNames;
            var firstTag = new ShaderTagId(shaderPasses[0]);
            var drawingSettings = new DrawingSettings(firstTag, sortingSettings);
            for (int i = 1; i < shaderPasses.Length; i++)
                drawingSettings.SetShaderPassName(i, new ShaderTagId(shaderPasses[i]));

            var filteringSettings = new FilteringSettings(
                Settings.ToRenderQueueRange(),
                Settings.LayerMask.value);

            var rendererListParams = new RendererListParams(
                context.CullingResults,
                drawingSettings,
                filteringSettings);

            var rendererList = context.RenderGraph.CreateRendererList(rendererListParams);
            return ResourceSlot.FromRendererList(rendererList);
        }
    }
}
