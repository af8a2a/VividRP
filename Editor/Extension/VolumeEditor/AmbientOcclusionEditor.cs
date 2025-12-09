using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AmbientOcclusion))]
    public class AmbientOcclusionEditor : VolumeComponentEditor
    {
        // Shared
        SerializedDataParameter m_Mode;
        SerializedDataParameter m_Enabled;

        // XeGTAO
        SerializedDataParameter m_FinalValuePower;
        SerializedDataParameter m_FalloffRange;
        SerializedDataParameter m_Resolution;
        SerializedDataParameter m_QualityLevel;
        SerializedDataParameter m_DenoisingLevel;
        SerializedDataParameter m_BentNormals;
        SerializedDataParameter m_DirectLightingMicroshadows;
        SerializedDataParameter m_DirectLightingStrength;

        // HBAO
        SerializedDataParameter m_Radius;
        SerializedDataParameter m_MaxRadiusPixels;
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_Bias;
        SerializedDataParameter m_Sharpness;
        SerializedDataParameter m_MaxDistance;
        SerializedDataParameter m_DistanceFalloff;

        // RTAO
        SerializedDataParameter m_RayLength;
        SerializedDataParameter m_SamplesPerPixel;
        SerializedDataParameter m_OccluderMotionRejection;
        SerializedDataParameter m_ReceiverMotionRejection;
        SerializedDataParameter m_DenoiseRadius;
        SerializedDataParameter m_RayQuery;
        SerializedDataParameter m_LayerMask;
        SerializedDataParameter m_ShaderExecutionReordering;

        static class Styles
        {
            public static readonly GUIContent Mode = EditorGUIUtility.TrTextContent("Mode");
            public static readonly GUIContent Enabled = EditorGUIUtility.TrTextContent("Enabled");

            public static readonly GUIContent FinalValuePower = EditorGUIUtility.TrTextContent("Final Value Power");
            public static readonly GUIContent FalloffRange = EditorGUIUtility.TrTextContent("Falloff Range");
            public static readonly GUIContent Resolution = EditorGUIUtility.TrTextContent("Resolution");
            public static readonly GUIContent QualityLevel = EditorGUIUtility.TrTextContent("Quality");
            public static readonly GUIContent DenoisingLevel = EditorGUIUtility.TrTextContent("Denoising");
            public static readonly GUIContent BentNormals = EditorGUIUtility.TrTextContent("Bent Normals");
            public static readonly GUIContent DirectLightingMicroshadows = EditorGUIUtility.TrTextContent("Direct Lighting Microshadows");
            public static readonly GUIContent DirectLightingStrength = EditorGUIUtility.TrTextContent("Direct Lighting Strength");

            public static readonly GUIContent Radius = EditorGUIUtility.TrTextContent("Radius");
            public static readonly GUIContent MaxRadiusPixels = EditorGUIUtility.TrTextContent("Max Radius (Pixels)");
            public static readonly GUIContent Intensity = EditorGUIUtility.TrTextContent("Intensity");
            public static readonly GUIContent Bias = EditorGUIUtility.TrTextContent("Bias");
            public static readonly GUIContent Sharpness = EditorGUIUtility.TrTextContent("Sharpness");
            public static readonly GUIContent MaxDistance = EditorGUIUtility.TrTextContent("Max Distance");
            public static readonly GUIContent DistanceFalloff = EditorGUIUtility.TrTextContent("Distance Falloff");

            public static readonly GUIContent RtEnabled = EditorGUIUtility.TrTextContent("Ray Traced AO");
            public static readonly GUIContent RayLength = EditorGUIUtility.TrTextContent("Ray Length");
            public static readonly GUIContent SamplesPerPixel = EditorGUIUtility.TrTextContent("Samples Per Pixel");
            public static readonly GUIContent OccluderMotionRejection = EditorGUIUtility.TrTextContent("Occluder Motion Rejection");
            public static readonly GUIContent ReceiverMotionRejection = EditorGUIUtility.TrTextContent("Receiver Motion Rejection");
            public static readonly GUIContent DenoiseRadius = EditorGUIUtility.TrTextContent("Denoise Radius");
            public static readonly GUIContent RayQuery = EditorGUIUtility.TrTextContent("Ray Query (Experimental)");
            public static readonly GUIContent ShaderExecutionReordering = EditorGUIUtility.TrTextContent("Shader Execution Reordering");
            public static readonly GUIContent AoLayerMask = EditorGUIUtility.TrTextContent("AO Layer Mask");
        }

        public override void OnEnable()
        {
            var o = new PropertyFetcher<AmbientOcclusion>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.ambientOcclusionModeParameter));
            m_Enabled = Unpack(o.Find(x => x.Enabled));

            m_FinalValuePower = Unpack(o.Find(x => x.FinalValuePower));
            m_FalloffRange = Unpack(o.Find(x => x.FalloffRange));
            m_Resolution = Unpack(o.Find(x => x.Resolution));
            m_QualityLevel = Unpack(o.Find(x => x.QualityLevel));
            m_DenoisingLevel = Unpack(o.Find(x => x.DenoisingLevel));
            m_BentNormals = Unpack(o.Find(x => x.BentNormals));
            m_DirectLightingMicroshadows = Unpack(o.Find(x => x.DirectLightingMicroshadows));
            m_DirectLightingStrength = Unpack(o.Find(x => x.directLightingStrength));

            m_Radius = Unpack(o.Find(x => x.radius));
            m_MaxRadiusPixels = Unpack(o.Find(x => x.maxRadiusPixels));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Bias = Unpack(o.Find(x => x.bias));
            m_Sharpness = Unpack(o.Find(x => x.sharpness));
            m_MaxDistance = Unpack(o.Find(x => x.maxDistance));
            m_DistanceFalloff = Unpack(o.Find(x => x.distanceFalloff));

            m_RayLength = Unpack(o.Find(x => x.rayLength));
            m_SamplesPerPixel = Unpack(o.Find(x => x.samplesPerPixel));
            m_OccluderMotionRejection = Unpack(o.Find(x => x.occluderMotionRejection));
            m_ReceiverMotionRejection = Unpack(o.Find(x => x.receiverMotionRejection));
            m_DenoiseRadius = Unpack(o.Find(x => x.denoiseRadius));
            m_RayQuery = Unpack(o.Find(x => x.rayQuery));
            m_ShaderExecutionReordering = Unpack(o.Find(x => x.shaderExecutionReordering));
            m_LayerMask = Unpack(o.Find(x => x.layerMask));

            base.OnEnable();
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode, Styles.Mode);

            // Draw shared toggle
            PropertyField(m_Enabled, Styles.Enabled);

            var mode = (AmbientOcclusionMode)m_Mode.value.intValue;
            switch (mode)
            {
                case AmbientOcclusionMode.GroundTruthAmbientOcclusion:
                    DrawXeGtao();
                    break;
                case AmbientOcclusionMode.HorizonBasedAmbientOcclusion:
                    DrawHbao();
                    break;
                case AmbientOcclusionMode.RaytracingAmbientOcclusion:
                    DrawRaytracedAo();
                    break;

                default:
                    DrawXeGtao();
                    break;
            }
        }

        void DrawXeGtao()
        {
            PropertyField(m_FinalValuePower, Styles.FinalValuePower);
            PropertyField(m_FalloffRange, Styles.FalloffRange);
            PropertyField(m_Resolution, Styles.Resolution);
            PropertyField(m_QualityLevel, Styles.QualityLevel);
            PropertyField(m_DenoisingLevel, Styles.DenoisingLevel);
            PropertyField(m_BentNormals, Styles.BentNormals);
            PropertyField(m_DirectLightingMicroshadows, Styles.DirectLightingMicroshadows);
            PropertyField(m_DirectLightingStrength, Styles.DirectLightingStrength);
        }

        void DrawHbao()
        {
            PropertyField(m_Radius, Styles.Radius);
            PropertyField(m_MaxRadiusPixels, Styles.MaxRadiusPixels);
            PropertyField(m_Intensity, Styles.Intensity);
            PropertyField(m_Bias, Styles.Bias);
            PropertyField(m_Sharpness, Styles.Sharpness);
            PropertyField(m_MaxDistance, Styles.MaxDistance);
            PropertyField(m_DistanceFalloff, Styles.DistanceFalloff);
        }

        void DrawRaytracedAo()
        {
            PropertyField(m_Intensity, Styles.Intensity);
            PropertyField(m_Radius, Styles.Radius);
            PropertyField(m_RayLength, Styles.RayLength);
            PropertyField(m_SamplesPerPixel, Styles.SamplesPerPixel);
            PropertyField(m_LayerMask, Styles.AoLayerMask);
            DrawHeader("Denoise");
            PropertyField(m_DenoiseRadius, Styles.DenoiseRadius);
            PropertyField(m_OccluderMotionRejection, Styles.OccluderMotionRejection);
            PropertyField(m_ReceiverMotionRejection, Styles.ReceiverMotionRejection);
            DrawHeader("Experiment Option");
            PropertyField(m_RayQuery, Styles.RayQuery);
            PropertyField(m_ShaderExecutionReordering, Styles.ShaderExecutionReordering);

        }
    }
}