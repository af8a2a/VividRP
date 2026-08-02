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
                EditorGUILayout.IntField("Surface Layers", data.Layers.Count);
            }

            EditorGUILayout.HelpBox(
                "The meshlet data is baked and compressed. Runtime terrain rendering and LOD management are intentionally not implemented yet.",
                MessageType.Info
            );
        }
    }
}
