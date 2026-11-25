using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor
{
    /// <summary>
    /// Streamlined Unreal Lumen-style Volume Inspector for <see cref="SdfDiffuseGISettings"/>.
    /// Exposes core GI and Reflections controls by default, keeping fine-tuning and diagnostics
    /// behind Unity's Additional Properties toggle.
    /// </summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(SdfDiffuseGISettings))]
    internal sealed class SdfDiffuseGISettingsVolumeEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_EnabledLabel =
            EditorGUIUtility.TrTextContent("Enabled", "Enable Lumen-style SDF Diffuse GI and Reflections for this volume.");
        private static readonly GUIContent s_TraceModeLabel =
            EditorGUIUtility.TrTextContent("Trace Mode", "Hybrid combines Screen Traces with Mesh SDF and Global SDF (Lumen default).");
        private static readonly GUIContent s_BounceOrdersLabel =
            EditorGUIUtility.TrTextContent("Bounce Orders", "Additional diffuse bounce orders propagated through the Surface Cache.");
        private static readonly GUIContent s_DetailDistanceLabel =
            EditorGUIUtility.TrTextContent("Detail Trace Distance", "Near-field Mesh SDF detail trace range before switching to Global SDF (meters).");
        private static readonly GUIContent s_MaxTraceDistanceLabel =
            EditorGUIUtility.TrTextContent("Max Trace Distance", "Maximum world-space trace distance for far radiance cache and sky visibility (meters).");
        private static readonly GUIContent s_ShortRangeAOLabel =
            EditorGUIUtility.TrTextContent("Short-Range AO", "Strength of screen-space bent-normal contact ambient occlusion on indirect diffuse lighting.");
        private static readonly GUIContent s_AdaptiveBudgetLabel =
            EditorGUIUtility.TrTextContent("Adaptive Ray Budget", "Concentrate Screen Probe rays onto high-variance, high-contribution directions and cull redundant back-facing rays on converged probes.");
        private static readonly GUIContent s_MinRayFractionLabel =
            EditorGUIUtility.TrTextContent("Min Ray Fraction", "Minimum active ray budget fraction on converged low-variance probes (0.5 = balanced, 1.0 = maximum quality).");
        private static readonly GUIContent s_TemporalHistoryLabel =
            EditorGUIUtility.TrTextContent("Temporal History", "Maximum accumulated history frames for Screen Probe GI temporal stability (Lumen default: 10).");

        private static readonly GUIContent s_ReflectionsEnabledLabel =
            EditorGUIUtility.TrTextContent("Reflections", "Enable dedicated Lumen specular reflections (Screen Trace + Mesh SDF + Global SDF + native denoiser).");
        private static readonly GUIContent s_ReflectionDownsampleLabel =
            EditorGUIUtility.TrTextContent("Downsample Factor", "Reflection ray tracing resolution divisor (1 = full resolution, 2 = half resolution).");
        private static readonly GUIContent s_ReflectionMaxRoughnessLabel =
            EditorGUIUtility.TrTextContent("Max Roughness To Trace", "Maximum perceptual roughness that traces dedicated reflection rays before fading into Rough Specular.");
        private static readonly GUIContent s_ReflectionFadeLengthLabel =
            EditorGUIUtility.TrTextContent("Roughness Fade Length", "Transition width over which dedicated reflections blend into Rough Specular.");
        private static readonly GUIContent s_ReflectionScreenTracesLabel =
            EditorGUIUtility.TrTextContent("Screen Traces", "Trace HZB screen space before falling back to Mesh SDF and Global SDF.");
        private static readonly GUIContent s_RoughSpecularLabel =
            EditorGUIUtility.TrTextContent("Rough Specular", "Integrate occluded rough specular and sky visibility from Screen Probes for rougher surfaces.");

        // Core artist-facing Lumen parameters
        private SerializedDataParameter m_Enabled;
        private SerializedDataParameter m_TraceMode;
        private SerializedDataParameter m_SurfaceCacheBounceOrders;
        private SerializedDataParameter m_MeshSdfDetailDistance;
        private SerializedDataParameter m_FarTraceDistance;
        private SerializedDataParameter m_NearOcclusionStrength;
        private SerializedDataParameter m_ScreenProbeAdaptiveRayBudget;
        private SerializedDataParameter m_ScreenProbeMinAdaptiveRayFraction;
        private SerializedDataParameter m_PixelHistoryMaxAge;

        // Core Lumen Reflections parameters
        private SerializedDataParameter m_LumenReflections;
        private SerializedDataParameter m_LumenReflectionDownsampleFactor;
        private SerializedDataParameter m_LumenReflectionMaxRoughnessToTrace;
        private SerializedDataParameter m_LumenReflectionRoughnessFadeLength;
        private SerializedDataParameter m_LumenReflectionScreenTraces;
        private SerializedDataParameter m_ScreenProbeRoughSpecular;

        // Additional / Expert parameters: Surface Cache Radiosity
        private SerializedDataParameter m_SurfaceCacheRadiosityFeedback;
        private SerializedDataParameter m_SurfaceCacheRadiositySamples;
        private SerializedDataParameter m_SurfaceCacheRadiosityAccumulate;
        private SerializedDataParameter m_SurfaceCacheProgressiveRadiosity;
        private SerializedDataParameter m_SurfaceCacheLightingTolerance;
        private SerializedDataParameter m_SurfaceCacheUpdateBoost;
        private SerializedDataParameter m_SurfaceCacheRelightSweeps;
        private SerializedDataParameter m_SurfaceCacheRadiosityBatchLimit;
        private SerializedDataParameter m_SurfaceCacheCardsPerFrame;
        private SerializedDataParameter m_SurfaceCacheFullUpdateCardLimit;
        private SerializedDataParameter m_SurfaceCacheSampledLighting;
        private SerializedDataParameter m_SurfaceCacheSampledLightingHardwareRayTracing;
        private SerializedDataParameter m_SurfaceCacheSampledLightingIterations;
        private SerializedDataParameter m_SurfaceCacheSampledLightingCandidates;

        // Additional / Expert parameters: Tracing & Sky
        private SerializedDataParameter m_LumenScreenTracing;
        private SerializedDataParameter m_SegmentedSdfTracing;
        private SerializedDataParameter m_FarRadianceCache;
        private SerializedDataParameter m_OccludedSky;
        private SerializedDataParameter m_SkyTraceDistance;
        private SerializedDataParameter m_CorrectReceiverSelfHits;
        private SerializedDataParameter m_ScreenHitCardLighting;

        // Additional / Expert parameters: Screen Probe Gather & Temporal
        private SerializedDataParameter m_GatherMethod;
        private SerializedDataParameter m_ScreenProbeFullSphere;
        private SerializedDataParameter m_ScreenProbeDitherTraceDirections;
        private SerializedDataParameter m_ScreenProbeStochasticInterpolation;
        private SerializedDataParameter m_ScreenProbeTemporalFilter;
        private SerializedDataParameter m_ScreenProbeMaxRayIntensity;
        private SerializedDataParameter m_ScreenProbeSamplingGuide;
        private SerializedDataParameter m_ScreenProbeHierarchicalGuide;
        private SerializedDataParameter m_ScreenProbeImportanceSubdivisionLevels;
        private SerializedDataParameter m_ScreenProbeImportanceVarianceWeight;
        private SerializedDataParameter m_AdaptiveScreenProbes;
        private SerializedDataParameter m_ScreenProbeSpatialFilter;
        private SerializedDataParameter m_ScreenProbeSpatialKernel;
        private SerializedDataParameter m_ScreenProbeHitDistanceFilter;
        private SerializedDataParameter m_RestirOpenPbrDiffuse;
        private SerializedDataParameter m_PixelTemporal;
        private SerializedDataParameter m_ScreenProbeBentNormalAO;
        private SerializedDataParameter m_ScreenProbeBentNormalApplyDuringIntegration;
        private SerializedDataParameter m_DirectionalNearOcclusion;
        private SerializedDataParameter m_NearOcclusionRadius;
        private SerializedDataParameter m_ScreenProbeTemporalRejectBasedOnNormal;
        private SerializedDataParameter m_ScreenProbeTemporalPlaneDisocclusion;
        private SerializedDataParameter m_ScreenProbeCoherentRadianceFastUpdate;
        private SerializedDataParameter m_ScreenProbeDisocclusionSpatialFilter;
        private SerializedDataParameter m_ScreenProbeFastUpdateNeighborhoodClamp;
        private SerializedDataParameter m_SpatialDenoise;

        // Additional / Expert parameters: Lumen Reflections
        private SerializedDataParameter m_LumenReflectionMaxRoughnessToTraceForFoliage;
        private SerializedDataParameter m_LumenReflectionSmoothBias;
        private SerializedDataParameter m_LumenReflectionGGXSamplingBias;
        private SerializedDataParameter m_LumenReflectionMaxRayIntensity;
        private SerializedDataParameter m_LumenReflectionMeshSdfTraces;
        private SerializedDataParameter m_LumenReflectionScreenSpaceReconstruction;
        private SerializedDataParameter m_LumenReflectionReconstructionSamples;
        private SerializedDataParameter m_LumenReflectionReconstructionKernelRadius;
        private SerializedDataParameter m_LumenReflectionTemporalFilter;
        private SerializedDataParameter m_LumenReflectionTemporalMaxFramesAccumulated;
        private SerializedDataParameter m_LumenReflectionTemporalNeighborhoodClampScale;
        private SerializedDataParameter m_LumenReflectionTemporalDistanceThreshold;
        private SerializedDataParameter m_LumenReflectionDenoiserTonemapRange;
        private SerializedDataParameter m_LumenReflectionBilateralFilter;
        private SerializedDataParameter m_LumenReflectionBilateralFilterKernelRadius;
        private SerializedDataParameter m_LumenReflectionBilateralFilterNumSamples;
        private SerializedDataParameter m_LumenReflectionBilateralFilterDepthWeightScale;

        // Additional / Diagnostics
        private SerializedDataParameter m_RealtimeOnly;
        private SerializedDataParameter m_OffscreenBounceOnly;
        private SerializedDataParameter m_RecordRayDiagnostics;
        private SerializedDataParameter m_RecordFirstHitDiagnostics;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<SdfDiffuseGISettings>(serializedObject);

            m_Enabled = Unpack(o.Find(x => x.enabled));
            m_TraceMode = Unpack(o.Find(x => x.traceMode));
            m_SurfaceCacheBounceOrders = Unpack(o.Find(x => x.surfaceCacheBounceOrders));
            m_MeshSdfDetailDistance = Unpack(o.Find(x => x.meshSdfDetailDistance));
            m_FarTraceDistance = Unpack(o.Find(x => x.farTraceDistance));
            m_NearOcclusionStrength = Unpack(o.Find(x => x.nearOcclusionStrength));
            m_ScreenProbeAdaptiveRayBudget = Unpack(o.Find(x => x.screenProbeAdaptiveRayBudget));
            m_ScreenProbeMinAdaptiveRayFraction = Unpack(o.Find(x => x.screenProbeMinAdaptiveRayFraction));
            m_PixelHistoryMaxAge = Unpack(o.Find(x => x.pixelHistoryMaxAge));

            m_LumenReflections = Unpack(o.Find(x => x.lumenReflections));
            m_LumenReflectionDownsampleFactor = Unpack(o.Find(x => x.lumenReflectionDownsampleFactor));
            m_LumenReflectionMaxRoughnessToTrace = Unpack(o.Find(x => x.lumenReflectionMaxRoughnessToTrace));
            m_LumenReflectionRoughnessFadeLength = Unpack(o.Find(x => x.lumenReflectionRoughnessFadeLength));
            m_LumenReflectionScreenTraces = Unpack(o.Find(x => x.lumenReflectionScreenTraces));
            m_ScreenProbeRoughSpecular = Unpack(o.Find(x => x.screenProbeRoughSpecular));

            m_SurfaceCacheRadiosityFeedback = Unpack(o.Find(x => x.surfaceCacheRadiosityFeedback));
            m_SurfaceCacheRadiositySamples = Unpack(o.Find(x => x.surfaceCacheRadiositySamples));
            m_SurfaceCacheRadiosityAccumulate = Unpack(o.Find(x => x.surfaceCacheRadiosityAccumulate));
            m_SurfaceCacheProgressiveRadiosity = Unpack(o.Find(x => x.surfaceCacheProgressiveRadiosity));
            m_SurfaceCacheLightingTolerance = Unpack(o.Find(x => x.surfaceCacheLightingTolerance));
            m_SurfaceCacheUpdateBoost = Unpack(o.Find(x => x.surfaceCacheUpdateBoost));
            m_SurfaceCacheRelightSweeps = Unpack(o.Find(x => x.surfaceCacheRelightSweeps));
            m_SurfaceCacheRadiosityBatchLimit = Unpack(o.Find(x => x.surfaceCacheRadiosityBatchLimit));
            m_SurfaceCacheCardsPerFrame = Unpack(o.Find(x => x.surfaceCacheCardsPerFrame));
            m_SurfaceCacheFullUpdateCardLimit = Unpack(o.Find(x => x.surfaceCacheFullUpdateCardLimit));
            m_SurfaceCacheSampledLighting = Unpack(o.Find(x => x.surfaceCacheSampledLighting));
            m_SurfaceCacheSampledLightingHardwareRayTracing = Unpack(o.Find(x => x.surfaceCacheSampledLightingHardwareRayTracing));
            m_SurfaceCacheSampledLightingIterations = Unpack(o.Find(x => x.surfaceCacheSampledLightingIterations));
            m_SurfaceCacheSampledLightingCandidates = Unpack(o.Find(x => x.surfaceCacheSampledLightingCandidates));

            m_LumenScreenTracing = Unpack(o.Find(x => x.lumenScreenTracing));
            m_SegmentedSdfTracing = Unpack(o.Find(x => x.segmentedSdfTracing));
            m_FarRadianceCache = Unpack(o.Find(x => x.farRadianceCache));
            m_OccludedSky = Unpack(o.Find(x => x.occludedSky));
            m_SkyTraceDistance = Unpack(o.Find(x => x.skyTraceDistance));
            m_CorrectReceiverSelfHits = Unpack(o.Find(x => x.correctReceiverSelfHits));
            m_ScreenHitCardLighting = Unpack(o.Find(x => x.screenHitCardLighting));

            m_GatherMethod = Unpack(o.Find(x => x.gatherMethod));
            m_ScreenProbeFullSphere = Unpack(o.Find(x => x.screenProbeFullSphere));
            m_ScreenProbeDitherTraceDirections = Unpack(o.Find(x => x.screenProbeDitherTraceDirections));
            m_ScreenProbeStochasticInterpolation = Unpack(o.Find(x => x.screenProbeStochasticInterpolation));
            m_ScreenProbeTemporalFilter = Unpack(o.Find(x => x.screenProbeTemporalFilter));
            m_ScreenProbeMaxRayIntensity = Unpack(o.Find(x => x.screenProbeMaxRayIntensity));
            m_ScreenProbeSamplingGuide = Unpack(o.Find(x => x.screenProbeSamplingGuide));
            m_ScreenProbeHierarchicalGuide = Unpack(o.Find(x => x.screenProbeHierarchicalGuide));
            m_ScreenProbeImportanceSubdivisionLevels = Unpack(o.Find(x => x.screenProbeImportanceSubdivisionLevels));
            m_ScreenProbeImportanceVarianceWeight = Unpack(o.Find(x => x.screenProbeImportanceVarianceWeight));
            m_AdaptiveScreenProbes = Unpack(o.Find(x => x.adaptiveScreenProbes));
            m_ScreenProbeSpatialFilter = Unpack(o.Find(x => x.screenProbeSpatialFilter));
            m_ScreenProbeSpatialKernel = Unpack(o.Find(x => x.screenProbeSpatialKernel));
            m_ScreenProbeHitDistanceFilter = Unpack(o.Find(x => x.screenProbeHitDistanceFilter));
            m_RestirOpenPbrDiffuse = Unpack(o.Find(x => x.restirOpenPbrDiffuse));
            m_PixelTemporal = Unpack(o.Find(x => x.pixelTemporal));
            m_ScreenProbeBentNormalAO = Unpack(o.Find(x => x.screenProbeBentNormalAO));
            m_ScreenProbeBentNormalApplyDuringIntegration = Unpack(o.Find(x => x.screenProbeBentNormalApplyDuringIntegration));
            m_DirectionalNearOcclusion = Unpack(o.Find(x => x.directionalNearOcclusion));
            m_NearOcclusionRadius = Unpack(o.Find(x => x.nearOcclusionRadius));
            m_ScreenProbeTemporalRejectBasedOnNormal = Unpack(o.Find(x => x.screenProbeTemporalRejectBasedOnNormal));
            m_ScreenProbeTemporalPlaneDisocclusion = Unpack(o.Find(x => x.screenProbeTemporalPlaneDisocclusion));
            m_ScreenProbeCoherentRadianceFastUpdate = Unpack(o.Find(x => x.screenProbeCoherentRadianceFastUpdate));
            m_ScreenProbeDisocclusionSpatialFilter = Unpack(o.Find(x => x.screenProbeDisocclusionSpatialFilter));
            m_ScreenProbeFastUpdateNeighborhoodClamp = Unpack(o.Find(x => x.screenProbeFastUpdateNeighborhoodClamp));
            m_SpatialDenoise = Unpack(o.Find(x => x.spatialDenoise));

            m_LumenReflectionMaxRoughnessToTraceForFoliage = Unpack(o.Find(x => x.lumenReflectionMaxRoughnessToTraceForFoliage));
            m_LumenReflectionSmoothBias = Unpack(o.Find(x => x.lumenReflectionSmoothBias));
            m_LumenReflectionGGXSamplingBias = Unpack(o.Find(x => x.lumenReflectionGGXSamplingBias));
            m_LumenReflectionMaxRayIntensity = Unpack(o.Find(x => x.lumenReflectionMaxRayIntensity));
            m_LumenReflectionMeshSdfTraces = Unpack(o.Find(x => x.lumenReflectionMeshSdfTraces));
            m_LumenReflectionScreenSpaceReconstruction = Unpack(o.Find(x => x.lumenReflectionScreenSpaceReconstruction));
            m_LumenReflectionReconstructionSamples = Unpack(o.Find(x => x.lumenReflectionReconstructionSamples));
            m_LumenReflectionReconstructionKernelRadius = Unpack(o.Find(x => x.lumenReflectionReconstructionKernelRadius));
            m_LumenReflectionTemporalFilter = Unpack(o.Find(x => x.lumenReflectionTemporalFilter));
            m_LumenReflectionTemporalMaxFramesAccumulated = Unpack(o.Find(x => x.lumenReflectionTemporalMaxFramesAccumulated));
            m_LumenReflectionTemporalNeighborhoodClampScale = Unpack(o.Find(x => x.lumenReflectionTemporalNeighborhoodClampScale));
            m_LumenReflectionTemporalDistanceThreshold = Unpack(o.Find(x => x.lumenReflectionTemporalDistanceThreshold));
            m_LumenReflectionDenoiserTonemapRange = Unpack(o.Find(x => x.lumenReflectionDenoiserTonemapRange));
            m_LumenReflectionBilateralFilter = Unpack(o.Find(x => x.lumenReflectionBilateralFilter));
            m_LumenReflectionBilateralFilterKernelRadius = Unpack(o.Find(x => x.lumenReflectionBilateralFilterKernelRadius));
            m_LumenReflectionBilateralFilterNumSamples = Unpack(o.Find(x => x.lumenReflectionBilateralFilterNumSamples));
            m_LumenReflectionBilateralFilterDepthWeightScale = Unpack(o.Find(x => x.lumenReflectionBilateralFilterDepthWeightScale));

            m_RealtimeOnly = Unpack(o.Find(x => x.realtimeOnly));
            m_OffscreenBounceOnly = Unpack(o.Find(x => x.offscreenBounceOnly));
            m_RecordRayDiagnostics = Unpack(o.Find(x => x.recordRayDiagnostics));
            m_RecordFirstHitDiagnostics = Unpack(o.Find(x => x.recordFirstHitDiagnostics));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Enabled, s_EnabledLabel);
            bool disabled = m_Enabled.overrideState.boolValue && !m_Enabled.value.boolValue;

            using (new EditorGUI.DisabledScope(disabled))
            {
                DrawGlobalIlluminationSection();
                DrawLumenReflectionsSection();

                if (showAdditionalProperties)
                {
                    DrawAdditionalSurfaceCacheSection();
                    DrawAdditionalTracingSection();
                    DrawAdditionalScreenProbeSection();
                    DrawAdditionalReflectionsSection();
                    DrawDiagnosticsSection();
                }
            }
        }

        private void DrawGlobalIlluminationSection()
        {
            DrawSectionHeader("Global Illumination");
            PropertyField(m_TraceMode, s_TraceModeLabel);

            bool usesSdf = !m_TraceMode.overrideState.boolValue
                || (SdfDiffuseTraceMode)m_TraceMode.value.intValue != SdfDiffuseTraceMode.ScreenOnly;
            if (usesSdf)
            {
                PropertyField(m_SurfaceCacheBounceOrders, s_BounceOrdersLabel);
                PropertyField(m_MeshSdfDetailDistance, s_DetailDistanceLabel);
                PropertyField(m_FarTraceDistance, s_MaxTraceDistanceLabel);
            }

            PropertyField(m_NearOcclusionStrength, s_ShortRangeAOLabel);
            PropertyField(m_ScreenProbeAdaptiveRayBudget, s_AdaptiveBudgetLabel);
            if (!m_ScreenProbeAdaptiveRayBudget.overrideState.boolValue || m_ScreenProbeAdaptiveRayBudget.value.boolValue)
            {
                using (new IndentLevelScope())
                    PropertyField(m_ScreenProbeMinAdaptiveRayFraction, s_MinRayFractionLabel);
            }

            PropertyField(m_PixelHistoryMaxAge, s_TemporalHistoryLabel);
        }

        private void DrawLumenReflectionsSection()
        {
            DrawSectionHeader("Lumen Reflections");
            PropertyField(m_LumenReflections, s_ReflectionsEnabledLabel);

            bool reflectionsEnabled = !m_LumenReflections.overrideState.boolValue || m_LumenReflections.value.boolValue;
            using (new EditorGUI.DisabledScope(!reflectionsEnabled))
            using (new IndentLevelScope())
            {
                PropertyField(m_LumenReflectionDownsampleFactor, s_ReflectionDownsampleLabel);
                PropertyField(m_LumenReflectionMaxRoughnessToTrace, s_ReflectionMaxRoughnessLabel);
                PropertyField(m_LumenReflectionRoughnessFadeLength, s_ReflectionFadeLengthLabel);
                PropertyField(m_LumenReflectionScreenTraces, s_ReflectionScreenTracesLabel);
            }

            PropertyField(m_ScreenProbeRoughSpecular, s_RoughSpecularLabel);
        }

        private void DrawAdditionalSurfaceCacheSection()
        {
            DrawSectionHeader("Surface Cache Radiosity (Advanced)");
            PropertyField(m_SurfaceCacheRadiosityFeedback);
            PropertyField(m_SurfaceCacheRadiositySamples);
            PropertyField(m_SurfaceCacheRadiosityAccumulate);
            PropertyField(m_SurfaceCacheProgressiveRadiosity);
            PropertyField(m_SurfaceCacheLightingTolerance);
            PropertyField(m_SurfaceCacheUpdateBoost);
            PropertyField(m_SurfaceCacheRelightSweeps);
            PropertyField(m_SurfaceCacheRadiosityBatchLimit);
            PropertyField(m_SurfaceCacheCardsPerFrame);
            PropertyField(m_SurfaceCacheFullUpdateCardLimit);

            DrawSectionHeader("Surface Cache Sampled Lighting (Advanced)");
            PropertyField(m_SurfaceCacheSampledLighting);
            bool sampledEnabled = !m_SurfaceCacheSampledLighting.overrideState.boolValue
                || m_SurfaceCacheSampledLighting.value.boolValue;
            using (new EditorGUI.DisabledScope(!sampledEnabled))
            using (new IndentLevelScope())
            {
                PropertyField(m_SurfaceCacheSampledLightingHardwareRayTracing);
                PropertyField(m_SurfaceCacheSampledLightingIterations);
                PropertyField(m_SurfaceCacheSampledLightingCandidates);
            }
        }

        private void DrawAdditionalTracingSection()
        {
            DrawSectionHeader("Tracing & Sky (Advanced)");
            PropertyField(m_LumenScreenTracing);
            PropertyField(m_SegmentedSdfTracing);
            PropertyField(m_FarRadianceCache);
            PropertyField(m_OccludedSky);
            PropertyField(m_SkyTraceDistance);
            PropertyField(m_CorrectReceiverSelfHits);
            PropertyField(m_ScreenHitCardLighting);
        }

        private void DrawAdditionalScreenProbeSection()
        {
            DrawSectionHeader("Screen Probe Gather (Advanced)");
            PropertyField(m_GatherMethod);
            bool isRestir = m_GatherMethod.overrideState.boolValue
                && (SdfDiffuseGatherMethod)m_GatherMethod.value.intValue == SdfDiffuseGatherMethod.ReSTIRGather;
            if (isRestir)
                PropertyField(m_RestirOpenPbrDiffuse);

            PropertyField(m_ScreenProbeFullSphere);
            bool fullSphere = !m_ScreenProbeFullSphere.overrideState.boolValue || m_ScreenProbeFullSphere.value.boolValue;

            PropertyField(m_ScreenProbeSamplingGuide);
            PropertyField(m_ScreenProbeImportanceSubdivisionLevels);
            PropertyField(m_ScreenProbeImportanceVarianceWeight);
            PropertyField(m_ScreenProbeMaxRayIntensity);
            PropertyField(m_ScreenProbeDitherTraceDirections);
            PropertyField(m_ScreenProbeStochasticInterpolation);
            PropertyField(m_ScreenProbeTemporalFilter);
            PropertyField(m_AdaptiveScreenProbes);
            PropertyField(m_ScreenProbeSpatialFilter);
            if (!fullSphere)
            {
                PropertyField(m_ScreenProbeHierarchicalGuide);
                PropertyField(m_ScreenProbeSpatialKernel);
                PropertyField(m_ScreenProbeHitDistanceFilter);
            }

            PropertyField(m_ScreenProbeBentNormalAO);
            PropertyField(m_ScreenProbeBentNormalApplyDuringIntegration);
            PropertyField(m_DirectionalNearOcclusion);
            PropertyField(m_NearOcclusionRadius);

            PropertyField(m_PixelTemporal);
            PropertyField(m_ScreenProbeTemporalRejectBasedOnNormal);
            PropertyField(m_ScreenProbeTemporalPlaneDisocclusion);
            PropertyField(m_ScreenProbeCoherentRadianceFastUpdate);
            PropertyField(m_ScreenProbeDisocclusionSpatialFilter);
            PropertyField(m_ScreenProbeFastUpdateNeighborhoodClamp);
            PropertyField(m_SpatialDenoise);
        }

        private void DrawAdditionalReflectionsSection()
        {
            DrawSectionHeader("Lumen Reflections (Advanced)");
            PropertyField(m_LumenReflectionMaxRoughnessToTraceForFoliage);
            PropertyField(m_LumenReflectionSmoothBias);
            PropertyField(m_LumenReflectionGGXSamplingBias);
            PropertyField(m_LumenReflectionMaxRayIntensity);
            PropertyField(m_LumenReflectionMeshSdfTraces);
            PropertyField(m_LumenReflectionScreenSpaceReconstruction);
            PropertyField(m_LumenReflectionReconstructionSamples);
            PropertyField(m_LumenReflectionReconstructionKernelRadius);
            PropertyField(m_LumenReflectionTemporalFilter);
            PropertyField(m_LumenReflectionTemporalMaxFramesAccumulated);
            PropertyField(m_LumenReflectionTemporalNeighborhoodClampScale);
            PropertyField(m_LumenReflectionTemporalDistanceThreshold);
            PropertyField(m_LumenReflectionDenoiserTonemapRange);
            PropertyField(m_LumenReflectionBilateralFilter);
            PropertyField(m_LumenReflectionBilateralFilterKernelRadius);
            PropertyField(m_LumenReflectionBilateralFilterNumSamples);
            PropertyField(m_LumenReflectionBilateralFilterDepthWeightScale);
        }

        private void DrawDiagnosticsSection()
        {
            DrawSectionHeader("Diagnostics");
            PropertyField(m_RealtimeOnly);
            PropertyField(m_OffscreenBounceOnly);
            PropertyField(m_RecordRayDiagnostics);
            PropertyField(m_RecordFirstHitDiagnostics);
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
