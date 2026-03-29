using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;

namespace VividRP.Runtime
{
    public enum RenderGraphRenderQueueRange
    {
        All,
        Opaque,
        Transparent
    }

    [Serializable]
    public class RenderGraphRenderListDesc
    {
        internal const string ForwardShaderTagName = "VividForward";
        internal const string PreDepthShaderTagName = "VividPreDepth";
        internal const string DefaultUnlitShaderTagName = "SRPDefaultUnlit";

        private static readonly string[] s_DefaultShaderTagNames =
        {
            ForwardShaderTagName,
        };

        public string[] ShaderTagNames = (string[])s_DefaultShaderTagNames.Clone();
        public RenderGraphRenderQueueRange RenderQueueRange = RenderGraphRenderQueueRange.Opaque;
        public SortingCriteria SortingCriteria = SortingCriteria.CommonOpaque;
        public LayerMask LayerMask = ~0;
        public uint RenderingLayerMask = uint.MaxValue;
        public PerObjectData RendererConfiguration = PerObjectData.None;
        public bool ExcludeObjectMotionVectors;
        public Material OverrideMaterial;
        public int OverrideMaterialPassIndex;
        public Shader OverrideShader;
        public int OverrideShaderPassIndex;

        public RenderGraphRenderListDesc Clone()
        {
            return new RenderGraphRenderListDesc
            {
                ShaderTagNames = ShaderTagNames != null ? (string[])ShaderTagNames.Clone() : null,
                RenderQueueRange = RenderQueueRange,
                SortingCriteria = SortingCriteria,
                LayerMask = LayerMask,
                RenderingLayerMask = RenderingLayerMask,
                RendererConfiguration = RendererConfiguration,
                ExcludeObjectMotionVectors = ExcludeObjectMotionVectors,
                OverrideMaterial = OverrideMaterial,
                OverrideMaterialPassIndex = OverrideMaterialPassIndex,
                OverrideShader = OverrideShader,
                OverrideShaderPassIndex = OverrideShaderPassIndex,
            };
        }

        internal RendererListDesc CreateRendererListDesc(CullingResults cullingResults, Camera camera)
        {
            var shaderTags = BuildShaderTagIds();

            var desc = shaderTags.Length == 1
                ? new RendererListDesc(shaderTags[0], cullingResults, camera)
                : new RendererListDesc(shaderTags, cullingResults, camera);

            desc.renderQueueRange = ResolveRenderQueueRange(RenderQueueRange);
            desc.sortingCriteria = SortingCriteria;
            desc.layerMask = LayerMask;
            desc.renderingLayerMask = RenderingLayerMask;
            desc.rendererConfiguration = RendererConfiguration;
            desc.excludeObjectMotionVectors = ExcludeObjectMotionVectors;
            desc.overrideMaterial = OverrideMaterial;
            desc.overrideMaterialPassIndex = OverrideMaterialPassIndex;
            desc.overrideShader = OverrideShader;
            desc.overrideShaderPassIndex = OverrideShaderPassIndex;
            return desc;
        }

        public static RenderGraphRenderListDesc CreateOpaque(params string[] shaderTagNames)
        {
            return new RenderGraphRenderListDesc
            {
                ShaderTagNames = shaderTagNames != null && shaderTagNames.Length > 0
                    ? (string[])shaderTagNames.Clone()
                    : (string[])s_DefaultShaderTagNames.Clone(),
                RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                SortingCriteria = SortingCriteria.CommonOpaque,
            };
        }

        public static RenderGraphRenderListDesc CreateTransparent(params string[] shaderTagNames)
        {
            return new RenderGraphRenderListDesc
            {
                ShaderTagNames = shaderTagNames != null && shaderTagNames.Length > 0
                    ? (string[])shaderTagNames.Clone()
                    : (string[])s_DefaultShaderTagNames.Clone(),
                RenderQueueRange = RenderGraphRenderQueueRange.Transparent,
                SortingCriteria = SortingCriteria.CommonTransparent,
            };
        }

        private ShaderTagId[] BuildShaderTagIds()
        {
            if (ShaderTagNames == null || ShaderTagNames.Length == 0)
                return BuildDefaultShaderTagIds();

            var validCount = 0;
            for (var i = 0; i < ShaderTagNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(ShaderTagNames[i]))
                    validCount++;
            }

            if (validCount == 0)
                return BuildDefaultShaderTagIds();

            var shaderTags = new ShaderTagId[validCount];
            var outputIndex = 0;
            for (var i = 0; i < ShaderTagNames.Length; i++)
            {
                var shaderTagName = ShaderTagNames[i];
                if (string.IsNullOrWhiteSpace(shaderTagName))
                    continue;

                shaderTags[outputIndex++] = new ShaderTagId(shaderTagName);
            }

            return shaderTags;
        }

        private static ShaderTagId[] BuildDefaultShaderTagIds()
        {
            return new[]
            {
                new ShaderTagId(ForwardShaderTagName),
                new ShaderTagId(DefaultUnlitShaderTagName),
            };
        }

        private static RenderQueueRange ResolveRenderQueueRange(RenderGraphRenderQueueRange range)
        {
            return range switch
            {
                RenderGraphRenderQueueRange.Opaque => UnityEngine.Rendering.RenderQueueRange.opaque,
                RenderGraphRenderQueueRange.Transparent => UnityEngine.Rendering.RenderQueueRange.transparent,
                _ => UnityEngine.Rendering.RenderQueueRange.all,
            };
        }
    }

    [Serializable]
    public class RenderGraphRenderList
    {
        public RenderGraphRenderListDesc desc;
        internal RendererListHandle innerHandle;

        public RenderGraphRenderList()
        {
            desc = new RenderGraphRenderListDesc();
        }

        internal bool IsValid => innerHandle.IsValid();

        public static implicit operator RendererListHandle(RenderGraphRenderList renderList)
        {
            return renderList != null ? renderList.innerHandle : default;
        }

        public static implicit operator RendererList(RenderGraphRenderList renderList)
        {
            return renderList != null ? renderList.innerHandle : default;
        }
    }
}
