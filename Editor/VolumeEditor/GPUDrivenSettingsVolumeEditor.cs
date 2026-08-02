using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(GPUDrivenSettingsVolume))]
    internal sealed class GPUDrivenSettingsVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_ForcedMeshLODNodeDepth;
        private SerializedDataParameter m_MeshLODErrorThreshold;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<GPUDrivenSettingsVolume>(serializedObject);
            m_ForcedMeshLODNodeDepth = Unpack(fetcher.Find(x => x.forcedMeshLODNodeDepth));
            m_MeshLODErrorThreshold = Unpack(fetcher.Find(x => x.meshLODErrorThreshold));
        }

        public override bool RequiresConstantRepaint()
        {
            return true;
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_ForcedMeshLODNodeDepth);
            if (!m_ForcedMeshLODNodeDepth.value.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Negative forced LOD depth values keep automatic mesh LOD selection.", MessageType.Info);
            }

            PropertyField(m_MeshLODErrorThreshold);

            EditorGUILayout.Space();
            DrawStatsPanel();
        }

        private static void DrawStatsPanel()
        {
            EditorGUILayout.LabelField("GPUDriven Stats", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                VividGPUDrivenStats stats = VividGPUDrivenStatsRegistry.LastStats;
                if (!stats.IsAvailable)
                {
                    EditorGUILayout.HelpBox(
                        "No GPUDriven statistics are available yet. Open a SceneView/GameView rendered by VividRP or enter Play Mode.",
                        MessageType.Info);
                    return;
                }

                if (!string.IsNullOrEmpty(stats.StatusMessage))
                {
                    EditorGUILayout.HelpBox(stats.StatusMessage, stats.TextureBackendAvailable ? MessageType.Info : MessageType.Warning);
                }

                DrawStatLine("Camera", BuildCameraLabel(stats));
                DrawStatLine("Frame", stats.FrameIndex.ToString());
                DrawStatLine("Texture Backend", stats.TextureBackendName);
                DrawStatLine("Backend State", stats.TextureBackendAvailable ? "Available" : "Unavailable");
                DrawStatLine("Tracked Renderers", stats.RendererCount.ToString("N0"));
                DrawStatLine("Instances", stats.InstanceCount.ToString("N0"));
                DrawStatLine("Materials", stats.MaterialCount.ToString("N0"));
                DrawStatLine("Surface Bindings", stats.SurfaceBindingCount.ToString("N0"));
                DrawStatLine("Mesh LOD Nodes", stats.MeshLODNodeCount.ToString("N0"));
                DrawStatLine("Meshlets", stats.MeshletCount.ToString("N0"));
                DrawStatLine("Vertices", stats.VertexCount.ToString("N0"));
                DrawStatLine("Indices", stats.IndexCount.ToString("N0"));
                DrawStatLine("Build Job Capacity", stats.MaxMeshletListBuildJobCount.ToString("N0"));
                DrawStatLine("Visible Request Capacity", stats.MaxVisibleMeshletRenderRequestCount.ToString("N0"));
                DrawStatLine("Backend Resource Pools", stats.BackendPoolCount.ToString("N0"));
                DrawStatLine("Backend Resources Used", $"{stats.AllocatedBackendResourceCount:N0} / {stats.BackendResourceCapacity:N0}");
                DrawStatLine("Resource Creates/Frame", stats.CreateBackendResourceCallCountThisFrame.ToString("N0"));
                DrawStatLine("Tracked Resources", stats.RegisteredBackendResourceCount.ToString("N0"));
                DrawStatLine(
                    "Forced Mesh LOD Depth",
                    stats.ForcedMeshLODNodeDepth < 0 ? "Automatic" : stats.ForcedMeshLODNodeDepth.ToString());
                DrawStatLine("Mesh LOD Error Threshold", stats.MeshLODErrorThreshold.ToString("0.###"));
            }
        }

        private static string BuildCameraLabel(VividGPUDrivenStats stats)
        {
            if (!stats.HasCamera)
            {
                return "N/A";
            }

            if (string.IsNullOrEmpty(stats.CameraName))
            {
                return stats.CameraType.ToString();
            }

            return $"{stats.CameraName} ({stats.CameraType})";
        }

        private static void DrawStatLine(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(160f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }
    }
}
