using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.TerrainTools
{
    [CustomEditor(typeof(VividTerrain))]
    internal sealed class VividTerrainEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Data"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_ShadowCastingMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_ReceiveShadows"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_RenderingLayerMask"));
            serializedObject.ApplyModifiedProperties();

            var terrain = (VividTerrain) target;
            if (terrain.Data == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a baked VividTerrainData asset or create a VividTerrain copy from a Unity Terrain context menu.",
                    MessageType.Info
                );
                return;
            }

            VividTerrainData data = terrain.Data;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Heightmap Resolution", data.SourceHeightmapResolution);
                EditorGUILayout.Vector3Field("Terrain Size", data.Size);
                EditorGUILayout.Vector2IntField("Chunk Grid", data.ChunkGridSize);
                EditorGUILayout.IntField("Geometry Chunks", data.GeometryChunkCount);
                EditorGUILayout.IntField("Baked LOD Limit", data.BakeSettings.MaxMeshLODLevelCount);
                Vector2Int lodRange = data.GeometryChunkLODRange;
                EditorGUILayout.TextField(
                    "Chunk LOD Range",
                    lodRange == Vector2Int.zero ? "No geometry" : $"{lodRange.x}..{lodRange.y}"
                );
                EditorGUILayout.IntField("Surface Layers", data.Layers.Count);
                EditorGUILayout.IntField("Control Maps", data.ControlMaps.Count);
            }

            if (!terrain.TryValidateData(out string validationReason))
            {
                EditorGUILayout.HelpBox(
                    $"This terrain cannot enter the GPUDriven scene: {validationReason}",
                    MessageType.Error
                );
                return;
            }

            if (data.Layers.Count > VividTerrainData.MaximumSurfaceLayerCount)
            {
                EditorGUILayout.HelpBox(
                    $"GPUDriven terrain supports the first {VividTerrainData.MaximumSurfaceLayerCount} surface layers. "
                    + $"This asset contains {data.Layers.Count}; additional layers are ignored.",
                    MessageType.Warning
                );
            }
            else if (!data.HasCompleteControlMapData)
            {
                EditorGUILayout.HelpBox(
                    "This baked asset does not contain all required control maps. Re-bake it to enable multi-layer blending; until then the first layer is used.",
                    MessageType.Warning
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    data.SupportedSurfaceLayerCount > 1
                        ? $"GPUDriven terrain LOD rendering is active with control-map blending across {data.SupportedSurfaceLayerCount} layers."
                        : "GPUDriven terrain rendering is active. Terrain chunks use the shared meshlet LOD selection, culling, visibility buffer, shadow, and texture backend paths.",
                    MessageType.Info
                );
            }
        }
    }
}
