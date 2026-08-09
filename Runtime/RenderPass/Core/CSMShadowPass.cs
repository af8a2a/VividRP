using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowPass : UnsafePass
    {
        private const int AtlasGridSize = 2; // 2x2 grid for up to 4 cascades
        private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private bool m_IsActive;
        private int m_CascadeCount;
        private int m_AtlasResolution;
        private int m_CascadeResolution;
        private int m_MainLightVisibleIndex = -1;
        private float m_SlopeScaleDepthBias;
        private ShaderVariablesGlobal m_CameraShaderGlobals;

        private readonly ShadowDrawingSettings[] m_ShadowDrawSettings = new ShadowDrawingSettings[VividShadowData.MaxCascadeCount];

        private CullingResults m_CullingResults;
        private ScriptableRenderContext m_RenderContext;
        private VividShadowData m_ShadowData;

        public CSMShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(CSMShadowPass));
            m_ShadowAtlas = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth16)
            };
            m_ShadowAtlas.desc.Name = "CSMShadowAtlas";
            m_ShadowAtlas.desc.ClearBuffer = true;
            m_ShadowAtlas.desc.ClearColor = Color.black;
            m_ShadowAtlas.desc.IsShadowMap = true;
            m_ShadowAtlas.desc.FilterMode = FilterMode.Bilinear;
            m_ShadowAtlas.desc.WrapMode = TextureWrapMode.Clamp;
        }

        public override void Create()
        {
            PassRecorder.RegisterCascadedShadowCasterPass();
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_CascadeCount = 0;
            m_MainLightVisibleIndex = -1;
            m_SlopeScaleDepthBias = 0.0f;
            m_CameraShaderGlobals = default;
            m_ShadowData = null;

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive
                || shadowData.cascadeCount <= 0
                || shadowData.atlasResolution <= 0
                || shadowData.cascadeResolution <= 0)
            {
                return;
            }

            var renderingData = frameData.GetOrCreate<VividRenderingData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_CullingResults = renderingData.cullingResults;
            m_RenderContext = renderingData.context;
            m_CameraShaderGlobals = ResolveCameraShaderGlobals(frameData, cameraData);
            m_MainLightVisibleIndex = shadowData.mainLightVisibleIndex;
            m_CascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
            m_AtlasResolution = shadowData.atlasResolution;
            m_CascadeResolution = shadowData.cascadeResolution;
            m_SlopeScaleDepthBias = shadowData.slopeScaleDepthBias;
            m_ShadowData = shadowData;

            m_IsActive = true;

            // Configure atlas texture size
            m_ShadowAtlas.desc.Width = m_AtlasResolution;
            m_ShadowAtlas.desc.Height = m_AtlasResolution;

            // Configure ShadowDrawingSettings per cascade
            for (int i = 0; i < m_CascadeCount; i++)
            {
                var settings = new ShadowDrawingSettings(
                    m_CullingResults,
                    m_MainLightVisibleIndex);
                settings.splitIndex = i;
                settings.useRenderingLayerMaskTest = false;
                settings.objectsFilter = ShadowObjectsFilter.AllObjects;
                m_ShadowDrawSettings[i] = settings;
            }
        }

        public override void Record(UnsafePassContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                if (!m_IsActive || !m_ShadowAtlas.innerHandle.IsValid())
                    return;

                nativeCmd.SetRenderTarget(m_ShadowAtlas.innerHandle,RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                // nativeCmd.ClearRenderTarget(true, false, Color.clear);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);

                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    int offsetX = (cascadeIndex % AtlasGridSize) * m_CascadeResolution;
                    int offsetY = (cascadeIndex / AtlasGridSize) * m_CascadeResolution;
                    var gpuProjMatrix = GL.GetGPUProjectionMatrix(m_ShadowData.projMatrices[cascadeIndex], true);
                    var cascadeShaderGlobals = BuildCascadeShaderGlobals(cascadeIndex, gpuProjMatrix);

                    nativeCmd.SetViewport(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.EnableScissorRect(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.SetViewProjectionMatrices(
                        m_ShadowData.viewMatrices[cascadeIndex],
                        m_ShadowData.projMatrices[cascadeIndex]);
                    ConstantBuffer.PushGlobal(nativeCmd, cascadeShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);
                    nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowData.shadowCasterState);

                    var settings = m_ShadowDrawSettings[cascadeIndex];
                    var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                    nativeCmd.DrawRendererList(rendererList);
                }

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
                nativeCmd.DisableScissorRect();
                nativeCmd.SetViewProjectionMatrices(
                    m_CameraShaderGlobals._VividWorldToCamera,
                    m_CameraShaderGlobals._VividCameraProjection);
                ConstantBuffer.PushGlobal(nativeCmd, m_CameraShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);
            }
        }

        // Shadow caster shaders read Vivid's redirected global matrices rather than Unity's transient view/projection state.
        internal static ShaderVariablesGlobal BuildCascadeShaderGlobals(
            in ShaderVariablesGlobal cameraShaderGlobals,
            Matrix4x4 viewMatrix,
            Matrix4x4 projMatrix,
            Matrix4x4 gpuProjMatrix)
        {
            var invViewMatrix = viewMatrix.inverse;
            var invProjMatrix = projMatrix.inverse;
            var gpuInvProjMatrix = gpuProjMatrix.inverse;
            var viewProjMatrix = gpuProjMatrix * viewMatrix;
            var invViewProjMatrix = viewProjMatrix.inverse;

            var shadowGlobals = cameraShaderGlobals;
            shadowGlobals._VividWorldSpaceCameraPos = invViewMatrix.GetColumn(3);
            shadowGlobals._VividCameraProjection = projMatrix;
            shadowGlobals._VividCameraInvProjection = invProjMatrix;
            shadowGlobals._VividWorldToCamera = viewMatrix;
            shadowGlobals._VividCameraToWorld = invViewMatrix;
            shadowGlobals._VividGlstateMatrixProjection = gpuProjMatrix;
            shadowGlobals._VividMatrixV = viewMatrix;
            shadowGlobals._VividMatrixInvV = invViewMatrix;
            shadowGlobals._VividMatrixInvP = gpuInvProjMatrix;
            shadowGlobals._VividMatrixVP = viewProjMatrix;
            shadowGlobals._VividMatrixInvVP = invViewProjMatrix;
            shadowGlobals._VividPrevViewProjMatrix = viewProjMatrix;
            shadowGlobals._VividNonJitteredViewProjMatrix = viewProjMatrix;
            shadowGlobals._VividViewProjMatrix = viewProjMatrix;
            shadowGlobals._VividViewMatrix = viewMatrix;
            shadowGlobals._VividProjMatrix = gpuProjMatrix;
            shadowGlobals._VividInvViewProjMatrix = invViewProjMatrix;
            shadowGlobals._VividInvViewMatrix = invViewMatrix;
            shadowGlobals._VividInvProjMatrix = gpuInvProjMatrix;
            shadowGlobals._VividPrevViewMatrix = viewMatrix;
            shadowGlobals._VividPrevProjMatrix = gpuProjMatrix;
            shadowGlobals._VividJitterParams = Vector4.zero;
            return shadowGlobals;
        }

        private ShaderVariablesGlobal BuildCascadeShaderGlobals(int cascadeIndex, Matrix4x4 gpuProjMatrix)
        {
            return BuildCascadeShaderGlobals(
                m_CameraShaderGlobals,
                m_ShadowData.viewMatrices[cascadeIndex],
                m_ShadowData.projMatrices[cascadeIndex],
                gpuProjMatrix);
        }

        internal static ShaderVariablesGlobal ResolveCameraShaderGlobals(
            ContextContainer frameData,
            VividCameraData cameraData)
        {
            if (cameraData == null)
                return default;

            if (cameraData.hasShaderVariablesGlobal)
                return cameraData.shaderVariablesGlobal;

            var temporalData = FrameContextSystem.GetOrCreate(cameraData?.camera);
            var skyData = frameData.GetOrCreate<VividSkyData>();
            return ShaderVariablesGlobal.Create(cameraData.BuildShaderVariables(temporalData), temporalData, skyData);
        }

        public override void Dispose()
        {
            m_IsActive = false;
            m_MainLightVisibleIndex = -1;
            m_CascadeCount = 0;
            m_SlopeScaleDepthBias = 0.0f;
            m_CameraShaderGlobals = default;
            m_ShadowData = null;
        }
    }
}
