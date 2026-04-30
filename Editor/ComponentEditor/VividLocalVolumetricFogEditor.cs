using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Rendering;
using UnityEditorInternal;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CustomEditor(typeof(VividLocalVolumetricFog))]
    [CanEditMultipleObjects]
    internal sealed class VividLocalVolumetricFogEditor : UnityEditor.Editor
    {
        private static readonly Color s_GizmoColor = new(0.23f, 0.73f, 0.67f, 0.08f);
        private static readonly Color[] s_BaseHandleColors =
        {
            new Color(0.95f, 0.48f, 0.34f, 1.0f),
            new Color(0.31f, 0.78f, 0.41f, 1.0f),
            new Color(0.28f, 0.58f, 0.96f, 1.0f),
            new Color(0.95f, 0.48f, 0.34f, 1.0f),
            new Color(0.31f, 0.78f, 0.41f, 1.0f),
            new Color(0.28f, 0.58f, 0.96f, 1.0f),
        };
        private static readonly Color[] s_BlendHandleColors =
        {
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
            new Color(0.98f, 0.76f, 0.26f, 1.0f),
        };
        private static readonly GUIContent s_VolumeHeader = EditorGUIUtility.TrTextContent("Volume");
        private static readonly GUIContent s_MaskTextureHeader = EditorGUIUtility.TrTextContent("Mask Texture");
        private static readonly GUIContent s_MaskMaterialHeader = EditorGUIUtility.TrTextContent("Mask Material");
        private static readonly GUIContent s_AlbedoLabel =
            EditorGUIUtility.TrTextContent("Single Scattering Albedo", "The color this fog scatters light to.");
        private static readonly GUIContent s_MeanFreePathLabel =
            EditorGUIUtility.TrTextContent("Fog Distance", "Determines how far you can see through the fog in meters.");
        private static readonly GUIContent s_MaskModeLabel =
            EditorGUIUtility.TrTextContent("Mask Mode", "Texture mode uses a 3D texture as density mask. Material mode uses a Fog Volume material.");
        private static readonly GUIContent s_BlendingModeLabel =
            EditorGUIUtility.TrTextContent("Blending Mode", "Determines how this fog volume blends with other fog volumes in the scene.");
        private static readonly GUIContent s_PriorityLabel =
            EditorGUIUtility.TrTextContent("Priority", "Rendering priority for overlapping local volumetric fog volumes.");
        private static readonly GUIContent s_SizeLabel =
            EditorGUIUtility.TrTextContent("Size", "Size of the local volumetric fog volume.");
        private static readonly GUIContent s_BlendDistanceLabel =
            EditorGUIUtility.TrTextContent("Blend Distance", "Interior distance from each face where the fog fades in completely.");
        private static readonly GUIContent s_PerAxisControlLabel =
            EditorGUIUtility.TrTextContent("Per Axis Control", "When checked, each face can be manipulated separately.");
        private static readonly GUIContent s_PositiveFadeLabel =
            EditorGUIUtility.TrTextContent("Positive Blend", "Blend distance along the positive local X, Y and Z faces.");
        private static readonly GUIContent s_NegativeFadeLabel =
            EditorGUIUtility.TrTextContent("Negative Blend", "Blend distance along the negative local X, Y and Z faces.");
        private static readonly GUIContent s_InvertFadeLabel =
            EditorGUIUtility.TrTextContent("Invert Blend", "Inverts the face blend so the edge is denser than the center.");
        private static readonly GUIContent s_FalloffModeLabel =
            EditorGUIUtility.TrTextContent("Falloff Mode", "Controls the falloff curve used by the blend distance.");
        private static readonly GUIContent s_DistanceFadeStartLabel =
            EditorGUIUtility.TrTextContent("Distance Fade Start", "Distance from the camera where this local volumetric fog starts to fade out.");
        private static readonly GUIContent s_DistanceFadeEndLabel =
            EditorGUIUtility.TrTextContent("Distance Fade End", "Distance from the camera where this local volumetric fog is fully faded out.");
        private static readonly GUIContent s_TextureLabel =
            EditorGUIUtility.TrTextContent("Texture", "3D texture used as the density mask.");
        private static readonly GUIContent s_TextureScrollLabel =
            EditorGUIUtility.TrTextContent("Scroll Speed", "Speed at which the density mask scrolls on each local axis.");
        private static readonly GUIContent s_TextureTileLabel =
            EditorGUIUtility.TrTextContent("Tiling", "Tiling of the density mask on each local axis.");
        private static readonly GUIContent s_TextureOffsetLabel =
            EditorGUIUtility.TrTextContent("Offset", "Offset of the density mask on each local axis.");
        private static readonly GUIContent s_MaterialMaskLabel =
            EditorGUIUtility.TrTextContent("Material", "Material used to mask color and density. It must contain a FogVolumeVoxelize pass.");
        private const string InvalidMaterialMessage = "Material not compatible. Please use a material with a FogVolumeVoxelize pass.";
        internal const EditMode.SceneViewEditMode k_EditShape = EditMode.SceneViewEditMode.ReflectionProbeBox;
        internal const EditMode.SceneViewEditMode k_EditBlend = EditMode.SceneViewEditMode.GridBox;
        private const float k_MinimumSize = 0.001f;

        private static HierarchicalBox s_ShapeBox;
        private static HierarchicalBox s_BlendBox;

        private SerializedProperty m_BoundProxy;
        private SerializedProperty m_Parameters;
        private SerializedProperty m_Albedo;
        private SerializedProperty m_MeanFreePath;
        private SerializedProperty m_BlendingMode;
        private SerializedProperty m_Priority;
        private SerializedProperty m_Anisotropy;
        private SerializedProperty m_MaskMode;
        private SerializedProperty m_VolumeMask;
        private SerializedProperty m_MaterialMask;
        private SerializedProperty m_TextureScrollingSpeed;
        private SerializedProperty m_TextureTiling;
        private SerializedProperty m_TextureOffset;
        private SerializedProperty m_PositiveFade;
        private SerializedProperty m_NegativeFade;
        private SerializedProperty m_EditorUniformFade;
        private SerializedProperty m_EditorPositiveFade;
        private SerializedProperty m_EditorNegativeFade;
        private SerializedProperty m_EditorAdvancedFade;
        private SerializedProperty m_InvertFade;
        private SerializedProperty m_DistanceFadeStart;
        private SerializedProperty m_DistanceFadeEnd;
        private SerializedProperty m_FalloffMode;
        private SerializedBoundProxyShape m_SerializedShape;
        private UnityEditor.Editor m_MaterialEditor;
        private static bool s_ShowVolume = true;
        private static bool s_ShowMaskTexture = true;
        private static bool s_ShowMaskMaterial = true;

        private static HierarchicalBox ShapeBox
        {
            get
            {
                if (s_ShapeBox == null || s_ShapeBox.Equals(null))
                {
                    s_ShapeBox = new HierarchicalBox(s_GizmoColor, s_BaseHandleColors)
                    {
                        monoHandle = false,
                    };
                }

                return s_ShapeBox;
            }
        }

        private static HierarchicalBox BlendBox
        {
            get
            {
                if (s_BlendBox == null || s_BlendBox.Equals(null))
                    s_BlendBox = new HierarchicalBox(s_GizmoColor, s_BlendHandleColors, parent: ShapeBox);

                return s_BlendBox;
            }
        }

        private void OnEnable()
        {
            m_BoundProxy = serializedObject.FindProperty("m_BoundProxy");
            m_Parameters = serializedObject.FindProperty("m_Parameters");
            m_Albedo = FindParameter("albedo");
            m_MeanFreePath = FindParameter("meanFreePath");
            m_BlendingMode = FindParameter("blendingMode");
            m_Priority = FindParameter("priority");
            m_Anisotropy = FindParameter("anisotropy");
            m_MaskMode = FindParameter("maskMode");
            m_VolumeMask = FindParameter("volumeMask");
            m_MaterialMask = FindParameter("materialMask");
            m_TextureScrollingSpeed = FindParameter("textureScrollingSpeed");
            m_TextureTiling = FindParameter("textureTiling");
            m_TextureOffset = FindParameter("textureOffset");
            m_PositiveFade = FindParameter("positiveFade");
            m_NegativeFade = FindParameter("negativeFade");
            m_EditorUniformFade = FindParameter("m_EditorUniformFade");
            m_EditorPositiveFade = FindParameter("m_EditorPositiveFade");
            m_EditorNegativeFade = FindParameter("m_EditorNegativeFade");
            m_EditorAdvancedFade = FindParameter("m_EditorAdvancedFade");
            m_InvertFade = FindParameter("invertFade");
            m_DistanceFadeStart = FindParameter("distanceFadeStart");
            m_DistanceFadeEnd = FindParameter("distanceFadeEnd");
            m_FalloffMode = FindParameter("falloffMode");
            m_SerializedShape = new SerializedBoundProxyShape(m_BoundProxy);
        }

        private void OnDisable()
        {
            if (m_MaterialEditor == null)
                return;

            DestroyImmediate(m_MaterialEditor);
            m_MaterialEditor = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ForceBoxShape(m_SerializedShape);

            DrawPrimarySettings();
            EditorGUILayout.Space();
            DrawVolumeSettings();
            DrawMaskSettings();

            serializedObject.ApplyModifiedProperties();
            DrawMaterialInspector();
        }

        private void OnSceneGUI()
        {
            var fog = (VividLocalVolumetricFog)target;
            if (fog == null)
                return;

            var so = new SerializedObject(target);
            var shapeProp = so.FindProperty("m_BoundProxy");
            var shape = new SerializedBoundProxyShape(shapeProp);
            so.Update();
            ForceBoxShape(shape);
            so.ApplyModifiedProperties();

            if (EditMode.editMode == k_EditBlend)
            {
                DrawBlendSceneHandle(so, shape, fog);
                return;
            }

            DrawBaseShapeSceneHandle(so, shape, fog);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawGizmosSelected(VividLocalVolumetricFog fog, GizmoType gizmoType)
        {
            DrawSelectedVolumeGizmos(fog);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawGizmosNonSelected(VividLocalVolumetricFog fog, GizmoType gizmoType)
        {
            BoundProxyEditorUtility.DrawGizmo(fog.transform, fog.BoundProxyShape, filled: false, s_GizmoColor * 0.5f);
        }

        private static void DrawBaseShapeSceneHandle(
            SerializedObject serializedObject,
            SerializedBoundProxyShape serializedShape,
            VividLocalVolumetricFog fog)
        {
            serializedObject.Update();
            BoundProxyShape currentShape = BoundProxyEditorUtility.GetShapeValue(serializedShape);
            Vector3 previousSize = Max(currentShape.GetSanitizedSize(), k_MinimumSize);
            if (!BoundProxyEditorUtility.TryDrawSceneHandles(
                    currentShape,
                    fog.transform,
                    out BoundProxyShape updatedShape,
                    allowCenterHandle: true))
            {
                return;
            }

            serializedObject.Update();
            Undo.RecordObjects(serializedObject.targetObjects, "Edit Local Volumetric Fog Bounds");
            serializedShape.center.vector3Value = updatedShape.center;
            serializedShape.size.vector3Value = updatedShape.size;
            serializedShape.radius.floatValue = updatedShape.radius;

            SerializedProperty parameters = serializedObject.FindProperty("m_Parameters");
            ApplySizeChangeToEditorFade(parameters, previousSize, Max(updatedShape.GetSanitizedSize(), k_MinimumSize));
            ApplySerializedEditorFadeToRuntimeProperties(parameters, Max(updatedShape.GetSanitizedSize(), k_MinimumSize));
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawBlendSceneHandle(
            SerializedObject serializedObject,
            SerializedBoundProxyShape serializedShape,
            VividLocalVolumetricFog fog)
        {
            serializedObject.Update();
            BoundProxyShape shape = BoundProxyEditorUtility.GetShapeValue(serializedShape);
            Vector3 shapeSize = Max(shape.GetSanitizedSize(), k_MinimumSize);
            SerializedProperty parameters = serializedObject.FindProperty("m_Parameters");
            SerializedProperty editorAdvancedFade = FindParameter(parameters, "m_EditorAdvancedFade");

            using (new Handles.DrawingScope(Matrix4x4.TRS(fog.transform.position, fog.transform.rotation, Vector3.one)))
            {
                ShapeBox.center = shape.center;
                ShapeBox.size = shapeSize;
                ShapeBox.SetBaseColor(s_GizmoColor);
                ShapeBox.DrawHull(false);

                Color blendColor = fog.parameters.albedo;
                blendColor.a = 8.0f / 255.0f;
                BlendBox.baseColor = blendColor;
                BlendBox.monoHandle = editorAdvancedFade == null || !editorAdvancedFade.boolValue;
                BlendBox.center = CenterBlendLocalPosition(shape, ReadParameters(parameters));
                BlendBox.size = BlendSize(shape, ReadParameters(parameters));

                EditorGUI.BeginChangeCheck();
                BlendBox.DrawHandle();
                if (!EditorGUI.EndChangeCheck())
                    return;
            }

            serializedObject.Update();
            Undo.RecordObjects(serializedObject.targetObjects, "Edit Local Volumetric Fog Blend");
            parameters = serializedObject.FindProperty("m_Parameters");
            editorAdvancedFade = FindParameter(parameters, "m_EditorAdvancedFade");

            if (editorAdvancedFade != null && editorAdvancedFade.boolValue)
                ApplyAdvancedBlendHandle(parameters, shape, BlendBox.center, BlendBox.size);
            else
                ApplyUniformBlendHandle(parameters, shapeSize, BlendBox.size);

            ApplySerializedEditorFadeToRuntimeProperties(parameters, shapeSize);
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSelectedVolumeGizmos(VividLocalVolumetricFog fog)
        {
            if (fog == null || fog.transform == null)
                return;

            BoundProxyShape shape = fog.BoundProxyShape;
            Vector3 shapeSize = Max(shape.GetSanitizedSize(), k_MinimumSize);
            using (new Handles.DrawingScope(Matrix4x4.TRS(fog.transform.position, fog.transform.rotation, Vector3.one)))
            {
                Color blendColor = fog.parameters.albedo;
                blendColor.a = 8.0f / 255.0f;
                BlendBox.baseColor = blendColor;
                BlendBox.center = CenterBlendLocalPosition(shape, fog.parameters);
                BlendBox.size = BlendSize(shape, fog.parameters);
                BlendBox.DrawHull(EditMode.editMode == k_EditBlend);

                ShapeBox.center = shape.center;
                ShapeBox.size = shapeSize;
                ShapeBox.SetBaseColor(s_GizmoColor);
                ShapeBox.DrawHull(EditMode.editMode == k_EditShape);
            }
        }

        private void DrawPrimarySettings()
        {
            if (ShouldDrawTextureModeSettings())
            {
                DrawProperty(m_Albedo, s_AlbedoLabel);
                DrawProperty(m_MeanFreePath, s_MeanFreePathLabel);
            }

            DrawProperty(m_MaskMode, s_MaskModeLabel);
            if (ShouldDrawTextureModeSettings())
                DrawProperty(m_BlendingMode, s_BlendingModeLabel);
            DrawProperty(m_Priority, s_PriorityLabel);
            DrawProperty(m_Anisotropy);
        }

        private void DrawVolumeSettings()
        {
            s_ShowVolume = EditorGUILayout.Foldout(s_ShowVolume, s_VolumeHeader, true);
            if (!s_ShowVolume)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(m_SerializedShape.center);
                DrawSizeProperty();
                DrawBlendDistanceSettings();
                DrawProperty(m_FalloffMode, s_FalloffModeLabel);
                DrawProperty(m_InvertFade, s_InvertFadeLabel);
                DrawProperty(m_DistanceFadeStart, s_DistanceFadeStartLabel);
                DrawProperty(m_DistanceFadeEnd, s_DistanceFadeEndLabel);
            }
        }

        private void DrawSizeProperty()
        {
            Vector3 previousSize = m_SerializedShape.size.vector3Value;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_SerializedShape.size, s_SizeLabel);
            if (!EditorGUI.EndChangeCheck())
                return;

            Vector3 newSize = m_SerializedShape.size.vector3Value;
            RescaleEditorFadeAfterSizeChange(previousSize, newSize);
            ClampUniformEditorFade(newSize);
        }

        private void DrawBlendDistanceSettings()
        {
            DrawProperty(m_EditorAdvancedFade, s_PerAxisControlLabel);

            if (m_EditorAdvancedFade != null && m_EditorAdvancedFade.hasMultipleDifferentValues)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField(s_BlendDistanceLabel, EditorGUIUtility.TrTextContent("Multiple values for Per Axis Control"));
                }

                return;
            }

            bool advancedFade = m_EditorAdvancedFade != null && m_EditorAdvancedFade.boolValue;
            if (advancedFade)
            {
                EditorGUI.BeginChangeCheck();
                DrawProperty(m_EditorPositiveFade, s_PositiveFadeLabel);
                DrawProperty(m_EditorNegativeFade, s_NegativeFadeLabel);
                if (EditorGUI.EndChangeCheck())
                    ClampAdvancedEditorFade();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                DrawProperty(m_EditorUniformFade, s_BlendDistanceLabel);
                if (EditorGUI.EndChangeCheck())
                    ClampUniformEditorFade(m_SerializedShape.size.vector3Value);
            }

            ApplyEditorFadeToRuntimeProperties();
        }

        private void RescaleEditorFadeAfterSizeChange(Vector3 previousSize, Vector3 newSize)
        {
            if (m_EditorPositiveFade == null
                || m_EditorNegativeFade == null
                || m_EditorPositiveFade.hasMultipleDifferentValues
                || m_EditorNegativeFade.hasMultipleDifferentValues)
            {
                return;
            }

            previousSize = Max(previousSize, 0.001f);
            newSize = Max(newSize, 0.001f);

            Vector3 positiveFade = VividLocalVolumetricFogArtistParameters.RescaleNormalizedFade(
                m_EditorPositiveFade.vector3Value,
                previousSize,
                newSize);
            Vector3 negativeFade = VividLocalVolumetricFogArtistParameters.RescaleNormalizedFade(
                m_EditorNegativeFade.vector3Value,
                previousSize,
                newSize);

            VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
            m_EditorPositiveFade.vector3Value = positiveFade;
            m_EditorNegativeFade.vector3Value = negativeFade;
        }

        private void ClampAdvancedEditorFade()
        {
            if (m_EditorPositiveFade == null
                || m_EditorNegativeFade == null
                || m_EditorPositiveFade.hasMultipleDifferentValues
                || m_EditorNegativeFade.hasMultipleDifferentValues)
            {
                return;
            }

            Vector3 positiveFade = m_EditorPositiveFade.vector3Value;
            Vector3 negativeFade = m_EditorNegativeFade.vector3Value;
            VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
            m_EditorPositiveFade.vector3Value = positiveFade;
            m_EditorNegativeFade.vector3Value = negativeFade;
        }

        private void ClampUniformEditorFade(Vector3 volumeSize)
        {
            if (m_EditorUniformFade == null || m_EditorUniformFade.hasMultipleDifferentValues)
                return;

            volumeSize = Max(volumeSize, 0.001f);
            float maximumBlendDistance = MinComponent(volumeSize) * 0.5f;
            m_EditorUniformFade.floatValue = Mathf.Clamp(m_EditorUniformFade.floatValue, 0.0f, maximumBlendDistance);
        }

        private void ApplyEditorFadeToRuntimeProperties()
        {
            if (m_PositiveFade == null
                || m_NegativeFade == null
                || m_EditorAdvancedFade == null
                || m_SerializedShape.size.hasMultipleDifferentValues
                || m_EditorAdvancedFade.hasMultipleDifferentValues)
            {
                return;
            }

            if (m_EditorAdvancedFade.boolValue)
            {
                if (m_EditorPositiveFade == null
                    || m_EditorNegativeFade == null
                    || m_EditorPositiveFade.hasMultipleDifferentValues
                    || m_EditorNegativeFade.hasMultipleDifferentValues)
                {
                    return;
                }

                Vector3 positiveFade = m_EditorPositiveFade.vector3Value;
                Vector3 negativeFade = m_EditorNegativeFade.vector3Value;
                VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
                m_EditorPositiveFade.vector3Value = positiveFade;
                m_EditorNegativeFade.vector3Value = negativeFade;
                m_PositiveFade.vector3Value = positiveFade;
                m_NegativeFade.vector3Value = negativeFade;
                return;
            }

            if (m_EditorUniformFade == null || m_EditorUniformFade.hasMultipleDifferentValues)
                return;

            ClampUniformEditorFade(m_SerializedShape.size.vector3Value);
            Vector3 normalizedFade = VividLocalVolumetricFogArtistParameters.ComputeNormalizedFadeFromUniform(
                m_EditorUniformFade.floatValue,
                Max(m_SerializedShape.size.vector3Value, 0.001f));
            m_PositiveFade.vector3Value = normalizedFade;
            m_NegativeFade.vector3Value = normalizedFade;
        }

        internal static Vector3 CenterBlendLocalPosition(VividLocalVolumetricFog fog)
        {
            if (fog == null)
                return Vector3.zero;

            return CenterBlendLocalPosition(fog.BoundProxyShape, fog.parameters);
        }

        internal static Vector3 BlendSize(VividLocalVolumetricFog fog)
        {
            if (fog == null)
                return Vector3.zero;

            return BlendSize(fog.BoundProxyShape, fog.parameters);
        }

        internal static Vector3 CenterBlendLocalPosition(
            BoundProxyShape shape,
            VividLocalVolumetricFogArtistParameters parameters)
        {
            shape.Sanitize();
            Vector3 shapeSize = Max(shape.GetSanitizedSize(), k_MinimumSize);
            if (!parameters.m_EditorAdvancedFade)
                return shape.center;

            Vector3 positiveBlend = Scale(parameters.m_EditorPositiveFade, shapeSize);
            Vector3 negativeBlend = Scale(parameters.m_EditorNegativeFade, shapeSize);
            return shape.center + (negativeBlend - positiveBlend) * 0.5f;
        }

        internal static Vector3 BlendSize(
            BoundProxyShape shape,
            VividLocalVolumetricFogArtistParameters parameters)
        {
            shape.Sanitize();
            Vector3 shapeSize = Max(shape.GetSanitizedSize(), k_MinimumSize);
            if (parameters.m_EditorAdvancedFade)
            {
                Vector3 normalizedSize = Vector3.one
                    - parameters.m_EditorPositiveFade
                    - parameters.m_EditorNegativeFade;
                return Max(Scale(normalizedSize, shapeSize), 0.0f);
            }

            return Max(shapeSize - Vector3.one * parameters.m_EditorUniformFade * 2.0f, 0.0f);
        }

        private static void ApplySizeChangeToEditorFade(
            SerializedProperty parameters,
            Vector3 previousSize,
            Vector3 newSize)
        {
            SerializedProperty editorPositiveFade = FindParameter(parameters, "m_EditorPositiveFade");
            SerializedProperty editorNegativeFade = FindParameter(parameters, "m_EditorNegativeFade");
            if (editorPositiveFade == null
                || editorNegativeFade == null
                || editorPositiveFade.hasMultipleDifferentValues
                || editorNegativeFade.hasMultipleDifferentValues)
            {
                return;
            }

            previousSize = Max(previousSize, k_MinimumSize);
            newSize = Max(newSize, k_MinimumSize);
            Vector3 positiveFade = VividLocalVolumetricFogArtistParameters.RescaleNormalizedFade(
                editorPositiveFade.vector3Value,
                previousSize,
                newSize);
            Vector3 negativeFade = VividLocalVolumetricFogArtistParameters.RescaleNormalizedFade(
                editorNegativeFade.vector3Value,
                previousSize,
                newSize);

            VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
            editorPositiveFade.vector3Value = positiveFade;
            editorNegativeFade.vector3Value = negativeFade;
            ClampSerializedUniformEditorFade(parameters, newSize);
        }

        private static void ApplyAdvancedBlendHandle(
            SerializedProperty parameters,
            BoundProxyShape shape,
            Vector3 blendCenter,
            Vector3 blendSize)
        {
            SerializedProperty editorPositiveFade = FindParameter(parameters, "m_EditorPositiveFade");
            SerializedProperty editorNegativeFade = FindParameter(parameters, "m_EditorNegativeFade");
            if (editorPositiveFade == null || editorNegativeFade == null)
                return;

            shape.Sanitize();
            Vector3 shapeSize = Max(shape.GetSanitizedSize(), k_MinimumSize);
            Vector3 centerRelativeToShape = blendCenter - shape.center;
            Vector3 halfSize = Max(blendSize, 0.0f) * 0.5f;
            Vector3 positiveHandlePosition = centerRelativeToShape + halfSize;
            Vector3 negativeHandlePosition = centerRelativeToShape - halfSize;

            Vector3 positiveFade = new(
                ComputePositiveFade(positiveHandlePosition.x, shapeSize.x),
                ComputePositiveFade(positiveHandlePosition.y, shapeSize.y),
                ComputePositiveFade(positiveHandlePosition.z, shapeSize.z));
            Vector3 negativeFade = new(
                ComputeNegativeFade(negativeHandlePosition.x, shapeSize.x),
                ComputeNegativeFade(negativeHandlePosition.y, shapeSize.y),
                ComputeNegativeFade(negativeHandlePosition.z, shapeSize.z));

            VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
            editorPositiveFade.vector3Value = positiveFade;
            editorNegativeFade.vector3Value = negativeFade;
        }

        private static void ApplyUniformBlendHandle(
            SerializedProperty parameters,
            Vector3 shapeSize,
            Vector3 blendSize)
        {
            SerializedProperty editorUniformFade = FindParameter(parameters, "m_EditorUniformFade");
            if (editorUniformFade == null)
                return;

            shapeSize = Max(shapeSize, k_MinimumSize);
            float uniformFade = (shapeSize.x - Mathf.Max(blendSize.x, 0.0f)) * 0.5f;
            float maximumBlendDistance = MinComponent(shapeSize) * 0.5f;
            editorUniformFade.floatValue = Mathf.Clamp(uniformFade, 0.0f, maximumBlendDistance);
        }

        private static void ApplySerializedEditorFadeToRuntimeProperties(
            SerializedProperty parameters,
            Vector3 shapeSize)
        {
            SerializedProperty positiveFadeProperty = FindParameter(parameters, "positiveFade");
            SerializedProperty negativeFadeProperty = FindParameter(parameters, "negativeFade");
            SerializedProperty editorAdvancedFade = FindParameter(parameters, "m_EditorAdvancedFade");
            if (positiveFadeProperty == null
                || negativeFadeProperty == null
                || editorAdvancedFade == null
                || editorAdvancedFade.hasMultipleDifferentValues)
            {
                return;
            }

            if (editorAdvancedFade.boolValue)
            {
                SerializedProperty editorPositiveFade = FindParameter(parameters, "m_EditorPositiveFade");
                SerializedProperty editorNegativeFade = FindParameter(parameters, "m_EditorNegativeFade");
                if (editorPositiveFade == null || editorNegativeFade == null)
                    return;

                Vector3 positiveFade = editorPositiveFade.vector3Value;
                Vector3 negativeFade = editorNegativeFade.vector3Value;
                VividLocalVolumetricFogArtistParameters.ClampCombinedEditorFade(ref positiveFade, ref negativeFade);
                editorPositiveFade.vector3Value = positiveFade;
                editorNegativeFade.vector3Value = negativeFade;
                positiveFadeProperty.vector3Value = positiveFade;
                negativeFadeProperty.vector3Value = negativeFade;
                return;
            }

            SerializedProperty editorUniformFade = FindParameter(parameters, "m_EditorUniformFade");
            if (editorUniformFade == null)
                return;

            ClampSerializedUniformEditorFade(parameters, shapeSize);
            Vector3 normalizedFade = VividLocalVolumetricFogArtistParameters.ComputeNormalizedFadeFromUniform(
                editorUniformFade.floatValue,
                Max(shapeSize, k_MinimumSize));
            positiveFadeProperty.vector3Value = normalizedFade;
            negativeFadeProperty.vector3Value = normalizedFade;
        }

        private static void ClampSerializedUniformEditorFade(SerializedProperty parameters, Vector3 shapeSize)
        {
            SerializedProperty editorUniformFade = FindParameter(parameters, "m_EditorUniformFade");
            if (editorUniformFade == null || editorUniformFade.hasMultipleDifferentValues)
                return;

            shapeSize = Max(shapeSize, k_MinimumSize);
            editorUniformFade.floatValue = Mathf.Clamp(
                editorUniformFade.floatValue,
                0.0f,
                MinComponent(shapeSize) * 0.5f);
        }

        private static VividLocalVolumetricFogArtistParameters ReadParameters(SerializedProperty parameters)
        {
            var result = VividLocalVolumetricFogArtistParameters.CreateDefault();
            result.m_EditorUniformFade = FindParameter(parameters, "m_EditorUniformFade")?.floatValue ?? result.m_EditorUniformFade;
            result.m_EditorPositiveFade = FindParameter(parameters, "m_EditorPositiveFade")?.vector3Value ?? result.m_EditorPositiveFade;
            result.m_EditorNegativeFade = FindParameter(parameters, "m_EditorNegativeFade")?.vector3Value ?? result.m_EditorNegativeFade;
            result.m_EditorAdvancedFade = FindParameter(parameters, "m_EditorAdvancedFade")?.boolValue ?? result.m_EditorAdvancedFade;
            return result;
        }

        private static SerializedProperty FindParameter(SerializedProperty parameters, string propertyName)
        {
            return parameters?.FindPropertyRelative(propertyName);
        }

        private static Vector3 Scale(Vector3 value, Vector3 scale)
        {
            return new Vector3(value.x * scale.x, value.y * scale.y, value.z * scale.z);
        }

        private static float ComputePositiveFade(float positiveHandlePosition, float size)
        {
            return size > k_MinimumSize ? 0.5f - positiveHandlePosition / size : 0.0f;
        }

        private static float ComputeNegativeFade(float negativeHandlePosition, float size)
        {
            return size > k_MinimumSize ? 0.5f + negativeHandlePosition / size : 0.0f;
        }

        private void DrawMaskSettings()
        {
            if (ShouldDrawTextureModeSettings())
                DrawMaskTextureSettings();

            if (ShouldDrawMaterialModeSettings())
                DrawMaskMaterialSettings();
        }

        private void DrawMaskTextureSettings()
        {
            s_ShowMaskTexture = EditorGUILayout.Foldout(s_ShowMaskTexture, s_MaskTextureHeader, true);
            if (!s_ShowMaskTexture)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawProperty(m_VolumeMask, s_TextureLabel);
                DrawProperty(m_TextureScrollingSpeed, s_TextureScrollLabel);
                DrawProperty(m_TextureTiling, s_TextureTileLabel);
                DrawProperty(m_TextureOffset, s_TextureOffsetLabel);
            }
        }

        private void DrawMaskMaterialSettings()
        {
            s_ShowMaskMaterial = EditorGUILayout.Foldout(s_ShowMaskMaterial, s_MaskMaterialHeader, true);
            if (!s_ShowMaskMaterial)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawProperty(m_MaterialMask, s_MaterialMaskLabel);
                if (m_MaterialMask != null && m_MaterialMask.objectReferenceValue != null && !IsMaterialMaskCompatible())
                    EditorGUILayout.HelpBox(InvalidMaterialMessage, MessageType.Error);
            }
        }

        private void DrawMaterialInspector()
        {
            if (!ShouldDrawMaterialModeSettings() || m_MaterialMask == null || m_MaterialMask.objectReferenceValue is not Material material)
                return;

            if (m_MaterialEditor == null || m_MaterialEditor.target != material)
                UnityEditor.Editor.CreateCachedEditor(material, typeof(MaterialEditor), ref m_MaterialEditor);

            using (new EditorGUI.DisabledScope((material.hideFlags & HideFlags.NotEditable) != 0))
            {
                m_MaterialEditor.DrawHeader();
                m_MaterialEditor.OnInspectorGUI();
            }
        }

        private SerializedProperty FindParameter(string propertyName)
        {
            return m_Parameters?.FindPropertyRelative(propertyName);
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label = null)
        {
            if (property == null)
                return;

            if (label == null)
                EditorGUILayout.PropertyField(property);
            else
                EditorGUILayout.PropertyField(property, label);
        }

        private bool ShouldDrawTextureModeSettings()
        {
            return m_MaskMode == null
                || m_MaskMode.hasMultipleDifferentValues
                || (VividLocalVolumetricFogMaskMode)m_MaskMode.intValue == VividLocalVolumetricFogMaskMode.Texture;
        }

        private bool ShouldDrawMaterialModeSettings()
        {
            return m_MaskMode != null
                && (m_MaskMode.hasMultipleDifferentValues
                    || (VividLocalVolumetricFogMaskMode)m_MaskMode.intValue == VividLocalVolumetricFogMaskMode.Material);
        }

        private bool IsMaterialMaskCompatible()
        {
            return m_MaterialMask.objectReferenceValue is Material material
                && material.FindPass("FogVolumeVoxelize") >= 0;
        }

        private static void ForceBoxShape(SerializedBoundProxyShape shape)
        {
            if (shape == null)
                return;

            shape.shape.intValue = (int)BoundProxyShapeType.Box;
            shape.radius.floatValue = 0.0f;
        }

        private static Vector3 Max(Vector3 value, float minimum)
        {
            return new Vector3(
                Mathf.Max(value.x, minimum),
                Mathf.Max(value.y, minimum),
                Mathf.Max(value.z, minimum));
        }

        private static float MinComponent(Vector3 value)
        {
            return Mathf.Min(value.x, value.y, value.z);
        }
    }

    internal abstract class VividLocalVolumetricFogEditorToolBase : EditorTool
    {
        private readonly string m_Description;
        private readonly EditMode.SceneViewEditMode m_Mode;
        private readonly string m_IconName;
        private GUIContent m_IconContent;

        protected VividLocalVolumetricFogEditorToolBase(
            string description,
            EditMode.SceneViewEditMode mode,
            string iconName)
        {
            m_Description = description;
            m_Mode = mode;
            m_IconName = iconName;
        }

        public override GUIContent toolbarIcon => m_IconContent;

        public override void OnWillBeDeactivated()
        {
            EditMode.SetEditModeToNone();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (EditMode.editMode == m_Mode)
                return;

            if (!TryGetTargetsBounds(out Bounds bounds))
                return;

            EditMode.ChangeEditMode(m_Mode, bounds);
            ToolManager.SetActiveTool(this);
        }

        private void OnEnable()
        {
            m_IconContent = EditorGUIUtility.TrIconContent(m_IconName, m_Description);
        }

        private bool TryGetTargetsBounds(out Bounds bounds)
        {
            bool foundTarget = false;
            bounds = new Bounds { min = Vector3.positiveInfinity, max = Vector3.negativeInfinity };
            foreach (UnityEngine.Object targetObject in targets)
            {
                if (targetObject is not VividLocalVolumetricFog fog || fog.transform == null)
                    continue;

                foundTarget = true;
                bounds.Encapsulate(fog.transform.position);
            }

            return foundTarget;
        }
    }

    [EditorTool(Description, typeof(VividLocalVolumetricFog), toolPriority = (int)VividLocalVolumetricFogEditor.k_EditBlend)]
    internal sealed class VividLocalVolumetricFogModifyInfluenceVolumeTool : VividLocalVolumetricFogEditorToolBase
    {
        private const string Description = "Modify the influence volume";

        public VividLocalVolumetricFogModifyInfluenceVolumeTool()
            : base(Description, VividLocalVolumetricFogEditor.k_EditBlend, "PreMatCube")
        {
        }
    }

    [EditorTool(Description, typeof(VividLocalVolumetricFog), toolPriority = (int)VividLocalVolumetricFogEditor.k_EditShape)]
    internal sealed class VividLocalVolumetricFogModifyBaseShapeTool : VividLocalVolumetricFogEditorToolBase
    {
        private const string Description = "Modify the base shape";

        public VividLocalVolumetricFogModifyBaseShapeTool()
            : base(Description, VividLocalVolumetricFogEditor.k_EditShape, "EditCollider")
        {
        }
    }
}
