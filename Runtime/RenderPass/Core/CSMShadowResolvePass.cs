using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowResolvePass : ComputePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "CSMShadowResolve";

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int CSMShadowAtlasId = Shader.PropertyToID("_CSMShadowAtlas");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int CSMViewProjMatricesId = Shader.PropertyToID("_CSMViewProjMatrices");
        private static readonly int CSMCascadeSpheresId = Shader.PropertyToID("_CSMCascadeSpheres");
        private static readonly int CSMAtlasScaleOffsetsId = Shader.PropertyToID("_CSMAtlasScaleOffsets");
        private static readonly int CSMCascadeCountId = Shader.PropertyToID("_CSMCascadeCount");
        private static readonly int CSMMaxShadowDistanceId = Shader.PropertyToID("_CSMMaxShadowDistance");
        private static readonly int CSMDepthBiasId = Shader.PropertyToID("_CSMDepthBias");
        private static readonly int CSMNormalBiasId = Shader.PropertyToID("_CSMNormalBias");
        private static readonly int CSMInvViewProjMatrixId = Shader.PropertyToID("_CSMInvViewProjMatrix");
        private static readonly int CSMOutputWidthId = Shader.PropertyToID("_CSMOutputWidth");
        private static readonly int CSMOutputHeightId = Shader.PropertyToID("_CSMOutputHeight");
        private static readonly int CSMLightDirectionWSId = Shader.PropertyToID("_CSMLightDirectionWS");
        private static readonly int CSMAtlasResolutionId = Shader.PropertyToID("_CSMAtlasResolution");
        private static readonly int CSMCascadeResolutionId = Shader.PropertyToID("_CSMCascadeResolution");
        private static readonly int CSMShadowQualityId = Shader.PropertyToID("_CSMShadowQuality");
        private static readonly int CSMLightAngularDiameterId = Shader.PropertyToID("_CSMLightAngularDiameter");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CSMShadowAtlas;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        private ComputeShader m_ResolveCompute;
        private int m_Kernel = -1;
        private bool m_IsActive;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Matrix4x4 m_InvViewProjMatrix = Matrix4x4.identity;
        private Vector4 m_LightDirectionWS;

        // Cached shadow data for shader upload
        private readonly Matrix4x4[] m_ViewProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_AtlasScaleOffsets = new Vector4[VividShadowData.MaxCascadeCount];
        private int m_CascadeCount;
        private float m_MaxShadowDistance;
        private float m_DepthBias;
        private float m_NormalBias;
        private int m_AtlasResolution;
        private int m_CascadeResolution;
        private int m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;
        private float m_LightAngularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;

        public CSMShadowResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(CSMShadowResolvePass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_CSMShadowAtlas = RenderGraphTexture.CreateInput("CSMShadowAtlas", GraphicsFormat.None, DepthBits.Depth16);
            m_CSMShadowAtlas.desc.IsShadowMap = true;
            m_DirectionalShadowTexture = RenderGraphTexture.CreateOutput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_DirectionalShadowTexture.desc.ClearBuffer = true;
            m_DirectionalShadowTexture.desc.ClearColor = Color.white;
            m_DirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DirectionalShadowTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            m_ResolveCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.CSMShadowResolveCompute;

            if (m_ResolveCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource for {nameof(CSMShadowResolvePass)}.");
                return;
            }

            m_Kernel = m_ResolveCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_LightDirectionWS = Vector4.zero;
            m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;
            m_LightAngularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = cameraData.actualWidth;
            var height = cameraData.actualHeight;

            m_DirectionalShadowTexture.Resize(width, height);
            m_DispatchGroupCountX = CoreUtils.DivRoundUp(width, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(height, ThreadGroupSizeY);

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive)
                return;

            m_IsActive = true;
            m_InvViewProjMatrix = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true).inverse;

            m_CascadeCount = shadowData.cascadeCount;
            m_MaxShadowDistance = shadowData.maxShadowDistance;
            m_DepthBias = shadowData.depthBias;
            m_NormalBias = shadowData.normalBias;
            m_AtlasResolution = shadowData.atlasResolution;
            m_CascadeResolution = shadowData.cascadeResolution;

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                m_ViewProjMatrices[i] = shadowData.viewProjMatrices[i];
                m_CascadeSpheres[i] = shadowData.cascadeSpheres[i];
                m_AtlasScaleOffsets[i] = shadowData.cascadeAtlasScaleOffsets[i];
            }

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (lightData.hasMainDirectionalLight)
            {
                var dir = lightData.mainDirectionalLight.directionWS;
                m_LightDirectionWS = new Vector4(dir.x, dir.y, dir.z, 0f);

                if (DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)
                    && additionalLightData != null)
                {
                    m_ShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;
                    m_LightAngularDiameter = Mathf.Max(additionalLightData.resolvedAngularDiameter, 0.0f);
                }
            }
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_IsActive || m_ResolveCompute == null || m_Kernel < 0)
                return;

            if (!m_DepthTexture.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_CSMShadowAtlas.innerHandle.IsValid()
                || !m_DirectionalShadowTexture.innerHandle.IsValid())
                return;

            var cmd = context.cmd;

            cmd.SetComputeTextureParam(m_ResolveCompute, m_Kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_Kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_Kernel, CSMShadowAtlasId, m_CSMShadowAtlas.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_Kernel, DirectionalShadowTextureId, m_DirectionalShadowTexture.innerHandle);

            cmd.SetComputeMatrixArrayParam(m_ResolveCompute, CSMViewProjMatricesId, m_ViewProjMatrices);
            cmd.SetComputeVectorArrayParam(m_ResolveCompute, CSMCascadeSpheresId, m_CascadeSpheres);
            cmd.SetComputeVectorArrayParam(m_ResolveCompute, CSMAtlasScaleOffsetsId, m_AtlasScaleOffsets);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMCascadeCountId, m_CascadeCount);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMMaxShadowDistanceId, m_MaxShadowDistance);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMDepthBiasId, m_DepthBias);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMNormalBiasId, m_NormalBias);
            cmd.SetComputeMatrixParam(m_ResolveCompute, CSMInvViewProjMatrixId, m_InvViewProjMatrix);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputWidthId, m_DirectionalShadowTexture.desc.Width);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputHeightId, m_DirectionalShadowTexture.desc.Height);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMLightDirectionWSId, m_LightDirectionWS);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMAtlasResolutionId, m_AtlasResolution);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMCascadeResolutionId, m_CascadeResolution);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMShadowQualityId, m_ShadowQuality);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMLightAngularDiameterId, m_LightAngularDiameter);

            cmd.DispatchCompute(m_ResolveCompute, m_Kernel,
                m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
        }

        public override void Dispose()
        {
            m_ResolveCompute = null;
            m_Kernel = -1;
            m_IsActive = false;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
        }
    }
}
