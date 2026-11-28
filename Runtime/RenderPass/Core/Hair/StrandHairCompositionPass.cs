using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Phase S1 Strand-Based Hair Composition Pass (IJGC SIGGRAPH 2025):
    /// Composites each visible hair model back-to-front over SceneColor using
    /// the 3 ordered front visibility layers, quadratic total coverage reconstruction,
    /// and deep-layer weighted radiance extrapolation (w_i = C_i * 0.25^(3-i)).
    /// </summary>
    public sealed class StrandHairCompositionPass : ComputePass
    {
        private const string ComposeKernelName = "KComposeHairModel";

        private static readonly int RestVerticesId = Shader.PropertyToID("_HairRestVertices");
        private static readonly int SegmentIndicesId = Shader.PropertyToID("_HairSegmentIndices");
        private static readonly int PostTransformClipId = Shader.PropertyToID("_HairPostTransformClip");
        private static readonly int WorldPosRadiusId = Shader.PropertyToID("_HairWorldPosRadius");
        private static readonly int VisAtlasId = Shader.PropertyToID("_HairVisAtlas");
        private static readonly int SceneColorId = Shader.PropertyToID("_SceneColor");

        private static readonly int LocalToWorldId = Shader.PropertyToID("_HairLocalToWorld");
        private static readonly int AtlasTileRectId = Shader.PropertyToID("_HairAtlasTileRect");
        private static readonly int ComposeRectId = Shader.PropertyToID("_HairComposeRect");
        private static readonly int FirstAtlasTileId = Shader.PropertyToID("_HairFirstAtlasTile");
        private static readonly int DownscaleFactorId = Shader.PropertyToID("_HairDownscaleFactor");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_HairScreenSize");
        private static readonly int AtlasScreenSizeId = Shader.PropertyToID("_HairAtlasScreenSize");
        private static readonly int ProjScaleYId = Shader.PropertyToID("_HairProjScaleY");
        private static readonly int DebugViewModeId = Shader.PropertyToID("_HairDebugViewMode");

        private static readonly int CameraPosWSId = Shader.PropertyToID("_HairCameraPosWS");
        private static readonly int KeyLightDirWSId = Shader.PropertyToID("_HairKeyLightDirWS");
        private static readonly int KeyLightColorId = Shader.PropertyToID("_HairKeyLightColor");
        private static readonly int RimLightDirWSId = Shader.PropertyToID("_HairRimLightDirWS");
        private static readonly int RimLightColorId = Shader.PropertyToID("_HairRimLightColor");
        private static readonly int AmbientColorId = Shader.PropertyToID("_HairAmbientColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_HairBaseColor");
        private static readonly int RoughnessParamsId = Shader.PropertyToID("_HairRoughnessParams");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ColorTexture;

        private ComputeShader m_Compute;
        private int m_ComposeKernel = -1;

        private StrandHairFrameData m_FrameData;
        private StrandHairGpuPool m_GpuPool;
        private GraphicsBuffer m_PreExposureBuffer;
        private Vector3 m_CameraPosWS;
        private Vector3 m_KeyLightDirWS = new Vector3(0.45f, 0.72f, -0.52f);
        private Vector3 m_KeyLightColor = new Vector3(2.2f, 2.1f, 2.0f);
        private Vector3 m_RimLightDirWS = new Vector3(0.0f, 0.55f, 0.83f);
        private Vector3 m_RimLightColor = new Vector3(2.5f, 2.25f, 1.9f);
        private Vector3 m_AmbientColor = new Vector3(0.18f, 0.19f, 0.22f);

        public StrandHairCompositionPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StrandHairCompositionPass));
            m_ColorTexture = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            EnsureShaderLoaded();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_ComposeKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.StrandHairCompositionCompute;
            if (m_Compute == null)
                return false;

            m_ComposeKernel = m_Compute.FindKernel(ComposeKernelName);
            return m_ComposeKernel >= 0;
        }

        public override void Prepare(ContextContainer frameData)
        {
            PrepareInternal(frameData, null);
        }

        internal bool PrepareInternal(ContextContainer frameData, StrandHairGpuPool gpuPool)
        {
            m_FrameData = frameData.Contains<StrandHairFrameData>()
                ? frameData.Get<StrandHairFrameData>()
                : null;
            m_GpuPool = gpuPool;

            if (m_FrameData == null || !m_FrameData.HasVisibleHair || !EnsureShaderLoaded())
                return false;

            var cameraData = frameData.Get<VividCameraData>();
            if (cameraData == null || cameraData.camera == null)
                return false;

            m_CameraPosWS = cameraData.camera.transform.position;
            m_PreExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(
                frameData.Get<VividExposureData>());

            ResolvePreviewLights(frameData);
            return true;
        }

        private void ResolvePreviewLights(ContextContainer frameData)
        {
            // Defaults tuned for studio LookDev if no scene lights are gathered
            m_KeyLightDirWS = new Vector3(0.45f, 0.72f, -0.52f).normalized;
            m_KeyLightColor = new Vector3(1.8f, 1.72f, 1.62f);
            m_RimLightDirWS = new Vector3(0.0f, 0.55f, 0.83f).normalized;
            m_RimLightColor = new Vector3(2.0f, 1.82f, 1.55f);
            m_AmbientColor = new Vector3(0.14f, 0.15f, 0.17f);

            if (!frameData.Contains<VividLightData>())
                return;

            var lightData = frameData.Get<VividLightData>();
            if (lightData == null || !lightData.visibleLights.IsCreated || lightData.visibleLights.Length == 0)
                return;

            // Compute average centroid of visible hair models so point/spot lights point accurately
            Vector3 hairCenter = Vector3.zero;
            int visibleEntries = 0;
            for (int i = 0; i < m_FrameData.Allocations.Count; i++)
            {
                var alloc = m_FrameData.Allocations[i];
                if (!alloc.IsVisible) continue;
                hairCenter += m_FrameData.ActiveEntries[alloc.EntryIndex].WorldBounds.center;
                visibleEntries++;
            }
            if (visibleEntries > 0)
                hairCenter /= visibleEntries;

            Vector3 camForward = (hairCenter - m_CameraPosWS).sqrMagnitude > 1e-6f
                ? (hairCenter - m_CameraPosWS).normalized
                : Vector3.forward;

            float bestKeyScore = -1.0f;
            float bestRimScore = -1.0f;
            bool foundKey = false;
            bool foundRim = false;

            var visibleLights = lightData.visibleLights;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight vl = visibleLights[i];
                Color col = vl.finalColor;
                float lum = 0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b;
                if (lum <= 1e-4f)
                    continue;

                Vector3 toLight;
                float atten = 1.0f;
                if (vl.lightType == LightType.Directional)
                {
                    toLight = -((Vector3)vl.localToWorldMatrix.GetColumn(2)).normalized;
                }
                else
                {
                    Vector3 lightPos = vl.localToWorldMatrix.GetColumn(3);
                    Vector3 delta = lightPos - hairCenter;
                    float distSq = Mathf.Max(delta.sqrMagnitude, 0.09f);
                    float dist = Mathf.Sqrt(distSq);
                    toLight = delta / dist;
                    float rangeRatio = vl.range > 1e-3f ? Mathf.Clamp01(dist / vl.range) : 0.0f;
                    float smoothFade = Mathf.Clamp01(1.0f - rangeRatio * rangeRatio);
                    // Physical lumen -> irradiance attenuation: 1 / (4 * PI * d^2)
                    atten = (smoothFade * smoothFade) / (4.0f * Mathf.PI * distSq);
                    if (vl.lightType == LightType.Spot)
                    {
                        Vector3 spotForward = ((Vector3)vl.localToWorldMatrix.GetColumn(2)).normalized;
                        float cosAngle = Vector3.Dot(-toLight, spotForward);
                        float cosOuter = Mathf.Cos(Mathf.Clamp(vl.spotAngle, 1.0f, 179.0f) * 0.5f * Mathf.Deg2Rad);
                        float cosInner = Mathf.Lerp(cosOuter, 1.0f, 0.25f);
                        float spotAtten = Mathf.SmoothStep(cosOuter, cosInner, cosAngle);
                        atten *= spotAtten;
                    }
                }

                float effLum = lum * atten;
                // Rim lights lie behind the hair (dot(toLight, camForward) > 0)
                bool isBacklight = Vector3.Dot(toLight, camForward) > 0.15f;
                Vector3 rgb = new Vector3(col.r, col.g, col.b) * atten;

                if (isBacklight)
                {
                    if (effLum > bestRimScore)
                    {
                        bestRimScore = effLum;
                        m_RimLightDirWS = toLight;
                        m_RimLightColor = rgb;
                        foundRim = true;
                    }
                }
                else
                {
                    if (effLum > bestKeyScore)
                    {
                        bestKeyScore = effLum;
                        m_KeyLightDirWS = toLight;
                        m_KeyLightColor = rgb;
                        foundKey = true;
                    }
                }
            }

            if (!foundKey && foundRim)
            {
                m_KeyLightDirWS = m_RimLightDirWS;
                m_KeyLightColor = m_RimLightColor * 0.5f;
            }
            if (!foundRim && foundKey)
            {
                m_RimLightDirWS = -m_KeyLightDirWS;
                m_RimLightColor = m_KeyLightColor * 0.25f;
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ColorTexture == null || !m_ColorTexture.innerHandle.IsValid())
                return;

            RecordWithHandle(context.cmd, m_ColorTexture.innerHandle, m_GpuPool);
        }

        internal void RecordWithHandle(
            ComputeCommandBuffer cmd,
            TextureHandle colorHandle,
            StrandHairGpuPool gpuPool)
        {
            if (m_FrameData == null
                || !m_FrameData.HasVisibleHair
                || !m_FrameData.VisibilityRecordedThisFrame
                || m_FrameData.VisAtlasBuffer == null
                || !m_FrameData.VisAtlasBuffer.IsValid()
                || !colorHandle.IsValid()
                || gpuPool == null
                || !EnsureShaderLoaded())
            {
                return;
            }

            int downscale = Mathf.Max(1, m_FrameData.DownscaleFactor);
            float atlasWidth = Mathf.Max(1, (m_FrameData.ScreenWidth + downscale - 1) / downscale);
            float atlasHeight = Mathf.Max(1, (m_FrameData.ScreenHeight + downscale - 1) / downscale);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetComputeTextureParam(m_Compute, m_ComposeKernel, SceneColorId, colorHandle);
                cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, VisAtlasId, m_FrameData.VisAtlasBuffer);
                if (m_PreExposureBuffer != null)
                {
                    cmd.SetComputeBufferParam(
                        m_Compute,
                        m_ComposeKernel,
                        VividAutoExposureSystem.PreExposureBufferId,
                        m_PreExposureBuffer);
                }

                cmd.SetComputeIntParam(m_Compute, DownscaleFactorId, downscale);
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_FrameData.ScreenWidth, m_FrameData.ScreenHeight, 0f, 0f));
                cmd.SetComputeVectorParam(m_Compute, AtlasScreenSizeId, new Vector4(atlasWidth, atlasHeight, 0f, 0f));
                cmd.SetComputeFloatParam(m_Compute, ProjScaleYId, m_FrameData.ProjScaleY);
                cmd.SetComputeIntParam(m_Compute, DebugViewModeId, (int)StrandHairGpuPool.GlobalDebugViewMode);

                cmd.SetComputeVectorParam(m_Compute, CameraPosWSId, m_CameraPosWS);
                cmd.SetComputeVectorParam(m_Compute, KeyLightDirWSId, m_KeyLightDirWS);
                cmd.SetComputeVectorParam(m_Compute, KeyLightColorId, m_KeyLightColor);
                cmd.SetComputeVectorParam(m_Compute, RimLightDirWSId, m_RimLightDirWS);
                cmd.SetComputeVectorParam(m_Compute, RimLightColorId, m_RimLightColor);
                cmd.SetComputeVectorParam(m_Compute, AmbientColorId, m_AmbientColor);

                // Allocations are already sorted back-to-front by StrandHairAtlasAllocator
                int allocCount = m_FrameData.Allocations.Count;
                for (int slotIdx = 0; slotIdx < allocCount; slotIdx++)
                {
                    StrandHairModelAtlasAllocation alloc = m_FrameData.Allocations[slotIdx];
                    if (!alloc.IsVisible || alloc.TileCount <= 0 || alloc.ComposeSize.x <= 0 || alloc.ComposeSize.y <= 0)
                        continue;

                    HairGroomRenderEntry entry = m_FrameData.ActiveEntries[alloc.EntryIndex];
                    HairGroomGpuResource gpuRes = entry.Asset?.GetOrCreateGpuResource();
                    if (gpuRes == null || !gpuRes.IsValid)
                        continue;

                    StrandHairModelScratchSlot slot = gpuPool.GetOrCreateSlot(slotIdx, gpuRes.VertexCount, gpuRes.SegmentCount);

                    cmd.SetComputeMatrixParam(m_Compute, LocalToWorldId, entry.LocalToWorld);
                    cmd.SetComputeIntParam(m_Compute, FirstAtlasTileId, alloc.FirstAtlasTile);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        AtlasTileRectId,
                        new Vector4(alloc.TileOrigin.x, alloc.TileOrigin.y, alloc.TileGridSize.x, alloc.TileGridSize.y));
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        ComposeRectId,
                        new Vector4(alloc.ComposeMinPixel.x, alloc.ComposeMinPixel.y, alloc.ComposeSize.x, alloc.ComposeSize.y));

                    Color tint = ResolveHairTint(in entry.Shading);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        BaseColorId,
                        new Vector4(tint.r, tint.g, tint.b, entry.Shading.Melanin));

                    float cuticleSin = Mathf.Sin(entry.Shading.CuticleAngleDegrees * Mathf.Deg2Rad);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        RoughnessParamsId,
                        new Vector4(
                            entry.Shading.LongitudinalRoughness,
                            entry.Shading.AzimuthalRoughness,
                            cuticleSin,
                            entry.Shading.DualScatterGain));

                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, RestVerticesId, gpuRes.RestVertexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, SegmentIndicesId, gpuRes.SegmentIndexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, m_ComposeKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);

                    int gx = (alloc.ComposeSize.x + 7) / 8;
                    int gy = (alloc.ComposeSize.y + 7) / 8;
                    cmd.DispatchCompute(m_Compute, m_ComposeKernel, gx, gy, 1);
                }
            }
        }

        private static Color ResolveHairTint(in HairGroomShadingParameters shading)
        {
            if (shading.AbsorptionMode == HairGroomAbsorptionMode.Color)
                return shading.BaseColor.linear;

            // Physical melanin -> linear reflectance preview approximation (Chiang 2015 eu/pheo coefficients)
            float m = Mathf.Clamp01(shading.Melanin);
            float redness = Mathf.Clamp01(shading.MelaninRedness);
            float concentration = -Mathf.Log(Mathf.Max(1.0f - m * 0.96f, 1e-4f));
            float eu = concentration * (1.0f - redness);
            float pheo = concentration * redness;
            float sigmaR = eu * 0.419f + pheo * 0.187f;
            float sigmaG = eu * 0.697f + pheo * 0.400f;
            float sigmaB = eu * 1.370f + pheo * 1.050f;
            // Effective 2-pass absorption in hair cortex (~2.2x path length for TRT/multiple scatter)
            return new Color(
                Mathf.Exp(-sigmaR * 2.2f),
                Mathf.Exp(-sigmaG * 2.2f),
                Mathf.Exp(-sigmaB * 2.2f),
                1.0f);
        }

        public override void Dispose()
        {
            m_Compute = null;
            m_ComposeKernel = -1;
            m_FrameData = null;
            m_GpuPool = null;
            m_PreExposureBuffer = null;
        }
    }
}
