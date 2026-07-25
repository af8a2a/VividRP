// Copyright (c) Jason Ma
using System.IO;
using System.Linq;
using LWGUI.LwguiGradientEditor;
using LWGUI.Runtime.LwguiGradient;
using UnityEditor;
using UnityEngine;

namespace LWGUI
{
	public static class RampHelper
	{
		#region RampEditor

		private const string _iconCloneGUID = "9cdef444d18d2ce4abb6bbc4fed4d109";

		private static readonly GUIContent _iconAdd     = new (EditorGUIUtility.IconContent("d_Toolbar Plus").image, "Add"),
										   _iconClone   = new (EditorGUIUtility.IconContent("AnimatorController Icon").image, "Clone"),
										   _iconEdit    = new (EditorGUIUtility.IconContent("editicon.sml").image, "Edit"),
										   _iconDiscard = new (EditorGUIUtility.IconContent("d_TreeEditor.Refresh").image, "Discard"),
										   _iconSave    = new (EditorGUIUtility.IconContent("SaveActive").image, "Save");

		public static void RampEditor(
			Rect buttonRect,
			ref LwguiGradient gradient,
			ColorSpace colorSpace,
			LwguiGradient.ChannelMask viewChannelMask,
			LwguiGradient.GradientTimeRange timeRange,
			bool isDirty,
			out bool hasChange,
			out bool doEditWhenNoGradient,
			out bool doRegisterUndo,
			out bool doClone,
			out bool doCreate,
			out bool doSave,
			out bool doDiscard,
			LwguiGradientWindow.ChangeGradientCallback onChangeGradient = null
			)
		{
			var hasNoGradient = gradient == null;
			var _doEditWhenNoGradient = false;
			var doOpenWindow = false;
			var singleButtonWidth = buttonRect.width * 0.2f;
			var editRect = new Rect(buttonRect.x + singleButtonWidth * 0, buttonRect.y, singleButtonWidth, buttonRect.height);
			var saveRect = new Rect(buttonRect.x + singleButtonWidth * 1, buttonRect.y, singleButtonWidth, buttonRect.height);
			var cloneRect = new Rect(buttonRect.x + singleButtonWidth * 2, buttonRect.y, singleButtonWidth, buttonRect.height);
			var addRect = new Rect(buttonRect.x + singleButtonWidth * 3, buttonRect.y, singleButtonWidth, buttonRect.height);
			var discardRect = new Rect(buttonRect.x + singleButtonWidth * 4, buttonRect.y, singleButtonWidth, buttonRect.height);

			// Edit button event
			hasChange = false;
			{
				EditorGUI.BeginChangeCheck();
				LwguiGradientEditorHelper.GradientEditButton(editRect, _iconEdit, gradient, colorSpace, viewChannelMask, timeRange, () =>
				{
					// if the current edited texture is null, create new one
					if (hasNoGradient)
					{
						_doEditWhenNoGradient = true;
						Event.current.Use();
						return false;
					}
					else
					{
						doOpenWindow = true;
						return true;
					}
				}, onChangeGradient);
				if (EditorGUI.EndChangeCheck())
				{
					hasChange = true;
					if (LwguiGradientWindow.instance)
					{
						gradient = LwguiGradientWindow.instance.lwguiGradient;
					}
				}

				doRegisterUndo = doOpenWindow;
			}
			doEditWhenNoGradient = _doEditWhenNoGradient;

			// Clone button
			doClone = GUI.Button(cloneRect, _iconClone);
			
			// Create button
			doCreate = GUI.Button(addRect, _iconAdd);

			// Save button
			{
				var color = GUI.color;
				if (isDirty) GUI.color = Color.yellow;
				doSave = GUI.Button(saveRect, _iconSave);
				GUI.color = color;
			}
			
			// Discard button
			doDiscard = GUI.Button(discardRect, _iconDiscard);
		}

		public static bool HasGradient(AssetImporter assetImporter) { return assetImporter.userData.Contains("#");}
		
		public static LwguiGradient GetGradientFromTexture(Texture texture, out bool isDirty, bool doDiscard = false, bool doRegisterUndo = false)
		{
			isDirty = false;
			if (texture == null) return null;

			var assetImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
			if (doRegisterUndo)
			{
				LwguiGradientWindow.RegisterRampMapUndo(texture, assetImporter);
			}
			if (assetImporter != null && HasGradient(assetImporter))
			{
				isDirty = DecodeGradientFromJSON(assetImporter.userData, out var savedGradient, out var editingGradient);
				var outGradient = doDiscard ? savedGradient : editingGradient;
				return outGradient;
			}
			else
			{
				Debug.LogError("LWGUI: Can not find texture: "
							 + texture.name
							 + " or it's userData on disk! \n"
							 + "If you are moving or copying the Ramp Map, make sure your .meta file is not lost!");
				return null;
			}
		}

		public static void SetGradientToTexture(Texture texture, LwguiGradient gradient, bool doSaveToDisk = false)
		{
			if (texture == null || gradient == null) return;

			var texture2D = (Texture2D)texture;
			var path = AssetDatabase.GetAssetPath(texture);
			var assetImporter = AssetImporter.GetAtPath(path);
			VersionControlHelper.Checkout(texture2D);
			
			LwguiGradientWindow.RegisterRampMapUndo(texture2D, assetImporter);

			// Save to texture
			var pixels = gradient.GetPixels(texture.width, texture.height);
			texture2D.SetPixels(pixels);
			texture2D.Apply();

			// Save gradient JSON to userData
			DecodeGradientFromJSON(assetImporter.userData, out var savedGradient, out _);
			assetImporter.userData = EncodeGradientToJSON(doSaveToDisk ? gradient : savedGradient, gradient);

			// Save texture to disk
			if (doSaveToDisk)
			{
				VersionControlHelper.Checkout(path);
				File.WriteAllBytes(IOHelper.GetAbsPath(path), texture2D.EncodeToPNG());
				assetImporter.SaveAndReimport();
			}
		}

		private static string EncodeGradientToJSON(LwguiGradient savedGradient, LwguiGradient editingGradient)
		{
			string savedJSON = " ", editingJSON = " ";
			if (savedGradient != null)
				savedJSON = EditorJsonUtility.ToJson(savedGradient);
			if (editingGradient != null)
				editingJSON = EditorJsonUtility.ToJson(editingGradient);

			return savedJSON + "#" + editingJSON;
		}

		private static bool DecodeGradientFromJSON(string json, out LwguiGradient savedGradient, out LwguiGradient editingGradient)
		{
			savedGradient = new LwguiGradient(); 
			editingGradient = new LwguiGradient();

			var isLegacyJSON = json.Contains("MonoBehaviour");
			var subJSONs = json.Split('#');
			
			// Upgrading from deprecated GradientObject to LwguiGradient
			if (isLegacyJSON)
			{
				var savedGradientLegacy = ScriptableObject.CreateInstance<GradientObject>();
				var editingGradientLegacy = ScriptableObject.CreateInstance<GradientObject>();
				
				EditorJsonUtility.FromJsonOverwrite(subJSONs[0], savedGradientLegacy);
				EditorJsonUtility.FromJsonOverwrite(subJSONs[1], editingGradientLegacy);

				savedGradient = LwguiGradient.FromGradient(savedGradientLegacy.gradient);
				editingGradient = LwguiGradient.FromGradient(editingGradientLegacy.gradient);
			}
			else
			{
				EditorJsonUtility.FromJsonOverwrite(subJSONs[0], savedGradient);
				EditorJsonUtility.FromJsonOverwrite(subJSONs[1], editingGradient);
			}
			
			return subJSONs[0] != subJSONs[1];
		}

		public static bool CreateAndSaveNewGradientTexture(int width, int height, string unityPath, bool isLinear, LwguiGradient sourceGradient = null)
		{
			var gradient = sourceGradient != null ? new LwguiGradient(sourceGradient) : new LwguiGradient();

			var ramp = gradient.GetPreviewRampTexture(width, height, ColorSpace.Linear);
			var png = ramp.EncodeToPNG();

			File.WriteAllBytes(IOHelper.GetAbsPath(unityPath), png);

			AssetDatabase.ImportAsset(unityPath);
			SetRampTextureImporter(unityPath, true, isLinear, EncodeGradientToJSON(gradient, gradient));

			return true;
		}

		public static void SetRampTextureImporter(string unityPath, bool isReadable = true, bool isLinear = false, string userData = null)
		{
			var textureImporter = AssetImporter.GetAtPath(unityPath) as TextureImporter;
			if (!textureImporter)
			{
				Debug.LogError($"LWGUI: Can NOT get TextureImporter at path: { unityPath }");
				return;
			}
			
			textureImporter.wrapMode = TextureWrapMode.Clamp;
			textureImporter.isReadable = isReadable;
			textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
			textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
			textureImporter.mipmapEnabled = false;
			textureImporter.sRGBTexture = !isLinear;

			foreach (var platformName in Helper.platformNamesForTextureSettings)
			{
				var platformTextureSettings = textureImporter.GetPlatformTextureSettings(platformName);
				platformTextureSettings.format = TextureImporterFormat.RGBA32;
				textureImporter.SetPlatformTextureSettings(platformTextureSettings);
			}

			if (userData != null)
				textureImporter.userData = userData;
			
			textureImporter.SaveAndReimport();
		}

		#endregion


		#region RampSelector

		public static void RampMapSelectorOverride(Rect rect, MaterialProperty prop, string rootPath, RampSelectorWindow.SwitchRampMapCallback switchRampMapEvent)
		{
			var e = Event.current;
			if (e.type == UnityEngine.EventType.MouseDown && rect.Contains(e.mousePosition))
			{
				e.Use();
				var textureGUIDs = AssetDatabase.FindAssets("t:Texture2D", new[] { rootPath });
				var rampMaps = textureGUIDs.Select((GUID) =>
				{
					var path = AssetDatabase.GUIDToAssetPath(GUID);
					var assetImporter = AssetImporter.GetAtPath(path);
					if (HasGradient(assetImporter))
					{
						return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
					}
					else
						return null;
				}).ToArray();
				RampSelectorWindow.ShowWindow(prop, rampMaps, switchRampMapEvent);
			}
		}

		#endregion
	}

	public class RampSelectorWindow : EditorWindow
	{
		public delegate void SwitchRampMapCallback(MaterialProperty prop, Texture2D newRampMap, int index);
		public delegate void SwitchRampCallback(MaterialProperty prop, int rampIndex);

		private LwguiRampAtlas _rampAtlas;
		private Texture2D[] _rampMaps;
		private Vector2 _scrollPosition;
		private MaterialProperty _prop;
		private SwitchRampCallback _switchRampEvent;
		private SwitchRampMapCallback _switchRampMapEvent;

		private const float RowHeight = 18f;
		private const float RowSpacing = 2f;

		public static void ShowWindow(MaterialProperty prop, LwguiRampAtlas rampAtlas, SwitchRampCallback switchRampEvent)
		{
			LwguiGradientWindow.CloseWindow();
			var window = CreateInstance<RampSelectorWindow>();
			window.titleContent = new GUIContent("Ramp Selector (Atlas)");
			window.minSize = new Vector2(400, 500);
			window._rampAtlas = rampAtlas;
			window._prop = prop;
			window._switchRampEvent = switchRampEvent;
			window.ShowAuxWindow();
		}

		public static void ShowWindow(MaterialProperty prop, Texture2D[] rampMaps, SwitchRampMapCallback switchRampMapEvent)
		{
			LwguiGradientWindow.CloseWindow();
			var window = CreateInstance<RampSelectorWindow>();
			window.titleContent = new GUIContent("Ramp Selector");
			window.minSize = new Vector2(400, 500);
			window._rampMaps = rampMaps;
			window._prop = prop;
			window._switchRampMapEvent = switchRampMapEvent;
			window.ShowAuxWindow();
		}
		
		private void OnGUI()
		{
			if (_rampAtlas != null)
				DrawRampAtlasSelector();
			else if (_rampMaps != null)
				DrawRampMapSelector();
			else
				EditorGUILayout.HelpBox("No Ramp data available", MessageType.Error);
		}

		private void DrawRampAtlasSelector()
		{
			EditorGUILayout.BeginVertical();
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			for (int i = 0; i < _rampAtlas.RampCount; i++)
			{
				var ramp = _rampAtlas.GetRamp(i);
				if (ramp == null) continue;

				var previewTextures = ramp.GetPreviewTexturesForRampSelector(_rampAtlas.rampAtlasWidth);
				var textureCount = previewTextures?.Length ?? 0;
				var totalHeight = Mathf.Max(1, textureCount) * RowHeight + Mathf.Max(0, textureCount - 1) * RowSpacing;

				var rect = EditorGUILayout.GetControlRect(GUILayout.Height(totalHeight));
				var guiContent = new GUIContent($"{i}. {ramp.Name}");
				var buttonWidth = Mathf.Min(300f, Mathf.Max(GUI.skin.button.CalcSize(guiContent).x, rect.width * 0.35f));
				var buttonRect = new Rect(rect.x + rect.width - buttonWidth, rect.y, buttonWidth, totalHeight);
				var previewWidth = rect.width - buttonWidth - 3.0f;

				// Draw preview textures vertically
				if (previewTextures != null)
				{
					for (int j = 0; j < previewTextures.Length; j++)
					{
						if (previewTextures[j] == null) continue;
						var previewRect = new Rect(rect.x, rect.y + j * (RowHeight + RowSpacing), previewWidth, RowHeight);
						EditorGUI.DrawPreviewTexture(previewRect, previewTextures[j]);
					}
				}

				// Draw button (stretches to cover all preview rows)
				if (GUI.Button(buttonRect, guiContent, GUIStyles.rampSelectButton) && _switchRampEvent != null)
				{
					_switchRampEvent(_prop, i);
					LwguiGradientWindow.CloseWindow();
					Close();
				}

				GUILayout.Space(RowSpacing);
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void DrawRampMapSelector()
		{
			EditorGUILayout.BeginVertical();
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			for (int i = 0; i < _rampMaps.Length; i++)
			{
				var rampMap = _rampMaps[i];
				if (rampMap == null) continue;

				var rect = EditorGUILayout.GetControlRect(GUILayout.Height(RowHeight));
				var guiContent = new GUIContent($"{i}. {rampMap.name}");
				var buttonWidth = Mathf.Min(300f, Mathf.Max(GUI.skin.button.CalcSize(guiContent).x, rect.width * 0.35f));
				var buttonRect = new Rect(rect.x + rect.width - buttonWidth, rect.y, buttonWidth, RowHeight);
				var previewRect = new Rect(rect.x, rect.y, rect.width - buttonWidth - 3.0f, RowHeight);

				EditorGUI.DrawPreviewTexture(previewRect, rampMap);

				if (GUI.Button(buttonRect, guiContent, GUIStyles.rampSelectButton) && _switchRampMapEvent != null)
				{
					_switchRampMapEvent(_prop, rampMap, i);
					LwguiGradientWindow.CloseWindow();
					Close();
				}

				GUILayout.Space(RowSpacing);
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}
	}
}