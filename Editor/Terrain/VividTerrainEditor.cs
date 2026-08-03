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
            }

            if (!terrain.TryValidateData(out string validationReason))
            {
                EditorGUILayout.HelpBox(
                    $"This terrain cannot enter the GPUDriven scene: {validationReason}",
                    MessageType.Error
                );
                return;
            }

            EditorGUILayout.HelpBox(
                data.Layers.Count > 1
                    ? "GPUDriven terrain LOD rendering is active. This surface integration still samples only the first terrain layer; control-map blending is not implemented yet."
                    : "GPUDriven terrain rendering is active. Terrain chunks use the shared meshlet LOD selection, culling, visibility buffer, shadow, and texture backend paths.",
                data.Layers.Count > 1 ? MessageType.Warning : MessageType.Info
            );
        }
    }
}
