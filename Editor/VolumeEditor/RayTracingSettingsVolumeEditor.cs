using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(RayTracingSettingsVolume))]
    internal sealed class RayTracingSettingsVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_RayBias;
        private SerializedDataParameter m_DistantRayBias;
        private SerializedDataParameter m_ExtendShadowCulling;
        private SerializedDataParameter m_ExtendCameraCulling;
        private SerializedDataParameter m_BuildMode;
        private SerializedDataParameter m_CullingMode;
        private SerializedDataParameter m_CullingDistance;
        private SerializedDataParameter m_MinSolidAngle;
        private SerializedDataParameter m_LayerMask;
        private SerializedDataParameter m_RayTracingModeMask;
        private SerializedDataParameter m_BuildFlagsStaticGeometries;
        private SerializedDataParameter m_BuildFlagsDynamicGeometries;
        private SerializedDataParameter m_EnableCompaction;
        private SerializedDataParameter m_SigmaDenoisingRange;
        private SerializedDataParameter m_SigmaPlaneDistanceSensitivity;
        private SerializedDataParameter m_SigmaMaxStabilizedFrameNum;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<RayTracingSettingsVolume>(serializedObject);
            m_RayBias = Unpack(fetcher.Find(x => x.rayBias));
            m_DistantRayBias = Unpack(fetcher.Find(x => x.distantRayBias));
            m_ExtendShadowCulling = Unpack(fetcher.Find(x => x.extendShadowCulling));
            m_ExtendCameraCulling = Unpack(fetcher.Find(x => x.extendCameraCulling));
            m_BuildMode = Unpack(fetcher.Find(x => x.buildMode));
            m_CullingMode = Unpack(fetcher.Find(x => x.cullingMode));
            m_CullingDistance = Unpack(fetcher.Find(x => x.cullingDistance));
            m_MinSolidAngle = Unpack(fetcher.Find(x => x.minSolidAngle));
            m_LayerMask = Unpack(fetcher.Find(x => x.layerMask));
            m_RayTracingModeMask = Unpack(fetcher.Find(x => x.rayTracingModeMask));
            m_BuildFlagsStaticGeometries = Unpack(fetcher.Find(x => x.buildFlagsStaticGeometries));
            m_BuildFlagsDynamicGeometries = Unpack(fetcher.Find(x => x.buildFlagsDynamicGeometries));
            m_EnableCompaction = Unpack(fetcher.Find(x => x.enableCompaction));
            m_SigmaDenoisingRange = Unpack(fetcher.Find(x => x.sigmaDenoisingRange));
            m_SigmaPlaneDistanceSensitivity = Unpack(fetcher.Find(x => x.sigmaPlaneDistanceSensitivity));
            m_SigmaMaxStabilizedFrameNum = Unpack(fetcher.Find(x => x.sigmaMaxStabilizedFrameNum));
        }

        public override bool RequiresConstantRepaint()
        {
            return true;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_RayBias);
            PropertyField(m_DistantRayBias);
            PropertyField(m_ExtendShadowCulling);
            PropertyField(m_ExtendCameraCulling);
            PropertyField(m_BuildMode);
            PropertyField(m_CullingMode);
            DrawCullingModeDependentFields();
            PropertyField(m_LayerMask);
            PropertyField(m_RayTracingModeMask);
            PropertyField(m_BuildFlagsStaticGeometries);
            PropertyField(m_BuildFlagsDynamicGeometries);
            PropertyField(m_EnableCompaction);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SIGMA Shadow Denoise", EditorStyles.boldLabel);
            PropertyField(m_SigmaDenoisingRange);
            PropertyField(m_SigmaPlaneDistanceSensitivity);
            PropertyField(m_SigmaMaxStabilizedFrameNum);

            EditorGUILayout.Space();
            DrawStatsPanel();
        }

        private void DrawCullingModeDependentFields()
        {
            if (m_CullingMode.value.hasMultipleDifferentValues)
            {
                PropertyField(m_CullingDistance);
                PropertyField(m_MinSolidAngle);
                return;
            }

            switch ((VividRTASCullingMode)m_CullingMode.value.intValue)
            {
                case VividRTASCullingMode.Sphere:
                    PropertyField(m_CullingDistance);
                    break;
                case VividRTASCullingMode.SolidAngle:
                    PropertyField(m_MinSolidAngle);
                    break;
            }
        }

        private static void DrawStatsPanel()
        {
            EditorGUILayout.LabelField("RTAS Stats", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var stats = VividRayTracingAccelerationStructureStatsRegistry.LastStats;
                if (!stats.IsAvailable)
                {
                    var message = string.IsNullOrEmpty(stats.StatusMessage)
                        ? "No RTAS statistics are available yet. Open a SceneView/GameView rendered by VividRP or enter Play Mode."
                        : stats.StatusMessage;
                    EditorGUILayout.HelpBox(message, MessageType.Info);

                    if (!string.IsNullOrEmpty(stats.CameraName))
                        DrawStatLine("Camera", $"{stats.CameraName} ({stats.CameraType})");

                    return;
                }

                DrawStatLine("Camera", BuildCameraLabel(stats));
                DrawStatLine("Frame", stats.FrameIndex.ToString());
                DrawStatLine("Build Mode", stats.BuildMode.ToString());
                DrawStatLine("Culling Mode", stats.CullingMode.ToString());
                DrawStatLine("Instance Count", stats.InstanceCount.ToString("N0"));
                DrawStatLine("Candidate Renderers", stats.CandidateRendererCount.ToString("N0"));
                DrawStatLine("Cull Rate (Est.)", stats.HasCullRate ? FormatPercentage(stats.CullRate) : "N/A");
                DrawStatLine("Memory Usage", FormatBytes(stats.MemoryBytes));

                if (stats.UsedShaderTagFallback)
                {
                    EditorGUILayout.HelpBox(
                        "The initial Vivid RenderPipeline shader-tag filter produced 0 instances. Stats reflect the fallback scene scan without that tag filter.",
                        MessageType.Warning);
                }
            }
        }

        private static string BuildCameraLabel(VividRayTracingAccelerationStructureStats stats)
        {
            if (string.IsNullOrEmpty(stats.CameraName))
                return stats.CameraType.ToString();

            return $"{stats.CameraName} ({stats.CameraType})";
        }

        private static void DrawStatLine(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(140f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unitIndex = 0;

            while (value >= 1024.0 && unitIndex < units.Length - 1)
            {
                value /= 1024.0;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }

        private static string FormatPercentage(float value)
        {
            return $"{value * 100f:0.0}%";
        }
    }
}
