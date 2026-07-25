// Copyright (c) Jason Ma
using UnityEngine;
using UnityEditor;

namespace LWGUI
{
	[CustomEditor(typeof(LwguiShaderPropertyPreset))]
	public class LwguiShaderPropertyPresetEditor : Editor
	{
		private SerializedProperty _presetsProp;

		private void OnEnable()
		{
			_presetsProp = serializedObject.FindProperty("presets");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.PropertyField(_presetsProp, GUILayout.ExpandHeight(true));

			serializedObject.ApplyModifiedProperties();
		}
	}
}
