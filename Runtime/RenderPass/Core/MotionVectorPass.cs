using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Serialization;

namespace VividRP.Runtime.RenderPass.Core
{
    public class MotionVectorPass : RasterPass, IRenderGraphPreparePass
    {
        internal const GraphicsFormat MotionVectorFormat = GraphicsFormat.R16G16B16A16_UNorm;
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

        private static readonly int MotionVisibilityId = Shader.PropertyToID("_MotionVisibility");
        private Material m_CameraMotionMaterial;
        private Material m_MeshletMotionMaterial;
        private readonly MaterialPropertyBlock m_CameraInputs = new();
        private TextureHandle m_Visibility;
        private Material m_ObjectMotionVectorFallbackMaterial;
        private Camera m_Camera;
        private readonly string[] m_MotionVectorShaderTagNames;
        private readonly string[] m_FallbackShaderTagNames;

        internal static Material CreateMotionDecodeMaterial()
        {
            var shader = PipelineResourceManager.Get<VividRPCoreResources>()?.CameraMotionVectorsShader;
            return CoreUtils.CreateEngineMaterial(shader != null ? shader : Shader.Find(CameraMotionVectorsShaderName));
        }

        internal static TextureHandle DecodeMotionUV(RenderGraph graph, TextureHandle source, Material material)
        {
            var descriptor = graph.GetTextureDesc(source);
            if (descriptor.format != MotionVectorFormat)
                return source;
            descriptor.format = GraphicsFormat.R16G16_SFloat;
            descriptor.name = "Decoded Motion UV";
            var output = graph.CreateTexture(descriptor);
            graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, output, material, 1), descriptor.name);
            return output;
        }

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
                    ExcludeObjectMotionVectors = true,
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_CameraDepthStencilTexture = RenderGraphTexture.CreateInput("CameraDepthStencil", GraphicsFormat.None, DepthBits.Depth32);
            m_MotionVectorTexture = RenderGraphTexture.CreateColorTarget("MotionVectors", MotionVectorPass.MotionVectorFormat);
            m_MotionVectorTexture.desc.ClearBuffer = true;
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

        public void PrepareRenderGraph(ContextContainer frameData)
        {
            m_Visibility = default;
            if (!frameData.Contains<VividSdfReceiverFrameData>())
                return;
            var receiver = frameData.Get<VividSdfReceiverFrameData>();
            if (!ReferenceEquals(receiver.Depth, m_CameraDepthStencilTexture) || receiver.Visibility == null)
                return;
            m_Visibility = receiver.Visibility.innerHandle;
            if (m_Visibility.IsValid())
                PassRecorder.RegisterImportedTextureForPass(this, m_Visibility, AccessFlags.Read);
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

                m_CameraInputs.Clear();
                m_CameraInputs.SetTexture(CameraDepthTextureId, cameraDepthTexture.innerHandle);
                bool hasVisibility = m_Visibility.IsValid();
                if (hasVisibility)
                    m_CameraInputs.SetTexture(MotionVisibilityId, m_Visibility);
                var material = hasVisibility ? m_MeshletMotionMaterial : m_CameraMotionMaterial;
                context.cmd.DrawProcedural(
                    Matrix4x4.identity,
                    material,
                    CameraMotionVectorsPassIndex,
                    MeshTopology.Triangles,
                    3,
                    1,
                    m_CameraInputs);
            }
        }

        public override void Dispose()
        {
            if (m_CameraMotionMaterial != null)
            {
                CoreUtils.Destroy(m_MeshletMotionMaterial);
                m_MeshletMotionMaterial = null;
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
                {
                    m_CameraMotionMaterial = CoreUtils.CreateEngineMaterial(cameraShader);
                    m_CameraMotionMaterial.SetInt(StencilRefId, ObjectMotionVectorStencilBit);
                    m_CameraMotionMaterial.SetInt(StencilMaskId, ObjectMotionVectorStencilBit);
                    m_MeshletMotionMaterial = CoreUtils.CreateEngineMaterial(cameraShader);
                    m_MeshletMotionMaterial.SetInt(StencilRefId, ObjectMotionVectorStencilBit);
                    m_MeshletMotionMaterial.SetInt(StencilMaskId, ObjectMotionVectorStencilBit);
                    m_MeshletMotionMaterial.EnableKeyword("VIVID_MESHLET_MOTION");
                }
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
            m_MotionVectorTexture.desc.ColorFormat = MotionVectorPass.MotionVectorFormat;
            m_MotionVectorTexture.desc.DepthBufferBits = DepthBits.None;
            m_MotionVectorTexture.desc.MsaaSamples = MSAASamples.None;
            m_MotionVectorTexture.desc.FilterMode = FilterMode.Point;
            m_MotionVectorTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_MotionVectorTexture.desc.ClearBuffer = true;
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
