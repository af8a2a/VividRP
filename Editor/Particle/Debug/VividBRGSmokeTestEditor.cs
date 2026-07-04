using UnityEditor;
using UnityEngine;
using VividRP.Runtime.Particle.Debug;

namespace VividRP.Editor.Particle.Debug
{
    [CustomEditor(typeof(VividBRGSmokeTest))]
    internal sealed class VividBRGSmokeTestEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var smokeTest = (VividBRGSmokeTest)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BRG Debug", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Initialized", smokeTest.IsInitialized);
                EditorGUILayout.IntField("Culling Calls", smokeTest.CullingCallCount);
                EditorGUILayout.IntField("Visible Culling Calls", smokeTest.VisibleCullingCallCount);
                EditorGUILayout.Toggle("Last Visible", smokeTest.LastVisible);
                EditorGUILayout.EnumPopup("Last View Type", smokeTest.LastViewType);
                EditorGUILayout.IntField("Last Draw Commands", smokeTest.LastDrawCommandCount);
                EditorGUILayout.BoundsField("World Bounds", smokeTest.WorldBounds);
            }
        }
    }
}
