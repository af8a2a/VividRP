using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;


namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(GlobalIllumination))]
    class GlobalIlluminatorEditor : VolumeComponentEditor
    {
        // Shared rasterization / ray tracing parameter
        SerializedDataParameter m_Enable;
        SerializedDataParameter m_Tracing;
        SerializedDataParameter m_RayMiss;
        SerializedDataParameter m_APVMask;

        // Screen space global illumination parameters
        SerializedDataParameter m_FullResolutionSS;
        SerializedDataParameter m_DepthBufferThickness;
        SerializedDataParameter m_RaySteps;

        // Ray tracing generic attributes
        SerializedDataParameter m_LastBounce;
        SerializedDataParameter m_AmbientProbeDimmer;
        SerializedDataParameter m_LayerMask;
        SerializedDataParameter m_ReceiverMotionRejection;
        SerializedDataParameter m_TextureLodBias;
        SerializedDataParameter m_RayLength;
        SerializedDataParameter m_ClampValue;
        SerializedDataParameter m_Mode;

        // Mixed
        SerializedDataParameter m_MaxMixedRaySteps;

        // Performance
        SerializedDataParameter m_FullResolution;

        // Quality
        SerializedDataParameter m_SampleCount;
        SerializedDataParameter m_BounceCount;

        // Filtering RT
        SerializedDataParameter m_Denoise;
        SerializedDataParameter m_HalfResolutionDenoiser;
        SerializedDataParameter m_DenoiserRadius;
        SerializedDataParameter m_SecondDenoiserPass;

        // Filtering SS
        SerializedDataParameter m_DenoiseSS;
        SerializedDataParameter m_HalfResolutionDenoiserSS;
        SerializedDataParameter m_DenoiserRadiusSS;
        SerializedDataParameter m_SecondDenoiserPassSS;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<GlobalIllumination>(serializedObject);

            m_Enable = Unpack(o.Find(x => x.enable));
            m_Tracing = Unpack(o.Find(x => x.tracing));
            m_RayMiss = Unpack(o.Find(x => x.rayMiss));
            m_APVMask = Unpack(o.Find(x => x.adaptiveProbeVolumesLayerMask));

            // SSGI Parameters
            m_FullResolutionSS = Unpack(o.Find(x => x.fullResolutionSS));
            m_DepthBufferThickness = Unpack(o.Find(x => x.depthBufferThickness));
            m_RaySteps = Unpack(o.Find(x => x.maxRaySteps));

            // Ray Tracing shared parameters
            m_LastBounce = Unpack(o.Find(x => x.lastBounceFallbackHierarchy));
            m_AmbientProbeDimmer = Unpack(o.Find(x => x.ambientProbeDimmer));
            m_LayerMask = Unpack(o.Find(x => x.layerMask));
            m_ReceiverMotionRejection = Unpack(o.Find(x => x.receiverMotionRejection));
            m_TextureLodBias = Unpack(o.Find(x => x.textureLodBias));
            m_RayLength = Unpack(o.Find(x => x.rayLength));
            m_ClampValue = Unpack(o.Find(x => x.clampValue));
            m_Mode = Unpack(o.Find(x => x.mode));

            // Mixed
            m_MaxMixedRaySteps = Unpack(o.Find(x => x.maxMixedRaySteps));

            // Performance
            m_FullResolution = Unpack(o.Find(x => x.fullResolution));

            // Quality
            m_SampleCount = Unpack(o.Find(x => x.sampleCount));
            m_BounceCount = Unpack(o.Find(x => x.bounceCount));

            // Filtering
            m_Denoise = Unpack(o.Find(x => x.denoise));
            m_HalfResolutionDenoiser = Unpack(o.Find(x => x.halfResolutionDenoiser));
            m_DenoiserRadius = Unpack(o.Find(x => x.denoiserRadius));
            m_SecondDenoiserPass = Unpack(o.Find(x => x.secondDenoiserPass));

            // Filtering SS
            m_DenoiseSS = Unpack(o.Find(x => x.denoiseSS));
            m_HalfResolutionDenoiserSS = Unpack(o.Find(x => x.halfResolutionDenoiserSS));
            m_DenoiserRadiusSS = Unpack(o.Find(x => x.denoiserRadiusSS));
            m_SecondDenoiserPassSS = Unpack(o.Find(x => x.secondDenoiserPassSS));

            base.OnEnable();
        }

        static public readonly GUIContent k_RayLengthText = EditorGUIUtility.TrTextContent("Max Ray Length",
            "Controls the maximal length of global illumination rays in meters. The higher this value is, the more expensive ray traced global illumination is.");

        static public readonly GUIContent k_FullResolutionSSText = EditorGUIUtility.TrTextContent("Full Resolution",
            "Controls if the screen space global illumination should be evaluated at half resolution.");

        static public readonly GUIContent k_DepthBufferThicknessText =
            EditorGUIUtility.TrTextContent("Depth Tolerance", "Controls the tolerance when comparing the depth of two pixels.");

        static public readonly GUIContent k_RayMissFallbackHierarchyText =
            EditorGUIUtility.TrTextContent("Ray Miss", "Controls the fallback hierarchy for indirect diffuse in case the ray misses.");

        static public readonly GUIContent k_LastBounceFallbackHierarchyText =
            EditorGUIUtility.TrTextContent("Last Bounce", "Controls the fallback hierarchy for lighting the last bounce.");

        static public readonly GUIContent k_MaxMixedRaySteps =
            EditorGUIUtility.TrTextContent("Max Ray Steps", "Sets the maximum number of steps HDRP uses for mixed tracing.");

        static public readonly GUIContent k_DenoiseText = EditorGUIUtility.TrTextContent("Denoise", "Denoise the screen space GI.");

        static public readonly GUIContent k_HalfResolutionDenoiserText =
            EditorGUIUtility.TrTextContent("Half Resolution Denoiser", "Use a half resolution denoiser.");

        static public readonly GUIContent k_DenoiserRadiusText =
            EditorGUIUtility.TrTextContent("Denoiser Radius", "Controls the radius of the GI denoiser (First Pass).");

        static public readonly GUIContent k_SecondDenoiserPassText = EditorGUIUtility.TrTextContent("Second Denoiser Pass", "Enable second denoising pass.");

        public void DenoiserGUI()
        {
            PropertyField(m_DenoiseSS);

            PropertyField(m_HalfResolutionDenoiserSS);
            PropertyField(m_DenoiserRadiusSS);
            PropertyField(m_SecondDenoiserPassSS);
        }

        public void DenoiserSSGUI()
        {
            PropertyField(m_DenoiseSS, k_DenoiseText);

            using (new EditorGUI.IndentLevelScope())
            {
                PropertyField(m_HalfResolutionDenoiserSS, k_HalfResolutionDenoiserText);
                PropertyField(m_DenoiserRadiusSS, k_DenoiserRadiusText);
                PropertyField(m_SecondDenoiserPassSS, k_SecondDenoiserPassText);
            }
        }

        void RayTracingPerformanceModeGUI(bool mixed)
        {
            // base.OnInspectorGUI(); // Quality Setting
            {
                PropertyField(m_RayLength, k_RayLengthText);
                PropertyField(m_FullResolution);
                if (mixed)
                    PropertyField(m_MaxMixedRaySteps, k_MaxMixedRaySteps);
                DenoiserGUI();
            }
        }

        void RayTracingQualityModeGUI()
        {
            {
                PropertyField(m_RayLength, k_RayLengthText);
                PropertyField(m_SampleCount);
                PropertyField(m_BounceCount);
                DenoiserGUI();
            }
        }

        void RayMarchModeGUI()
        {
            PropertyField(m_RaySteps, k_MaxMixedRaySteps);

            DenoiserGUI();
        }


        public override void OnInspectorGUI()
        {
            PropertyField(m_Tracing);

            RayCastingMode tracingMode = m_Tracing.value.GetEnumValue<RayCastingMode>();

            if (tracingMode == RayCastingMode.RayTracing)
            {
                PropertyField(m_Mode);
                {
                    switch (m_Mode.value.GetEnumValue<RayTracingMode>())
                    {
                        case RayTracingMode.Performance:
                        {
                            RayTracingPerformanceModeGUI(false);
                        }
                            break;
                        case RayTracingMode.Quality:
                        {
                            RayTracingQualityModeGUI();
                        }
                            break;
                    }
                }
            }
            else
            {
                RayMarchModeGUI();
            }

            // PropertyField(m_FullResolutionSS, k_FullResolutionSSText);
            PropertyField(m_DepthBufferThickness, k_DepthBufferThicknessText);
            PropertyField(m_RayMiss, k_RayMissFallbackHierarchyText);

            // PropertyField(m_APVMask);
        }
    }
}