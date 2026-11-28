using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Standard RenderGraph pass that renders physically-tinted volumetric glass shadows
    /// and focused refractive caustics from solid optical glass casters onto opaque surfaces
    /// (supporting both Spot and Directional lights) prior to OpaqueColorPyramidPass.
    ///
    /// For each selected primary light (up to 2):
    ///   1. Light-space backface pass (LightMode = "VividCausticsBackface") writes exit linearZ.
    ///   2. Light-space frontface MRT pass (LightMode = "VividCausticsFrontface") computes
    ///      exact light-ray Beer-Lambert transmittance and Jacobian-based caustic flux focusing.
    ///   3. Screen-space compute pass (TransmissiveCaustics.compute) projects opaque pixels
    ///      into light space with 5-tap PCF + dispersive chromatic caustic offset, applying
    ///      colored volumetric glass shadows and luminous caustic hot-spots into SceneColor.
    /// </summary>
    public sealed class TransmissiveCausticsPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        public const string CausticsBackfaceTagName = "VividCausticsBackface";
        public const string CausticsFrontfaceTagName = "VividCausticsFrontface";
        private const string ApplyKernelName = "KApplyTransmissiveCaustics";
        private const int MaxCausticLights = 2;
        private const int CausticsMapResolution = 1024;
        private const int ThreadGroupSize = 8;

        private static readonly int LightVPId = Shader.PropertyToID("_CausticsLightVP");
        private static readonly int LightPosTypeId = Shader.PropertyToID("_CausticsLightPosType");
        private static readonly int LightForwardRangeId = Shader.PropertyToID("_CausticsLightForwardRange");
        private static readonly int LightColorShadowId = Shader.PropertyToID("_CausticsLightColorShadow");
        private static readonly int SpotParamsId = Shader.PropertyToID("_CausticsSpotParams");
        private static readonly int ControlParamsId = Shader.PropertyToID("_CausticsControlParams");
        private static readonly int BackfaceDepthTexId = Shader.PropertyToID("_CausticsLightBackfaceDepth");
        private static readonly int TransmittanceMapId = Shader.PropertyToID("_CausticsTransmittanceMap");
        private static readonly int FocusMapId = Shader.PropertyToID("_CausticsFocusMap");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int SceneColorId = Shader.PropertyToID("_SceneColor");
        private static readonly int ResolutionId = Shader.PropertyToID("_CausticsResolution");
        private static readonly int InvResolutionId = Shader.PropertyToID("_CausticsInvResolution");
        private static readonly int MapTexelSizeId = Shader.PropertyToID("_CausticsMapTexelSize");

        public static bool EnableCaustics { get; set; } = true;
        public static float CausticsIntensity { get; set; } = 1.0f;
        public static float ColoredShadowStrength { get; set; } = 1.0f;
        public static int DebugMode { get; set; } = 0; // 0 = Composite, 1 = Caustics & Colored Shadow Only

        private struct CausticLightSetup
        {
            public Matrix4x4 LightVP;
            public Vector4 PosType;      // xyz = origin, w = 1 Spot / 0 Directional
            public Vector4 ForwardRange; // xyz = forward, w = range
            public Vector4 ColorShadow;  // rgb = color, a = isShadowedInDeferred (0 or 1)
            public Vector4 SpotParams;   // x = angleScale, y = angleOffset, z = 0, w = 0
            public float Score;
        }

        [RenderGraphResource(
            Name = "BackfaceRenderList0",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_BackfaceRenderList0;

        [RenderGraphResource(
            Name = "FrontfaceRenderList0",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_FrontfaceRenderList0;

        [RenderGraphResource(
            Name = "BackfaceRenderList1",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_BackfaceRenderList1;

        [RenderGraphResource(
            Name = "FrontfaceRenderList1",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_FrontfaceRenderList1;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "CausticsLightBackfaceDepth", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_LightBackfaceDepth;

        [RenderGraphResource(Name = "CausticsLightTransmittance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_LightTransmittanceMap;

        [RenderGraphResource(Name = "CausticsLightFocus", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_LightFocusMap;

        [RenderGraphResource(Name = "CausticsLightDepthBuffer", Access = AccessFlags.Write, IsDepthAttachment = true)]
        [TransientResource]
        private RenderGraphTexture m_LightDepthBuffer;

        private ComputeShader m_Compute;
        private int m_ApplyKernel = -1;

        private readonly CausticLightSetup[] m_Lights = new CausticLightSetup[MaxCausticLights];
        private readonly RenderTargetIdentifier[] m_FrontfaceMrtTargets = new RenderTargetIdentifier[2];
        private readonly int[] m_ResolutionPair = new int[2];

        private int m_ActiveLightCount;
        private int m_Width = 1;
        private int m_Height = 1;

        public TransmissiveCausticsPass()
        {
            profilingSampler = new ProfilingSampler(nameof(TransmissiveCausticsPass));

            m_BackfaceRenderList0 = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(CausticsBackfaceTagName)
            };
            m_FrontfaceRenderList0 = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(CausticsFrontfaceTagName)
            };
            m_BackfaceRenderList1 = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(CausticsBackfaceTagName)
            };
            m_FrontfaceRenderList1 = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(CausticsFrontfaceTagName)
            };

            m_ColorTexture = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);

            m_LightBackfaceDepth = RenderGraphTexture.CreateColorTarget("CausticsLightBackfaceDepth", GraphicsFormat.R32_SFloat);
            ConfigureLightTarget(m_LightBackfaceDepth, GraphicsFormat.R32_SFloat, FilterMode.Point);

            m_LightTransmittanceMap = RenderGraphTexture.CreateColorTarget("CausticsLightTransmittance", GraphicsFormat.R16G16B16A16_SFloat);
            ConfigureLightTarget(m_LightTransmittanceMap, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);

            m_LightFocusMap = RenderGraphTexture.CreateColorTarget("CausticsLightFocus", GraphicsFormat.R16G16B16A16_SFloat);
            ConfigureLightTarget(m_LightFocusMap, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear);

            m_LightDepthBuffer = RenderGraphTexture.CreateDepthTarget("CausticsLightDepthBuffer", DepthBits.Depth32);
            m_LightDepthBuffer.Resize(CausticsMapResolution, CausticsMapResolution);
            m_LightDepthBuffer.desc.ClearBuffer = true;
        }

        private static void ConfigureLightTarget(RenderGraphTexture tex, GraphicsFormat format, FilterMode filterMode)
        {
            tex.Resize(CausticsMapResolution, CausticsMapResolution);
            tex.desc.ColorFormat = format;
            tex.desc.ClearBuffer = true;
            tex.desc.ClearColor = Color.clear;
            tex.desc.FilterMode = filterMode;
            tex.desc.WrapMode = TextureWrapMode.Clamp;
        }

        public override void Create()
        {
            EnsureShaderLoaded();
        }

        public override void Dispose()
        {
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_ApplyKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.TransmissiveCausticsCompute;
            if (m_Compute == null)
                return false;

            m_ApplyKernel = m_Compute.FindKernel(ApplyKernelName);
            return m_ApplyKernel >= 0;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return EnableCaustics && ForwardRefractionPass.EnableRefraction && EnsureShaderLoaded();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            int width = m_ColorTexture?.desc != null && m_ColorTexture.desc.Width > 0
                ? m_ColorTexture.desc.Width
                : CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            int height = m_ColorTexture?.desc != null && m_ColorTexture.desc.Height > 0
                ? m_ColorTexture.desc.Height
                : CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            m_Width = Mathf.Max(1, width);
            m_Height = Mathf.Max(1, height);

            if (m_ColorTexture?.desc != null)
                m_ColorTexture.desc.EnableRandomWrite = true;

            ConfigureLightTarget(m_LightBackfaceDepth, GraphicsFormat.R32_SFloat, FilterMode.Point);
            ConfigureLightTarget(m_LightTransmittanceMap, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point);
            ConfigureLightTarget(m_LightFocusMap, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear);
            m_LightDepthBuffer.Resize(CausticsMapResolution, CausticsMapResolution);
            m_LightDepthBuffer.desc.ClearBuffer = true;

            SelectCausticLights(frameData, cameraData?.camera);
        }

        private void SelectCausticLights(ContextContainer frameData, Camera camera)
        {
            m_ActiveLightCount = 0;
            if (!frameData.Contains<VividLightData>())
                return;

            var lightData = frameData.Get<VividLightData>();
            if (lightData == null || !lightData.visibleLights.IsCreated || lightData.visibleLights.Length == 0)
                return;

            Vector3 camFocus = camera != null
                ? camera.transform.position + camera.transform.forward * 2.5f
                : Vector3.zero;

            var visibleLights = lightData.visibleLights;
            for (int i = 0; i < visibleLights.Length; ++i)
            {
                VisibleLight vl = visibleLights[i];
                if (vl.lightType != LightType.Spot && vl.lightType != LightType.Directional)
                    continue;

                Color finalColor = vl.finalColor;
                float luma = finalColor.r * 0.2126f + finalColor.g * 0.7152f + finalColor.b * 0.0722f;
                if (luma <= 1e-4f)
                    continue;

                CausticLightSetup candidate = BuildLightSetup(vl, camFocus, luma);
                if (candidate.Score <= 0.0f)
                    continue;

                // Insert into fixed-size top-K array (zero allocation)
                if (m_ActiveLightCount < MaxCausticLights)
                {
                    m_Lights[m_ActiveLightCount++] = candidate;
                    if (m_ActiveLightCount == 2 && m_Lights[1].Score > m_Lights[0].Score)
                    {
                        var tmp = m_Lights[0];
                        m_Lights[0] = m_Lights[1];
                        m_Lights[1] = tmp;
                    }
                }
                else if (candidate.Score > m_Lights[MaxCausticLights - 1].Score)
                {
                    m_Lights[MaxCausticLights - 1] = candidate;
                    if (m_Lights[1].Score > m_Lights[0].Score)
                    {
                        var tmp = m_Lights[0];
                        m_Lights[0] = m_Lights[1];
                        m_Lights[1] = tmp;
                    }
                }
            }
        }

        private static CausticLightSetup BuildLightSetup(in VisibleLight vl, Vector3 camFocus, float luma)
        {
            CausticLightSetup setup = default;
            Matrix4x4 l2w = vl.localToWorldMatrix;
            Vector3 forward = ((Vector3)l2w.GetColumn(2)).normalized;
            Vector3 up = ((Vector3)l2w.GetColumn(1)).normalized;
            if (up.sqrMagnitude < 1e-4f || Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Mathf.Abs(forward.y) < 0.95f ? Vector3.up : Vector3.right;

            Color c = vl.finalColor;

            if (vl.lightType == LightType.Spot)
            {
                Vector3 pos = l2w.GetColumn(3);
                float range = Mathf.Max(vl.range, 0.5f);
                float spotAngleDeg = Mathf.Clamp(vl.spotAngle, 5.0f, 150.0f);
                float cosOuter = Mathf.Cos(spotAngleDeg * 0.5f * Mathf.Deg2Rad);
                float innerAngleDeg = vl.light != null && vl.light.innerSpotAngle > 0.0f
                    ? Mathf.Min(vl.light.innerSpotAngle, spotAngleDeg - 0.5f)
                    : spotAngleDeg * 0.75f;
                float cosInner = Mathf.Cos(innerAngleDeg * 0.5f * Mathf.Deg2Rad);
                float angleScale = 1.0f / Mathf.Max(cosInner - cosOuter, 1e-4f);
                float angleOffset = -cosOuter * angleScale;

                Matrix4x4 view = BuildRightHandedViewMatrix(pos, forward, up);
                Matrix4x4 proj = Matrix4x4.Perspective(spotAngleDeg, 1.0f, 0.08f, range);
                setup.LightVP = GL.GetGPUProjectionMatrix(proj, true) * view;
                setup.PosType = new Vector4(pos.x, pos.y, pos.z, 1.0f);
                setup.ForwardRange = new Vector4(forward.x, forward.y, forward.z, range);
                setup.ColorShadow = new Vector4(c.r, c.g, c.b, 0.0f); // Spot lights are unshadowed in DeferredLit
                setup.SpotParams = new Vector4(angleScale, angleOffset, 0.0f, 0.0f);
                setup.Score = luma;
            }
            else
            {
                // Directional light: focused orthographic volume around camera look target
                const float halfExtent = 4.0f;
                const float nearPlane = 0.1f;
                const float farPlane = 28.0f;
                Vector3 origin = camFocus - forward * 12.0f;

                Matrix4x4 view = BuildRightHandedViewMatrix(origin, forward, up);
                Matrix4x4 proj = Matrix4x4.Ortho(-halfExtent, halfExtent, -halfExtent, halfExtent, nearPlane, farPlane);
                bool castsCsmShadow = vl.light != null && vl.light.shadows != LightShadows.None;

                setup.LightVP = GL.GetGPUProjectionMatrix(proj, true) * view;
                setup.PosType = new Vector4(origin.x, origin.y, origin.z, 0.0f);
                setup.ForwardRange = new Vector4(forward.x, forward.y, forward.z, farPlane);
                setup.ColorShadow = new Vector4(c.r, c.g, c.b, castsCsmShadow ? 1.0f : 0.0f);
                setup.SpotParams = Vector4.zero;
                setup.Score = luma * 1.25f;
            }

            return setup;
        }

        private static Matrix4x4 BuildRightHandedViewMatrix(Vector3 origin, Vector3 forward, Vector3 up)
        {
            Matrix4x4 view = Matrix4x4.Inverse(Matrix4x4.TRS(origin, Quaternion.LookRotation(forward, up), Vector3.one));
            view.m20 = -view.m20;
            view.m21 = -view.m21;
            view.m22 = -view.m22;
            view.m23 = -view.m23;
            return view;
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ActiveLightCount <= 0
                || m_Compute == null
                || m_ApplyKernel < 0
                || m_ColorTexture == null || !m_ColorTexture.innerHandle.IsValid()
                || m_DepthTexture == null || !m_DepthTexture.innerHandle.IsValid()
                || m_GBuffer0 == null || !m_GBuffer0.innerHandle.IsValid()
                || m_GBuffer1 == null || !m_GBuffer1.innerHandle.IsValid()
                || m_LightBackfaceDepth == null || !m_LightBackfaceDepth.innerHandle.IsValid()
                || m_LightTransmittanceMap == null || !m_LightTransmittanceMap.innerHandle.IsValid()
                || m_LightFocusMap == null || !m_LightFocusMap.innerHandle.IsValid()
                || m_LightDepthBuffer == null || !m_LightDepthBuffer.innerHandle.IsValid())
            {
                return;
            }

            var nativeCmd = context.GetNativeCommandBuffer();
            int groupsX = (m_Width + ThreadGroupSize - 1) / ThreadGroupSize;
            int groupsY = (m_Height + ThreadGroupSize - 1) / ThreadGroupSize;
            m_ResolutionPair[0] = m_Width;
            m_ResolutionPair[1] = m_Height;

            m_FrontfaceMrtTargets[0] = m_LightTransmittanceMap.innerHandle;
            m_FrontfaceMrtTargets[1] = m_LightFocusMap.innerHandle;

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                for (int li = 0; li < m_ActiveLightCount; ++li)
                {
                    RenderGraphRenderList backList = li == 0 ? m_BackfaceRenderList0 : m_BackfaceRenderList1;
                    RenderGraphRenderList frontList = li == 0 ? m_FrontfaceRenderList0 : m_FrontfaceRenderList1;
                    if (backList == null || !backList.IsValid || frontList == null || !frontList.IsValid)
                        continue;

                    ref readonly CausticLightSetup light = ref m_Lights[li];

                    // Bind light-space transform & metadata for rasterization passes
                    nativeCmd.SetGlobalMatrix(LightVPId, light.LightVP);
                    nativeCmd.SetGlobalVector(LightPosTypeId, light.PosType);
                    nativeCmd.SetGlobalVector(LightForwardRangeId, light.ForwardRange);

                    // 1. Light-Space Backface Pass (nearest exit backface depth in light space)
                    CoreUtils.SetRenderTarget(
                        nativeCmd,
                        m_LightBackfaceDepth.innerHandle,
                        m_LightDepthBuffer.innerHandle,
                        ClearFlag.All,
                        Color.clear);
                    nativeCmd.DrawRendererList(backList);

                    // 2. Light-Space Frontface MRT Pass (Beer-Lambert transmittance + Jacobian caustic focus)
                    nativeCmd.SetGlobalTexture(BackfaceDepthTexId, m_LightBackfaceDepth.innerHandle);
                    nativeCmd.SetRenderTarget(m_FrontfaceMrtTargets, m_LightDepthBuffer.innerHandle);
                    nativeCmd.ClearRenderTarget(true, true, Color.clear);
                    nativeCmd.DrawRendererList(frontList);

                    // 3. Screen-Space Projection Compute Pass into opaque SceneColor
                    nativeCmd.SetComputeIntParams(m_Compute, ResolutionId, m_ResolutionPair);
                    nativeCmd.SetComputeVectorParam(m_Compute, InvResolutionId, new Vector4(1.0f / m_Width, 1.0f / m_Height, 0.0f, 0.0f));
                    nativeCmd.SetComputeVectorParam(m_Compute, MapTexelSizeId, new Vector4(1.0f / CausticsMapResolution, 1.0f / CausticsMapResolution, 0.0f, 0.0f));
                    nativeCmd.SetComputeMatrixParam(m_Compute, LightVPId, light.LightVP);
                    nativeCmd.SetComputeVectorParam(m_Compute, LightPosTypeId, light.PosType);
                    nativeCmd.SetComputeVectorParam(m_Compute, LightForwardRangeId, light.ForwardRange);
                    nativeCmd.SetComputeVectorParam(m_Compute, LightColorShadowId, light.ColorShadow);
                    nativeCmd.SetComputeVectorParam(m_Compute, SpotParamsId, light.SpotParams);
                    nativeCmd.SetComputeVectorParam(
                        m_Compute,
                        ControlParamsId,
                        new Vector4(
                            Mathf.Max(CausticsIntensity, 0.0f),
                            Mathf.Clamp01(ColoredShadowStrength),
                            DebugMode,
                            li));

                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, DepthTextureId, m_DepthTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, GBuffer0Id, m_GBuffer0.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, TransmittanceMapId, m_LightTransmittanceMap.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, FocusMapId, m_LightFocusMap.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_Compute, m_ApplyKernel, SceneColorId, m_ColorTexture.innerHandle);

                    nativeCmd.DispatchCompute(m_Compute, m_ApplyKernel, groupsX, groupsY, 1);
                }
            }
        }
    }
}
