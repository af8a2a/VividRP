using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [CustomEditor(typeof(GPUDrivenMaterialProxy))]
    internal sealed class GPUDrivenMaterialProxyEditor : UnityEditor.Editor
    {
        private bool m_ShowRawOverrides;

        public override void OnInspectorGUI()
        {
            var materialProxy = (GPUDrivenMaterialProxy) target;
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup(
                    "Texture Payload",
                    materialProxy.TextureMode);
            }

            MaterialGenericLayout activeLayout = TryResolveActiveGenericLayout(materialProxy, out int estimatedSubstrateBytes);
            if (materialProxy.TextureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
            {
                DrawPropertiesExcluding(
                    serializedObject,
                    "m_Script",
                    "m_TextureMode",
                    "m_BaseMap",
                    "m_BumpMap",
                    "m_MaskMap",
                    "m_ParameterOverrides",
                    "m_TextureOverrides",
                    "m_Revision");
            }
            else
            {
                DrawPropertiesExcluding(
                    serializedObject,
                    "m_Script",
                    "m_TextureMode",
                    "m_StreamedVirtualTexture",
                    "m_ParameterOverrides",
                    "m_TextureOverrides",
                    "m_Revision");
            }
            serializedObject.ApplyModifiedProperties();

            if (activeLayout != null)
            {
                DrawLayoutDrivenMaterialInstanceSection(materialProxy, activeLayout, estimatedSubstrateBytes);
                m_ShowRawOverrides = EditorGUILayout.Foldout(m_ShowRawOverrides, "Raw Serialized Overrides (Debug)", true);
                if (m_ShowRawOverrides)
                {
                    serializedObject.Update();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_ParameterOverrides"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("m_TextureOverrides"), true);
                    serializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_ParameterOverrides"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_TextureOverrides"), true);
                serializedObject.ApplyModifiedProperties();
            }

            DrawWarnings(materialProxy);

            using (new EditorGUI.DisabledScope(materialProxy.SourceMaterial == null))
            {
                if (GUILayout.Button("Sync From Source Material"))
                {
                    GPUDrivenMaterialProxySyncResult syncResult =
                        materialProxy.SyncFromSourceMaterial(
                            materialProxy.SourceMaterial,
                            GPUDrivenMaterialProxyEditorUtility.ResolveActiveTextureMode());
                    if (!syncResult.Success)
                    {
                        Debug.LogWarning($"[VividRP] Failed to sync GPUDriven material proxy '{materialProxy.name}': {syncResult.ErrorMessage}", materialProxy);
                    }
                    else if (materialProxy.TextureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture
                             && AssetDatabase.Contains(materialProxy)
                             && !GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                                 materialProxy,
                                 out _,
                                 out _,
                                 out string streamError))
                    {
                        Debug.LogWarning(
                            $"[VividRP] Failed to refresh streamed VT for '{materialProxy.name}': {streamError}",
                            materialProxy);
                    }

                    LogWarnings(materialProxy, syncResult.Warnings);
                }
            }

            using (new EditorGUI.DisabledScope(
                       EditorApplication.isCompiling
                       || (!AssetDatabase.Contains(materialProxy)
                           && materialProxy.StreamedVirtualTexture == null)))
            {
                if (GUILayout.Button("Build / Refresh Streamed VT Asset"))
                {
                    if (!GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                            materialProxy,
                            out string assetPath,
                            out _,
                            out string errorMessage))
                    {
                        Debug.LogWarning(
                            $"[VividRP] Failed to build streamed VT for '{materialProxy.name}': {errorMessage}",
                            materialProxy);
                    }
                    else if (!string.IsNullOrEmpty(assetPath))
                    {
                        Debug.Log($"[VividRP] Built GPUDriven streamed VT asset '{assetPath}'.", materialProxy);
                    }
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LongField("Revision", materialProxy.Revision);
                EditorGUILayout.LongField("Effective Revision", materialProxy.EffectiveRevision);
            }
        }

        private static MaterialGenericLayout TryResolveActiveGenericLayout(
            GPUDrivenMaterialProxy proxy,
            out int estimatedSubstrateBytes)
        {
            estimatedSubstrateBytes = 0;
            if (proxy == null)
                return null;
            MaterialGraphImportAsset effectiveGraph = proxy.EffectiveMaterialGraph;
            if (effectiveGraph == null)
                return null;

            MaterialProgramCatalog catalog = MaterialProgramCatalogBaker.CurrentCatalog;
            if (catalog != null && effectiveGraph.ProgramID != VividMaterialProgramID.Invalid)
            {
                try
                {
                    CompiledMaterialProgram program = catalog.GetMaterialProgram(effectiveGraph.ProgramID);
                    if (program != null)
                    {
                        estimatedSubstrateBytes = program.Module.Topology.EstimateSubstrateSliceCount()
                            * VividSubstrateContract.BytesPerSlice;
                        return program.Lowering.GenericLayout;
                    }
                }
                catch (System.Collections.Generic.KeyNotFoundException)
                {
                }
                catch (System.ArgumentException)
                {
                }
            }
            return null;
        }

        private static void DrawLayoutDrivenMaterialInstanceSection(
            GPUDrivenMaterialProxy proxy,
            MaterialGenericLayout layout,
            int estimatedSubstrateBytes)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material Instance Parameters (Layout-Driven)", EditorStyles.boldLabel);

            MaterialGenericLayoutInspectorModel model = MaterialGenericLayoutInspectorModel.Build(layout, proxy);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    "Layout Summary",
                    $"{model.Parameters.Count} params ({model.ParameterByteStride} B), {model.Textures.Count} textures, ~{estimatedSubstrateBytes} B/px Substrate");
            }

            for (int i = 0; i < model.Parameters.Count; i++)
            {
                MaterialInstanceParameterField field = model.Parameters[i];
                EditorGUILayout.BeginHorizontal();
                bool nextOverride = EditorGUILayout.ToggleLeft(
                    field.IsInheritedFromParent ? $"{field.Symbol} (inherited)" : field.Symbol,
                    field.IsOverriddenLocally,
                    GUILayout.Width(180f));

                if (nextOverride != field.IsOverriddenLocally)
                {
                    Undo.RecordObject(proxy, "Toggle Material Instance Parameter Override");
                    if (nextOverride)
                    {
                        proxy.SetParameterOverride(
                            field.Symbol,
                            MaterialGenericLayoutInspectorModel.ToOverrideParameterType(field.ValueType),
                            field.EffectiveValue);
                    }
                    else
                    {
                        proxy.RemoveParameterOverride(field.Symbol);
                    }
                    EditorUtility.SetDirty(proxy);
                }

                using (new EditorGUI.DisabledScope(!nextOverride))
                {
                    EditorGUI.BeginChangeCheck();
                    Vector4 edited = DrawTypedParameterControl(field);
                    if (EditorGUI.EndChangeCheck() && nextOverride)
                    {
                        Undo.RecordObject(proxy, "Edit Material Instance Parameter");
                        proxy.SetParameterOverride(
                            field.Symbol,
                            MaterialGenericLayoutInspectorModel.ToOverrideParameterType(field.ValueType),
                            edited);
                        EditorUtility.SetDirty(proxy);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            for (int i = 0; i < model.Textures.Count; i++)
            {
                MaterialInstanceTextureField field = model.Textures[i];
                EditorGUILayout.BeginHorizontal();
                bool nextOverride = EditorGUILayout.ToggleLeft(
                    field.IsInheritedFromParent ? $"{field.Symbol} (inherited)" : field.Symbol,
                    field.IsOverriddenLocally,
                    GUILayout.Width(180f));

                if (nextOverride != field.IsOverriddenLocally)
                {
                    Undo.RecordObject(proxy, "Toggle Material Instance Texture Override");
                    if (nextOverride)
                    {
                        proxy.SetTextureOverride(
                            field.Symbol,
                            field.EffectiveTexture,
                            field.EffectiveTilingOffset);
                    }
                    else
                    {
                        proxy.RemoveTextureOverride(field.Symbol);
                    }
                    EditorUtility.SetDirty(proxy);
                }

                using (new EditorGUI.DisabledScope(!nextOverride))
                {
                    EditorGUI.BeginChangeCheck();
                    var nextTex = (Texture2D)EditorGUILayout.ObjectField(
                        field.EffectiveTexture,
                        typeof(Texture2D),
                        allowSceneObjects: false);
                    if (EditorGUI.EndChangeCheck() && nextOverride)
                    {
                        Undo.RecordObject(proxy, "Edit Material Instance Texture");
                        proxy.SetTextureOverride(field.Symbol, nextTex, field.EffectiveTilingOffset);
                        EditorUtility.SetDirty(proxy);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static Vector4 DrawTypedParameterControl(in MaterialInstanceParameterField field)
        {
            Vector4 v = field.EffectiveValue;
            switch (field.ValueType)
            {
                case MaterialValueType.Bool:
                    bool b = EditorGUILayout.Toggle(v.x != 0.0f);
                    return new Vector4(b ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f);
                case MaterialValueType.Float:
                    return new Vector4(EditorGUILayout.FloatField(v.x), 0.0f, 0.0f, 0.0f);
                case MaterialValueType.Float2:
                    Vector2 v2 = EditorGUILayout.Vector2Field(GUIContent.none, new Vector2(v.x, v.y));
                    return new Vector4(v2.x, v2.y, 0.0f, 0.0f);
                case MaterialValueType.Float3:
                    if (MaterialGenericLayoutInspectorModel.IsLikelyColorSymbol(field.Symbol))
                    {
                        Color c = EditorGUILayout.ColorField(GUIContent.none, new Color(v.x, v.y, v.z, 1.0f), true, false, false);
                        return new Vector4(c.r, c.g, c.b, 0.0f);
                    }
                    Vector3 v3 = EditorGUILayout.Vector3Field(GUIContent.none, new Vector3(v.x, v.y, v.z));
                    return new Vector4(v3.x, v3.y, v3.z, 0.0f);
                case MaterialValueType.Float4:
                    if (MaterialGenericLayoutInspectorModel.IsLikelyColorSymbol(field.Symbol))
                    {
                        Color c = EditorGUILayout.ColorField(GUIContent.none, new Color(v.x, v.y, v.z, v.w), true, true, false);
                        return new Vector4(c.r, c.g, c.b, c.a);
                    }
                    return EditorGUILayout.Vector4Field(GUIContent.none, v);
                default:
                    return v;
            }
        }

        private static void DrawWarnings(GPUDrivenMaterialProxy materialProxy)
        {
            if (materialProxy == null)
            {
                return;
            }

            if (!GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                    materialProxy,
                    out string validationMessage))
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }

            if (materialProxy.SourceMaterial == null)
            {
                EditorGUILayout.HelpBox("Assign a source Material to enable one-click synchronization.", MessageType.Info);
                return;
            }

            string[] warnings = materialProxy.SourceMaterial.CollectUnsupportedWarnings();
            for (int warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
            {
                EditorGUILayout.HelpBox(warnings[warningIndex], MessageType.Warning);
            }
        }

        private static void LogWarnings(GPUDrivenMaterialProxy materialProxy, string[] warnings)
        {
            if (warnings == null)
            {
                return;
            }

            for (int warningIndex = 0; warningIndex < warnings.Length; warningIndex++)
            {
                Debug.LogWarning($"[VividRP] {warnings[warningIndex]}", materialProxy);
            }
        }
    }
}
