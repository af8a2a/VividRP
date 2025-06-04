// Copyright (c) Jason Ma
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LWGUI
{
	public class PresetHelper
	{
		private static Dictionary<string /*FileName*/, LwguiShaderPropertyPreset> _loadedPresets = new Dictionary<string, LwguiShaderPropertyPreset>();

		private static bool _isInitComplete;

		public static bool IsInitComplete { get { return _isInitComplete; } }

		public static void Init()
		{
			if (!_isInitComplete)
			{
				ForceInit();
			}
		}

		public static void ForceInit()
		{
			_loadedPresets.Clear();
			_isInitComplete = false;
			var GUIDs = AssetDatabase.FindAssets("t:" + typeof(LwguiShaderPropertyPreset));
			foreach (var GUID in GUIDs)
			{
				var preset = AssetDatabase.LoadAssetAtPath<LwguiShaderPropertyPreset>(AssetDatabase.GUIDToAssetPath(GUID));
				AddPreset(preset);
			}
			_isInitComplete = true;
		}

		public static void AddPreset(LwguiShaderPropertyPreset preset)
		{
			if (!preset) return;
			if (!_loadedPresets.ContainsKey(preset.name))
			{
				_loadedPresets.Add(preset.name, preset);
			}
		}

		public static LwguiShaderPropertyPreset GetPresetAsset(string presetFileName)
		{
			if (string.IsNullOrEmpty(presetFileName))
				return null;
			
			if (!_loadedPresets.ContainsKey(presetFileName) || !_loadedPresets[presetFileName])
				ForceInit();

			if (!_loadedPresets.ContainsKey(presetFileName) || !_loadedPresets[presetFileName])
			{
				Debug.LogError("LWGUI: Invalid ShaderPropertyPreset path: ‘" + presetFileName + "’ !");
				return null;
			}

			return _loadedPresets[presetFileName];
		}

		// For Developers: Call this after a material has modified in code
		public static void ApplyPresetsInMaterial(Material material)
		{
			var props = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
			foreach (var prop in props)
			{
				var drawer = ReflectionHelper.GetPropertyDrawer(material.shader, prop, out _);

				// Apply active preset
				if (drawer != null && drawer is IPresetDrawer)
				{
					var activePreset = (drawer as IPresetDrawer).GetActivePreset(prop, PresetHelper.GetPresetAsset((drawer as PresetDrawer).presetFileName));
					if (activePreset != null)
						activePreset.ApplyToDefaultMaterial(material);
				}
			}
			UnityEditorExtension.ApplyMaterialPropertyAndDecoratorDrawers(material);
		}
	}
}