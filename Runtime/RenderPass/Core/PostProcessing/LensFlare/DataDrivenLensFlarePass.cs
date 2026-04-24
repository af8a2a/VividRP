using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class DataDrivenLensFlarePass : UnsafePass, IAllowGlobalStateModificationPass
    {
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int LensFlareOcclusionId = Shader.PropertyToID("_LensFlareOcclusion");
        private static readonly int MultipassIdId = Shader.PropertyToID("_MultipassID");

        [RenderGraphResource(Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture depthTexture = new();

        private Material m_LensFlareMaterial;
        private ComputeShader m_MergeOcclusionCompute;
        private int m_MergeOcclusionKernel = -1;
        private Camera m_Camera;
        private Rect m_Viewport;
        private Vector3 m_CameraPositionWS;
        private Matrix4x4 m_NonJitteredViewProjectionMatrix;
        private bool m_TaaEnabled;
        private bool m_UseTemporalOcclusion;
        private bool m_ShouldRender;

        public DataDrivenLensFlarePass()
        {
            profilingSampler = new ProfilingSampler(nameof(DataDrivenLensFlarePass));
            source = RenderGraphTexture.CreateInput("source", GraphicsFormat.R16G16B16A16_SFloat);
            depthTexture = RenderGraphTexture.CreateInput("DepthTexture", GraphicsFormat.R32_SFloat);
            depthTexture.desc.FilterMode = FilterMode.Point;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null)
                return;

            if (resources.LensFlareDataDrivenShader != null)
            {
                m_LensFlareMaterial = CoreUtils.CreateEngineMaterial(resources.LensFlareDataDrivenShader);
                m_LensFlareMaterial.SetOverrideTag("RenderType", "Transparent");
            }

            m_MergeOcclusionCompute = resources.LensFlareMergeOcclusionDataDrivenCompute;
            if (m_MergeOcclusionCompute != null && m_MergeOcclusionCompute.HasKernel("MainCS"))
                m_MergeOcclusionKernel = m_MergeOcclusionCompute.FindKernel("MainCS");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Camera = cameraData?.camera;

            var width = ResolveWidth(cameraData);
            var height = ResolveHeight(cameraData);
            m_Viewport = new Rect(0f, 0f, width, height);
            m_TaaEnabled = TAASettings.FromCamera(cameraData?.additionalData).Enabled;
            m_CameraPositionWS = cameraData != null
                ? cameraData.inverseViewMatrix.GetColumn(3)
                : Vector3.zero;
            m_NonJitteredViewProjectionMatrix = cameraData != null
                ? cameraData.GetGPUProjectionMatrixNoJitter(true) * cameraData.GetViewMatrix()
                : Matrix4x4.identity;

            m_ShouldRender = m_Camera != null
                && CoreUtils.ArePostProcessesEnabled(m_Camera)
                && m_LensFlareMaterial != null
                && !LensFlareCommonSRP.Instance.IsEmpty();
            m_UseTemporalOcclusion = m_TaaEnabled
                && m_MergeOcclusionCompute != null
                && m_MergeOcclusionKernel >= 0;

            if (!m_ShouldRender)
                return;

            if (LensFlareCommonSRP.occlusionRT == null)
                LensFlareCommonSRP.Initialize();

            if (LensFlareCommonSRP.occlusionRT != null)
                PassRecorder.ImportTextureForPass(this, LensFlareCommonSRP.occlusionRT, AccessFlags.ReadWrite);
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_ShouldRender || source?.innerHandle.IsValid() != true)
                return;

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                BindDepthTexture(context.cmd);

                if (LensFlareCommonSRP.IsOcclusionRTCompatible() && LensFlareCommonSRP.occlusionRT != null)
                {
                    if (depthTexture?.innerHandle.IsValid() == true)
                    {
                        LensFlareCommonSRP.ComputeOcclusion(
                            m_LensFlareMaterial,
                            m_Camera,
                            XRSystem.emptyPass,
                            0,
                            m_Viewport.width,
                            m_Viewport.height,
                            false,
                            0f,
                            1f,
                            false,
                            m_CameraPositionWS,
                            m_NonJitteredViewProjectionMatrix,
                            context.cmd,
                            m_UseTemporalOcclusion,
                            false,
                            null,
                            null);

                        if (m_UseTemporalOcclusion)
                            MergeOcclusion(context.cmd);
                    }
                    else
                    {
                        ClearOcclusionToVisible(context.GetNativeCommandBuffer());
                    }
                }

                LensFlareCommonSRP.DoLensFlareDataDrivenCommon(
                    m_LensFlareMaterial,
                    m_Camera,
                    m_Viewport,
                    XRSystem.emptyPass,
                    0,
                    m_Viewport.width,
                    m_Viewport.height,
                    false,
                    0f,
                    1f,
                    false,
                    m_CameraPositionWS,
                    m_NonJitteredViewProjectionMatrix,
                    context.cmd,
                    m_UseTemporalOcclusion,
                    false,
                    null,
                    null,
                    source.innerHandle,
                    GetLensFlareLightAttenuation,
                    false);
            }
        }

        public override void Dispose()
        {
            if (m_LensFlareMaterial != null)
            {
                CoreUtils.Destroy(m_LensFlareMaterial);
                m_LensFlareMaterial = null;
            }

            m_MergeOcclusionCompute = null;
            m_MergeOcclusionKernel = -1;
        }

        private void BindDepthTexture(UnsafeCommandBuffer cmd)
        {
            if (depthTexture?.innerHandle.IsValid() == true)
                cmd.SetGlobalTexture(CameraDepthTextureId, depthTexture.innerHandle);
        }

        private void MergeOcclusion(UnsafeCommandBuffer cmd)
        {
            if (m_MergeOcclusionCompute == null || m_MergeOcclusionKernel < 0)
                return;

            cmd.SetComputeTextureParam(
                m_MergeOcclusionCompute,
                m_MergeOcclusionKernel,
                LensFlareOcclusionId,
                LensFlareCommonSRP.occlusionRT);
            cmd.SetComputeIntParam(m_MergeOcclusionCompute, MultipassIdId, 0);
            cmd.DispatchCompute(
                m_MergeOcclusionCompute,
                m_MergeOcclusionKernel,
                CoreUtils.DivRoundUp(LensFlareCommonSRP.maxLensFlareWithOcclusion, 8),
                CoreUtils.DivRoundUp(LensFlareCommonSRP.maxLensFlareWithOcclusionTemporalSample, 8),
                1);
        }

        private static void ClearOcclusionToVisible(CommandBuffer cmd)
        {
            CoreUtils.SetRenderTarget(cmd, LensFlareCommonSRP.occlusionRT);
            cmd.ClearRenderTarget(false, true, Color.white);
        }

        private static float GetLensFlareLightAttenuation(Light light, Camera cam, Vector3 wo)
        {
            if (light == null || cam == null)
                return 1.0f;

            return light.type switch
            {
                LightType.Directional => LensFlareCommonSRP.ShapeAttenuationDirLight(light.transform.forward, cam.transform.forward),
                LightType.Point => LensFlareCommonSRP.ShapeAttenuationPointLight(),
                LightType.Spot => LensFlareCommonSRP.ShapeAttenuationSpotConeLight(
                    light.transform.forward,
                    wo,
                    light.spotAngle,
                    light.spotAngle > 0f ? light.innerSpotAngle / light.spotAngle : 0f),
                LightType.Pyramid => LensFlareCommonSRP.ShapeAttenuationSpotPyramidLight(light.transform.forward, wo),
                LightType.Box => LensFlareCommonSRP.ShapeAttenuationSpotBoxLight(light.transform.forward, wo),
                LightType.Rectangle => LensFlareCommonSRP.ShapeAttenuationAreaRectangleLight(light.transform.forward, wo),
                LightType.Tube => LensFlareCommonSRP.ShapeAttenuationAreaTubeLight(
                    light.transform.position,
                    light.transform.right,
                    light.areaSize.x,
                    cam),
                LightType.Disc => LensFlareCommonSRP.ShapeAttenuationAreaDiscLight(light.transform.forward, wo),
                _ => 1.0f,
            };
        }

        private static int ResolveWidth(VividCameraData data)
        {
            if (data == null)
                return Mathf.Max(1, Screen.width);
            if (data.actualWidth > 0)
                return data.actualWidth;
            if (data.pixelWidth > 0)
                return data.pixelWidth;
            return Mathf.Max(1, Screen.width);
        }

        private static int ResolveHeight(VividCameraData data)
        {
            if (data == null)
                return Mathf.Max(1, Screen.height);
            if (data.actualHeight > 0)
                return data.actualHeight;
            if (data.pixelHeight > 0)
                return data.pixelHeight;
            return Mathf.Max(1, Screen.height);
        }
    }
}
