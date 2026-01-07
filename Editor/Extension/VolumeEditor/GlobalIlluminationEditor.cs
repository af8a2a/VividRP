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

        void DrawHeader(GUIContent header)
        {
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        }
    }
}
