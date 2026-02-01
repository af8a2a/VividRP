using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(GlobalIllumination))]
    public class GlobalIlluminationEditor : VolumeComponentEditor
    {
        #region Serialized Parameters

        // General
        SerializedDataParameter m_Technique;

        // Path Tracing - General
        SerializedDataParameter m_PathTracingQuality;
        SerializedDataParameter m_PathTracingIntensity;

        // Path Tracing - Ray Settings
        SerializedDataParameter m_MaxBounces;
        SerializedDataParameter m_SamplesPerPixel;
        SerializedDataParameter m_RayLength;
        SerializedDataParameter m_LayerMask;

        // Path Tracing - Quality
        SerializedDataParameter m_UseRussianRoulette;
        SerializedDataParameter m_RussianRouletteStartBounce;
        SerializedDataParameter m_FireflyClamp;
        SerializedDataParameter m_UseNVSER;
        SerializedDataParameter m_TextureLODBias;

        // Path Tracing - Denoising
        SerializedDataParameter m_DenoiseMode;

        // Path Tracing - Temporal Accumulation Settings (for TemporalAccumulation mode)
        SerializedDataParameter m_MaxAccumulatedFrames;
        SerializedDataParameter m_ResetOnCameraMove;
        SerializedDataParameter m_CameraMovementThreshold;

        // Path Tracing - Reprojection Rejection (for TemporalAccumulation mode)
        SerializedDataParameter m_EnableReprojectionRejection;
        SerializedDataParameter m_ReprojectionDepthThreshold;
        SerializedDataParameter m_ReprojectionNormalThreshold;
        SerializedDataParameter m_ReprojectionRoughnessThreshold;
        SerializedDataParameter m_EnableVarianceClamping;
        SerializedDataParameter m_VarianceClampingGamma;
        SerializedDataParameter m_MinTemporalBlendWeight;

        // NRD REBLUR Settings (for NRDReblur mode)
        SerializedDataParameter m_NrdMinBlurRadius;
        SerializedDataParameter m_NrdMaxBlurRadius;
        SerializedDataParameter m_NrdDiffusePrepassBlurRadius;
        SerializedDataParameter m_NrdSpecularPrepassBlurRadius;
        SerializedDataParameter m_NrdMaxAccumulatedFrameNum;
        SerializedDataParameter m_NrdMaxFastAccumulatedFrameNum;
        SerializedDataParameter m_NrdMaxStabilizedFrameNum;
        SerializedDataParameter m_NrdHistoryFixFrameNum;
        SerializedDataParameter m_NrdAntiFirefly;
        SerializedDataParameter m_NrdFireflySuppressorMinRelativeScale;
        SerializedDataParameter m_NrdFastHistoryClampingSigmaScale;
        SerializedDataParameter m_NrdMinHitDistanceWeight;
        SerializedDataParameter m_NrdLobeAngleFraction;
        SerializedDataParameter m_NrdRoughnessFraction;
        SerializedDataParameter m_NrdPlaneDistanceSensitivity;
        SerializedDataParameter m_NrdAntilagLuminanceSigmaScale;
        SerializedDataParameter m_NrdAntilagLuminanceSensitivity;
        SerializedDataParameter m_NrdHitDistanceA;
        SerializedDataParameter m_NrdHitDistanceB;
        SerializedDataParameter m_NrdHitDistanceC;
        SerializedDataParameter m_NrdHitDistanceD;
        SerializedDataParameter m_NrdSplitScreen;

        // Path Tracing - Advanced
        SerializedDataParameter m_EnvironmentIntensity;
        SerializedDataParameter m_IncludeEmissive;
        SerializedDataParameter m_IncludeDirectLighting;
        SerializedDataParameter m_ReceiverMotionRejection;

        // Path Tracing - Debug
        SerializedDataParameter m_DebugMode;
        SerializedDataParameter m_DebugVisualizeBounce;
        SerializedDataParameter m_DebugShowPathTracingOnly;

        // SHARC Settings
        SerializedDataParameter m_EnableSharc;
        SerializedDataParameter m_SharcUpdate;
        SerializedDataParameter m_SharcQuery;
        SerializedDataParameter m_SharcSceneScale;
        SerializedDataParameter m_SharcEntriesK;
        SerializedDataParameter m_SharcRoughnessThreshold;
        SerializedDataParameter m_SharcRadianceScale;
        SerializedDataParameter m_SharcPropagationDepth;
        SerializedDataParameter m_SharcSampleThreshold;
        SerializedDataParameter m_SharcGridLevelBias;
        SerializedDataParameter m_SharcAntiFirefly;
        SerializedDataParameter m_SharcDebug;
        SerializedDataParameter m_SharcAccumulationFrames;
        SerializedDataParameter m_SharcStaleFrames;

#if DLSS_PLUGIN_INTEGRATE
        // DLSS-RR Settings (for DLSSRR mode)
        SerializedDataParameter m_DlssRRQuality;
        SerializedDataParameter m_DlssRRHitDistanceScale;
        SerializedDataParameter m_DlssRRPreExposure;
        SerializedDataParameter m_DlssRRExposureScale;
        SerializedDataParameter m_DlssRRResetHistory;
        SerializedDataParameter m_DlssRRSharpness;
        SerializedDataParameter m_DlssRRAutoExposure;
        SerializedDataParameter m_DlssRRIsHDR;
        SerializedDataParameter m_DlssRRSplitScreen;
#endif

        // Surface Cache - General
        SerializedDataParameter m_SurfaceCacheEstimationMethod;
        SerializedDataParameter m_SurfaceCacheMultiBounce;

        // Surface Cache - Grid
        SerializedDataParameter m_SurfaceCacheGridResolution;
        SerializedDataParameter m_SurfaceCacheVolumeSize;
        SerializedDataParameter m_SurfaceCacheCascadeCount;
        SerializedDataParameter m_SurfaceCacheCascadeMovement;

        // Surface Cache - Uniform Estimation
        SerializedDataParameter m_SurfaceCacheUniformSampleCount;

        // Surface Cache - Restir Estimation
        SerializedDataParameter m_SurfaceCacheRestirConfidenceCap;
        SerializedDataParameter m_SurfaceCacheRestirSpatialSampleCount;
        SerializedDataParameter m_SurfaceCacheRestirSpatialFilterSize;
        SerializedDataParameter m_SurfaceCacheRestirValidationFrameInterval;

        // Surface Cache - RIS Estimation
        SerializedDataParameter m_SurfaceCacheRisCandidateCount;
        SerializedDataParameter m_SurfaceCacheRisTargetFunctionUpdateWeight;

        // Surface Cache - Patch Filtering
        SerializedDataParameter m_SurfaceCacheTemporalSmoothing;
        SerializedDataParameter m_SurfaceCacheSpatialFilterEnabled;
        SerializedDataParameter m_SurfaceCacheSpatialFilterSampleCount;
        SerializedDataParameter m_SurfaceCacheSpatialFilterRadius;
        SerializedDataParameter m_SurfaceCacheTemporalPostFilterEnabled;

        // Surface Cache - Screen Filtering
        SerializedDataParameter m_SurfaceCacheLookupSampleCount;
        SerializedDataParameter m_SurfaceCacheUpsamplingKernelSize;
        SerializedDataParameter m_SurfaceCacheUpsamplingSampleCount;

        // Surface Cache - Advanced
        SerializedDataParameter m_SurfaceCacheDefragCount;

        // Surface Cache - Debug
        SerializedDataParameter m_SurfaceCacheDebugEnabled;
        SerializedDataParameter m_SurfaceCacheDebugViewMode;
        SerializedDataParameter m_SurfaceCacheDebugShowSamplePosition;

        // Screen Probes - General
        SerializedDataParameter m_ScreenProbesEnabled;
        SerializedDataParameter m_ScreenProbesQuality;
        SerializedDataParameter m_ScreenProbesIntensity;

        // Screen Probes - Tracing
        SerializedDataParameter m_ScreenProbesMaxRayDistance;
        SerializedDataParameter m_ScreenProbesNearFieldDistance;
        SerializedDataParameter m_ScreenProbesUseImportanceSampling;
        SerializedDataParameter m_ScreenProbesUseSurfaceCacheFallback;

        // Screen Probes - Temporal Filtering
        SerializedDataParameter m_ScreenProbesTemporalFilterStrength;
        SerializedDataParameter m_ScreenProbesDepthRejectionThreshold;
        SerializedDataParameter m_ScreenProbesNormalRejectionThreshold;
        SerializedDataParameter m_ScreenProbesEnableVarianceClamping;

        // Screen Probes - Spatial Filtering
        SerializedDataParameter m_ScreenProbesSpatialFilterRadius;
        SerializedDataParameter m_ScreenProbesSpatialFilterSamples;

        // Screen Probes - Debug
        SerializedDataParameter m_ScreenProbesDebugVisualization;
        SerializedDataParameter m_ScreenProbesDebugShowOnlyProbes;

        #endregion

        #region Styles

        static class Styles
        {
            // Headers
            public static readonly GUIContent HeaderGeneral = EditorGUIUtility.TrTextContent("General");
            public static readonly GUIContent HeaderPathTracing = EditorGUIUtility.TrTextContent("Path Tracing");
            public static readonly GUIContent HeaderRaySettings = EditorGUIUtility.TrTextContent("Ray Settings");
            public static readonly GUIContent HeaderQuality = EditorGUIUtility.TrTextContent("Quality");
            public static readonly GUIContent HeaderTemporalAccumulation = EditorGUIUtility.TrTextContent("Temporal Accumulation");
            public static readonly GUIContent HeaderDenoising = EditorGUIUtility.TrTextContent("Denoising");
            public static readonly GUIContent HeaderNRDReblur = EditorGUIUtility.TrTextContent("NRD REBLUR");
            public static readonly GUIContent HeaderNRDBlurRadius = EditorGUIUtility.TrTextContent("Blur Radius");
            public static readonly GUIContent HeaderNRDTemporalAccum = EditorGUIUtility.TrTextContent("Temporal Accumulation");
            public static readonly GUIContent HeaderNRDQuality = EditorGUIUtility.TrTextContent("Quality");
            public static readonly GUIContent HeaderNRDRejection = EditorGUIUtility.TrTextContent("Rejection");
            public static readonly GUIContent HeaderNRDAntilag = EditorGUIUtility.TrTextContent("Antilag");
            public static readonly GUIContent HeaderNRDHitDistance = EditorGUIUtility.TrTextContent("Hit Distance Parameters");
            public static readonly GUIContent HeaderAdvanced = EditorGUIUtility.TrTextContent("Advanced");
            public static readonly GUIContent HeaderDebug = EditorGUIUtility.TrTextContent("Debug");
            public static readonly GUIContent HeaderSharc = EditorGUIUtility.TrTextContent("SHARC (Radiance Cache)");

            // Surface Cache Headers
            public static readonly GUIContent HeaderSurfaceCache = EditorGUIUtility.TrTextContent("Surface Cache");
            public static readonly GUIContent HeaderSurfaceCacheGeneral = EditorGUIUtility.TrTextContent("General");
            public static readonly GUIContent HeaderSurfaceCacheGrid = EditorGUIUtility.TrTextContent("Grid/Volume");
            public static readonly GUIContent HeaderSurfaceCacheEstimation = EditorGUIUtility.TrTextContent("Estimation");
            public static readonly GUIContent HeaderSurfaceCachePatchFiltering = EditorGUIUtility.TrTextContent("Patch Filtering");
            public static readonly GUIContent HeaderSurfaceCacheScreenFiltering = EditorGUIUtility.TrTextContent("Screen Filtering");
            public static readonly GUIContent HeaderSurfaceCacheAdvanced = EditorGUIUtility.TrTextContent("Advanced");
            public static readonly GUIContent HeaderSurfaceCacheDebug = EditorGUIUtility.TrTextContent("Debug");

            // Tooltips
            public static readonly GUIContent Technique = EditorGUIUtility.TrTextContent("Technique", "Global illumination technique to use.");
            public static readonly GUIContent PathTracingQuality = EditorGUIUtility.TrTextContent("Quality Preset", "Quality preset for path tracing. Custom allows manual control of all parameters.");
            public static readonly GUIContent PathTracingIntensity = EditorGUIUtility.TrTextContent("Intensity", "Global intensity multiplier for path traced indirect lighting.");
            public static readonly GUIContent DenoiseMode = EditorGUIUtility.TrTextContent("Denoise Mode", "Denoising mode. None = raw output (noisy), TemporalAccumulation = simple accumulation with reprojection, NRDReblur = NVIDIA REBLUR denoiser (best quality).");

            // NRD REBLUR
            public static readonly GUIContent NrdMinBlurRadius = EditorGUIUtility.TrTextContent("Min Blur Radius (px)", "Minimum blur radius when converged.");
            public static readonly GUIContent NrdMaxBlurRadius = EditorGUIUtility.TrTextContent("Max Blur Radius (px)", "Maximum blur radius before temporal convergence.");
            public static readonly GUIContent NrdDiffusePrepassBlurRadius = EditorGUIUtility.TrTextContent("Diffuse Prepass Radius", "Pre-accumulation blur radius for diffuse.");
            public static readonly GUIContent NrdSpecularPrepassBlurRadius = EditorGUIUtility.TrTextContent("Specular Prepass Radius", "Pre-accumulation blur radius for specular.");
            public static readonly GUIContent NrdMaxAccumulatedFrameNum = EditorGUIUtility.TrTextContent("Max Accumulated Frames", "Maximum frames for temporal accumulation.");
            public static readonly GUIContent NrdMaxFastAccumulatedFrameNum = EditorGUIUtility.TrTextContent("Max Fast Frames", "Maximum frames for fast history (responsive).");
            public static readonly GUIContent NrdMaxStabilizedFrameNum = EditorGUIUtility.TrTextContent("Max Stabilized Frames", "Maximum frames for stabilized radiance.");
            public static readonly GUIContent NrdHistoryFixFrameNum = EditorGUIUtility.TrTextContent("History Fix Frames", "Frames to reconstruct after disocclusion.");
            public static readonly GUIContent NrdAntiFirefly = EditorGUIUtility.TrTextContent("Anti-Firefly", "Enable anti-firefly filter.");
            public static readonly GUIContent NrdFireflySuppressorScale = EditorGUIUtility.TrTextContent("Firefly Suppressor Scale", "Minimum relative scale for firefly suppression.");
            public static readonly GUIContent NrdFastHistoryClampingScale = EditorGUIUtility.TrTextContent("Fast History Clamping", "Sigma scale for fast history clamping.");
            public static readonly GUIContent NrdMinHitDistanceWeight = EditorGUIUtility.TrTextContent("Min Hit Distance Weight", "Minimum weight for hit distance in filtering.");
            public static readonly GUIContent NrdLobeAngleFraction = EditorGUIUtility.TrTextContent("Lobe Angle Fraction", "Fraction for normal-based rejection.");
            public static readonly GUIContent NrdRoughnessFraction = EditorGUIUtility.TrTextContent("Roughness Fraction", "Fraction for roughness-based rejection.");
            public static readonly GUIContent NrdPlaneDistanceSensitivity = EditorGUIUtility.TrTextContent("Plane Distance Sensitivity", "Sensitivity to tangent plane deviation.");
            public static readonly GUIContent NrdAntilagLuminanceSigmaScale = EditorGUIUtility.TrTextContent("Luminance Sigma Scale", "Variance multiplier for antilag detection.");
            public static readonly GUIContent NrdAntilagLuminanceSensitivity = EditorGUIUtility.TrTextContent("Luminance Sensitivity", "Sensitivity of antilag to luminance differences.");
            public static readonly GUIContent NrdHitDistanceA = EditorGUIUtility.TrTextContent("A (Constant)", "Constant value for hit distance normalization.");
            public static readonly GUIContent NrdHitDistanceB = EditorGUIUtility.TrTextContent("B (ViewZ Scale)", "ViewZ-based linear scale.");
            public static readonly GUIContent NrdHitDistanceC = EditorGUIUtility.TrTextContent("C (Roughness Scale)", "Roughness-based scale.");
            public static readonly GUIContent NrdHitDistanceD = EditorGUIUtility.TrTextContent("D (Roughness Collapse)", "Roughness collapse factor.");
            public static readonly GUIContent NrdSplitScreen = EditorGUIUtility.TrTextContent("Split Screen Debug", "Split screen visualization for debugging.");

            // DLSS-RR
            public static readonly GUIContent HeaderDLSSRR = EditorGUIUtility.TrTextContent("DLSS Ray Reconstruction");
            public static readonly GUIContent DlssRRQuality = EditorGUIUtility.TrTextContent("Quality Preset", "DLSS-RR quality preset. Higher quality modes use lower internal resolution for better performance.");
            public static readonly GUIContent DlssRRHitDistanceScale = EditorGUIUtility.TrTextContent("Hit Distance Scale", "World-space scale for hit distances. Adjust based on scene scale (larger scenes need larger values).");
            public static readonly GUIContent DlssRRPreExposure = EditorGUIUtility.TrTextContent("Pre-Exposure", "Pre-exposure value for DLSS-RR input. Should match your rendering's pre-exposure if used.");
            public static readonly GUIContent DlssRRExposureScale = EditorGUIUtility.TrTextContent("Exposure Scale", "Exposure scale multiplier for DLSS-RR. Used when auto-exposure is enabled.");
            public static readonly GUIContent DlssRRResetHistory = EditorGUIUtility.TrTextContent("Reset History", "Force reset DLSS-RR temporal history. Enable temporarily when scene changes significantly.");
            public static readonly GUIContent DlssRRSharpness = EditorGUIUtility.TrTextContent("Sharpness", "Sharpness applied to DLSS-RR output. 0 = no sharpening, 1 = maximum sharpening.");
            public static readonly GUIContent DlssRRAutoExposure = EditorGUIUtility.TrTextContent("Auto Exposure", "Enable auto-exposure handling in DLSS-RR. Should be enabled if your rendering uses auto-exposure.");
            public static readonly GUIContent DlssRRIsHDR = EditorGUIUtility.TrTextContent("HDR Input", "Indicate that input is HDR (pre-tonemapped). Should be enabled for path tracing which outputs linear HDR radiance.");
            public static readonly GUIContent DlssRRSplitScreen = EditorGUIUtility.TrTextContent("Split Screen Debug", "Split screen visualization for DLSS-RR debugging. 0 = off, 0.5 = half screen shows raw input.");
        }

        #endregion

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<GlobalIllumination>(serializedObject);

            // General
            m_Technique = Unpack(o.Find(x => x.technique));

            // Path Tracing - General
            m_PathTracingQuality = Unpack(o.Find(x => x.pathTracingQuality));
            m_PathTracingIntensity = Unpack(o.Find(x => x.pathTracingIntensity));

            // Path Tracing - Ray Settings
            m_MaxBounces = Unpack(o.Find(x => x.maxBounces));
            m_SamplesPerPixel = Unpack(o.Find(x => x.samplesPerPixel));
            m_RayLength = Unpack(o.Find(x => x.rayLength));
            m_LayerMask = Unpack(o.Find(x => x.layerMask));

            // Path Tracing - Quality
            m_UseRussianRoulette = Unpack(o.Find(x => x.useRussianRoulette));
            m_RussianRouletteStartBounce = Unpack(o.Find(x => x.russianRouletteStartBounce));
            m_FireflyClamp = Unpack(o.Find(x => x.fireflyClamp));
            m_UseNVSER = Unpack(o.Find(x => x.useNVSER));
            m_TextureLODBias = Unpack(o.Find(x => x.textureLODBias));

            // Path Tracing - Denoising Mode
            m_DenoiseMode = Unpack(o.Find(x => x.denoiseMode));

            // Path Tracing - Temporal Accumulation Settings (for TemporalAccumulation mode)
            m_MaxAccumulatedFrames = Unpack(o.Find(x => x.maxAccumulatedFrames));
            m_ResetOnCameraMove = Unpack(o.Find(x => x.resetOnCameraMove));
            m_CameraMovementThreshold = Unpack(o.Find(x => x.cameraMovementThreshold));

            // Path Tracing - Reprojection Rejection (for TemporalAccumulation mode)
            m_EnableReprojectionRejection = Unpack(o.Find(x => x.enableReprojectionRejection));
            m_ReprojectionDepthThreshold = Unpack(o.Find(x => x.reprojectionDepthThreshold));
            m_ReprojectionNormalThreshold = Unpack(o.Find(x => x.reprojectionNormalThreshold));
            m_ReprojectionRoughnessThreshold = Unpack(o.Find(x => x.reprojectionRoughnessThreshold));
            m_EnableVarianceClamping = Unpack(o.Find(x => x.enableVarianceClamping));
            m_VarianceClampingGamma = Unpack(o.Find(x => x.varianceClampingGamma));
            m_MinTemporalBlendWeight = Unpack(o.Find(x => x.minTemporalBlendWeight));

            // NRD REBLUR Settings (for NRDReblur mode)
            m_NrdMinBlurRadius = Unpack(o.Find(x => x.nrdMinBlurRadius));
            m_NrdMaxBlurRadius = Unpack(o.Find(x => x.nrdMaxBlurRadius));
            m_NrdDiffusePrepassBlurRadius = Unpack(o.Find(x => x.nrdDiffusePrepassBlurRadius));
            m_NrdSpecularPrepassBlurRadius = Unpack(o.Find(x => x.nrdSpecularPrepassBlurRadius));
            m_NrdMaxAccumulatedFrameNum = Unpack(o.Find(x => x.nrdMaxAccumulatedFrameNum));
            m_NrdMaxFastAccumulatedFrameNum = Unpack(o.Find(x => x.nrdMaxFastAccumulatedFrameNum));
            m_NrdMaxStabilizedFrameNum = Unpack(o.Find(x => x.nrdMaxStabilizedFrameNum));
            m_NrdHistoryFixFrameNum = Unpack(o.Find(x => x.nrdHistoryFixFrameNum));
            m_NrdAntiFirefly = Unpack(o.Find(x => x.nrdAntiFirefly));
            m_NrdFireflySuppressorMinRelativeScale = Unpack(o.Find(x => x.nrdFireflySuppressorMinRelativeScale));
            m_NrdFastHistoryClampingSigmaScale = Unpack(o.Find(x => x.nrdFastHistoryClampingSigmaScale));
            m_NrdMinHitDistanceWeight = Unpack(o.Find(x => x.nrdMinHitDistanceWeight));
            m_NrdLobeAngleFraction = Unpack(o.Find(x => x.nrdLobeAngleFraction));
            m_NrdRoughnessFraction = Unpack(o.Find(x => x.nrdRoughnessFraction));
            m_NrdPlaneDistanceSensitivity = Unpack(o.Find(x => x.nrdPlaneDistanceSensitivity));
            m_NrdAntilagLuminanceSigmaScale = Unpack(o.Find(x => x.nrdAntilagLuminanceSigmaScale));
            m_NrdAntilagLuminanceSensitivity = Unpack(o.Find(x => x.nrdAntilagLuminanceSensitivity));
            m_NrdHitDistanceA = Unpack(o.Find(x => x.nrdHitDistanceA));
            m_NrdHitDistanceB = Unpack(o.Find(x => x.nrdHitDistanceB));
            m_NrdHitDistanceC = Unpack(o.Find(x => x.nrdHitDistanceC));
            m_NrdHitDistanceD = Unpack(o.Find(x => x.nrdHitDistanceD));
            m_NrdSplitScreen = Unpack(o.Find(x => x.nrdSplitScreen));

            // Path Tracing - Advanced
            m_EnvironmentIntensity = Unpack(o.Find(x => x.environmentIntensity));
            m_IncludeEmissive = Unpack(o.Find(x => x.includeEmissive));
            m_IncludeDirectLighting = Unpack(o.Find(x => x.includeDirectLighting));
            m_ReceiverMotionRejection = Unpack(o.Find(x => x.receiverMotionRejection));

            // Path Tracing - Debug
            m_DebugMode = Unpack(o.Find(x => x.debugMode));
            m_DebugVisualizeBounce = Unpack(o.Find(x => x.debugVisualizeBounce));
            m_DebugShowPathTracingOnly = Unpack(o.Find(x => x.debugShowPathTracingOnly));

            // SHARC Settings
            m_EnableSharc = Unpack(o.Find(x => x.enableSharc));
            m_SharcUpdate = Unpack(o.Find(x => x.sharcUpdate));
            m_SharcQuery = Unpack(o.Find(x => x.sharcQuery));
            m_SharcSceneScale = Unpack(o.Find(x => x.sharcSceneScale));
            m_SharcEntriesK = Unpack(o.Find(x => x.sharcEntriesK));
            m_SharcRoughnessThreshold = Unpack(o.Find(x => x.sharcRoughnessThreshold));
            m_SharcRadianceScale = Unpack(o.Find(x => x.sharcRadianceScale));
            m_SharcPropagationDepth = Unpack(o.Find(x => x.sharcPropagationDepth));
            m_SharcSampleThreshold = Unpack(o.Find(x => x.sharcSampleThreshold));
            m_SharcGridLevelBias = Unpack(o.Find(x => x.sharcGridLevelBias));
            m_SharcAntiFirefly = Unpack(o.Find(x => x.sharcAntiFirefly));
            m_SharcDebug = Unpack(o.Find(x => x.sharcDebug));
            m_SharcAccumulationFrames = Unpack(o.Find(x => x.sharcAccumulationFrames));
            m_SharcStaleFrames = Unpack(o.Find(x => x.sharcStaleFrames));

#if DLSS_PLUGIN_INTEGRATE
            // DLSS-RR Settings
            m_DlssRRQuality = Unpack(o.Find(x => x.dlssRRQuality));
            m_DlssRRHitDistanceScale = Unpack(o.Find(x => x.dlssRRHitDistanceScale));
            m_DlssRRPreExposure = Unpack(o.Find(x => x.dlssRRPreExposure));
            m_DlssRRExposureScale = Unpack(o.Find(x => x.dlssRRExposureScale));
            m_DlssRRResetHistory = Unpack(o.Find(x => x.dlssRRResetHistory));
            m_DlssRRSharpness = Unpack(o.Find(x => x.dlssRRSharpness));
            m_DlssRRAutoExposure = Unpack(o.Find(x => x.dlssRRAutoExposure));
            m_DlssRRIsHDR = Unpack(o.Find(x => x.dlssRRIsHDR));
            m_DlssRRSplitScreen = Unpack(o.Find(x => x.dlssRRSplitScreen));
#endif

            // Surface Cache - General
            m_SurfaceCacheEstimationMethod = Unpack(o.Find(x => x.surfaceCacheEstimationMethod));
            m_SurfaceCacheMultiBounce = Unpack(o.Find(x => x.surfaceCacheMultiBounce));

            // Surface Cache - Grid
            m_SurfaceCacheGridResolution = Unpack(o.Find(x => x.surfaceCacheGridResolution));
            m_SurfaceCacheVolumeSize = Unpack(o.Find(x => x.surfaceCacheVolumeSize));
            m_SurfaceCacheCascadeCount = Unpack(o.Find(x => x.surfaceCacheCascadeCount));
            m_SurfaceCacheCascadeMovement = Unpack(o.Find(x => x.surfaceCacheCascadeMovement));

            // Surface Cache - Uniform Estimation
            m_SurfaceCacheUniformSampleCount = Unpack(o.Find(x => x.surfaceCacheUniformSampleCount));

            // Surface Cache - Restir Estimation
            m_SurfaceCacheRestirConfidenceCap = Unpack(o.Find(x => x.surfaceCacheRestirConfidenceCap));
            m_SurfaceCacheRestirSpatialSampleCount = Unpack(o.Find(x => x.surfaceCacheRestirSpatialSampleCount));
            m_SurfaceCacheRestirSpatialFilterSize = Unpack(o.Find(x => x.surfaceCacheRestirSpatialFilterSize));
            m_SurfaceCacheRestirValidationFrameInterval = Unpack(o.Find(x => x.surfaceCacheRestirValidationFrameInterval));

            // Surface Cache - RIS Estimation
            m_SurfaceCacheRisCandidateCount = Unpack(o.Find(x => x.surfaceCacheRisCandidateCount));
            m_SurfaceCacheRisTargetFunctionUpdateWeight = Unpack(o.Find(x => x.surfaceCacheRisTargetFunctionUpdateWeight));

            // Surface Cache - Patch Filtering
            m_SurfaceCacheTemporalSmoothing = Unpack(o.Find(x => x.surfaceCacheTemporalSmoothing));
            m_SurfaceCacheSpatialFilterEnabled = Unpack(o.Find(x => x.surfaceCacheSpatialFilterEnabled));
            m_SurfaceCacheSpatialFilterSampleCount = Unpack(o.Find(x => x.surfaceCacheSpatialFilterSampleCount));
            m_SurfaceCacheSpatialFilterRadius = Unpack(o.Find(x => x.surfaceCacheSpatialFilterRadius));
            m_SurfaceCacheTemporalPostFilterEnabled = Unpack(o.Find(x => x.surfaceCacheTemporalPostFilterEnabled));

            // Surface Cache - Screen Filtering
            m_SurfaceCacheLookupSampleCount = Unpack(o.Find(x => x.surfaceCacheLookupSampleCount));
            m_SurfaceCacheUpsamplingKernelSize = Unpack(o.Find(x => x.surfaceCacheUpsamplingKernelSize));
            m_SurfaceCacheUpsamplingSampleCount = Unpack(o.Find(x => x.surfaceCacheUpsamplingSampleCount));

            // Surface Cache - Advanced
            m_SurfaceCacheDefragCount = Unpack(o.Find(x => x.surfaceCacheDefragCount));

            // Surface Cache - Debug
            m_SurfaceCacheDebugEnabled = Unpack(o.Find(x => x.surfaceCacheDebugEnabled));
            m_SurfaceCacheDebugViewMode = Unpack(o.Find(x => x.surfaceCacheDebugViewMode));
            m_SurfaceCacheDebugShowSamplePosition = Unpack(o.Find(x => x.surfaceCacheDebugShowSamplePosition));

            // Screen Probes - General
            m_ScreenProbesEnabled = Unpack(o.Find(x => x.screenProbesEnabled));
            m_ScreenProbesQuality = Unpack(o.Find(x => x.screenProbesQuality));
            m_ScreenProbesIntensity = Unpack(o.Find(x => x.screenProbesIntensity));

            // Screen Probes - Tracing
            m_ScreenProbesMaxRayDistance = Unpack(o.Find(x => x.screenProbesMaxRayDistance));
            m_ScreenProbesNearFieldDistance = Unpack(o.Find(x => x.screenProbesNearFieldDistance));
            m_ScreenProbesUseImportanceSampling = Unpack(o.Find(x => x.screenProbesUseImportanceSampling));
            m_ScreenProbesUseSurfaceCacheFallback = Unpack(o.Find(x => x.screenProbesUseSurfaceCacheFallback));

            // Screen Probes - Temporal Filtering
            m_ScreenProbesTemporalFilterStrength = Unpack(o.Find(x => x.screenProbesTemporalFilterStrength));
            m_ScreenProbesDepthRejectionThreshold = Unpack(o.Find(x => x.screenProbesDepthRejectionThreshold));
            m_ScreenProbesNormalRejectionThreshold = Unpack(o.Find(x => x.screenProbesNormalRejectionThreshold));
            m_ScreenProbesEnableVarianceClamping = Unpack(o.Find(x => x.screenProbesEnableVarianceClamping));

            // Screen Probes - Spatial Filtering
            m_ScreenProbesSpatialFilterRadius = Unpack(o.Find(x => x.screenProbesSpatialFilterRadius));
            m_ScreenProbesSpatialFilterSamples = Unpack(o.Find(x => x.screenProbesSpatialFilterSamples));

            // Screen Probes - Debug
            m_ScreenProbesDebugVisualization = Unpack(o.Find(x => x.screenProbesDebugVisualization));
            m_ScreenProbesDebugShowOnlyProbes = Unpack(o.Find(x => x.screenProbesDebugShowOnlyProbes));

            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Technique, Styles.Technique);

            var technique = (GlobalIlluminationTechnique)m_Technique.value.intValue;

            if (technique == GlobalIlluminationTechnique.ReferencedPathTracing)
            {
                DrawPathTracingUI();
            }
            else if (technique == GlobalIlluminationTechnique.SurfaceCache)
            {
                DrawSurfaceCacheUI();
            }

            // Screen Probes (can be used with Surface Cache)
            if (technique == GlobalIlluminationTechnique.SurfaceCache)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Near-Field Enhancement", EditorStyles.boldLabel);
                DrawScreenProbesUI();
            }
        }

        void DrawPathTracingUI()
        {
            EditorGUILayout.Space();

            // General Settings
            DrawHeader(Styles.HeaderGeneral);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_PathTracingQuality, Styles.PathTracingQuality);
                PropertyField(m_PathTracingIntensity, Styles.PathTracingIntensity);
            }

            EditorGUILayout.Space();

            // Ray Settings
            DrawHeader(Styles.HeaderRaySettings);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_MaxBounces);
                PropertyField(m_SamplesPerPixel);
                PropertyField(m_RayLength);
                PropertyField(m_LayerMask);
            }

            EditorGUILayout.Space();

            // Quality Settings
            DrawHeader(Styles.HeaderQuality);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_FireflyClamp);
                PropertyField(m_UseRussianRoulette);
                if (m_UseRussianRoulette.value.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_RussianRouletteStartBounce);
                    }
                }
                PropertyField(m_UseNVSER);
                PropertyField(m_TextureLODBias);
            }

            EditorGUILayout.Space();

            // Denoising Section
            DrawHeader(Styles.HeaderDenoising);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_DenoiseMode, Styles.DenoiseMode);

                var denoiseMode = (PathTracingDenoiseMode)m_DenoiseMode.value.intValue;

                // Temporal Accumulation mode settings
                if (denoiseMode == PathTracingDenoiseMode.TemporalAccumulation)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Temporal Accumulation Settings", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_MaxAccumulatedFrames);
                        PropertyField(m_ResetOnCameraMove);
                        if (m_ResetOnCameraMove.value.boolValue)
                        {
                            PropertyField(m_CameraMovementThreshold);
                        }

                        EditorGUILayout.Space();

                        // Reprojection Rejection
                        EditorGUILayout.LabelField("History Reprojection Rejection", EditorStyles.boldLabel);
                        PropertyField(m_EnableReprojectionRejection);
                        if (m_EnableReprojectionRejection.value.boolValue)
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                PropertyField(m_ReprojectionDepthThreshold);
                                PropertyField(m_ReprojectionNormalThreshold);
                                PropertyField(m_ReprojectionRoughnessThreshold);
                                PropertyField(m_EnableVarianceClamping);
                                if (m_EnableVarianceClamping.value.boolValue)
                                {
                                    PropertyField(m_VarianceClampingGamma);
                                }
                                PropertyField(m_MinTemporalBlendWeight);
                            }
                        }
                    }
                }
                // NRD REBLUR mode settings
                else if (denoiseMode == PathTracingDenoiseMode.NRDReblur)
                {
                    EditorGUILayout.Space();

                    // Blur Radius
                    EditorGUILayout.LabelField(Styles.HeaderNRDBlurRadius, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdMinBlurRadius, Styles.NrdMinBlurRadius);
                        PropertyField(m_NrdMaxBlurRadius, Styles.NrdMaxBlurRadius);
                        PropertyField(m_NrdDiffusePrepassBlurRadius, Styles.NrdDiffusePrepassBlurRadius);
                        PropertyField(m_NrdSpecularPrepassBlurRadius, Styles.NrdSpecularPrepassBlurRadius);
                    }

                    EditorGUILayout.Space();

                    // Temporal Accumulation
                    EditorGUILayout.LabelField(Styles.HeaderNRDTemporalAccum, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdMaxAccumulatedFrameNum, Styles.NrdMaxAccumulatedFrameNum);
                        PropertyField(m_NrdMaxFastAccumulatedFrameNum, Styles.NrdMaxFastAccumulatedFrameNum);
                        PropertyField(m_NrdMaxStabilizedFrameNum, Styles.NrdMaxStabilizedFrameNum);
                        PropertyField(m_NrdHistoryFixFrameNum, Styles.NrdHistoryFixFrameNum);
                    }

                    EditorGUILayout.Space();

                    // Quality
                    EditorGUILayout.LabelField(Styles.HeaderNRDQuality, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdAntiFirefly, Styles.NrdAntiFirefly);
                        PropertyField(m_NrdFireflySuppressorMinRelativeScale, Styles.NrdFireflySuppressorScale);
                        PropertyField(m_NrdFastHistoryClampingSigmaScale, Styles.NrdFastHistoryClampingScale);
                        PropertyField(m_NrdMinHitDistanceWeight, Styles.NrdMinHitDistanceWeight);
                    }

                    EditorGUILayout.Space();

                    // Rejection
                    EditorGUILayout.LabelField(Styles.HeaderNRDRejection, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdLobeAngleFraction, Styles.NrdLobeAngleFraction);
                        PropertyField(m_NrdRoughnessFraction, Styles.NrdRoughnessFraction);
                        PropertyField(m_NrdPlaneDistanceSensitivity, Styles.NrdPlaneDistanceSensitivity);
                    }

                    EditorGUILayout.Space();

                    // Antilag
                    EditorGUILayout.LabelField(Styles.HeaderNRDAntilag, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdAntilagLuminanceSigmaScale, Styles.NrdAntilagLuminanceSigmaScale);
                        PropertyField(m_NrdAntilagLuminanceSensitivity, Styles.NrdAntilagLuminanceSensitivity);
                    }

                    EditorGUILayout.Space();

                    // Hit Distance Parameters
                    EditorGUILayout.LabelField(Styles.HeaderNRDHitDistance, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_NrdHitDistanceA, Styles.NrdHitDistanceA);
                        PropertyField(m_NrdHitDistanceB, Styles.NrdHitDistanceB);
                        PropertyField(m_NrdHitDistanceC, Styles.NrdHitDistanceC);
                        PropertyField(m_NrdHitDistanceD, Styles.NrdHitDistanceD);
                    }

                    EditorGUILayout.Space();

                    // Debug
                    PropertyField(m_NrdSplitScreen, Styles.NrdSplitScreen);
                }
#if DLSS_PLUGIN_INTEGRATE
                // DLSS-RR mode settings
                else if (denoiseMode == PathTracingDenoiseMode.RayReconstruction)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(Styles.HeaderDLSSRR, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_DlssRRQuality, Styles.DlssRRQuality);
                        PropertyField(m_DlssRRHitDistanceScale, Styles.DlssRRHitDistanceScale);

                        EditorGUILayout.Space();

                        // Exposure settings
                        EditorGUILayout.LabelField("Exposure", EditorStyles.boldLabel);
                        using (new EditorGUI.IndentLevelScope())
                        {
                            PropertyField(m_DlssRRIsHDR, Styles.DlssRRIsHDR);
                            PropertyField(m_DlssRRAutoExposure, Styles.DlssRRAutoExposure);
                            PropertyField(m_DlssRRPreExposure, Styles.DlssRRPreExposure);
                            if (m_DlssRRAutoExposure.value.boolValue)
                            {
                                PropertyField(m_DlssRRExposureScale, Styles.DlssRRExposureScale);
                            }
                        }

                        EditorGUILayout.Space();

                        // Quality settings
                        EditorGUILayout.LabelField("Quality", EditorStyles.boldLabel);
                        using (new EditorGUI.IndentLevelScope())
                        {
                            PropertyField(m_DlssRRSharpness, Styles.DlssRRSharpness);
                        }

                        EditorGUILayout.Space();

                        // History settings
                        EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
                        using (new EditorGUI.IndentLevelScope())
                        {
                            PropertyField(m_DlssRRResetHistory, Styles.DlssRRResetHistory);
                        }

                        EditorGUILayout.Space();

                        // Debug
                        PropertyField(m_DlssRRSplitScreen, Styles.DlssRRSplitScreen);
                    }
                }
#endif
            }

            EditorGUILayout.Space();

            // SHARC Settings
            DrawHeader(Styles.HeaderSharc);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_EnableSharc);
                if (m_EnableSharc.value.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SharcUpdate);
                        PropertyField(m_SharcQuery);
                        PropertyField(m_SharcSceneScale);
                        PropertyField(m_SharcEntriesK);
                        PropertyField(m_SharcRoughnessThreshold);
                        PropertyField(m_SharcRadianceScale);
                        PropertyField(m_SharcPropagationDepth);
                        PropertyField(m_SharcSampleThreshold);
                        PropertyField(m_SharcGridLevelBias);
                        PropertyField(m_SharcAntiFirefly);
                        PropertyField(m_SharcAccumulationFrames);
                        PropertyField(m_SharcStaleFrames);
                        PropertyField(m_SharcDebug);
                    }
                }
            }

            EditorGUILayout.Space();

            // Advanced Settings
            DrawHeader(Styles.HeaderAdvanced);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_EnvironmentIntensity);
                PropertyField(m_IncludeEmissive);
                PropertyField(m_IncludeDirectLighting);
                PropertyField(m_ReceiverMotionRejection);
            }

            EditorGUILayout.Space();

            // Debug Settings
            DrawHeader(Styles.HeaderDebug);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_DebugMode);
                PropertyField(m_DebugVisualizeBounce);
                PropertyField(m_DebugShowPathTracingOnly);
            }
        }

        void DrawSurfaceCacheUI()
        {
            EditorGUILayout.Space();

            // General Settings
            DrawHeader(Styles.HeaderSurfaceCacheGeneral);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheEstimationMethod);
                PropertyField(m_SurfaceCacheMultiBounce);
            }

            EditorGUILayout.Space();

            // Grid/Volume Settings
            DrawHeader(Styles.HeaderSurfaceCacheGrid);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheGridResolution);
                PropertyField(m_SurfaceCacheVolumeSize);
                PropertyField(m_SurfaceCacheCascadeCount);
                PropertyField(m_SurfaceCacheCascadeMovement);
            }

            EditorGUILayout.Space();

            // Estimation Settings (method-specific)
            DrawHeader(Styles.HeaderSurfaceCacheEstimation);
            using (new EditorGUI.IndentLevelScope())
            {
                var estimationMethod = (SurfaceCacheEstimationMethod)m_SurfaceCacheEstimationMethod.value.intValue;

                // Uniform Estimation
                if (estimationMethod == SurfaceCacheEstimationMethod.Uniform)
                {
                    EditorGUILayout.LabelField("Uniform Estimation", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SurfaceCacheUniformSampleCount);
                    }
                }
                // Restir Estimation
                else if (estimationMethod == SurfaceCacheEstimationMethod.Restir)
                {
                    EditorGUILayout.LabelField("Restir Estimation", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SurfaceCacheRestirConfidenceCap);
                        PropertyField(m_SurfaceCacheRestirSpatialSampleCount);
                        PropertyField(m_SurfaceCacheRestirSpatialFilterSize);
                        PropertyField(m_SurfaceCacheRestirValidationFrameInterval);
                    }
                }
                // RIS Estimation
                else if (estimationMethod == SurfaceCacheEstimationMethod.Ris)
                {
                    EditorGUILayout.LabelField("RIS Estimation", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SurfaceCacheRisCandidateCount);
                        PropertyField(m_SurfaceCacheRisTargetFunctionUpdateWeight);
                    }
                }
            }

            EditorGUILayout.Space();

            // Patch Filtering Settings
            DrawHeader(Styles.HeaderSurfaceCachePatchFiltering);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheTemporalSmoothing);
                PropertyField(m_SurfaceCacheSpatialFilterEnabled);
                if (m_SurfaceCacheSpatialFilterEnabled.value.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SurfaceCacheSpatialFilterSampleCount);
                        PropertyField(m_SurfaceCacheSpatialFilterRadius);
                    }
                }
                PropertyField(m_SurfaceCacheTemporalPostFilterEnabled);
            }

            EditorGUILayout.Space();

            // Screen Filtering Settings
            DrawHeader(Styles.HeaderSurfaceCacheScreenFiltering);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheLookupSampleCount);
                PropertyField(m_SurfaceCacheUpsamplingKernelSize);
                PropertyField(m_SurfaceCacheUpsamplingSampleCount);
            }

            EditorGUILayout.Space();

            // Advanced Settings
            DrawHeader(Styles.HeaderSurfaceCacheAdvanced);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheDefragCount);
            }

            EditorGUILayout.Space();

            // Debug Settings
            DrawHeader(Styles.HeaderSurfaceCacheDebug);
            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheDebugEnabled);
                if (m_SurfaceCacheDebugEnabled.value.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        PropertyField(m_SurfaceCacheDebugViewMode);
                        PropertyField(m_SurfaceCacheDebugShowSamplePosition);
                    }
                }
            }
        }

        void DrawScreenProbesUI()
        {
            // General Settings
            PropertyField(m_ScreenProbesEnabled);

            if (!m_ScreenProbesEnabled.value.boolValue)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.Space();

                // Quality
                EditorGUILayout.LabelField("Quality", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ScreenProbesQuality);
                    PropertyField(m_ScreenProbesIntensity);
                }

                EditorGUILayout.Space();

                // Tracing
                EditorGUILayout.LabelField("Tracing", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ScreenProbesMaxRayDistance);
                    PropertyField(m_ScreenProbesNearFieldDistance);
                    PropertyField(m_ScreenProbesUseSurfaceCacheFallback);
                    PropertyField(m_ScreenProbesUseImportanceSampling);
                }

                EditorGUILayout.Space();

                // Temporal Filtering
                EditorGUILayout.LabelField("Temporal Filtering", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ScreenProbesTemporalFilterStrength);
                    PropertyField(m_ScreenProbesDepthRejectionThreshold);
                    PropertyField(m_ScreenProbesNormalRejectionThreshold);
                    PropertyField(m_ScreenProbesEnableVarianceClamping);
                }

                EditorGUILayout.Space();

                // Spatial Filtering
                EditorGUILayout.LabelField("Spatial Filtering", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ScreenProbesSpatialFilterRadius);
                    PropertyField(m_ScreenProbesSpatialFilterSamples);
                }

                EditorGUILayout.Space();

                // Debug
                EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ScreenProbesDebugVisualization);
                    if (m_ScreenProbesDebugVisualization.value.boolValue)
                    {
                        PropertyField(m_ScreenProbesDebugShowOnlyProbes);
                    }
                }
            }
        }

        void DrawHeader(GUIContent header)
        {
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        }
    }
}
