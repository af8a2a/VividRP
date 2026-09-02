using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;

namespace VividRP.Runtime.RenderPass.Core
{
    public class MotionVectorPass : RasterPass
    {
        internal const string CameraMotionVectorsShaderName = "Hidden/VividRP/CameraMotionVectors";
        internal const string ObjectMotionVectorFallbackShaderName = "Hidden/VividRP/ObjectMotionVectorFallback";
        internal const string MotionVectorsShaderTagName = "MotionVectors";
        internal const string ObjectMotionVectorFallbackShaderTagName = "ObjectMotionVectorFallback";
        internal const int ObjectMotionVectorStencilBit = 1 << 5;
        private const int CameraMotionVectorsPassIndex = 0;

        private static readonly string[] s_FallbackShaderTagNames =
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
        private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
        private static readonly int StencilMaskId = Shader.PropertyToID("_StencilMask");

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "FallbackRenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_FallbackRenderList;

        [FormerlySerializedAs("m_MotionVectorDepthTexture")]
        [RenderGraphResource(Name = "CameraDepthStencil", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_CameraDepthStencilTexture;


        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_MotionVectorTexture;

        private Material m_CameraMotionMaterial;
        private Material m_ObjectMotionVectorFallbackMaterial;
        private Camera m_Camera;
        private readonly string[] m_MotionVectorShaderTagNames;
        private readonly string[] m_FallbackShaderTagNames;

        public MotionVectorPass()
        {
            m_MotionVectorShaderTagNames = (string[])s_MotionVectorShaderTagNames.Clone();
            m_FallbackShaderTagNames = (string[])s_FallbackShaderTagNames.Clone();

            m_RenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = m_MotionVectorShaderTagNames,
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_FallbackRenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = m_FallbackShaderTagNames,
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_CameraDepthStencilTexture = RenderGraphTexture.CreateInput("CameraDepthStencil", GraphicsFormat.None, DepthBits.Depth32);
            m_MotionVectorTexture = RenderGraphTexture.CreateColorTarget("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_MotionVectorTexture.desc.ClearBuffer = false;
            m_MotionVectorTexture.desc.ClearColor = Color.clear;
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
            VividCameraData.EnsureCameraDepthTextureMode(m_Camera);

            ConfigureRenderLists();
            ConfigureTargets(cameraData);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Camera == null || m_Camera.cameraType == CameraType.Preview)
                return;

            if (!m_CameraDepthStencilTexture.innerHandle.IsValid()
                || !m_MotionVectorTexture.innerHandle.IsValid())
            {
                return;
            }

            if (m_RenderList != null && m_RenderList.IsValid)
                context.cmd.DrawRendererList(m_RenderList);

            if (m_ObjectMotionVectorFallbackMaterial != null
                && m_FallbackRenderList != null
                && m_FallbackRenderList.IsValid)
            {
                context.cmd.DrawRendererList(m_FallbackRenderList);
            }

            if (m_CameraMotionMaterial != null)
            {
                var cameraDepthTexture = ResolveCameraDepthTextureForSampling();
                if (cameraDepthTexture == null || !cameraDepthTexture.innerHandle.IsValid())
                    return;

                m_CameraMotionMaterial.SetTexture(CameraDepthTextureId, cameraDepthTexture.innerHandle);
                m_CameraMotionMaterial.SetInt(StencilRefId, ObjectMotionVectorStencilBit);
                m_CameraMotionMaterial.SetInt(StencilMaskId, ObjectMotionVectorStencilBit);
                context.cmd.DrawProcedural(
                    Matrix4x4.identity,
                    m_CameraMotionMaterial,
                    CameraMotionVectorsPassIndex,
                    MeshTopology.Triangles,
                    3,
                    1);
            }
        }

        public override void Dispose()
        {
            if (m_CameraMotionMaterial != null)
            {
                CoreUtils.Destroy(m_CameraMotionMaterial);
                m_CameraMotionMaterial = null;
            }

            if (m_ObjectMotionVectorFallbackMaterial != null)
            {
                CoreUtils.Destroy(m_ObjectMotionVectorFallbackMaterial);
                m_ObjectMotionVectorFallbackMaterial = null;
            }

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

            if (m_ObjectMotionVectorFallbackMaterial == null)
            {
                var objectShader = resources?.ObjectMotionVectorFallbackShader;
                objectShader ??= Shader.Find(ObjectMotionVectorFallbackShaderName);
                if (objectShader != null)
                    m_ObjectMotionVectorFallbackMaterial = CoreUtils.CreateEngineMaterial(objectShader);
            }
        }

        private void ConfigureRenderLists()
        {
            m_RenderList ??= new RenderGraphRenderList();
            m_RenderList.desc ??= new RenderGraphRenderListDesc();

            s_MotionVectorShaderTagNames.CopyTo(m_MotionVectorShaderTagNames, 0);
            m_RenderList.desc.ShaderTagNames = m_MotionVectorShaderTagNames;
            m_RenderList.desc.RendererConfiguration |= PerObjectData.MotionVectors;
            m_RenderList.desc.ExcludeObjectMotionVectors = false;

            m_FallbackRenderList ??= new RenderGraphRenderList();
            m_FallbackRenderList.desc ??= new RenderGraphRenderListDesc();

            s_FallbackShaderTagNames.CopyTo(m_FallbackShaderTagNames, 0);
            m_FallbackRenderList.desc.ShaderTagNames = m_FallbackShaderTagNames;
            m_FallbackRenderList.desc.RendererConfiguration |= PerObjectData.MotionVectors;
            m_FallbackRenderList.desc.ExcludeObjectMotionVectors = true;
            m_FallbackRenderList.desc.OverrideMaterial = m_ObjectMotionVectorFallbackMaterial;
            m_FallbackRenderList.desc.OverrideMaterialPassIndex = 0;
            m_FallbackRenderList.desc.OverrideShader = null;
            m_FallbackRenderList.desc.OverrideShaderPassIndex = 0;
        }

        private void ConfigureTargets(VividCameraData cameraData)
        {
            var sourceDescriptor = m_CameraDepthStencilTexture.desc;
            var hasExplicitSourceSize = sourceDescriptor.HasExplicitSize();

            var width = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Width)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            var height = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Height)
                : CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);

            ConfigureMotionVectorTexture(width, height, sourceDescriptor);
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

        private RenderGraphTexture ResolveCameraDepthTextureForSampling()
        {
            return m_CameraDepthStencilTexture;
        }

        private static bool IsDefaultCameraDepthTexture(RenderGraphTexture cameraDepthTexture, RenderGraphTexture depthStencilTexture)
        {
            var cameraDepthDesc = cameraDepthTexture?.desc;
            var depthStencilDesc = depthStencilTexture?.desc;
            if (cameraDepthDesc == null || depthStencilDesc == null)
                return false;

            return cameraDepthDesc.Width <= 1
                   && cameraDepthDesc.Height <= 1
                   && (depthStencilDesc.Width > 1 || depthStencilDesc.Height > 1);
        }

    }
}
