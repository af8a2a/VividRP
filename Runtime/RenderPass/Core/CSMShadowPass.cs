using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowPass : UnsafePass
    {
        private const int AtlasGridSize = 2; // 2x2 grid for up to 4 cascades
        private const float CascadeBlendCullingFactor = 0.6f;
        private static readonly int ShadowBiasId = Shader.PropertyToID("_ShadowBias");

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private bool m_IsActive;
        private int m_CascadeCount;
        private int m_AtlasResolution;
        private int m_CascadeResolution;
        private int m_MainLightVisibleIndex = -1;
        private float m_NormalBias;
        private float m_SlopeScaleDepthBias;
        private ShaderVariablesGlobal m_CameraShaderGlobals;

        private readonly Matrix4x4[] m_ViewMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Matrix4x4[] m_ProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
        private readonly float[] m_CascadeWorldTexelSizes = new float[VividShadowData.MaxCascadeCount];
        private readonly float[] m_CascadeBorders = new float[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_ShadowCasterBiases = new Vector4[VividShadowData.MaxCascadeCount];
        private readonly ShadowSplitData[] m_SplitData = new ShadowSplitData[VividShadowData.MaxCascadeCount];
        private readonly ShadowDrawingSettings[] m_ShadowDrawSettings = new ShadowDrawingSettings[VividShadowData.MaxCascadeCount];

        private CullingResults m_CullingResults;
        private ScriptableRenderContext m_RenderContext;

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
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_CascadeCount = 0;
            m_MainLightVisibleIndex = -1;
            m_NormalBias = 0.0f;
            m_SlopeScaleDepthBias = 0.0f;
            m_CameraShaderGlobals = default;

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            shadowData.Reset();

            var csmSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (csmSettings == null || !csmSettings.IsActive())
                return;

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (!lightData.hasMainDirectionalLight
                || !lightData.hasVisibleLights
                || lightData.mainLightIndex < 0
                || lightData.mainLightIndex >= lightData.visibleLights.Length
                || !DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out var light, out var additionalLightData)
                || light == null
                || additionalLightData == null
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.shadows == LightShadows.None)
            {
                return;
            }

            var renderingData = frameData.GetOrCreate<VividRenderingData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var temporalData = FrameContextSystem.GetOrCreate(cameraData.camera);
            var skyData = frameData.GetOrCreate<VividSkyData>();
            m_CullingResults = renderingData.cullingResults;
            m_RenderContext = renderingData.context;
            m_CameraShaderGlobals = ShaderVariablesGlobal.Create(cameraData.BuildShaderVariables(temporalData), temporalData, skyData);
            m_MainLightVisibleIndex = lightData.mainLightIndex;

            m_CascadeCount = Mathf.Clamp(csmSettings.cascadeCount.value, 1, VividShadowData.MaxCascadeCount);
            m_AtlasResolution = Mathf.Max(AtlasGridSize, additionalLightData.resolvedShadowAtlasResolution);
            m_CascadeResolution = Mathf.Max(1, m_AtlasResolution / AtlasGridSize);
            m_NormalBias = Mathf.Max(0.0f, additionalLightData.normalBias);
            m_SlopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);
            var mainVisibleLight = lightData.mainVisibleLight;

            var splitRatios = csmSettings.GetCascadeSplitRatios();
            var cascadeBorders = csmSettings.GetCascadeBorderRatios();

            bool allCascadesValid = true;
            for (int i = 0; i < m_CascadeCount; i++)
            {
                bool success = m_CullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    m_MainLightVisibleIndex,
                    i,
                    m_CascadeCount,
                    splitRatios,
                    m_CascadeResolution,
                    QualitySettings.shadowNearPlaneOffset,
                    out m_ViewMatrices[i],
                    out m_ProjMatrices[i],
                    out m_SplitData[i]);

                if (!success)
                {
                    allCascadesValid = false;
                    break;
                }

                // Match HDRP/Unity's directional cascade overlap.
                // Higher values cull more casters, which causes blend regions to lose moving occluders.
                m_SplitData[i].shadowCascadeBlendCullingFactor = CascadeBlendCullingFactor;

                StabilizeCascadeProjection(ref m_ProjMatrices[i], m_ViewMatrices[i], m_CascadeResolution);

                var sphere = m_SplitData[i].cullingSphere;
                // Store radius squared in w for GPU sphere test
                m_CascadeSpheres[i] = new Vector4(sphere.x, sphere.y, sphere.z, sphere.w * sphere.w);
                m_CascadeWorldTexelSizes[i] = ComputeCascadeWorldTexelSize(m_ProjMatrices[i], m_CascadeResolution);
                m_CascadeBorders[i] = cascadeBorders[i];
                m_ShadowCasterBiases[i] = BuildShadowCasterState(mainVisibleLight);
            }

            if (!allCascadesValid)
                return;

            m_IsActive = true;

            // Configure atlas texture size
            m_ShadowAtlas.desc.Width = m_AtlasResolution;
            m_ShadowAtlas.desc.Height = m_AtlasResolution;

            // Configure ShadowDrawingSettings per cascade
            for (int i = 0; i < m_CascadeCount; i++)
            {
                var settings = new ShadowDrawingSettings(
                    m_CullingResults,
                    m_MainLightVisibleIndex,
                    BatchCullingProjectionType.Orthographic);
                settings.splitIndex = i;
                settings.splitData = m_SplitData[i];
                settings.useRenderingLayerMaskTest = false;
                settings.objectsFilter = ShadowObjectsFilter.AllObjects;
                m_ShadowDrawSettings[i] = settings;
            }

            // Populate VividShadowData for downstream passes.
            shadowData.isCSMActive = true;
            shadowData.cascadeCount = m_CascadeCount;
            shadowData.maxShadowDistance = csmSettings.maxShadowDistance.value;
            shadowData.atlasResolution = m_AtlasResolution;
            shadowData.cascadeResolution = m_CascadeResolution;
            shadowData.normalBias = m_NormalBias;

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                if (i < m_CascadeCount)
                {
                    shadowData.viewMatrices[i] = m_ViewMatrices[i];
                    shadowData.projMatrices[i] = m_ProjMatrices[i];
                    shadowData.viewProjMatrices[i] = BuildWorldToShadowMatrix(m_ProjMatrices[i], m_ViewMatrices[i]);
                    shadowData.cascadeSpheres[i] = m_CascadeSpheres[i];
                    shadowData.cascadeWorldTexelSizes[i] = m_CascadeWorldTexelSizes[i];
                    shadowData.cascadeBorders[i] = m_CascadeBorders[i];
                }
                else
                {
                    shadowData.viewMatrices[i] = Matrix4x4.identity;
                    shadowData.projMatrices[i] = Matrix4x4.identity;
                    shadowData.viewProjMatrices[i] = Matrix4x4.identity;
                    shadowData.cascadeSpheres[i] = Vector4.zero;
                    shadowData.cascadeWorldTexelSizes[i] = 0.0f;
                    shadowData.cascadeBorders[i] = 0.0f;
                }
            }

            shadowData.ComputeAtlasLayout();
        }

        public override void Record(UnsafeGraphContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                if (!m_IsActive || !m_ShadowAtlas.innerHandle.IsValid())
                    return;

                nativeCmd.SetRenderTarget(m_ShadowAtlas.innerHandle,RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                nativeCmd.ClearRenderTarget(true, false, Color.clear);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);

                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    int offsetX = (cascadeIndex % AtlasGridSize) * m_CascadeResolution;
                    int offsetY = (cascadeIndex / AtlasGridSize) * m_CascadeResolution;
                    var gpuProjMatrix = GL.GetGPUProjectionMatrix(m_ProjMatrices[cascadeIndex], true);
                    var cascadeShaderGlobals = BuildCascadeShaderGlobals(cascadeIndex, gpuProjMatrix);

                    nativeCmd.SetViewport(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.EnableScissorRect(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.SetViewProjectionMatrices(m_ViewMatrices[cascadeIndex], m_ProjMatrices[cascadeIndex]);
                    ConstantBuffer.PushGlobal(nativeCmd, cascadeShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);
                    nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowCasterBiases[cascadeIndex]);

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
        private ShaderVariablesGlobal BuildCascadeShaderGlobals(int cascadeIndex, Matrix4x4 gpuProjMatrix)
        {
            var viewMatrix = m_ViewMatrices[cascadeIndex];
            var projMatrix = m_ProjMatrices[cascadeIndex];
            var invViewMatrix = viewMatrix.inverse;
            var invProjMatrix = projMatrix.inverse;
            var gpuInvProjMatrix = gpuProjMatrix.inverse;
            var viewProjMatrix = gpuProjMatrix * viewMatrix;
            var invViewProjMatrix = viewProjMatrix.inverse;

            var shadowGlobals = m_CameraShaderGlobals;
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

        private static Matrix4x4 BuildWorldToShadowMatrix(Matrix4x4 projMatrix, Matrix4x4 viewMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projMatrix.m20 = -projMatrix.m20;
                projMatrix.m21 = -projMatrix.m21;
                projMatrix.m22 = -projMatrix.m22;
                projMatrix.m23 = -projMatrix.m23;
            }

            var worldToShadow = projMatrix * viewMatrix;
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            return textureScaleAndBias * worldToShadow;
        }

        private static Vector4 BuildShadowCasterState(in VisibleLight shadowLight)
        {
            // Match HDRP's directional shadow path: rely on raster slope-scale depth bias,
            // receiver normal bias, and a tiny fixed compare bias instead of caster vertex offsets.
            return new Vector4(
                0.0f,
                0.0f,
                (float)shadowLight.lightType,
                0.0f);
        }

        private static void StabilizeCascadeProjection(ref Matrix4x4 projMatrix, Matrix4x4 viewMatrix, float cascadeResolution)
        {
            if (cascadeResolution <= 0.0f)
                return;

            // Transform world origin into clip space to get a stable reference point.
            Vector4 originClip = projMatrix * viewMatrix * new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

            // Each texel spans 2/resolution in clip space (NDC range is -1..1).
            float texelSizeClip = 2.0f / cascadeResolution;

            float offsetX = originClip.x % texelSizeClip;
            float offsetY = originClip.y % texelSizeClip;

            projMatrix.m03 -= offsetX;
            projMatrix.m13 -= offsetY;
        }

        private static float ComputeCascadeWorldTexelSize(Matrix4x4 lightProjectionMatrix, float shadowResolution)
        {
            float projectionScale = Mathf.Max(Mathf.Abs(lightProjectionMatrix.m00), 1e-6f);
            float frustumSize = 2.0f / projectionScale;
            float texelSize = frustumSize / Mathf.Max(shadowResolution, 1.0f);
            return texelSize * Mathf.Sqrt(2.0f);
        }

        public override void Dispose()
        {
            m_IsActive = false;
            m_MainLightVisibleIndex = -1;
            m_CascadeCount = 0;
            m_NormalBias = 0.0f;
            m_SlopeScaleDepthBias = 0.0f;
            m_CameraShaderGlobals = default;
        }
    }
}
