using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class MotionVectorPass : RasterPass
    {
        internal const string CameraMotionVectorsShaderName = "Hidden/VividRP/CameraMotionVectors";
        internal const string ObjectMotionVectorFallbackShaderName = "Hidden/VividRP/ObjectMotionVectorFallback";
        internal const string MotionVectorsShaderTagName = "MotionVectors";

        private static readonly string[] s_DefaultShaderTagNames =
        {
            "VividGBuffer",
            RenderGraphRenderListDesc.ForwardShaderTagName,
            RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
        };

        private static readonly string[] s_MotionVectorShaderTagNames =
        {
            MotionVectorsShaderTagName,
        };

        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "FallbackRenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_FallbackRenderList;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepthTexture;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_MotionVectorTexture;

        [RenderGraphResource(Name = "MotionVectorDepth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_MotionVectorDepthTexture;

        private Material m_CameraMotionMaterial;
        private Shader m_ObjectMotionVectorFallbackShader;
        private Camera m_Camera;

        public MotionVectorPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = (string[])s_MotionVectorShaderTagNames.Clone(),
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_FallbackRenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = (string[])s_DefaultShaderTagNames.Clone(),
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_CameraDepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_MotionVectorTexture = RenderGraphTexture.CreateColorTarget("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_MotionVectorTexture.desc.ClearBuffer = false;
            m_MotionVectorDepthTexture = RenderGraphTexture.CreateDepthTarget("MotionVectorDepth");
            m_MotionVectorDepthTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            EnsureMaterialsCreated();
        }

        public override void Prepare(ContextContainer frameData)
        {
            EnsureMaterialsCreated();

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Camera = cameraData.camera;

            ConfigureRenderLists();
            ConfigureTargets(cameraData);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Camera == null || m_Camera.cameraType == CameraType.Preview)
                return;

            if (!m_CameraDepthTexture.innerHandle.IsValid()
                || !m_MotionVectorTexture.innerHandle.IsValid()
                || !m_MotionVectorDepthTexture.innerHandle.IsValid())
            {
                return;
            }

            if (m_CameraMotionMaterial != null)
            {
                m_CameraMotionMaterial.SetTexture(CameraDepthTextureId, m_CameraDepthTexture.innerHandle);
                context.cmd.DrawProcedural(Matrix4x4.identity, m_CameraMotionMaterial, 0, MeshTopology.Triangles, 3, 1);
            }

            if (m_RenderList != null && m_RenderList.IsValid)
                context.cmd.DrawRendererList(m_RenderList);

            if (m_ObjectMotionVectorFallbackShader != null
                && m_FallbackRenderList != null
                && m_FallbackRenderList.IsValid)
            {
                context.cmd.DrawRendererList(m_FallbackRenderList);
            }
        }

        public override void Dispose()
        {
            if (m_CameraMotionMaterial != null)
            {
                CoreUtils.Destroy(m_CameraMotionMaterial);
                m_CameraMotionMaterial = null;
            }

            m_ObjectMotionVectorFallbackShader = null;
            m_Camera = null;
        }

        private void EnsureMaterialsCreated()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            if (m_CameraMotionMaterial == null)
            {
                var cameraShader = resources?.CameraMotionVectorsShader;
                cameraShader ??= Shader.Find(CameraMotionVectorsShaderName);
                if (cameraShader != null)
                    m_CameraMotionMaterial = CoreUtils.CreateEngineMaterial(cameraShader);
            }

            if (m_ObjectMotionVectorFallbackShader == null)
            {
                var objectShader = resources?.ObjectMotionVectorFallbackShader;
                objectShader ??= Shader.Find(ObjectMotionVectorFallbackShaderName);
                if (objectShader != null)
                    m_ObjectMotionVectorFallbackShader = objectShader;
            }
        }

        private void ConfigureRenderLists()
        {
            m_RenderList ??= new RenderGraphRenderList();
            m_RenderList.desc ??= new RenderGraphRenderListDesc();

            if (m_RenderList.desc.ShaderTagNames == null || m_RenderList.desc.ShaderTagNames.Length == 0)
                m_RenderList.desc.ShaderTagNames = (string[])s_MotionVectorShaderTagNames.Clone();

            m_RenderList.desc.RendererConfiguration |= PerObjectData.MotionVectors;
            m_RenderList.desc.ExcludeObjectMotionVectors = false;
            m_RenderList.desc.OverrideMaterial = null;
            m_RenderList.desc.OverrideMaterialPassIndex = 0;
            m_RenderList.desc.OverrideShader = null;
            m_RenderList.desc.OverrideShaderPassIndex = 0;

            m_FallbackRenderList ??= new RenderGraphRenderList();
            m_FallbackRenderList.desc ??= new RenderGraphRenderListDesc();

            if (m_FallbackRenderList.desc.ShaderTagNames == null || m_FallbackRenderList.desc.ShaderTagNames.Length == 0)
                m_FallbackRenderList.desc.ShaderTagNames = (string[])s_DefaultShaderTagNames.Clone();

            m_FallbackRenderList.desc.RendererConfiguration |= PerObjectData.MotionVectors;
            m_FallbackRenderList.desc.ExcludeObjectMotionVectors = false;
            m_FallbackRenderList.desc.OverrideMaterial = null;
            m_FallbackRenderList.desc.OverrideMaterialPassIndex = 0;
            m_FallbackRenderList.desc.OverrideShader = m_ObjectMotionVectorFallbackShader;
            m_FallbackRenderList.desc.OverrideShaderPassIndex = 0;
        }

        private void ConfigureTargets(VividCameraData cameraData)
        {
            var sourceDescriptor = m_CameraDepthTexture?.desc;
            var hasExplicitSourceSize = RenderGraphTextureDescUtility.HasExplicitSize(sourceDescriptor);

            var width = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Width)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            var height = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Height)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);

            ConfigureMotionVectorTexture(width, height, sourceDescriptor);
            ConfigureDepthTexture(width, height, sourceDescriptor);
        }

        private void ConfigureMotionVectorTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_MotionVectorTexture?.desc == null)
                return;

            m_MotionVectorTexture.Resize(width, height);
            m_MotionVectorTexture.desc.ColorFormat = GraphicsFormat.R16G16_SFloat;
            m_MotionVectorTexture.desc.DepthBufferBits = DepthBits.None;
            m_MotionVectorTexture.desc.MsaaSamples = MSAASamples.None;
            m_MotionVectorTexture.desc.FilterMode = FilterMode.Point;
            m_MotionVectorTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_MotionVectorTexture.desc.ClearBuffer = false;
            m_MotionVectorTexture.desc.ClearColor = Color.clear;
            m_MotionVectorTexture.desc.UseMipMap = false;
            m_MotionVectorTexture.desc.AutoGenerateMips = false;
            m_MotionVectorTexture.desc.MipCount = 1;
            m_MotionVectorTexture.desc.EnableRandomWrite = false;
            m_MotionVectorTexture.desc.BindTextureMS = false;
            m_MotionVectorTexture.desc.Name = "MotionVectors";

            if (sourceDescriptor == null)
                return;

            m_MotionVectorTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_MotionVectorTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_MotionVectorTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_MotionVectorTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_MotionVectorTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private void ConfigureDepthTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_MotionVectorDepthTexture?.desc == null)
                return;

            m_MotionVectorDepthTexture.Resize(width, height);
            m_MotionVectorDepthTexture.desc.ColorFormat = GraphicsFormat.None;
            m_MotionVectorDepthTexture.desc.DepthBufferBits = sourceDescriptor != null && sourceDescriptor.DepthBufferBits != DepthBits.None
                ? sourceDescriptor.DepthBufferBits
                : DepthBits.Depth32;
            m_MotionVectorDepthTexture.desc.MsaaSamples = sourceDescriptor?.MsaaSamples ?? MSAASamples.None;
            m_MotionVectorDepthTexture.desc.ClearBuffer = false;
            m_MotionVectorDepthTexture.desc.Name = "MotionVectorDepth";

            if (sourceDescriptor == null)
                return;

            m_MotionVectorDepthTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_MotionVectorDepthTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_MotionVectorDepthTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_MotionVectorDepthTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_MotionVectorDepthTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

    }
}
