using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// NRD REBLUR denoiser for path tracing diffuse and specular radiance.
    /// Handles both diffuse and specular signals with RGBA16F format (RGB = radiance, A = hit distance).
    /// Based on AmbientOcclusionDenoiser.REBLUR.cs but extended for full radiance denoising.
    /// </summary>
    public class PathTracingDenoiser : IDisposable
    {
        // REBLUR compute shaders
        ComputeShader m_ClassifyTiles;
        ComputeShader m_PrePass;
        ComputeShader m_TemporalAccumulation;
        ComputeShader m_HistoryFix;
        ComputeShader m_Blur;
        ComputeShader m_PostBlur;
        ComputeShader m_TemporalStabilization;
        ComputeShader m_SplitScreen;

        // Re-modulation shader for applying material factors after denoising
        ComputeShader m_RemodulationCS;

        IntPtr m_NRDContext;
        bool m_Initialized;

        /// <summary>
        /// Denoiser settings exposed for volume control
        /// </summary>
        public struct Settings
        {
            // Blur Radius Settings
            public float minBlurRadius;
            public float maxBlurRadius;
            public float diffusePrepassBlurRadius;
            public float specularPrepassBlurRadius;

            // Temporal Accumulation Settings
            public int maxAccumulatedFrameNum;
            public int maxFastAccumulatedFrameNum;
            public int maxStabilizedFrameNum;
            public int historyFixFrameNum;

            // Quality Settings
            public bool enableAntiFirefly;
            public float fireflySuppressorMinRelativeScale;
            public float fastHistoryClampingSigmaScale;
            public float minHitDistanceWeight;

            // Rejection Settings
            public float lobeAngleFraction;
            public float roughnessFraction;
            public float planeDistanceSensitivity;

            // Antilag Settings
            public float antilagLuminanceSigmaScale;
            public float antilagLuminanceSensitivity;

            // Hit Distance Parameters (ABCD)
            public float hitDistanceA;
            public float hitDistanceB;
            public float hitDistanceC;
            public float hitDistanceD;

            // Debug
            public float splitScreen;

            public static Settings Default => new Settings
            {
                // Blur Radius
                minBlurRadius = 1.0f,
                maxBlurRadius = 30.0f,
                diffusePrepassBlurRadius = 30.0f,
                specularPrepassBlurRadius = 50.0f,

                // Temporal Accumulation
                maxAccumulatedFrameNum = 30,
                maxFastAccumulatedFrameNum = 6,
                maxStabilizedFrameNum = 0,
                historyFixFrameNum = 3,

                // Quality
                enableAntiFirefly = true,
                fireflySuppressorMinRelativeScale = 1.0f,
                fastHistoryClampingSigmaScale = 2.0f,
                minHitDistanceWeight = 0.1f,

                // Rejection
                lobeAngleFraction = 0.15f,
                roughnessFraction = 0.15f,
                planeDistanceSensitivity = 0.005f,

                // Antilag
                antilagLuminanceSigmaScale = 4.0f,
                antilagLuminanceSensitivity = 3.0f,

                // Hit Distance Parameters
                hitDistanceA = 3.0f,
                hitDistanceB = 0.1f,
                hitDistanceC = 20.0f,
                hitDistanceD = -25.0f,

                // Debug
                splitScreen = 0.0f
            };
        }

        public void Init()
        {
            if (m_Initialized)
                return;

            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<DenoiserRuntimeShader>();
            var vividShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();

            m_ClassifyTiles = runtimeShaders.REBLURClassifyTiles;
            m_PrePass = runtimeShaders.REBLURPrePass;
            m_TemporalAccumulation = runtimeShaders.REBLURTemporalAccumulation;
            m_HistoryFix = runtimeShaders.REBLURHistoryFix;
            m_Blur = runtimeShaders.REBLURBlur;
            m_PostBlur = runtimeShaders.REBLURPostBlur;
            m_TemporalStabilization = runtimeShaders.REBLURTemporalStabilization;
            m_SplitScreen = runtimeShaders.REBLURSplitScreen;

            // Get re-modulation shader
            m_RemodulationCS = vividShaders.pathTracingRemodulationCS;

            m_NRDContext = NRDInitlizer.NRD_GetContext();
            m_Initialized = true;
        }

        public void Dispose()
        {
            if (m_Initialized && m_NRDContext != IntPtr.Zero)
            {
                NRDInitlizer.NRD_ReleaseContext(m_NRDContext);
                m_NRDContext = IntPtr.Zero;
            }
            m_Initialized = false;
        }

        #region History Buffer Management

        internal bool ReAllocateDiffuseInternalDataIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_DiffuseInternalData{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingDiffuseInternalData);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingDiffuseInternalData);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingDiffuseInternalData,
                    Allocator, GraphicsFormat.R8_UNorm, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingDiffuseInternalData);
            return valid;
        }

        internal bool ReAllocateSpecularInternalDataIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle currFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_SpecularInternalData{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingSpecularInternalData);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingSpecularInternalData);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingSpecularInternalData,
                    Allocator, GraphicsFormat.R8_UNorm, 1);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingSpecularInternalData);
            return valid;
        }

        internal bool ReAllocateDiffuseRadianceHistoryIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_DiffuseRadiance{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingDiffuseRadiance);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingDiffuseRadiance);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingDiffuseRadiance,
                    Allocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.REBLURPathTracingDiffuseRadiance);
            return valid;
        }

        internal bool ReAllocateSpecularRadianceHistoryIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_SpecularRadiance{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingSpecularRadiance);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingSpecularRadiance);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingSpecularRadiance,
                    Allocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.REBLURPathTracingSpecularRadiance);
            return valid;
        }

        internal bool ReAllocateDiffuseFastHistoryIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT, out RTHandle currFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_DiffuseFast{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingDiffuseFast);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingDiffuseFast);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingDiffuseFast,
                    Allocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingDiffuseFast);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.REBLURPathTracingDiffuseFast);
            return valid;
        }

        internal bool ReAllocateSpecularFastHistoryIfNeeded(HistoryFrameRTSystem historyRTSystem, out RTHandle prevFrameRT, out RTHandle currFrameRT)
        {
            static RTHandle Allocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
            {
                frameIndex &= 1;
                return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                    enableRandomWrite: true, useDynamicScale: true,
                    name: $"{viewName}_REBLUR_PT_SpecularFast{frameIndex}");
            }

            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingSpecularFast);
            bool valid = curTexture != null;

            if (!valid)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.REBLURPathTracingSpecularFast);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.REBLURPathTracingSpecularFast,
                    Allocator, GraphicsFormat.R16G16B16A16_SFloat, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.REBLURPathTracingSpecularFast);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.REBLURPathTracingSpecularFast);
            return valid;
        }

        #endregion

            #region Pass Data

        class DiffuseDenoisePassData
        {
            internal ComputeShader ClassifyTiles;
            internal ComputeShader TemporalAccumulation;
            internal ComputeShader HistoryFix;
            internal ComputeShader Blur;
            internal ComputeShader PostBlur;
            internal ComputeShader SplitScreen;

            // Input textures
            internal TextureHandle MotionTexture;
            internal TextureHandle CurrNormalRoughnessTexture;
            internal TextureHandle PrevNormalRoughnessTexture;
            internal TextureHandle CurrViewZTexture;
            internal TextureHandle PrevViewZTexture;
            internal TextureHandle DummyTexture;

            // History textures
            internal TextureHandle PrevInternalDataTexture;
            internal TextureHandle PrevDiffuseFastTexture;
            internal TextureHandle CurrDiffuseFastTexture;
            internal TextureHandle PrevDiffuseRadianceTexture;

            // Transient textures
            internal TextureHandle TileTexture;
            internal TextureHandle TempTexture1;
            internal TextureHandle TempTexture2;
            internal TextureHandle TempDataTexture;
            internal TextureHandle TempData2Texture;
            internal TextureHandle InternalDataTexture;

            // Input/Output
            internal TextureHandle UnfilteredDiffuseTexture;
            internal TextureHandle OutputDiffuseTexture;

            internal int width, height;
            internal ReblurSharedConstants ReblurSharedConstants;
        }

        class SpecularDenoisePassData
        {
            internal ComputeShader ClassifyTiles;
            internal ComputeShader TemporalAccumulation;
            internal ComputeShader HistoryFix;
            internal ComputeShader Blur;
            internal ComputeShader PostBlur;
            internal ComputeShader SplitScreen;

            // Input textures
            internal TextureHandle MotionTexture;
            internal TextureHandle CurrNormalRoughnessTexture;
            internal TextureHandle PrevNormalRoughnessTexture;
            internal TextureHandle CurrViewZTexture;
            internal TextureHandle PrevViewZTexture;
            internal TextureHandle DummyTexture;

            // History textures
            internal TextureHandle PrevInternalDataTexture;
            internal TextureHandle PrevSpecularFastTexture;
            internal TextureHandle CurrSpecularFastTexture;
            internal TextureHandle PrevSpecularRadianceTexture;

            // Transient textures
            internal TextureHandle TileTexture;
            internal TextureHandle TempTexture1;
            internal TextureHandle TempTexture2;
            internal TextureHandle TempDataTexture;
            internal TextureHandle TempData2Texture;
            internal TextureHandle InternalDataTexture;

            // Input/Output
            internal TextureHandle UnfilteredSpecularTexture;
            internal TextureHandle OutputSpecularTexture;

            internal int width, height;
            internal ReblurSharedConstants ReblurSharedConstants;
        }

        class RemodulationPassData
        {
            internal ComputeShader RemodulationCS;
            internal TextureHandle DenoisedDiffuseTexture;
            internal TextureHandle DenoisedSpecularTexture;
            internal TextureHandle MaterialFactorsTexture;
            internal TextureHandle OutputTexture;
            internal int width, height;
        }

        #endregion

        #region Shader Property IDs

        static readonly int gIn_ViewZ = Shader.PropertyToID("gIn_ViewZ");
        static readonly int gIn_Tiles = Shader.PropertyToID("gIn_Tiles");
        static readonly int gOut_Tiles = Shader.PropertyToID("gOut_Tiles");
        static readonly int gIn_Normal_Roughness = Shader.PropertyToID("gIn_Normal_Roughness");
        static readonly int gIn_Mv = Shader.PropertyToID("gIn_Mv");
        static readonly int gIn_Diff = Shader.PropertyToID("gIn_Diff");
        static readonly int gIn_Spec = Shader.PropertyToID("gIn_Spec");
        static readonly int gOut_Diff = Shader.PropertyToID("gOut_Diff");
        static readonly int gOut_Spec = Shader.PropertyToID("gOut_Spec");
        static readonly int gPrev_ViewZ = Shader.PropertyToID("gPrev_ViewZ");
        static readonly int gPrev_Normal_Roughness = Shader.PropertyToID("gPrev_Normal_Roughness");
        static readonly int gIn_DisocclusionThresholdMix = Shader.PropertyToID("gIn_DisocclusionThresholdMix");
        static readonly int gIn_DiffConfidence = Shader.PropertyToID("gIn_DiffConfidence");
        static readonly int gIn_SpecConfidence = Shader.PropertyToID("gIn_SpecConfidence");
        static readonly int gHistory_Diff = Shader.PropertyToID("gHistory_Diff");
        static readonly int gHistory_Spec = Shader.PropertyToID("gHistory_Spec");
        static readonly int gHistory_DiffFast = Shader.PropertyToID("gHistory_DiffFast");
        static readonly int gHistory_SpecFast = Shader.PropertyToID("gHistory_SpecFast");
        static readonly int gPrev_InternalData = Shader.PropertyToID("gPrev_InternalData");
        static readonly int gOut_Data1 = Shader.PropertyToID("gOut_Data1");
        static readonly int gOut_Data2 = Shader.PropertyToID("gOut_Data2");
        static readonly int gIn_Data1 = Shader.PropertyToID("gIn_Data1");
        static readonly int gOut_DiffFast = Shader.PropertyToID("gOut_DiffFast");
        static readonly int gOut_SpecFast = Shader.PropertyToID("gOut_SpecFast");
        static readonly int gIn_DiffFast = Shader.PropertyToID("gIn_DiffFast");
        static readonly int gIn_SpecFast = Shader.PropertyToID("gIn_SpecFast");
        static readonly int gOut_Normal_Roughness = Shader.PropertyToID("gOut_Normal_Roughness");
        static readonly int gOut_ViewZ = Shader.PropertyToID("gOut_ViewZ");
        static readonly int gOut_InternalData = Shader.PropertyToID("gOut_InternalData");
        static readonly int gOut_DiffCopy = Shader.PropertyToID("gOut_DiffCopy");
        static readonly int gOut_SpecCopy = Shader.PropertyToID("gOut_SpecCopy");

        static readonly int REBLUR_ClassifyTilesConstants = Shader.PropertyToID("REBLUR_ClassifyTilesConstants");
        static readonly int REBLUR_TemporalAccumulationConstants = Shader.PropertyToID("REBLUR_TemporalAccumulationConstants");
        static readonly int REBLUR_HistoryFixConstants = Shader.PropertyToID("REBLUR_HistoryFixConstants");
        static readonly int REBLUR_BlurConstants = Shader.PropertyToID("REBLUR_BlurConstants");
        static readonly int REBLUR_PostBlurConstants = Shader.PropertyToID("REBLUR_PostBlurConstants");
        static readonly int REBLUR_SplitScreenConstants = Shader.PropertyToID("REBLUR_SplitScreenConstants");

        // Re-modulation shader properties
        static readonly int _DenoisedDiffuse = Shader.PropertyToID("_DenoisedDiffuse");
        static readonly int _DenoisedSpecular = Shader.PropertyToID("_DenoisedSpecular");
        static readonly int _MaterialFactors = Shader.PropertyToID("_MaterialFactors");
        static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        static readonly int _ScreenSize = Shader.PropertyToID("_ScreenSize");

        #endregion

        #region Execute Functions

        static void ExecuteDiffusePass(DiffuseDenoisePassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var kernel = 0;

            var tx = CoreUtils.DivRoundUp(data.width, 16);
            var ty = CoreUtils.DivRoundUp(data.height, 16);

            // Enable diffuse-only keyword
            CoreUtils.SetKeyword(cmd, "_NRD_DIFFUSE", true);
            CoreUtils.SetKeyword(cmd, "_NRD_SPECULAR", false);

            // CLASSIFY_TILES
            {
                var cs = data.ClassifyTiles;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_ClassifyTilesConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Tiles, data.TileTexture);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            tx = CoreUtils.DivRoundUp(data.width, 8);
            ty = CoreUtils.DivRoundUp(data.height, 8);

            // TEMPORAL_ACCUMULATION
            {
                var cs = data.TemporalAccumulation;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_TemporalAccumulationConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Mv, data.MotionTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_InternalData, data.PrevInternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DisocclusionThresholdMix, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DiffConfidence, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.UnfilteredDiffuseTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_Diff, data.PrevDiffuseRadianceTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_DiffFast, data.PrevDiffuseFastTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Data2, data.TempData2Texture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffFast, data.CurrDiffuseFastTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // HISTORY_FIX
            {
                var cs = data.HistoryFix;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_HistoryFixConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DiffFast, data.CurrDiffuseFastTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture1);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffFast, data.PrevDiffuseFastTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // BLUR
            {
                var cs = data.Blur;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_BlurConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture1);

                cmd.SetComputeTextureParam(cs, kernel, gOut_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.TempTexture2);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // POST_BLUR
            {
                var cs = data.PostBlur;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_PostBlurConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.TempTexture2);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.OutputDiffuseTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_InternalData, data.InternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_DiffCopy, data.PrevDiffuseRadianceTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // SPLIT_SCREEN (optional debug)
            if (data.ReblurSharedConstants.gSplitScreen > 0)
            {
                var cs = data.SplitScreen;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_SplitScreenConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Diff, data.UnfilteredDiffuseTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Diff, data.OutputDiffuseTexture);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }
        }

        static void ExecuteSpecularPass(SpecularDenoisePassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var kernel = 0;

            var tx = CoreUtils.DivRoundUp(data.width, 16);
            var ty = CoreUtils.DivRoundUp(data.height, 16);

            // Enable specular-only keyword
            CoreUtils.SetKeyword(cmd, "_NRD_DIFFUSE", false);
            CoreUtils.SetKeyword(cmd, "_NRD_SPECULAR", true);

            // CLASSIFY_TILES (reuse tiles from diffuse pass if running both)
            {
                var cs = data.ClassifyTiles;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_ClassifyTilesConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Tiles, data.TileTexture);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            tx = CoreUtils.DivRoundUp(data.width, 8);
            ty = CoreUtils.DivRoundUp(data.height, 8);

            // TEMPORAL_ACCUMULATION
            {
                var cs = data.TemporalAccumulation;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_TemporalAccumulationConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Mv, data.MotionTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gPrev_InternalData, data.PrevInternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_DisocclusionThresholdMix, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_SpecConfidence, data.DummyTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Spec, data.UnfilteredSpecularTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_Spec, data.PrevSpecularRadianceTexture);
                cmd.SetComputeTextureParam(cs, kernel, gHistory_SpecFast, data.PrevSpecularFastTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Data2, data.TempData2Texture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Spec, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gOut_SpecFast, data.CurrSpecularFastTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // HISTORY_FIX
            {
                var cs = data.HistoryFix;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_HistoryFixConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Spec, data.TempTexture2);
                cmd.SetComputeTextureParam(cs, kernel, gIn_SpecFast, data.CurrSpecularFastTexture);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Spec, data.TempTexture1);
                cmd.SetComputeTextureParam(cs, kernel, gOut_SpecFast, data.PrevSpecularFastTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // BLUR
            {
                var cs = data.Blur;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_BlurConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Spec, data.TempTexture1);

                cmd.SetComputeTextureParam(cs, kernel, gOut_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Spec, data.TempTexture2);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // POST_BLUR
            {
                var cs = data.PostBlur;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_PostBlurConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Tiles, data.TileTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Normal_Roughness, data.CurrNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Data1, data.TempDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.PrevViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Spec, data.TempTexture2);

                cmd.SetComputeTextureParam(cs, kernel, gOut_Normal_Roughness, data.PrevNormalRoughnessTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Spec, data.OutputSpecularTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_InternalData, data.InternalDataTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_SpecCopy, data.PrevSpecularRadianceTexture);

                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }

            // SPLIT_SCREEN (optional debug)
            if (data.ReblurSharedConstants.gSplitScreen > 0)
            {
                var cs = data.SplitScreen;
                ConstantBuffer.Push(cmd, data.ReblurSharedConstants, cs, REBLUR_SplitScreenConstants);
                cmd.SetComputeTextureParam(cs, kernel, gIn_ViewZ, data.CurrViewZTexture);
                cmd.SetComputeTextureParam(cs, kernel, gIn_Spec, data.UnfilteredSpecularTexture);
                cmd.SetComputeTextureParam(cs, kernel, gOut_Spec, data.OutputSpecularTexture);
                cmd.DispatchCompute(cs, kernel, tx, ty, 1);
            }
        }

        static void ExecuteRemodulationPass(RemodulationPassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var cs = data.RemodulationCS;
            var kernel = cs.FindKernel("CSRemodulate");

            cmd.SetComputeTextureParam(cs, kernel, _DenoisedDiffuse, data.DenoisedDiffuseTexture);
            cmd.SetComputeTextureParam(cs, kernel, _DenoisedSpecular, data.DenoisedSpecularTexture);
            cmd.SetComputeTextureParam(cs, kernel, _MaterialFactors, data.MaterialFactorsTexture);
            cmd.SetComputeTextureParam(cs, kernel, _OutputTexture, data.OutputTexture);
            cmd.SetComputeVectorParam(cs, _ScreenSize, new Vector4(data.width, data.height, 1.0f / data.width, 1.0f / data.height));

            int tx = CoreUtils.DivRoundUp(data.width, 8);
            int ty = CoreUtils.DivRoundUp(data.height, 8);
            cmd.DispatchCompute(cs, kernel, tx, ty, 1);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Result of denoising operation
        /// </summary>
        public struct DenoiseResult
        {
            public TextureHandle denoisedDiffuse;
            public TextureHandle denoisedSpecular;
            public TextureHandle compositedOutput;
        }

        /// <summary>
        /// Denoise path tracing diffuse and specular radiance using NRD REBLUR
        /// </summary>
        public DenoiseResult Denoise(
            RenderGraph renderGraph,
            ContextContainer frameData,
            TextureHandle motionTexture,
            TextureHandle normalRoughnessTexture,
            TextureHandle viewZTexture,
            TextureHandle unfilteredDiffuseTexture,
            TextureHandle unfilteredSpecularTexture,
            TextureHandle materialFactorsTexture,
            Settings settings)
        {
            if (!m_Initialized)
            {
                Init();
            }

            var result = new DenoiseResult();
            var cameraData = frameData.Get<UniversalCameraData>();
            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            int width = cameraData.scaledWidth;
            int height = cameraData.scaledHeight;

            // Setup NRD common settings
            NRDCommonSettings commonSettings = NRDCommonSettings.Default();
            var cameraExt = cameraData.cameraExtension;

            var view = cameraExt.previousViewMatrix;
            var viewPrev = cameraExt.previousViewMatrix;
            var gpuProj = cameraExt.gpuProjectionMatrix;
            var gpuProjPrev = cameraExt.previousGPUProjectionMatrix;

            commonSettings.viewToClipMatrix = gpuProj.Pack();
            commonSettings.viewToClipMatrixPrev = gpuProjPrev.Pack();
            commonSettings.worldToViewMatrix = view.Pack();
            commonSettings.worldToViewMatrixPrev = viewPrev.Pack();
            commonSettings.cameraJitter = cameraExt.jitter.Pack();
            commonSettings.cameraJitterPrev = cameraExt.previousJitter.Pack();
            commonSettings.resourceSize = new[] { (ushort)width, (ushort)height };
            commonSettings.resourceSizePrev = new[] { (ushort)width, (ushort)height };
            commonSettings.rectSize = new[] { (ushort)width, (ushort)height };
            commonSettings.rectSizePrev = new[] { (ushort)width, (ushort)height };
            commonSettings.frameIndex = (uint)Time.frameCount;
            commonSettings.timeDeltaBetweenFrames = Time.deltaTime;
            commonSettings.denoisingRange = cameraData.camera.farClipPlane;
            commonSettings.accumulationMode = AccumulationMode.CONTINUE;
            commonSettings.splitScreen = settings.splitScreen;

            NRDInitlizer.NRD_SetCommonSettings(m_NRDContext, ref commonSettings);

            // Setup REBLUR settings with all volume parameters
            var reblurSettings = ReblurSettings.Default();

            // Blur Radius Settings
            reblurSettings.minBlurRadius = settings.minBlurRadius;
            reblurSettings.maxBlurRadius = settings.maxBlurRadius;
            reblurSettings.diffusePrepassBlurRadius = settings.diffusePrepassBlurRadius;
            reblurSettings.specularPrepassBlurRadius = settings.specularPrepassBlurRadius;

            // Temporal Accumulation Settings
            reblurSettings.maxAccumulatedFrameNum = (uint)settings.maxAccumulatedFrameNum;
            reblurSettings.maxFastAccumulatedFrameNum = (uint)settings.maxFastAccumulatedFrameNum;
            reblurSettings.maxStabilizedFrameNum = (uint)settings.maxStabilizedFrameNum;
            reblurSettings.historyFixFrameNum = (uint)settings.historyFixFrameNum;

            // Quality Settings
            reblurSettings.enableAntiFirefly = settings.enableAntiFirefly;
            reblurSettings.fireflySuppressorMinRelativeScale = settings.fireflySuppressorMinRelativeScale;
            reblurSettings.fastHistoryClampingSigmaScale = settings.fastHistoryClampingSigmaScale;
            reblurSettings.minHitDistanceWeight = settings.minHitDistanceWeight;

            // Rejection Settings
            reblurSettings.lobeAngleFraction = settings.lobeAngleFraction;
            reblurSettings.roughnessFraction = settings.roughnessFraction;
            reblurSettings.planeDistanceSensitivity = settings.planeDistanceSensitivity;

            // Antilag Settings
            reblurSettings.antilagSettings.luminanceSigmaScale = settings.antilagLuminanceSigmaScale;
            reblurSettings.antilagSettings.luminanceSensitivity = settings.antilagLuminanceSensitivity;

            // Hit Distance Parameters
            reblurSettings.hitDistanceParameters.A = settings.hitDistanceA;
            reblurSettings.hitDistanceParameters.B = settings.hitDistanceB;
            reblurSettings.hitDistanceParameters.C = settings.hitDistanceC;
            reblurSettings.hitDistanceParameters.D = settings.hitDistanceD;

            var reblurConstants = new ReblurSharedConstants();
            NRDInitlizer.NRD_SetupReblurConstBuffer(m_NRDContext, ref commonSettings, ref reblurSettings, ref reblurConstants);

            // Allocate history buffers
            ReAllocateDiffuseInternalDataIfNeeded(historyRT, out var diffuseInternalData);
            ReAllocateSpecularInternalDataIfNeeded(historyRT, out var specularInternalData);
            ReAllocateDiffuseRadianceHistoryIfNeeded(historyRT, out var prevDiffuseRadiance);
            ReAllocateSpecularRadianceHistoryIfNeeded(historyRT, out var prevSpecularRadiance);
            ReAllocateDiffuseFastHistoryIfNeeded(historyRT, out var prevDiffuseFast, out var currDiffuseFast);
            ReAllocateSpecularFastHistoryIfNeeded(historyRT, out var prevSpecularFast, out var currSpecularFast);

            // Create output textures
            var outputDiffuseDesc = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                name = "REBLUR_DenoisedDiffuse"
            };
            var outputDiffuseTexture = renderGraph.CreateTexture(outputDiffuseDesc);

            var outputSpecularDesc = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                name = "REBLUR_DenoisedSpecular"
            };
            var outputSpecularTexture = renderGraph.CreateTexture(outputSpecularDesc);

            var compositeOutputDesc = new TextureDesc(width, height)
            {
                format = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                name = "PathTracing_CompositeOutput"
            };
            var compositeOutputTexture = renderGraph.CreateTexture(compositeOutputDesc);

            // Denoise Diffuse
            using (var builder = renderGraph.AddComputePass<DiffuseDenoisePassData>("NRD REBLUR Diffuse Denoise", out var data))
            {
                data.ClassifyTiles = m_ClassifyTiles;
                data.TemporalAccumulation = m_TemporalAccumulation;
                data.HistoryFix = m_HistoryFix;
                data.Blur = m_Blur;
                data.PostBlur = m_PostBlur;
                data.SplitScreen = m_SplitScreen;

                data.MotionTexture = motionTexture;
                data.CurrNormalRoughnessTexture = normalRoughnessTexture;
                data.CurrViewZTexture = viewZTexture;
                data.PrevViewZTexture = renderGraph.ImportTexture(historyRT.GetPreviousFrameRT(HistoryFrameType.ViewZ));
                data.PrevNormalRoughnessTexture = renderGraph.ImportTexture(historyRT.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness));

                data.PrevInternalDataTexture = renderGraph.ImportTexture(diffuseInternalData);
                data.PrevDiffuseRadianceTexture = renderGraph.ImportTexture(prevDiffuseRadiance);
                data.PrevDiffuseFastTexture = renderGraph.ImportTexture(prevDiffuseFast);
                data.CurrDiffuseFastTexture = renderGraph.ImportTexture(currDiffuseFast);

                data.UnfilteredDiffuseTexture = unfilteredDiffuseTexture;
                data.OutputDiffuseTexture = outputDiffuseTexture;

                data.TileTexture = builder.CreateTransientTexture(new TextureDesc(
                    CoreUtils.DivRoundUp(width, 16), CoreUtils.DivRoundUp(height, 16))
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_Tiles"
                });

                data.TempTexture1 = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "REBLUR_TempDiff1"
                });

                data.TempTexture2 = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "REBLUR_TempDiff2"
                });

                data.TempDataTexture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_TempData"
                });

                data.TempData2Texture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_TempData2"
                });

                data.InternalDataTexture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_InternalData"
                });

                data.DummyTexture = renderGraph.defaultResources.blackTexture;

                data.width = width;
                data.height = height;
                data.ReblurSharedConstants = reblurConstants;

                builder.UseTexture(data.MotionTexture);
                builder.UseTexture(data.CurrNormalRoughnessTexture);
                builder.UseTexture(data.CurrViewZTexture);
                builder.UseTexture(data.PrevViewZTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevNormalRoughnessTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevInternalDataTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevDiffuseRadianceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevDiffuseFastTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.CurrDiffuseFastTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.UnfilteredDiffuseTexture);
                builder.UseTexture(data.OutputDiffuseTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.DummyTexture);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc<DiffuseDenoisePassData>(ExecuteDiffusePass);
            }

            // Denoise Specular
            using (var builder = renderGraph.AddComputePass<SpecularDenoisePassData>("NRD REBLUR Specular Denoise", out var data))
            {
                data.ClassifyTiles = m_ClassifyTiles;
                data.TemporalAccumulation = m_TemporalAccumulation;
                data.HistoryFix = m_HistoryFix;
                data.Blur = m_Blur;
                data.PostBlur = m_PostBlur;
                data.SplitScreen = m_SplitScreen;

                data.MotionTexture = motionTexture;
                data.CurrNormalRoughnessTexture = normalRoughnessTexture;
                data.CurrViewZTexture = viewZTexture;
                data.PrevViewZTexture = renderGraph.ImportTexture(historyRT.GetPreviousFrameRT(HistoryFrameType.ViewZ));
                data.PrevNormalRoughnessTexture = renderGraph.ImportTexture(historyRT.GetCurrentFrameRT(HistoryFrameType.PrevNormalRoughness));

                data.PrevInternalDataTexture = renderGraph.ImportTexture(specularInternalData);
                data.PrevSpecularRadianceTexture = renderGraph.ImportTexture(prevSpecularRadiance);
                data.PrevSpecularFastTexture = renderGraph.ImportTexture(prevSpecularFast);
                data.CurrSpecularFastTexture = renderGraph.ImportTexture(currSpecularFast);

                data.UnfilteredSpecularTexture = unfilteredSpecularTexture;
                data.OutputSpecularTexture = outputSpecularTexture;

                data.TileTexture = builder.CreateTransientTexture(new TextureDesc(
                    CoreUtils.DivRoundUp(width, 16), CoreUtils.DivRoundUp(height, 16))
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_Tiles_Spec"
                });

                data.TempTexture1 = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "REBLUR_TempSpec1"
                });

                data.TempTexture2 = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "REBLUR_TempSpec2"
                });

                data.TempDataTexture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_TempDataSpec"
                });

                data.TempData2Texture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_TempData2Spec"
                });

                data.InternalDataTexture = builder.CreateTransientTexture(new TextureDesc(width, height)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true,
                    name = "REBLUR_InternalDataSpec"
                });

                data.DummyTexture = renderGraph.defaultResources.blackTexture;

                data.width = width;
                data.height = height;
                data.ReblurSharedConstants = reblurConstants;

                builder.UseTexture(data.MotionTexture);
                builder.UseTexture(data.CurrNormalRoughnessTexture);
                builder.UseTexture(data.CurrViewZTexture);
                builder.UseTexture(data.PrevViewZTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevNormalRoughnessTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevInternalDataTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevSpecularRadianceTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.PrevSpecularFastTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.CurrSpecularFastTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.UnfilteredSpecularTexture);
                builder.UseTexture(data.OutputSpecularTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.DummyTexture);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc<SpecularDenoisePassData>(ExecuteSpecularPass);
            }

            // Re-modulation pass: apply material factors to recover final colors
            if (m_RemodulationCS != null)
            {
                using (var builder = renderGraph.AddComputePass<RemodulationPassData>("Path Tracing Remodulation", out var data))
                {
                    data.RemodulationCS = m_RemodulationCS;
                    data.DenoisedDiffuseTexture = outputDiffuseTexture;
                    data.DenoisedSpecularTexture = outputSpecularTexture;
                    data.MaterialFactorsTexture = materialFactorsTexture;
                    data.OutputTexture = compositeOutputTexture;
                    data.width = width;
                    data.height = height;

                    builder.UseTexture(data.DenoisedDiffuseTexture);
                    builder.UseTexture(data.DenoisedSpecularTexture);
                    builder.UseTexture(data.MaterialFactorsTexture);
                    builder.UseTexture(data.OutputTexture, AccessFlags.ReadWrite);

                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc<RemodulationPassData>(ExecuteRemodulationPass);
                }
            }

            result.denoisedDiffuse = outputDiffuseTexture;
            result.denoisedSpecular = outputSpecularTexture;
            result.compositedOutput = compositeOutputTexture;

            return result;
        }

        #endregion
    }
}
