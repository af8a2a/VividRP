using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Phase 3: Zero-GC One-Pass Local Light Shadow Projection resolver and mask manager.
    // Owns the R32G32B32A32_UInt packed mask (4-bit x 32 local lights/pixel + blue-noise dither)
    // and bridges VSMShadowPass -> CSMShadowResolvePass -> DeferredLightingPass without graph asset edits.
    internal sealed class VirtualShadowMapLocalShadowResolver : IDisposable
    {
        internal const string ResolveLocalKernelName = "VSMResolveLocalShadows";
        // UE r.Shadow.Virtual.SMRT.MaxSlopeBiasLocal = 50, MaxRayAngleFromLight = 0.03 rad.
        internal const float DefaultMaxSlopeBiasLocal = 50.0f;
        internal const float DefaultMaxRayAngleFromLightRad = 0.03f;

        internal static readonly int LocalShadowMaskBitsRWId = Shader.PropertyToID("_VSMLocalShadowMaskBitsRW");
        internal static readonly int LocalShadowMaskBitsId = Shader.PropertyToID("_VSMLocalShadowMaskBits");
        internal static readonly int LocalShadowMaskEnabledId = Shader.PropertyToID("_VSMLocalShadowMaskEnabled");
        internal static readonly int LocalSMRTParametersId = Shader.PropertyToID("_VSMLocalSMRTParameters");
        internal static readonly int LocalSMRTSettingsId = Shader.PropertyToID("_VSMLocalSMRTSettings");

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int PhysicalPagePoolId = Shader.PropertyToID("_VSMPhysicalPagePool");
        private static readonly int PageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int PageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int PageTableEntryCountId = Shader.PropertyToID("_VSMPrototypePageTableEntryCount");
        private static readonly int OutputWidthId = Shader.PropertyToID("_CSMOutputWidth");
        private static readonly int OutputHeightId = Shader.PropertyToID("_CSMOutputHeight");
        private static readonly int InvViewProjMatrixId = Shader.PropertyToID("_CSMInvViewProjMatrix");
        private static readonly int FrameIndexId = Shader.PropertyToID("_CSMFrameIndex");
        private static readonly int ReceiverOffsetParametersId = Shader.PropertyToID("_VSMReceiverOffsetParameters");
        private static readonly int ReceiverViewForwardId = Shader.PropertyToID("_VSMReceiverViewForward");

        private static readonly ProfilingSampler ResolveLocalSampler = new("VSM.ResolveLocalShadows");

        private RTHandle m_LocalShadowMaskBits;
        private RTHandle m_FallbackMaskBits;
        private GraphicsBuffer m_FallbackPrunedBuffer;
        private int m_Width;
        private int m_Height;
        private TextureHandle m_ResolveMaskHandle;
        private TextureHandle m_ResolvePoolHandle;
        private TextureHandle m_DeferredMaskHandle;
        private BufferHandle m_DeferredPrunedOffsetsHandle;
        private BufferHandle m_DeferredPrunedIndicesHandle;
        private bool m_DeferredLocalMaskActive;

        internal bool Active { get; private set; }
        internal RTHandle LocalShadowMaskBits => m_LocalShadowMaskBits;
        internal int Width => m_Width;
        internal int Height => m_Height;

        internal void Disable()
        {
            Active = false;
            m_DeferredLocalMaskActive = false;
            m_ResolveMaskHandle = default;
            m_ResolvePoolHandle = default;
            m_DeferredMaskHandle = default;
            m_DeferredPrunedOffsetsHandle = default;
            m_DeferredPrunedIndicesHandle = default;
        }

        internal static Vector4 BuildLocalSMRTParameters(CascadedShadowSettingsVolume settings)
        {
            bool smrt = settings != null && settings.virtualShadowMapSMRT.value;
            int rayCount = smrt ? Mathf.Clamp(settings.virtualShadowMapSMRTRayCount.value, 1, 16) : 1;
            int samplesPerRay = smrt ? Mathf.Clamp(settings.virtualShadowMapSMRTSamplesPerRay.value, 0, 16) : 0;
            float cotMaxAngle = 1.0f / Mathf.Tan(DefaultMaxRayAngleFromLightRad);
            return new Vector4(rayCount, samplesPerRay, DefaultMaxSlopeBiasLocal, cotMaxAngle);
        }

        internal static Vector4 BuildLocalSMRTSettings(CascadedShadowSettingsVolume settings, bool useSTBN)
        {
            if (settings == null || !settings.virtualShadowMapSMRT.value)
                return new Vector4(0.0f, 0.0f, 0.0f, useSTBN ? 1.0f : 0.0f);

            float extrapolate = Mathf.Max(0.0f, settings.virtualShadowMapSMRTExtrapolateMaxSlope.value);
            float ditherScale = Mathf.Max(0.0f, settings.virtualShadowMapSMRTTexelDitherScale.value);
            float adaptiveRayCount = settings.virtualShadowMapSMRTAdaptiveRays.value
                ? Mathf.Max(0, settings.virtualShadowMapSMRTAdaptiveRayCount.value)
                : 0.0f;
            return new Vector4(extrapolate, ditherScale, adaptiveRayCount, useSTBN ? 1.0f : 0.0f);
        }

        internal bool PrepareForResolvePass(
            IRenderPass pass,
            int width,
            int height,
            VirtualShadowMapLocalLightGrid localGrid,
            VirtualShadowMapLocalCasterPipeline localPipeline)
        {
            Active = false;
            m_ResolveMaskHandle = default;
            m_ResolvePoolHandle = default;

            if (width <= 0 || height <= 0
                || localGrid == null || !localGrid.Active
                || localPipeline == null || !localPipeline.Active
                || localPipeline.PhysicalPagePool?.rt == null
                || localPipeline.PageTable == null || !localPipeline.PageTable.IsValid()
                || localGrid.UnifiedProjections == null || !localGrid.UnifiedProjections.IsValid()
                || localGrid.PrunedLightOffsetCount == null || !localGrid.PrunedLightOffsetCount.IsValid()
                || localGrid.PrunedLightIndices == null || !localGrid.PrunedLightIndices.IsValid()
                || localGrid.PunctualLightVSMInfo == null || !localGrid.PunctualLightVSMInfo.IsValid()
                || localGrid.SourcePunctualLights == null || !localGrid.SourcePunctualLights.IsValid()
                || localGrid.SourceLogBaseBuffer == null || !localGrid.SourceLogBaseBuffer.IsValid()
                || !IsMaskFormatSupported())
            {
                return false;
            }

            EnsureMaskTexture(width, height);
            if (m_LocalShadowMaskBits?.rt == null)
                return false;

            if (pass != null)
            {
                m_ResolveMaskHandle = PassRecorder.ImportTextureForPass(pass, m_LocalShadowMaskBits, AccessFlags.Write);
                m_ResolvePoolHandle = PassRecorder.ImportTextureForPass(pass, localPipeline.PhysicalPagePool, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localPipeline.PageTable, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.UnifiedProjections, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.PrunedLightOffsetCount, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.PrunedLightIndices, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.PunctualLightVSMInfo, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.SourcePunctualLights, AccessFlags.Read);
                PassRecorder.ImportBufferForPass(pass, localGrid.SourceLogBaseBuffer, AccessFlags.Read);
            }

            Active = true;
            return true;
        }

        internal void RecordResolve(
            ComputeCommandBuffer cmd,
            ComputeShader shader,
            int resolveLocalKernel,
            VirtualShadowMapLocalLightGrid localGrid,
            VirtualShadowMapLocalCasterPipeline localPipeline,
            TextureHandle depthTexture,
            TextureHandle gbuffer1,
            Matrix4x4 invViewProjection,
            int frameIndex,
            Vector4 receiverOffsetParameters,
            Vector4 receiverViewForward,
            CascadedShadowSettingsVolume settings)
        {
            if (!Active || shader == null || resolveLocalKernel < 0
                || localGrid == null || !localGrid.Active
                || localPipeline == null || !localPipeline.Active
                || !m_ResolveMaskHandle.IsValid()
                || !m_ResolvePoolHandle.IsValid())
            {
                return;
            }

            bool hasBlueNoise = BlueNoise.Instance != null;
            using (new ProfilingScope(cmd, ResolveLocalSampler))
            {
                if (hasBlueNoise)
                    BlueNoise.Instance.Bind(cmd, shader, resolveLocalKernel);

                cmd.SetComputeTextureParam(shader, resolveLocalKernel, DepthTextureId, depthTexture);
                cmd.SetComputeTextureParam(shader, resolveLocalKernel, GBuffer1Id, gbuffer1);
                cmd.SetComputeTextureParam(shader, resolveLocalKernel, PhysicalPagePoolId, m_ResolvePoolHandle);
                cmd.SetComputeTextureParam(shader, resolveLocalKernel, LocalShadowMaskBitsRWId, m_ResolveMaskHandle);

                cmd.SetComputeBufferParam(shader, resolveLocalKernel, PageTableId, localPipeline.PageTable);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapLocalLightGrid.PrunedLightOffsetCountId, localGrid.PrunedLightOffsetCount);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapLocalLightGrid.PrunedLightIndicesId, localGrid.PrunedLightIndices);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapLocalLightGrid.PunctualLightVSMInfoId, localGrid.PunctualLightVSMInfo);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapLocalLightGrid.PunctualLightsId, localGrid.SourcePunctualLights);
                cmd.SetComputeBufferParam(shader, resolveLocalKernel, VirtualShadowMapLocalLightGrid.LogBaseBufferId, localGrid.SourceLogBaseBuffer);

                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, localGrid.UnifiedProjectionCount);
                cmd.SetComputeIntParam(shader, PageTableEntryCountId, localGrid.UnifiedPageTableEntryCount);
                cmd.SetComputeIntParam(shader, PageSizeId, VirtualShadowMapPrototypeRuntime.PageSize);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.ClusterCountId, localGrid.ClusterCount);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.PunctualLightCountId, localGrid.PunctualLightCount);
                cmd.SetComputeIntParam(shader, OutputWidthId, m_Width);
                cmd.SetComputeIntParam(shader, OutputHeightId, m_Height);
                cmd.SetComputeIntParam(shader, FrameIndexId, frameIndex);

                cmd.SetComputeMatrixParam(shader, InvViewProjMatrixId, invViewProjection);
                cmd.SetComputeMatrixParam(shader, VirtualShadowMapLocalLightGrid.CameraViewMatrixId, localGrid.CameraViewMatrix);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.CameraViewParamsId, localGrid.CameraViewParams);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterParamsId, localGrid.ClusterParams);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterDepthParamsId, localGrid.ClusterDepthParams);
                cmd.SetComputeVectorParam(shader, ReceiverOffsetParametersId, receiverOffsetParameters);
                cmd.SetComputeVectorParam(shader, ReceiverViewForwardId, receiverViewForward);
                cmd.SetComputeVectorParam(shader, LocalSMRTParametersId, BuildLocalSMRTParameters(settings));
                cmd.SetComputeVectorParam(shader, LocalSMRTSettingsId, BuildLocalSMRTSettings(settings, hasBlueNoise));

                int gx = CoreUtils.DivRoundUp(m_Width, 8);
                int gy = CoreUtils.DivRoundUp(m_Height, 8);
                cmd.DispatchCompute(shader, resolveLocalKernel, gx, gy, 1);
            }
        }

        internal void PrepareForDeferredPass(IRenderPass pass, VirtualShadowMapLocalLightGrid localGrid)
        {
            m_DeferredLocalMaskActive = false;
            m_DeferredMaskHandle = default;
            m_DeferredPrunedOffsetsHandle = default;
            m_DeferredPrunedIndicesHandle = default;

            EnsureFallbackResources();
            if (pass == null)
                return;

            bool active = Active
                && m_LocalShadowMaskBits?.rt != null
                && localGrid != null && localGrid.Active
                && localGrid.PrunedLightOffsetCount != null && localGrid.PrunedLightOffsetCount.IsValid()
                && localGrid.PrunedLightIndices != null && localGrid.PrunedLightIndices.IsValid();

            if (active)
            {
                m_DeferredMaskHandle = PassRecorder.ImportTextureForPass(pass, m_LocalShadowMaskBits, AccessFlags.Read);
                m_DeferredPrunedOffsetsHandle = PassRecorder.ImportBufferForPass(pass, localGrid.PrunedLightOffsetCount, AccessFlags.Read);
                m_DeferredPrunedIndicesHandle = PassRecorder.ImportBufferForPass(pass, localGrid.PrunedLightIndices, AccessFlags.Read);
                m_DeferredLocalMaskActive = m_DeferredMaskHandle.IsValid()
                    && m_DeferredPrunedOffsetsHandle.IsValid()
                    && m_DeferredPrunedIndicesHandle.IsValid();
                return;
            }

            if (m_FallbackMaskBits != null)
                m_DeferredMaskHandle = PassRecorder.ImportTextureForPass(pass, m_FallbackMaskBits, AccessFlags.Read);
            if (m_FallbackPrunedBuffer != null && m_FallbackPrunedBuffer.IsValid())
            {
                m_DeferredPrunedOffsetsHandle = PassRecorder.ImportBufferForPass(pass, m_FallbackPrunedBuffer, AccessFlags.Read);
                m_DeferredPrunedIndicesHandle = m_DeferredPrunedOffsetsHandle;
            }
        }

        internal void BindToDeferredPass(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            if (shader == null || kernel < 0)
                return;

            bool enabled = m_DeferredLocalMaskActive
                && m_DeferredMaskHandle.IsValid()
                && m_DeferredPrunedOffsetsHandle.IsValid()
                && m_DeferredPrunedIndicesHandle.IsValid();

            cmd.SetComputeIntParam(shader, LocalShadowMaskEnabledId, enabled ? 1 : 0);
            if (m_DeferredMaskHandle.IsValid())
                cmd.SetComputeTextureParam(shader, kernel, LocalShadowMaskBitsId, m_DeferredMaskHandle);
            if (m_DeferredPrunedOffsetsHandle.IsValid())
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightOffsetCountId, m_DeferredPrunedOffsetsHandle);
            if (m_DeferredPrunedIndicesHandle.IsValid())
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightIndicesId, m_DeferredPrunedIndicesHandle);
        }

        internal void EnsureMaskTexture(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (m_LocalShadowMaskBits?.rt != null && m_Width == width && m_Height == height)
                return;

            m_LocalShadowMaskBits?.Release();
            m_LocalShadowMaskBits = RTHandles.Alloc(
                width,
                height,
                slices: 1,
                depthBufferBits: DepthBits.None,
                colorFormat: GraphicsFormat.R32G32B32A32_UInt,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                dimension: TextureDimension.Tex2D,
                enableRandomWrite: true,
                useMipMap: false,
                autoGenerateMips: false,
                isShadowMap: false,
                anisoLevel: 1,
                mipMapBias: 0.0f,
                msaaSamples: MSAASamples.None,
                bindTextureMS: false,
                useDynamicScale: false,
                useDynamicScaleExplicit: false,
                name: "VSMLocalShadowMaskBits");
            m_Width = width;
            m_Height = height;
        }

        private void EnsureFallbackResources()
        {
            if ((m_FallbackMaskBits == null || m_FallbackMaskBits.rt == null) && IsMaskFormatSupported())
            {
                m_FallbackMaskBits?.Release();
                // Integer UInt formats cannot be created via Texture2D (Unity validates GraphicsFormatUsage.Sample).
                // Allocate a 1x1 RenderTexture RTHandle with LoadStore/Render usage instead.
                m_FallbackMaskBits = RTHandles.Alloc(
                    1,
                    1,
                    slices: 1,
                    depthBufferBits: DepthBits.None,
                    colorFormat: GraphicsFormat.R32G32B32A32_UInt,
                    filterMode: FilterMode.Point,
                    wrapMode: TextureWrapMode.Clamp,
                    dimension: TextureDimension.Tex2D,
                    enableRandomWrite: true,
                    useMipMap: false,
                    autoGenerateMips: false,
                    isShadowMap: false,
                    anisoLevel: 1,
                    mipMapBias: 0.0f,
                    msaaSamples: MSAASamples.None,
                    bindTextureMS: false,
                    useDynamicScale: false,
                    useDynamicScaleExplicit: false,
                    name: "VSMLocalShadowMaskBitsFallback");
            }

            if (m_FallbackPrunedBuffer == null || !m_FallbackPrunedBuffer.IsValid())
            {
                m_FallbackPrunedBuffer?.Dispose();
                m_FallbackPrunedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint))
                {
                    name = "VSMPrunedLightFallback",
                };
                m_FallbackPrunedBuffer.SetData(new uint[] { 0u });
            }
        }

        internal static bool IsMaskFormatSupported()
        {
            return SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_UInt, GraphicsFormatUsage.Render)
                && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_UInt, GraphicsFormatUsage.LoadStore);
        }

        public void Dispose()
        {
            Disable();
            m_LocalShadowMaskBits?.Release();
            m_LocalShadowMaskBits = null;
            m_FallbackMaskBits?.Release();
            m_FallbackMaskBits = null;
            m_FallbackPrunedBuffer?.Dispose();
            m_FallbackPrunedBuffer = null;
            m_Width = 0;
            m_Height = 0;
        }
    }
}
