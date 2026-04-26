using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    public sealed class SIGMAShadowDenoisePass : UnsafePass
    {
        // --- shader property IDs ---
        private static readonly int gIn_ViewZ                 = Shader.PropertyToID("gIn_ViewZ");
        private static readonly int gIn_Penumbra              = Shader.PropertyToID("gIn_Penumbra");
        private static readonly int gIn_Tiles                 = Shader.PropertyToID("gIn_Tiles");
        private static readonly int gOut_Tiles                = Shader.PropertyToID("gOut_Tiles");
        private static readonly int gIn_Normal_Roughness      = Shader.PropertyToID("gIn_Normal_Roughness");
        private static readonly int gOut_Penumbra             = Shader.PropertyToID("gOut_Penumbra");
        private static readonly int gIn_Shadow_Translucency   = Shader.PropertyToID("gIn_Shadow_Translucency");
        private static readonly int gOut_Shadow_Translucency  = Shader.PropertyToID("gOut_Shadow_Translucency");
        private static readonly int gIn_Mv                    = Shader.PropertyToID("gIn_Mv");
        private static readonly int gIn_History               = Shader.PropertyToID("gIn_History");
        private static readonly int gIn_HistoryLength         = Shader.PropertyToID("gIn_HistoryLength");
        private static readonly int gOut_History              = Shader.PropertyToID("gOut_History");
        private static readonly int gOut_HistoryLength        = Shader.PropertyToID("gOut_HistoryLength");

        private static readonly int SIGMA_ClassifyTilesConstantsId       = Shader.PropertyToID("SIGMA_ClassifyTilesConstants");
        private static readonly int SIGMA_SmoothTilesConstantsId         = Shader.PropertyToID("SIGMA_SmoothTilesConstants");
        private static readonly int SIGMA_BlurConstantsId                = Shader.PropertyToID("SIGMA_BlurConstants");
        private static readonly int SIGMA_TemporalStabilizationConstantsId = Shader.PropertyToID("SIGMA_TemporalStabilizationConstants");
        private static readonly int SIGMA_CopyConstantsId                = Shader.PropertyToID("SIGMA_CopyConstants");

        // --- external inputs ---
        [RenderGraphResource(Name = "RawShadow",     Access = AccessFlags.Read)]
        private RenderGraphTexture m_RawShadowTexture;

        [RenderGraphResource(Name = "LinearDepth",   Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1",      Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectorTexture;

        // --- history (code-managed, hidden from graph editor) ---

        // Previous-frame read inputs (imported RTHandle by AllocHistoryTextureForPass)
        [RenderGraphResource(Name = "HistoryShadow",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_HistoryShadowTexture;

        [RenderGraphResource(Name = "HistoryLength",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_HistoryLengthTexture;

        // Current-frame write outputs (persistent history buffers imported when requested)
        [RenderGraphResource(Name = "SIGMA_HistoryShadowCurrent",
            Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_HistoryShadowCurrent;

        [RenderGraphResource(Name = "SIGMA_HistoryLengthCurrent",
            Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_HistoryLengthCurrent;

        // --- external outputs ---
        [RenderGraphResource(Name = "DenoisedShadow", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DenoisedShadowTexture;

        // --- pass-owned transients (hidden from graph editor) ---

        // Tile classification output  (float4, tile resolution)
        [RenderGraphResource(Name = "SIGMA_Tiles",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TileTexture;

        // Smoothed tile output  (float2, tile resolution)
        [RenderGraphResource(Name = "SIGMA_SmoothTiles",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_SmoothTileTexture;

        // Intermediate penumbra after pre-blur  (R16_SFloat, full resolution)
        [RenderGraphResource(Name = "SIGMA_Penumbra",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientPenumbra;

        // Intermediate shadow after pre-blur  (R8_UNorm, full resolution)
        [RenderGraphResource(Name = "SIGMA_Shadow",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientShadow;

        // Intermediate penumbra after post-blur  (R16_SFloat, full resolution)
        [RenderGraphResource(Name = "SIGMA_Penumbra2",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientPenumbra2;

        // Intermediate shadow after post-blur  (R8_UNorm, full resolution)
        [RenderGraphResource(Name = "SIGMA_Shadow2",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientShadow2;

        // Copy of history shadow for this frame  (R8_UNorm, full resolution)
        [RenderGraphResource(Name = "SIGMA_TransientHistory",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientHistory;

        // Copy of history length for this frame  (R32_UInt, full resolution)
        [RenderGraphResource(Name = "SIGMA_TransientHistoryLength",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_TransientHistoryLength;
        
        


        // --- compute shaders ---
        private ComputeShader m_ClassifyTiles;
        private ComputeShader m_SmoothTiles;
        private ComputeShader m_ShadowCopy;
        private ComputeShader m_ShadowBlur;
        private ComputeShader m_ShadowPostBlur;
        private ComputeShader m_ShadowTemporalStabilization;

        // --- per-frame state ---
        private SigmaSharedConstants m_Constants;
        private int m_Width;
        private int m_Height;
        private int m_PrevWidth;
        private int m_PrevHeight;
        private Vector3    m_PrevCameraPosition;
        private Matrix4x4  m_PrevWorldToView = Matrix4x4.identity;
        private Matrix4x4  m_PrevViewToClip  = Matrix4x4.identity;
        private bool m_HasValidHistory;
        private bool m_HistoryInvalidated;
        private bool m_UseRawShadowPassthrough;
        private uint m_MaxStabilizedFrameNum;

        internal readonly struct ResolvedSigmaSettings
        {
            public ResolvedSigmaSettings(
                float denoisingRange,
                float planeDistanceSensitivity,
                uint maxStabilizedFrameNum)
            {
                DenoisingRange = denoisingRange;
                PlaneDistanceSensitivity = planeDistanceSensitivity;
                MaxStabilizedFrameNum = maxStabilizedFrameNum;
            }

            public float DenoisingRange { get; }

            public float PlaneDistanceSensitivity { get; }

            public uint MaxStabilizedFrameNum { get; }

            public float StabilizationStrength => MaxStabilizedFrameNum / (1.0f + MaxStabilizedFrameNum);
        }

        public SIGMAShadowDenoisePass()
        {
            profilingSampler = new ProfilingSampler(nameof(SIGMAShadowDenoisePass));

            int tileW = 1, tileH = 1; // resized in Prepare

            m_RawShadowTexture     = RenderGraphTexture.CreateInput("RawShadow",     GraphicsFormat.R16_SFloat);
            m_DepthTexture         = RenderGraphTexture.CreateInput("LinearDepth",   GraphicsFormat.R32_SFloat);
            m_GBuffer1             = RenderGraphTexture.CreateInput("GBuffer1",      GraphicsFormat.A2B10G10R10_UNormPack32);
            m_MotionVectorTexture  = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_HistoryShadowTexture  = RenderGraphTexture.CreateInput("HistoryShadow", GraphicsFormat.R8_UNorm);
            m_HistoryLengthTexture  = RenderGraphTexture.CreateInput("HistoryLength", GraphicsFormat.R32_UInt);
            m_HistoryShadowCurrent  = CreatePassOwnedTexture("SIGMA_HistoryShadowCurrent", 1, 1, GraphicsFormat.R8_UNorm);
            m_HistoryLengthCurrent  = CreatePassOwnedTexture("SIGMA_HistoryLengthCurrent", 1, 1, GraphicsFormat.R32_UInt);

            m_DenoisedShadowTexture = CreatePassOwnedTexture("DenoisedShadow",           1, 1, GraphicsFormat.R16_SFloat);

            m_TileTexture             = CreatePassOwnedTexture("SIGMA_Tiles",                  tileW, tileH, GraphicsFormat.R8G8B8A8_UNorm);
            m_SmoothTileTexture       = CreatePassOwnedTexture("SIGMA_SmoothTiles",            tileW, tileH, GraphicsFormat.R8G8B8A8_UNorm);
            m_TransientPenumbra       = CreatePassOwnedTexture("SIGMA_Penumbra",               1, 1, GraphicsFormat.R16_SFloat);
            m_TransientShadow         = CreatePassOwnedTexture("SIGMA_Shadow",                 1, 1, GraphicsFormat.R8_UNorm);
            m_TransientPenumbra2      = CreatePassOwnedTexture("SIGMA_Penumbra2",              1, 1, GraphicsFormat.R16_SFloat);
            m_TransientShadow2        = CreatePassOwnedTexture("SIGMA_Shadow2",                1, 1, GraphicsFormat.R8_UNorm);
            m_TransientHistory        = CreatePassOwnedTexture("SIGMA_TransientHistory",       1, 1, GraphicsFormat.R8_UNorm, clearBuffer: true);
            m_TransientHistoryLength  = CreatePassOwnedTexture("SIGMA_TransientHistoryLength", 1, 1, GraphicsFormat.R32_UInt, clearBuffer: true);

            ConfigurePassOwnedClear(m_DenoisedShadowTexture, Color.white);
            ConfigurePassOwnedClear(m_TransientPenumbra, Color.clear);
            ConfigurePassOwnedClear(m_TransientShadow, Color.white);
            ConfigurePassOwnedClear(m_TransientPenumbra2, Color.clear);
            ConfigurePassOwnedClear(m_TransientShadow2, Color.white);
            ConfigurePassOwnedClear(m_TransientHistory, Color.white);
            ConfigurePassOwnedClear(m_TransientHistoryLength, Color.clear);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null) return;

            m_ClassifyTiles             = resources.SIGMAClassifyTilesCompute;
            m_SmoothTiles               = resources.SIGMASmoothTilesCompute;
            m_ShadowCopy                = resources.SIGMAShadowCopyCompute;
            m_ShadowBlur                = resources.SIGMAShadowPreBlurCompute;
            m_ShadowPostBlur            = resources.SIGMAShadowPostBlurCompute;
            m_ShadowTemporalStabilization = resources.SIGMATemporalStabilizationCompute;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width  = cameraData.actualWidth;
            m_Height = cameraData.actualHeight;

            int tileW = CoreUtils.DivRoundUp(m_Width,  16);
            int tileH = CoreUtils.DivRoundUp(m_Height, 16);

            // Resize all pass-owned transient descriptors
            ResizePassOwned(m_TileTexture,            tileW, tileH);
            ResizePassOwned(m_SmoothTileTexture,      tileW, tileH);
            ResizePassOwned(m_TransientPenumbra,      m_Width, m_Height);
            ResizePassOwned(m_TransientShadow,        m_Width, m_Height);
            ResizePassOwned(m_TransientPenumbra2,     m_Width, m_Height);
            ResizePassOwned(m_TransientShadow2,       m_Width, m_Height);
            ResizePassOwned(m_TransientHistory,       m_Width, m_Height);
            ResizePassOwned(m_TransientHistoryLength, m_Width, m_Height);
            ResizePassOwned(m_DenoisedShadowTexture,  m_Width, m_Height);
            ResizePassOwned(m_HistoryShadowCurrent,   m_Width, m_Height);
            ResizePassOwned(m_HistoryLengthCurrent,   m_Width, m_Height);

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            m_UseRawShadowPassthrough = shadowData.isCSMActive;

            // Light direction
            var lightData = frameData.GetOrCreate<VividLightData>();
            var lightDir  = Vector3.up;
            if (lightData != null && lightData.hasMainDirectionalLight)
                lightDir = lightData.mainDirectionalLight.directionWS.normalized;

            var camera     = cameraData.camera;
            var worldToView = cameraData.GetViewMatrix();
            var viewToClip  = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
            var cameraPos4  = cameraData.GetInverseViewMatrix().GetColumn(3);
            var cameraPos   = new Vector3(cameraPos4.x, cameraPos4.y, cameraPos4.z);
            var sigmaSettings = ResolveSettings(VividVolumeManagerUtility.GetRayTracingSettingsVolume());
            m_MaxStabilizedFrameNum = sigmaSettings.MaxStabilizedFrameNum;
            bool useTemporalStabilization = !m_UseRawShadowPassthrough && m_MaxStabilizedFrameNum > 0;

            if (m_UseRawShadowPassthrough)
            {
                m_HasValidHistory = false;
                m_MaxStabilizedFrameNum = 0;
                m_HistoryInvalidated = true;
                return;
            }

            // History allocation (code-managed)
            var shadowDesc = m_HistoryShadowTexture?.desc ?? m_DenoisedShadowTexture?.desc;
            var lengthDesc = m_HistoryLengthTexture?.desc;

            if (shadowDesc != null) shadowDesc = shadowDesc.Clone();
            if (shadowDesc != null) { shadowDesc.Width = m_Width; shadowDesc.Height = m_Height; shadowDesc.EnableRandomWrite = true; }
            if (lengthDesc != null) lengthDesc = lengthDesc.Clone();
            if (lengthDesc != null) { lengthDesc.Width = m_Width; lengthDesc.Height = m_Height; lengthDesc.EnableRandomWrite = true; }

            bool hasShadowHistory = PassRecorder.AllocHistoryTextureForPass(
                this, "HistoryShadow",
                previous: m_HistoryShadowTexture,
                current:  useTemporalStabilization ? m_HistoryShadowCurrent : null,
                desc:     shadowDesc);

            bool hasLengthHistory = PassRecorder.AllocHistoryTextureForPass(
                this, "HistoryLength",
                previous: m_HistoryLengthTexture,
                current:  useTemporalStabilization ? m_HistoryLengthCurrent : null,
                desc:     lengthDesc);

            var hadHistoryInvalidation = m_HistoryInvalidated;
            m_HasValidHistory = useTemporalStabilization
                && !hadHistoryInvalidation
                && hasShadowHistory
                && hasLengthHistory;

            Matrix4x4 previousWorldToView = m_HasValidHistory ? m_PrevWorldToView : worldToView;
            Matrix4x4 previousViewToClip = m_HasValidHistory ? m_PrevViewToClip : viewToClip;
            Vector3 previousCameraPosition = m_HasValidHistory ? m_PrevCameraPosition : cameraPos;
            int previousWidth = m_HasValidHistory && m_PrevWidth > 0 ? m_PrevWidth : m_Width;
            int previousHeight = m_HasValidHistory && m_PrevHeight > 0 ? m_PrevHeight : m_Height;
            uint frameIndex = (uint)Time.frameCount;
            float stabilizationStrength = m_HasValidHistory ? sigmaSettings.StabilizationStrength : 0.0f;

            m_Constants = SigmaSharedConstants.Compute(
                worldToView, viewToClip,
                previousWorldToView, previousViewToClip,
                cameraPos, previousCameraPosition,
                lightDir,
                m_Width, m_Height,
                previousWidth,
                previousHeight,
                frameIndex,
                sigmaSettings.DenoisingRange,
                sigmaSettings.PlaneDistanceSensitivity,
                stabilizationStrength,
                camera != null && camera.orthographic);

            // Store for next frame
            m_PrevWorldToView    = worldToView;
            m_PrevViewToClip     = viewToClip;
            m_PrevCameraPosition = cameraPos;
            m_PrevWidth          = m_Width;
            m_PrevHeight         = m_Height;
            m_HistoryInvalidated = !useTemporalStabilization;
        }

        public override void Record(UnsafePassContext context)
        {
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                if (m_UseRawShadowPassthrough)
                {
                    if (m_RawShadowTexture?.innerHandle.IsValid() == true
                        && m_DenoisedShadowTexture?.innerHandle.IsValid() == true)
                    {
                        nativeCmd.CopyTexture(m_RawShadowTexture, m_DenoisedShadowTexture);
                    }

                    return;
                }

                if (!CanExecute())
                    return;

                int kernel  = 0;
                int tileW   = CoreUtils.DivRoundUp(m_Width,  16);
                int tileH   = CoreUtils.DivRoundUp(m_Height, 16);
                int dispatchX = CoreUtils.DivRoundUp(m_Width,  8);
                int dispatchY = CoreUtils.DivRoundUp(m_Height, 16);
                bool useTemporalStabilization = !m_UseRawShadowPassthrough && m_MaxStabilizedFrameNum > 0;

                if (useTemporalStabilization)
                {
                    ClearTexture(nativeCmd, m_HistoryShadowCurrent, Color.white);
                    ClearTexture(nativeCmd, m_HistoryLengthCurrent, Color.clear);
                }

                // Stage 1: ClassifyTiles
                ConstantBuffer.Push(nativeCmd, m_Constants, m_ClassifyTiles, SIGMA_ClassifyTilesConstantsId);
                nativeCmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gIn_ViewZ,    m_DepthTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gIn_Penumbra, m_RawShadowTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gOut_Tiles,   m_TileTexture.innerHandle);
                nativeCmd.DispatchCompute(m_ClassifyTiles, kernel, tileW, tileH, 1);

                // Stage 2: SmoothTiles
                int smoothTileW = CoreUtils.DivRoundUp(tileW, 16);
                int smoothTileH = CoreUtils.DivRoundUp(tileH, 16);
                ConstantBuffer.Push(nativeCmd, m_Constants, m_SmoothTiles, SIGMA_SmoothTilesConstantsId);
                nativeCmd.SetComputeTextureParam(m_SmoothTiles, kernel, gIn_Tiles,  m_TileTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_SmoothTiles, kernel, gOut_Tiles, m_SmoothTileTexture.innerHandle);
                nativeCmd.DispatchCompute(m_SmoothTiles, kernel, smoothTileW, smoothTileH, 1);

                // Stage 3: ShadowCopy (history → transient)
                if (m_HasValidHistory)
                {
                    ConstantBuffer.Push(nativeCmd, m_Constants, m_ShadowCopy, SIGMA_CopyConstantsId);
                    nativeCmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_Tiles,        m_SmoothTileTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_History,      m_HistoryShadowTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_HistoryLength, m_HistoryLengthTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowCopy, kernel, gOut_History,      m_TransientHistory.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowCopy, kernel, gOut_HistoryLength, m_TransientHistoryLength.innerHandle);
                    nativeCmd.DispatchCompute(m_ShadowCopy, kernel, dispatchX, dispatchY, 1);
                }

                // Stage 4: PreBlur
                ConstantBuffer.Push(nativeCmd, m_Constants, m_ShadowBlur, SIGMA_BlurConstantsId);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_ViewZ,             m_DepthTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Normal_Roughness,  m_GBuffer1.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Tiles,             m_SmoothTileTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Penumbra,          m_RawShadowTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gOut_Penumbra,         m_TransientPenumbra.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowBlur, kernel, gOut_Shadow_Translucency, m_TransientShadow.innerHandle);
                nativeCmd.DispatchCompute(m_ShadowBlur, kernel, dispatchX, dispatchY, 1);

                // Stage 5: PostBlur
                ConstantBuffer.Push(nativeCmd, m_Constants, m_ShadowPostBlur, SIGMA_BlurConstantsId);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_ViewZ,            m_DepthTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Normal_Roughness, m_GBuffer1.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Tiles,            m_SmoothTileTexture.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Penumbra,         m_TransientPenumbra.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Shadow_Translucency, m_TransientShadow.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gOut_Penumbra,        m_TransientPenumbra2.innerHandle);
                nativeCmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gOut_Shadow_Translucency,
                    useTemporalStabilization ? m_TransientShadow2.innerHandle : m_DenoisedShadowTexture.innerHandle);
                nativeCmd.DispatchCompute(m_ShadowPostBlur, kernel, dispatchX, dispatchY, 1);

                // Stage 6: TemporalStabilization
                if (useTemporalStabilization)
                {
                    ConstantBuffer.Push(nativeCmd, m_Constants, m_ShadowTemporalStabilization, SIGMA_TemporalStabilizationConstantsId);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_ViewZ,             m_DepthTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Mv,                m_MotionVectorTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Penumbra,          m_TransientPenumbra2.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Shadow_Translucency, m_TransientShadow2.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_History,           m_TransientHistory.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_HistoryLength,     m_TransientHistoryLength.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Tiles,             m_SmoothTileTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_Shadow_Translucency, m_DenoisedShadowTexture.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_HistoryLength,    m_HistoryLengthCurrent.innerHandle);
                    nativeCmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_History,          m_HistoryShadowCurrent.innerHandle);
                    nativeCmd.DispatchCompute(m_ShadowTemporalStabilization, kernel, dispatchX, dispatchY, 1);
                }
            }
        }

        public override void Dispose()
        {
            m_HasValidHistory = false;
            m_HistoryInvalidated = false;
            m_UseRawShadowPassthrough = false;
            m_MaxStabilizedFrameNum = 0;
        }

        internal static ResolvedSigmaSettings ResolveSettings(RayTracingSettingsVolume volume)
        {
            float denoisingRange = RayTracingSettingsVolume.DefaultSigmaDenoisingRange;
            float planeDistanceSensitivity = RayTracingSettingsVolume.DefaultSigmaPlaneDistanceSensitivity;
            uint maxStabilizedFrameNum = (uint)RayTracingSettingsVolume.DefaultSigmaMaxStabilizedFrameNum;

            if (volume != null && volume.active)
            {
                if (volume.sigmaDenoisingRange != null && volume.sigmaDenoisingRange.overrideState)
                    denoisingRange = Mathf.Max(0.0f, volume.sigmaDenoisingRange.value);

                if (volume.sigmaPlaneDistanceSensitivity != null && volume.sigmaPlaneDistanceSensitivity.overrideState)
                    planeDistanceSensitivity = Mathf.Clamp01(volume.sigmaPlaneDistanceSensitivity.value);

                if (volume.sigmaMaxStabilizedFrameNum != null && volume.sigmaMaxStabilizedFrameNum.overrideState)
                {
                    maxStabilizedFrameNum = (uint)Mathf.Clamp(
                        volume.sigmaMaxStabilizedFrameNum.value,
                        0,
                        RayTracingSettingsVolume.MaxSigmaStabilizedFrameNum);
                }
            }

            return new ResolvedSigmaSettings(
                denoisingRange,
                planeDistanceSensitivity,
                maxStabilizedFrameNum);
        }

        private bool CanExecute()
        {
            return m_ClassifyTiles != null
                && m_SmoothTiles != null
                && m_ShadowCopy != null
                && m_ShadowBlur != null
                && m_ShadowPostBlur != null
                && m_ShadowTemporalStabilization != null
                && m_RawShadowTexture?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_DenoisedShadowTexture?.innerHandle.IsValid() == true;
        }

        private static void ConfigurePassOwnedClear(RenderGraphTexture tex, Color clearColor)
        {
            if (tex?.desc == null)
                return;

            tex.desc.ClearBuffer = true;
            tex.desc.ClearColor = clearColor;
        }

        private static void ClearTexture(CommandBuffer cmd, RenderGraphTexture texture, Color clearColor)
        {
            if (cmd == null || texture?.innerHandle.IsValid() != true)
                return;

            cmd.SetRenderTarget(texture);
            cmd.ClearRenderTarget(false, true, clearColor);
        }

        private static void ResizePassOwned(RenderGraphTexture tex, int width, int height)
        {
            if (tex?.desc == null) return;
            // Preserve bootstrap clears for pass-owned history buffers across resizes.
            bool clearBuffer = tex.desc.ClearBuffer;
            tex.desc.Width              = width;
            tex.desc.Height             = height;
            tex.desc.EnableRandomWrite  = true;
            tex.desc.ClearBuffer        = clearBuffer;
        }

        private static RenderGraphTexture CreatePassOwnedTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format,
            bool clearBuffer = false)
        {
            var tex = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            tex.desc.Name              = name;
            tex.desc.EnableRandomWrite = true;
            tex.desc.ClearBuffer       = clearBuffer;
            tex.desc.AnisoLevel = 1;
            return tex;
        }
    }
}
