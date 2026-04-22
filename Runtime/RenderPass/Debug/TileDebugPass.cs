using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class TileDebugPass : RasterPass
    {
        internal const string TileDebugShaderName = "Hidden/VividRP/TileDebug";

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int TileIndicesId = Shader.PropertyToID("_TileIndices");
        private static readonly int TileDebugScreenSizeId = Shader.PropertyToID("_TileDebugScreenSize");
        private static readonly int TileDebugColorId = Shader.PropertyToID("_TileDebugColor");
        private static readonly Color TileDebugColor = new(0.12f, 0.82f, 1f, 0.65f);

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "TileIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_TileIndices;

        [RenderGraphResource(Name = "IndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_IndirectArgs;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private Vector4 m_TileDebugScreenSize = new(1f, 1f, 1f, 1f);
        private bool m_ShouldSkipExecution;

        public TileDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(TileDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_TileIndices = CreateStructuredBuffer("TileIndices");
            m_IndirectArgs = CreateIndirectArgsBuffer("IndirectArgs");
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
        }

        public override void Create()
        {
            var shader = Shader.Find(TileDebugShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{TileDebugShaderName}' for {nameof(TileDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
            m_TileDebugScreenSize = new Vector4(
                width,
                height,
                1f / Mathf.Max(1, width),
                1f / Mathf.Max(1, height));
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            if (m_Material == null || !m_OutputTexture.innerHandle.IsValid())
                return;


            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                var sourceTexture = TextureResolveUtility.ResolveTexture(m_SourceTexture.innerHandle);
                if (sourceTexture != null)
                {
                    var copyProperties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                    copyProperties.SetTexture(SourceTextureId, sourceTexture);
                    copyProperties.SetVector(SourceTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_SourceTexture.innerHandle));
                    CoreUtils.DrawFullScreen(context.cmd, m_Material, copyProperties, 0);
                }

                var tileIndicesBuffer = m_TileIndices?.ImportedGraphicsBuffer;
                var indirectArgsBuffer = m_IndirectArgs?.ImportedGraphicsBuffer;
                if (tileIndicesBuffer == null || indirectArgsBuffer == null)
                    return;

                m_Material.SetBuffer(TileIndicesId, tileIndicesBuffer);
                m_Material.SetVector(TileDebugScreenSizeId, m_TileDebugScreenSize);
                m_Material.SetColor(TileDebugColorId, TileDebugColor);
                // Classification now emits dispatch-style args; the overlay shader consumes them
                // through SV_VertexID and compensates for startVertexLocation = 1.
                context.cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    m_Material,
                    1,
                    MeshTopology.Points,
                    indirectArgsBuffer,
                    0);
            }
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_ShouldSkipExecution = false;
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = ResolveOutputFormat(sourceDescriptor);
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if (HasExplicitSize(m_SourceTexture?.desc))
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }

        private static int ResolveOutputDimension(
            System.Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            var resolved = 0;

            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (!HasExplicitSize(descriptor))
                    continue;

                resolved = Mathf.Max(resolved, selector(descriptor));
            }

            if (resolved > 0)
                return resolved;

            return CameraDimensionUtility.ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
        }

        private static GraphicsFormat ResolveOutputFormat(RenderGraphTextureDesc sourceDescriptor)
        {
            if (sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None)
                return sourceDescriptor.ColorFormat;

            return GraphicsFormat.R8G8B8A8_UNorm;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 4,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = name
                }
            };
        }

    }
}
