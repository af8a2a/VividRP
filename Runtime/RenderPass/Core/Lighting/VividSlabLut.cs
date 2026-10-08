using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    // Pipeline-owned, built once per shader/device-resource lifetime. Never a material asset.
    internal sealed class VividSlabLut
    {
        internal const uint Version = 1u;
        internal const int Resolution = 64;
        internal const int SampleCount = 4096;
        internal const GraphicsFormat Format = GraphicsFormat.R16G16B16A16_SFloat;
        internal static readonly int TextureId = Shader.PropertyToID("_VividSlabLut");
        internal static readonly int ReadyId = Shader.PropertyToID("_VividSlabLutReady");
        private static readonly int ScratchId = Shader.PropertyToID("_VividSlabLutScratch");
        private static readonly int OutputId = Shader.PropertyToID("_SlabLutOutput");
        private static readonly int DirectionalId = Shader.PropertyToID("_SlabDirectionalAlbedo");
        private ComputeShader m_Shader;
        private RTHandle m_Texture;
        private static uint s_SourceRevision;
        private uint m_SourceRevision;

        internal RTHandle Texture => m_Texture;

        internal static void InvalidateSource() => ++s_SourceRevision;

        internal static RenderGraphTexture CreateGraphTexture()
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = Resolution, Height = Resolution,
                    Dimension = TextureDimension.Tex2D, ColorFormat = Format,
                    DepthBufferBits = DepthBits.None, FilterMode = FilterMode.Bilinear,
                    WrapMode = TextureWrapMode.Clamp, MipCount = 1,
                    UseMipMap = false, AutoGenerateMips = false, ClearBuffer = false,
                    EnableRandomWrite = true, Name = "VividSlabLut"
                }
            };
        }

        internal bool Create(ComputeShader shader, CommandBuffer cmd = null)
        {
            if (shader == null)
            {
                Dispose();
                return false;
            }
            if (m_Shader == shader && m_SourceRevision == s_SourceRevision
                && m_Texture != null && m_Texture.rt.IsCreated())
                return true;

            Dispose();
            int integrate = shader.FindKernel("IntegrateDirectionalAlbedo");
            int pack = shader.FindKernel("PackSlabLut");
            m_Texture = RTHandles.Alloc(Resolution, Resolution,
                colorFormat: Format, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true,
                useMipMap: false, name: "VividSlabLut");
            var descriptor = new RenderTextureDescriptor(Resolution, Resolution)
            {
                graphicsFormat = Format, depthBufferBits = 0, msaaSamples = 1,
                dimension = TextureDimension.Tex2D, volumeDepth = 1,
                enableRandomWrite = true, useMipMap = false, autoGenerateMips = false
            };
            CommandBuffer pooled = cmd == null ? CommandBufferPool.Get(nameof(VividSlabLut)) : null;
            CommandBuffer build = cmd ?? pooled;
            build.GetTemporaryRT(ScratchId, descriptor, FilterMode.Bilinear);
            build.SetComputeTextureParam(shader, integrate, OutputId, ScratchId);
            build.DispatchCompute(shader, integrate, Resolution / 8, Resolution / 8, 1);
            build.SetComputeTextureParam(shader, pack, DirectionalId, ScratchId);
            build.SetComputeTextureParam(shader, pack, OutputId, m_Texture);
            build.DispatchCompute(shader, pack, Resolution / 8, Resolution / 8, 1);
            build.ReleaseTemporaryRT(ScratchId);
            if (pooled != null)
            {
                Graphics.ExecuteCommandBuffer(pooled);
                CommandBufferPool.Release(pooled);
            }
            m_Shader = shader;
            m_SourceRevision = s_SourceRevision;
            return true;
        }

        internal void Dispose()
        {
            m_Texture?.Release();
            m_Texture = null;
            m_Shader = null;
        }
    }
}
