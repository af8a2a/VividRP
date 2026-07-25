// Copyright (c) Jason Ma
using UnityEngine;
using UnityEditor;

namespace LWGUI
{
	[CustomPropertyDrawer(typeof(LwguiShaderPropertyPreset.PropertyValue))]
	public class PropertyValueDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			var propertyNameProp = property.FindPropertyRelative("propertyName");
			var propertyTypeProp = property.FindPropertyRelative("propertyType");
			var floatValueProp = property.FindPropertyRelative("floatValue");
			var intValueProp = property.FindPropertyRelative("intValue");
			var colorValueProp = property.FindPropertyRelative("colorValue");
			var vectorValueProp = property.FindPropertyRelative("vectorValue");
			var textureValueProp = property.FindPropertyRelative("textureValue");

			float y = position.y;
			float lineHeight = EditorGUIUtility.singleLineHeight;
			float spacing = EditorGUIUtility.standardVerticalSpacing;

			Rect labelRect = new Rect(position.x, y, position.width, lineHeight);
			EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);
			y += lineHeight + spacing;

			EditorGUI.indentLevel++;

			Rect propertyNameRect = new Rect(position.x, y, position.width, lineHeight);
			EditorGUI.PropertyField(propertyNameRect, propertyNameProp);
			y += lineHeight + spacing;

			Rect propertyTypeRect = new Rect(position.x, y, position.width, lineHeight);
			EditorGUI.PropertyField(propertyTypeRect, propertyTypeProp);
			y += lineHeight + spacing;

			var propertyType = (LwguiShaderPropertyPreset.PropertyType)propertyTypeProp.enumValueIndex;

			switch (propertyType)
			{
				case LwguiShaderPropertyPreset.PropertyType.Color:
					Rect colorRect = new Rect(position.x, y, position.width, lineHeight);
					EditorGUI.PropertyField(colorRect, colorValueProp);
					break;

				case LwguiShaderPropertyPreset.PropertyType.Vector:
				Rect vectorRect = new Rect(position.x, y, position.width, lineHeight);
				vectorValueProp.vector4Value = EditorGUI.Vector4Field(vectorRect, "Vector Value", vectorValueProp.vector4Value);
				break;

				case LwguiShaderPropertyPreset.PropertyType.Float:
				case LwguiShaderPropertyPreset.PropertyType.Range:
					Rect floatRect = new Rect(position.x, y, position.width, lineHeight);
					EditorGUI.PropertyField(floatRect, floatValueProp);
					break;

				case LwguiShaderPropertyPreset.PropertyType.Integer:
					Rect intRect = new Rect(position.x, y, position.width, lineHeight);
					EditorGUI.PropertyField(intRect, intValueProp);
					break;

				case LwguiShaderPropertyPreset.PropertyType.Texture:
					Rect textureRect = new Rect(position.x, y, position.width, lineHeight);
					EditorGUI.PropertyField(textureRect, textureValueProp);
					break;
			}

			EditorGUI.indentLevel--;

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float lineHeight = EditorGUIUtility.singleLineHeight;
			float spacing = EditorGUIUtility.standardVerticalSpacing;

			int lines = 4;
			return lines * lineHeight + (lines - 1) * spacing;
		}
	}
}
