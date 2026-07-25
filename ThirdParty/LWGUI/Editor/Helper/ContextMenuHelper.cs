// Copyright (c) Jason Ma

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LWGUI
{
    public static class ContextMenuHelper
    {
	    #region Material Context Menu
		
	    [MenuItem("CONTEXT/Material/Reimport Shader", false, 100)]
	    static void MenuItem_ReimportShader(MenuCommand command)
	    {
		    var mat = command.context as Material;
		    if (mat != null)
		    {
			    var path = AssetDatabase.GetAssetPath(mat.shader);
			    if (!string.IsNullOrEmpty(path))
			    {
				    AssetDatabase.ImportAsset(path);
			    }
		    }
	    }

	    [MenuItem("CONTEXT/Material/Copy Material Name", false, 990)]
	    static void MenuItem_CopyMaterialName(MenuCommand command)
	    {
		    var mat = command.context as Material;
		    if (mat != null)
		    {
			    GUIUtility.systemCopyBuffer = mat.name;
		    }
	    }

	    #endregion
	    
		#region Copy and Paste
		
		private static Material     _copiedMaterial;
		private static List<string> _copiedProps = new();

		public static void CopyMaterial(Material mat)
		{
			_copiedMaterial = Object.Instantiate(mat);
		}
		
		public static void PastePropertiesToMaterials(LWGUIMetaDatas metaDatas, uint valueMask)
		{
			if (!_copiedMaterial)
			{
				Debug.LogError("LWGUI: Please copy Material Properties first!");
				return;
			}

			var targetMaterials = metaDatas.GetMaterialEditor().targets;
			if (!VersionControlHelper.Checkout(targetMaterials))
			{
				Debug.LogError("LWGUI: One or more materials unable to write!");
				return;
			}

			Undo.RecordObjects(targetMaterials, "LWGUI: Paste Material Properties");
			foreach (Material material in targetMaterials)
			{
				PastePropertiesToMaterial(material, valueMask);
			}
		}

		public static void PastePropertiesToMaterial(Material target, uint valueMask = ToolbarHelper.CopyMaterialValueMaskAll)
		{
			if (!_copiedMaterial)
			{
				Debug.LogError("LWGUI: Please copy Material Properties first!");
				return;
			}
			if (!VersionControlHelper.Checkout(target))
			{
				Debug.LogError("LWGUI: Unable to write material!");
				return;
			}
			Undo.RecordObject(target, "LWGUI: Paste Material Properties");
			for (int i = 0; i < _copiedMaterial.shader.GetPropertyCount(); i++)
			{
				var name = _copiedMaterial.shader.GetPropertyName(i);
				var type = _copiedMaterial.shader.GetPropertyType(i);
				PastePropertyValueToMaterial(target, name, name, type, valueMask);
			}
			if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.Keyword) != 0)
				target.shaderKeywords = _copiedMaterial.shaderKeywords;
			if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.RenderQueue) != 0)
				target.renderQueue = _copiedMaterial.renderQueue;
		}

		private static void PastePropertyValueToMaterial(Material material, string srcName, string dstName)
		{
			for (int i = 0; i < _copiedMaterial.shader.GetPropertyCount(); i++)
			{
				var name = _copiedMaterial.shader.GetPropertyName(i);
				if (name == srcName)
				{
					var type = _copiedMaterial.shader.GetPropertyType(i);
					PastePropertyValueToMaterial(material, srcName, dstName, type);
					return;
				}
			}
		}

		private static void PastePropertyValueToMaterial(Material material, string srcName, string dstName, ShaderPropertyType type, uint valueMask = (uint)ToolbarHelper.CopyMaterialValueMask.All)
		{
			switch (type)
			{
				case ShaderPropertyType.Color:
					if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.Vector) != 0)
						material.SetColor(dstName, _copiedMaterial.GetColor(srcName));
					break;
				case ShaderPropertyType.Vector:
					if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.Vector) != 0)
						material.SetVector(dstName, _copiedMaterial.GetVector(srcName));
					break;
				case ShaderPropertyType.Texture:
					if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.Texture) != 0)
						material.SetTexture(dstName, _copiedMaterial.GetTexture(srcName));
					break;
				// Float
				default:
					if ((valueMask & (uint)ToolbarHelper.CopyMaterialValueMask.Float) != 0)
						material.SetFloat(dstName, _copiedMaterial.GetFloat(srcName));
					break;
			}
		}

		#endregion
		
        private static void EditPresetEvent(string mode, LwguiShaderPropertyPreset presetAsset, List<LwguiShaderPropertyPreset.Preset> targetPresets, MaterialProperty prop, LWGUIMetaDatas metaDatas)
		{
			if (!VersionControlHelper.Checkout(presetAsset))
			{
				Debug.LogError("LWGUI: Can not edit the preset: " + presetAsset);
				return;
			}
			foreach (var targetPreset in targetPresets)
			{
				switch (mode)
				{
					case "Add":
					case "Update":
						targetPreset.AddOrUpdateIncludeExtraProperties(metaDatas, prop);
						break;
					case "Remove":
						targetPreset.RemoveIncludeExtraProperties(metaDatas, prop.name);
						break;
				}
			}
			EditorUtility.SetDirty(presetAsset);
			MetaDataHelper.ForceUpdateMaterialMetadataCache(metaDatas.GetMaterial());
		}

		public static void DoPropertyContextMenus(Rect rect, MaterialProperty prop, LWGUIMetaDatas metaDatas)
		{
			if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition)) return;

			Event.current.Use();

			var (perShaderData, perMaterialData, perInspectorData) = metaDatas.GetDatas();
			var (propStaticData, propDynamicData) = metaDatas.GetPropDatas(prop);
			var menu = new GenericMenu();

			// 2022+ Material Varant Menus
#if UNITY_2022_1_OR_NEWER
			ReflectionHelper.HandleApplyRevert(menu, prop);
#endif

			// Copy
			menu.AddItem(new GUIContent("Copy"), false, () =>
			{
				_copiedMaterial = UnityEngine.Object.Instantiate(metaDatas.GetMaterial());
				_copiedProps.Clear();
				_copiedProps.Add(prop.name);
				foreach (var extraPropName in propStaticData.extraPropNames)
				{
					_copiedProps.Add(extraPropName);
				}

				// Copy Children
				foreach (var childPropStaticData in propStaticData.children)
				{
					_copiedProps.Add(childPropStaticData.name);
					foreach (var extraPropName in childPropStaticData.extraPropNames)
					{
						_copiedProps.Add(extraPropName);
					}

					foreach (var childChildPropStaticData in childPropStaticData.children)
					{
						_copiedProps.Add(childChildPropStaticData.name);
						foreach (var extraPropName in childChildPropStaticData.extraPropNames)
						{
							_copiedProps.Add(extraPropName);
						}
					}
				}
			});

			// Paste
			GenericMenu.MenuFunction pasteAction = () =>
			{
				if (!VersionControlHelper.Checkout(prop.targets))
				{
					Debug.LogError("LWGUI: One or more materials unable to write!");
					return;
				}

				Undo.RecordObjects(prop.targets, "LWGUI: Paste Material Properties");

				foreach (Material material in prop.targets)
				{
					var index = 0;

					PastePropertyValueToMaterial(material, _copiedProps[index++], prop.name);
					foreach (var extraPropName in propStaticData.extraPropNames)
					{
						if (index == _copiedProps.Count) break;
						PastePropertyValueToMaterial(material, _copiedProps[index++], extraPropName);
					}

					// Paste Children
					foreach (var childPropStaticData in propStaticData.children)
					{
						if (index == _copiedProps.Count) break;
						PastePropertyValueToMaterial(material, _copiedProps[index++], childPropStaticData.name);
						foreach (var extraPropName in childPropStaticData.extraPropNames)
						{
							if (index == _copiedProps.Count) break;
							PastePropertyValueToMaterial(material, _copiedProps[index++], extraPropName);
						}

						foreach (var childChildPropStaticData in childPropStaticData.children)
						{
							if (index == _copiedProps.Count) break;
							PastePropertyValueToMaterial(material, _copiedProps[index++], childChildPropStaticData.name);
							foreach (var extraPropName in childChildPropStaticData.extraPropNames)
							{
								if (index == _copiedProps.Count) break;
								PastePropertyValueToMaterial(material, _copiedProps[index++], extraPropName);
							}
						}
					}
					MetaDataHelper.ForceUpdateMaterialMetadataCache(material);
				}
			};

			if (_copiedMaterial != null && _copiedProps.Count > 0 && GUI.enabled)
				menu.AddItem(new GUIContent("Paste"), false, pasteAction);
			else
				menu.AddDisabledItem(new GUIContent("Paste"));

			menu.AddSeparator("");

			// Copy Display Name
			menu.AddItem(new GUIContent("Copy Display Name"), false, () =>
			{
				EditorGUIUtility.systemCopyBuffer = propStaticData.displayName;
			});

			// Copy Property Names
			menu.AddItem(new GUIContent("Copy Property Names"), false, () =>
			{
				EditorGUIUtility.systemCopyBuffer = prop.name;
				foreach (var extraPropName in propStaticData.extraPropNames)
				{
					EditorGUIUtility.systemCopyBuffer += ", " + extraPropName;
				}
			});

			// menus.AddSeparator("");
			//
			// // Add to Favorites
			// menus.AddItem(new GUIContent("Add to Favorites"), false, () =>
			// {
			// });
			//
			// // Remove from Favorites
			// menus.AddItem(new GUIContent("Remove from Favorites"), false, () =>
			// {
			// });

			// Preset
			if (GUI.enabled)
			{
				menu.AddSeparator("");
				foreach (var activePresetData in perMaterialData.activePresetDatas)
				{
					// Cull self
					if (activePresetData.property == prop) continue;

					var activePreset = activePresetData.preset;
					var (presetPropStaticData, presetPropDynamicData) = metaDatas.GetPropDatas(activePresetData.property);
					var presetAsset = presetPropStaticData.propertyPresetAsset;
					var presetPropDisplayName = presetPropStaticData.displayName;
					
					// Cull invisible presets
					if (!presetPropDynamicData.isShowing) continue;

					if (activePreset.GetPropertyValue(prop.name) != null)
					{
						menu.AddItem(new GUIContent("Update to Preset/" + presetPropDisplayName + "/" + "All"), false, () => EditPresetEvent("Update", presetAsset, presetAsset.GetPresets(), prop, metaDatas));
						menu.AddItem(new GUIContent("Update to Preset/" + presetPropDisplayName + "/" + activePreset.presetName), false, () => EditPresetEvent("Update", presetAsset, new List<LwguiShaderPropertyPreset.Preset>(){activePreset}, prop, metaDatas));
						menu.AddItem(new GUIContent("Remove from Preset/" + presetPropDisplayName + "/" + "All"), false, () => EditPresetEvent("Remove", presetAsset, presetAsset.GetPresets(), prop, metaDatas));
						menu.AddItem(new GUIContent("Remove from Preset/" + presetPropDisplayName + "/" + activePreset.presetName), false, () => EditPresetEvent("Remove", presetAsset, new List<LwguiShaderPropertyPreset.Preset>(){activePreset}, prop, metaDatas));
					}
					else
					{
						menu.AddItem(new GUIContent("Add to Preset/" + presetPropDisplayName + "/" + "All"), false, () => EditPresetEvent("Add", presetAsset, presetAsset.GetPresets(), prop, metaDatas));
						menu.AddItem(new GUIContent("Add to Preset/" + presetPropDisplayName + "/" + activePreset.presetName), false, () => EditPresetEvent("Add", presetAsset, new List<LwguiShaderPropertyPreset.Preset>(){activePreset}, prop, metaDatas));
					}
				}
			}
			
			// Custom
			if (propStaticData.baseDrawers != null)
			{
				foreach (var baseDrawer in propStaticData.baseDrawers)
				{
					baseDrawer.GetCustomContextMenus(menu, rect, prop, metaDatas);
				}
			}

			menu.ShowAsContext();
		}

    }
}