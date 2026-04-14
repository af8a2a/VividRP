using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowPass : UnsafePass
    {
        private const int AtlasGridSize = 2; // 2x2 grid for up to 4 cascades

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private bool m_IsActive;
        private int m_CascadeCount;
        private int m_AtlasResolution;
        private int m_CascadeResolution;
        private int m_MainLightVisibleIndex = -1;
        private float m_DepthBias;
        private Matrix4x4 m_CameraViewMatrix = Matrix4x4.identity;
        private Matrix4x4 m_CameraProjMatrix = Matrix4x4.identity;

        private readonly Matrix4x4[] m_ViewMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Matrix4x4[] m_ProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
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
            m_DepthBias = 0.0f;
            m_CameraViewMatrix = Matrix4x4.identity;
            m_CameraProjMatrix = Matrix4x4.identity;

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
                || !DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out var light, out _)
                || light == null
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.shadows == LightShadows.None)
            {
                return;
            }

            var renderingData = frameData.GetOrCreate<VividRenderingData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_CullingResults = renderingData.cullingResults;
            m_RenderContext = renderingData.context;
            m_CameraViewMatrix = cameraData.GetViewMatrix();
            m_CameraProjMatrix = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
            m_MainLightVisibleIndex = lightData.mainLightIndex;

            m_CascadeCount = Mathf.Clamp(csmSettings.cascadeCount.value, 1, VividShadowData.MaxCascadeCount);
            m_CascadeResolution = Mathf.Max(1, csmSettings.shadowResolution.value);
            m_AtlasResolution = m_CascadeResolution * AtlasGridSize;
            m_DepthBias = Mathf.Max(0.0f, csmSettings.depthBias.value);

            var splitRatios = csmSettings.GetCascadeSplitRatios();

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

                var sphere = m_SplitData[i].cullingSphere;
                // Store radius squared in w for GPU sphere test
                m_CascadeSpheres[i] = new Vector4(sphere.x, sphere.y, sphere.z, sphere.w * sphere.w);
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
                m_ShadowDrawSettings[i] = new ShadowDrawingSettings(m_CullingResults, m_MainLightVisibleIndex);
                m_ShadowDrawSettings[i].splitData = m_SplitData[i];
                m_ShadowDrawSettings[i].useRenderingLayerMaskTest = false;
                m_ShadowDrawSettings[i].objectsFilter = ShadowObjectsFilter.AllObjects;
            }

            // Populate VividShadowData for downstream passes.
            shadowData.isCSMActive = true;
            shadowData.cascadeCount = m_CascadeCount;
            shadowData.maxShadowDistance = csmSettings.maxShadowDistance.value;
            shadowData.atlasResolution = m_AtlasResolution;
            shadowData.cascadeResolution = m_CascadeResolution;
            shadowData.depthBias = m_DepthBias;
            shadowData.normalBias = Mathf.Max(0.0f, csmSettings.normalBias.value);

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                if (i < m_CascadeCount)
                {
                    shadowData.viewMatrices[i] = m_ViewMatrices[i];
                    shadowData.projMatrices[i] = m_ProjMatrices[i];
                    shadowData.viewProjMatrices[i] = m_ProjMatrices[i] * m_ViewMatrices[i];
                    shadowData.cascadeSpheres[i] = m_CascadeSpheres[i];
                }
                else
                {
                    shadowData.viewMatrices[i] = Matrix4x4.identity;
                    shadowData.projMatrices[i] = Matrix4x4.identity;
                    shadowData.viewProjMatrices[i] = Matrix4x4.identity;
                    shadowData.cascadeSpheres[i] = Vector4.zero;
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
                nativeCmd.SetGlobalDepthBias(m_DepthBias, 0.0f);

                for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
                {
                    int offsetX = (cascadeIndex % AtlasGridSize) * m_CascadeResolution;
                    int offsetY = (cascadeIndex / AtlasGridSize) * m_CascadeResolution;

                    nativeCmd.SetViewport(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.EnableScissorRect(new Rect(offsetX, offsetY, m_CascadeResolution, m_CascadeResolution));
                    nativeCmd.SetViewProjectionMatrices(m_ViewMatrices[cascadeIndex], m_ProjMatrices[cascadeIndex]);

                    var settings = m_ShadowDrawSettings[cascadeIndex];
                    var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                    nativeCmd.DrawRendererList(rendererList);
                }

                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
                nativeCmd.DisableScissorRect();
                nativeCmd.SetViewProjectionMatrices(m_CameraViewMatrix, m_CameraProjMatrix);
            }
        }

        public override void Dispose()
        {
            m_IsActive = false;
            m_MainLightVisibleIndex = -1;
            m_CascadeCount = 0;
            m_DepthBias = 0.0f;
            m_CameraViewMatrix = Matrix4x4.identity;
            m_CameraProjMatrix = Matrix4x4.identity;
        }
    }
}
