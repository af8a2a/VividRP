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
            DefaultUnlitShaderTagName,
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

        private string[] m_CachedShaderTagNames;
        private ShaderTagId[] m_CachedShaderTagIds;
        private int m_CachedShaderTagNamesHash;

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
            var desc = TryGetSingleShaderTagId(out var singleShaderTag)
                ? new RendererListDesc(singleShaderTag, cullingResults, camera)
                : new RendererListDesc(GetShaderTagIds(), cullingResults, camera);

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
                ExcludeObjectMotionVectors = true
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

        private bool TryGetSingleShaderTagId(out ShaderTagId shaderTag)
        {
            if (ShaderTagNames == null || ShaderTagNames.Length == 0)
            {
                shaderTag = default;
                return false;
            }

            var validCount = 0;
            var validShaderTagName = default(string);
            for (var i = 0; i < ShaderTagNames.Length; i++)
            {
                var shaderTagName = ShaderTagNames[i];
                if (!IsValidShaderTagName(shaderTagName))
                    continue;

                validCount++;
                validShaderTagName ??= shaderTagName;
                if (validCount > 1)
                    break;
            }

            if (validCount == 1)
            {
                shaderTag = new ShaderTagId(validShaderTagName);
                return true;
            }

            shaderTag = default;
            return false;
        }

        private ShaderTagId[] GetShaderTagIds()
        {
            var shaderTagNames = ShaderTagNames;
            if (shaderTagNames == null || shaderTagNames.Length == 0)
                shaderTagNames = s_DefaultShaderTagNames;

            var validCount = 0;
            var hash = 17;
            for (var i = 0; i < shaderTagNames.Length; i++)
            {
                var shaderTagName = shaderTagNames[i];
                if (!IsValidShaderTagName(shaderTagName))
                    continue;

                validCount++;
                unchecked
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(shaderTagName);
                }
            }

            if (validCount == 0)
            {
                shaderTagNames = s_DefaultShaderTagNames;
                validCount = s_DefaultShaderTagNames.Length;
                hash = GetShaderTagNamesHash(s_DefaultShaderTagNames);
            }

            if (IsShaderTagCacheCurrent(shaderTagNames, validCount, hash))
                return m_CachedShaderTagIds;

            m_CachedShaderTagNames = new string[validCount];
            m_CachedShaderTagIds = new ShaderTagId[validCount];
            m_CachedShaderTagNamesHash = hash;

            var outputIndex = 0;
            for (var i = 0; i < shaderTagNames.Length; i++)
            {
                var shaderTagName = shaderTagNames[i];
                if (!IsValidShaderTagName(shaderTagName))
                    continue;

                m_CachedShaderTagNames[outputIndex] = shaderTagName;
                m_CachedShaderTagIds[outputIndex] = new ShaderTagId(shaderTagName);
                outputIndex++;
            }

            return m_CachedShaderTagIds;
        }

        private bool IsShaderTagCacheCurrent(string[] shaderTagNames, int validCount, int hash)
        {
            if (m_CachedShaderTagIds == null
                || m_CachedShaderTagNames == null
                || m_CachedShaderTagIds.Length != validCount
                || m_CachedShaderTagNames.Length != validCount
                || m_CachedShaderTagNamesHash != hash)
            {
                return false;
            }

            var outputIndex = 0;
            for (var i = 0; i < shaderTagNames.Length; i++)
            {
                var shaderTagName = shaderTagNames[i];
                if (!IsValidShaderTagName(shaderTagName))
                    continue;

                if (!string.Equals(m_CachedShaderTagNames[outputIndex], shaderTagName, StringComparison.Ordinal))
                    return false;

                outputIndex++;
            }

            return outputIndex == validCount;
        }

        private static int GetShaderTagNamesHash(string[] shaderTagNames)
        {
            var hash = 17;
            for (var i = 0; i < shaderTagNames.Length; i++)
            {
                unchecked
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(shaderTagNames[i]);
                }
            }

            return hash;
        }

        private static bool IsValidShaderTagName(string shaderTagName)
        {
            return !string.IsNullOrWhiteSpace(shaderTagName);
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
