using UnityEditor;
using UnityEngine;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    [CustomEditor(typeof(RenderGraphAsset))]
    public class RenderGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (RenderGraphAsset)target;

            EditorGUILayout.LabelField("Nodes", asset.Nodes?.Count.ToString() ?? "0");
            EditorGUILayout.LabelField("Edges", asset.Edges?.Count.ToString() ?? "0");

            EditorGUILayout.Space();

            if (GUILayout.Button("Open in Graph Editor"))
            {
                RenderGraphEditorWindow.Open(asset);
            }
        }

        [UnityEditor.Callbacks.OnOpenAsset]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceID) as RenderGraphAsset;
            if (asset == null) return false;

            RenderGraphEditorWindow.Open(asset);
            return true;
        }
    }
}
