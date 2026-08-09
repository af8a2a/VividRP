// Copyright (c) Jason Ma

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LWGUI
{
	/// <summary>
	/// Draw a Vector property as two Tiling fields (XY) and two Offset fields (ZW).
	///
	/// group: parent group name (Default: none)
	/// Target Property Type: Vector
	/// </summary>
	[LwguiDrawerCategory("Vector")]
	[LwguiDrawerParameterString("group", "", "Empty")]
	public class TilingOffsetDrawer : SubDrawer
	{
		private static readonly GUIContent[] s_AxisLabels =
		{
			new GUIContent("X"),
			new GUIContent("Y"),
		};

		public TilingOffsetDrawer() { }

		public TilingOffsetDrawer(string group)
		{
			this.group = group;
		}

		public override bool IsMatchPropType(ShaderPropertyType propType)
		{
			return propType == ShaderPropertyType.Vector;
		}

		protected override float GetVisibleHeight(MaterialProperty prop)
		{
			return EditorGUIUtility.singleLineHeight * 2.0f + EditorGUIUtility.standardVerticalSpacing;
		}

		public override void DrawProp(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
		{
			Vector4 value = prop.vectorValue;
			float[] tiling = { value.x, value.y };
			float[] offset = { value.z, value.w };
			float lineHeight = EditorGUIUtility.singleLineHeight;
			var tilingRect = new Rect(position.x, position.y, position.width, lineHeight);
			var offsetRect = new Rect(
				position.x,
				position.y + lineHeight + EditorGUIUtility.standardVerticalSpacing,
				position.width,
				lineHeight);

			EditorGUI.BeginChangeCheck();
			EditorGUI.showMixedValue = prop.hasMixedValue;
			EditorGUI.MultiFloatField(tilingRect, new GUIContent("Tiling", label.tooltip), s_AxisLabels, tiling);
			EditorGUI.MultiFloatField(offsetRect, new GUIContent("Offset", label.tooltip), s_AxisLabels, offset);
			EditorGUI.showMixedValue = false;

			if (Helper.EndChangeCheck(metaDatas, prop))
			{
				prop.vectorValue = new Vector4(tiling[0], tiling[1], offset[0], offset[1]);
			}
		}
	}
}
