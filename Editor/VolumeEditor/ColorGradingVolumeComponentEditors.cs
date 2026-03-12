using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    // TODO: Should be cleaned up and put into CoreRP/Editor
    sealed class TrackballUIDrawer
    {
        static readonly int s_ThumbHash = "colorWheelThumb".GetHashCode();
        static GUIStyle s_WheelThumb;
        static Vector2 s_WheelThumbSize;
        static Material s_Material;
        static bool s_MaterialConfigured;

        Func<Vector4, Vector3> m_ComputeFunc;
        bool m_ResetState;
        Vector2 m_CursorPos;

        public void OnGUI(SerializedProperty property, [CanBeNull] SerializedProperty overrideState, GUIContent title,
            Func<Vector4, Vector3> computeFunc)
        {
            if (property.propertyType != SerializedPropertyType.Vector4)
            {
                Debug.LogWarning("TrackballUIDrawer requires a Vector4 property");
                return;
            }

            if (!s_MaterialConfigured)
            {
                // Initialization of materials with Shader.Find from static constructors is not allowed.
                s_Material = new Material(Shader.Find("Hidden/VividRP/Editor/Trackball"))
                    { hideFlags = HideFlags.HideAndDontSave };
                s_MaterialConfigured = true;
            }

            m_ComputeFunc = computeFunc;
            var value = property.vector4Value;

            using (new EditorGUILayout.VerticalScope())
            {
                bool isOverridden = overrideState?.boolValue ?? true;
                using (new EditorGUI.DisabledScope(!isOverridden))
                    DrawWheel(ref value, isOverridden);

                DrawLabelAndOverride(title, overrideState);
            }

            if (m_ResetState)
            {
                value = new Vector4(1f, 1f, 1f, 0f);
                m_ResetState = false;
            }

            property.vector4Value = value;
        }

        void DrawWheel(ref Vector4 value, bool overrideState)
        {
            var wheelRect = GUILayoutUtility.GetAspectRect(1f);
            float size = wheelRect.width;
            float hsize = size / 2f;
            float radius = 0.38f * size;

            Vector3 hsv;
            Color.RGBToHSV(value, out hsv.x, out hsv.y, out hsv.z);
            float offset = value.w;

            // Thumb
            var thumbPos = Vector2.zero;
            float theta = hsv.x * (Mathf.PI * 2f);
            thumbPos.x = Mathf.Cos(theta + (Mathf.PI / 2f));
            thumbPos.y = Mathf.Sin(theta - (Mathf.PI / 2f));
            thumbPos *= hsv.y * radius;

            // Draw the wheel
            if (Event.current.type == EventType.Repaint)
            {
                // Style init
                if (s_WheelThumb == null)
                {
                    s_WheelThumb = new GUIStyle("ColorPicker2DThumb");
                    s_WheelThumbSize = new Vector2(
                        !Mathf.Approximately(s_WheelThumb.fixedWidth, 0f)
                            ? s_WheelThumb.fixedWidth
                            : s_WheelThumb.padding.horizontal,
                        !Mathf.Approximately(s_WheelThumb.fixedHeight, 0f)
                            ? s_WheelThumb.fixedHeight
                            : s_WheelThumb.padding.vertical
                    );
                }

                // Retina support
                float scale = EditorGUIUtility.pixelsPerPoint;

                // Wheel texture
                var oldRT = RenderTexture.active;
                var rt = RenderTexture.GetTemporary((int)(size * scale), (int)(size * scale), 0,
                    GraphicsFormat.R8G8B8A8_SRGB);
                s_Material.SetFloat("_Offset", offset);
                s_Material.SetFloat("_DisabledState", overrideState && GUI.enabled ? 1f : 0.5f);
                s_Material.SetVector("_Resolution", new Vector2(size * scale, size * scale / 2f));
                Graphics.Blit(null, rt, s_Material, EditorGUIUtility.isProSkin ? 0 : 1);
                RenderTexture.active = oldRT;

                GUI.DrawTexture(wheelRect, rt);
                RenderTexture.ReleaseTemporary(rt);

                var thumbSize = s_WheelThumbSize;
                var thumbSizeH = thumbSize / 2f;
                s_WheelThumb.Draw(
                    new Rect(wheelRect.x + hsize + thumbPos.x - thumbSizeH.x,
                        wheelRect.y + hsize + thumbPos.y - thumbSizeH.y, thumbSize.x, thumbSize.y), false, false, false,
                    false);
            }

            // Input
            var bounds = wheelRect;
            bounds.x += hsize - radius;
            bounds.y += hsize - radius;
            bounds.width = bounds.height = radius * 2f;
            hsv = GetInput(bounds, hsv, thumbPos, radius);


            Vector3Int displayHSV = new Vector3Int(Mathf.RoundToInt(hsv.x * 360), Mathf.RoundToInt(hsv.y * 100), 100);
            bool displayInputFields = EditorGUIUtility.currentViewWidth > 600;
            if (displayInputFields)
            {
                var valuesRect = GUILayoutUtility.GetRect(1f, 17f);
                valuesRect.width /= 5f;
                float textOff = valuesRect.width * 0.2f;
                EditorGUI.LabelField(valuesRect, "Y");
                valuesRect.x += textOff;
                offset = EditorGUI.DelayedFloatField(valuesRect, offset);
                offset = Mathf.Clamp(offset, -1.0f, 1.0f);
                valuesRect.x += valuesRect.width + valuesRect.width * 0.05f;
                EditorGUI.LabelField(valuesRect, "H");
                valuesRect.x += textOff;
                displayHSV.x = EditorGUI.DelayedIntField(valuesRect, displayHSV.x);
                hsv.x = displayHSV.x / 360.0f;
                valuesRect.x += valuesRect.width + valuesRect.width * 0.05f;
                EditorGUI.LabelField(valuesRect, "S");
                valuesRect.x += textOff;
                displayHSV.y = EditorGUI.DelayedIntField(valuesRect, displayHSV.y);
                displayHSV.y = Mathf.Clamp(displayHSV.y, 0, 100);
                hsv.y = displayHSV.y / 100.0f;
                valuesRect.x += valuesRect.width + valuesRect.width * 0.05f;
                EditorGUI.LabelField(valuesRect, "V");
                valuesRect.x += textOff;
                GUI.enabled = false;
                EditorGUI.IntField(valuesRect, 100);
                GUI.enabled = true;
            }


            value = Color.HSVToRGB(hsv.x, hsv.y, 1f);
            value.w = offset;

            // Offset
            var sliderRect = GUILayoutUtility.GetRect(1f, 17f);
            float padding = sliderRect.width * 0.05f; // 5% padding
            sliderRect.xMin += padding;
            sliderRect.xMax -= padding;
            value.w = GUI.HorizontalSlider(sliderRect, value.w, -1f, 1f);

            if (m_ComputeFunc == null)
                return;

            // Values
            var displayValue = m_ComputeFunc(value);
            using (new EditorGUI.DisabledGroupScope(true))
            {
                var valuesRect = GUILayoutUtility.GetRect(1f, 17f);
                valuesRect.width /= (displayInputFields ? 4f : 3.0f);
                if (displayInputFields)
                {
                    GUI.Label(valuesRect, "RGB Value:", EditorStyles.centeredGreyMiniLabel);
                    valuesRect.x += valuesRect.width;
                }

                GUI.Label(valuesRect, displayValue.x.ToString("F2"), EditorStyles.centeredGreyMiniLabel);
                valuesRect.x += valuesRect.width;
                GUI.Label(valuesRect, displayValue.y.ToString("F2"), EditorStyles.centeredGreyMiniLabel);
                valuesRect.x += valuesRect.width;
                GUI.Label(valuesRect, displayValue.z.ToString("F2"), EditorStyles.centeredGreyMiniLabel);
                valuesRect.x += valuesRect.width;
            }
        }

        void DrawLabelAndOverride(GUIContent title, SerializedProperty overrideState)
        {
            // Title
            var areaRect = GUILayoutUtility.GetRect(1f, 17f);
            var labelSize = EditorStyles.miniLabel.CalcSize(title);
            var labelRect = new Rect(areaRect.x + areaRect.width / 2 - labelSize.x / 2, areaRect.y, labelSize.x,
                labelSize.y);
            GUI.Label(labelRect, title, EditorStyles.miniLabel);

            // Override checkbox
            if (overrideState != null)
            {
                var overrideRect = new Rect(labelRect.x - 17, labelRect.y + 3, 17f, 17f);
                overrideState.boolValue = GUI.Toggle(overrideRect, overrideState.boolValue,
                    EditorGUIUtility.TrTextContent("", "Override this setting for this volume."),
                    CoreEditorStyles.smallTickbox);
            }
        }

        Vector3 GetInput(Rect bounds, Vector3 hsv, Vector2 thumbPos, float radius)
        {
            var e = Event.current;
            var id = GUIUtility.GetControlID(s_ThumbHash, FocusType.Passive, bounds);
            var mousePos = e.mousePosition;

            if (e.type == EventType.MouseDown && GUIUtility.hotControl == 0 && bounds.Contains(mousePos))
            {
                if (e.button == 0)
                {
                    var center = new Vector2(bounds.x + radius, bounds.y + radius);
                    float dist = Vector2.Distance(center, mousePos);

                    if (dist <= radius)
                    {
                        e.Use();
                        m_CursorPos = new Vector2(thumbPos.x + radius, thumbPos.y + radius);
                        GUIUtility.hotControl = id;
                        GUI.changed = true;
                    }
                }
                else if (e.button == 1)
                {
                    e.Use();
                    GUI.changed = true;
                    m_ResetState = true;
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && GUIUtility.hotControl == id)
            {
                e.Use();
                GUI.changed = true;
                m_CursorPos += e.delta * 0.2f; // Sensitivity
                GetWheelHueSaturation(m_CursorPos.x, m_CursorPos.y, radius, out hsv.x, out hsv.y);
            }
            else if (e.rawType == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == id)
            {
                e.Use();
                GUIUtility.hotControl = 0;
            }

            return hsv;
        }

        void GetWheelHueSaturation(float x, float y, float radius, out float hue, out float saturation)
        {
            float dx = (x - radius) / radius;
            float dy = (y - radius) / radius;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            hue = Mathf.Atan2(dx, -dy);
            hue = 1f - ((hue > 0) ? hue : (Mathf.PI * 2f) + hue) / (Mathf.PI * 2f);
            saturation = Mathf.Clamp01(d);
        }
    }


    [VolumeParameterDrawer(typeof(TextureCurveParameter))]
    internal sealed class TextureCurveParameterDrawer : VolumeParameterDrawer
    {
        private const string LengthPropertyName = "<length>k__BackingField";

        public override bool OnGUI(SerializedDataParameter parameter, GUIContent title)
        {
            var curveProperty = parameter.value.FindPropertyRelative("m_Curve");
            if (curveProperty == null || curveProperty.propertyType != SerializedPropertyType.AnimationCurve)
                return false;

            EditorGUI.BeginChangeCheck();
            var curve = EditorGUILayout.CurveField(title, curveProperty.animationCurveValue);
            if (!EditorGUI.EndChangeCheck())
                return true;

            curveProperty.animationCurveValue = curve;

            var lengthProperty = parameter.value.FindPropertyRelative(LengthPropertyName);
            if (lengthProperty != null)
                lengthProperty.intValue = curve != null ? curve.length : 0;

            var curveParameter = parameter.GetObjectRef<TextureCurveParameter>();
            curveParameter?.value?.SetDirty();
            return true;
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(WhiteBalance))]
    internal sealed class WhiteBalanceEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Temperature;
        private SerializedDataParameter m_Tint;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<WhiteBalance>(serializedObject);
            m_Temperature = Unpack(o.Find(x => x.temperature));
            m_Tint = Unpack(o.Find(x => x.tint));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Temperature);
            PropertyField(m_Tint);
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ColorAdjustments))]
    internal sealed class ColorAdjustmentsEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_PostExposure;
        private SerializedDataParameter m_Contrast;
        private SerializedDataParameter m_ColorFilter;
        private SerializedDataParameter m_HueShift;
        private SerializedDataParameter m_Saturation;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ColorAdjustments>(serializedObject);
            m_PostExposure = Unpack(o.Find(x => x.postExposure));
            m_Contrast = Unpack(o.Find(x => x.contrast));
            m_ColorFilter = Unpack(o.Find(x => x.colorFilter));
            m_HueShift = Unpack(o.Find(x => x.hueShift));
            m_Saturation = Unpack(o.Find(x => x.saturation));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_PostExposure);
            PropertyField(m_Contrast);
            PropertyField(m_ColorFilter);
            PropertyField(m_HueShift);
            PropertyField(m_Saturation);
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ChannelMixer))]
    internal sealed class ChannelMixerEditor : VolumeComponentEditor
    {
        private static readonly GUIContent RedLabel = EditorGUIUtility.TrTextContent("Red");
        private static readonly GUIContent GreenLabel = EditorGUIUtility.TrTextContent("Green");
        private static readonly GUIContent BlueLabel = EditorGUIUtility.TrTextContent("Blue");

        private SerializedDataParameter m_RedOutRedIn;
        private SerializedDataParameter m_RedOutGreenIn;
        private SerializedDataParameter m_RedOutBlueIn;
        private SerializedDataParameter m_GreenOutRedIn;
        private SerializedDataParameter m_GreenOutGreenIn;
        private SerializedDataParameter m_GreenOutBlueIn;
        private SerializedDataParameter m_BlueOutRedIn;
        private SerializedDataParameter m_BlueOutGreenIn;
        private SerializedDataParameter m_BlueOutBlueIn;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ChannelMixer>(serializedObject);
            m_RedOutRedIn = Unpack(o.Find(x => x.redOutRedIn));
            m_RedOutGreenIn = Unpack(o.Find(x => x.redOutGreenIn));
            m_RedOutBlueIn = Unpack(o.Find(x => x.redOutBlueIn));
            m_GreenOutRedIn = Unpack(o.Find(x => x.greenOutRedIn));
            m_GreenOutGreenIn = Unpack(o.Find(x => x.greenOutGreenIn));
            m_GreenOutBlueIn = Unpack(o.Find(x => x.greenOutBlueIn));
            m_BlueOutRedIn = Unpack(o.Find(x => x.blueOutRedIn));
            m_BlueOutGreenIn = Unpack(o.Find(x => x.blueOutGreenIn));
            m_BlueOutBlueIn = Unpack(o.Find(x => x.blueOutBlueIn));
        }

        public override void OnInspectorGUI()
        {
            DrawMixerSection("Red Output", m_RedOutRedIn, m_RedOutGreenIn, m_RedOutBlueIn);
            DrawMixerSection("Green Output", m_GreenOutRedIn, m_GreenOutGreenIn, m_GreenOutBlueIn);
            DrawMixerSection("Blue Output", m_BlueOutRedIn, m_BlueOutGreenIn, m_BlueOutBlueIn);
        }

        private void DrawMixerSection(
            string header,
            SerializedDataParameter red,
            SerializedDataParameter green,
            SerializedDataParameter blue)
        {
            DrawHeader(header);
            PropertyField(red, RedLabel);
            PropertyField(green, GreenLabel);
            PropertyField(blue, BlueLabel);
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(SplitToning))]
    internal sealed class SplitToningEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Shadows;
        private SerializedDataParameter m_Highlights;
        private SerializedDataParameter m_Balance;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<SplitToning>(serializedObject);
            m_Shadows = Unpack(o.Find(x => x.shadows));
            m_Highlights = Unpack(o.Find(x => x.highlights));
            m_Balance = Unpack(o.Find(x => x.balance));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Shadows);
            PropertyField(m_Highlights);
            PropertyField(m_Balance);
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(LiftGammaGain))]
    internal sealed class LiftGammaGainEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Lift;
        private SerializedDataParameter m_Gamma;
        private SerializedDataParameter m_Gain;
        readonly TrackballUIDrawer m_TrackballUIDrawer = new TrackballUIDrawer();

        static class Styles
        {
            public static readonly GUIContent liftLabel = EditorGUIUtility.TrTextContent("Lift",
                "Use this to control and apply a hue to the dark tones. This has a more exaggerated effect on shadows.");

            public static readonly GUIContent gammaLabel = EditorGUIUtility.TrTextContent("Gamma",
                "Use this to control and apply a hue to the mid-range tones with a power function.");

            public static readonly GUIContent gainLabel = EditorGUIUtility.TrTextContent("Gain",
                "Use this to increase and apply a hue to the signal and make highlights brighter.");
        }

        public override void OnEnable()
        {
            var o = new PropertyFetcher<LiftGammaGain>(serializedObject);
            m_Lift = Unpack(o.Find(x => x.lift));
            m_Gamma = Unpack(o.Find(x => x.gamma));
            m_Gain = Unpack(o.Find(x => x.gain));
        }

        public override void OnInspectorGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                m_TrackballUIDrawer.OnGUI(m_Lift.value, enableOverrides ? m_Lift.overrideState : null, Styles.liftLabel,
                    GetLiftValue);
                GUILayout.Space(4f);
                m_TrackballUIDrawer.OnGUI(m_Gamma.value, enableOverrides ? m_Gamma.overrideState : null,
                    Styles.gammaLabel, GetLiftValue);
                GUILayout.Space(4f);
                m_TrackballUIDrawer.OnGUI(m_Gain.value, enableOverrides ? m_Gain.overrideState : null, Styles.gainLabel,
                    GetLiftValue);
            }
        }

        Vector3 GetLiftValue(Vector4 x) => new Vector3(x.x + x.w, x.y + x.w, x.z + x.w);
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ShadowsMidtonesHighlights))]
    internal sealed class ShadowsMidtonesHighlightsEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Shadows;
        private SerializedDataParameter m_Midtones;
        private SerializedDataParameter m_Highlights;
        private SerializedDataParameter m_ShadowsStart;
        private SerializedDataParameter m_ShadowsEnd;
        private SerializedDataParameter m_HighlightsStart;
        private SerializedDataParameter m_HighlightsEnd;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ShadowsMidtonesHighlights>(serializedObject);
            m_Shadows = Unpack(o.Find(x => x.shadows));
            m_Midtones = Unpack(o.Find(x => x.midtones));
            m_Highlights = Unpack(o.Find(x => x.highlights));
            m_ShadowsStart = Unpack(o.Find(x => x.shadowsStart));
            m_ShadowsEnd = Unpack(o.Find(x => x.shadowsEnd));
            m_HighlightsStart = Unpack(o.Find(x => x.highlightsStart));
            m_HighlightsEnd = Unpack(o.Find(x => x.highlightsEnd));
        }

        public override void OnInspectorGUI()
        {
            DrawHeader("Color Weights");
            PropertyField(m_Shadows);
            PropertyField(m_Midtones);
            PropertyField(m_Highlights);

            DrawHeader("Ranges");
            PropertyField(m_ShadowsStart);
            PropertyField(m_ShadowsEnd);
            PropertyField(m_HighlightsStart);
            PropertyField(m_HighlightsEnd);
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ColorCurves))]
    internal sealed class ColorCurvesEditor : VolumeComponentEditor
    {
        private const string CurveBackgroundShaderName = "Hidden/VividRP PostProcessing/Editor/CurveBackground";
        private static readonly int DisabledStateShaderId = Shader.PropertyToID("_DisabledState");

        private static readonly string[] CurveNames =
        {
            "Master",
            "Red",
            "Green",
            "Blue",
            "Hue Vs Hue",
            "Hue Vs Sat",
            "Sat Vs Sat",
            "Lum Vs Sat",
        };

        private static GUIStyle s_PreLabel;
        private static Material s_CurveBackgroundMaterial;
        private static Texture2D s_HueBackgroundTexture;
        private static Texture2D s_GrayscaleBackgroundTexture;

        private SerializedDataParameter m_Master;
        private SerializedDataParameter m_Red;
        private SerializedDataParameter m_Green;
        private SerializedDataParameter m_Blue;
        private SerializedDataParameter m_HueVsHue;
        private SerializedDataParameter m_HueVsSat;
        private SerializedDataParameter m_SatVsSat;
        private SerializedDataParameter m_LumVsSat;

        private SerializedProperty m_RawMaster;
        private SerializedProperty m_RawRed;
        private SerializedProperty m_RawGreen;
        private SerializedProperty m_RawBlue;
        private SerializedProperty m_RawHueVsHue;
        private SerializedProperty m_RawHueVsSat;
        private SerializedProperty m_RawSatVsSat;
        private SerializedProperty m_RawLumVsSat;
        private SerializedProperty m_SelectedCurve;

        private InspectorCurveEditor m_CurveEditor;
        private Dictionary<SerializedProperty, Color> m_CurveColors;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ColorCurves>(serializedObject);
            m_Master = Unpack(o.Find(x => x.master));
            m_Red = Unpack(o.Find(x => x.red));
            m_Green = Unpack(o.Find(x => x.green));
            m_Blue = Unpack(o.Find(x => x.blue));
            m_HueVsHue = Unpack(o.Find(x => x.hueVsHue));
            m_HueVsSat = Unpack(o.Find(x => x.hueVsSat));
            m_SatVsSat = Unpack(o.Find(x => x.satVsSat));
            m_LumVsSat = Unpack(o.Find(x => x.lumVsSat));

            m_RawMaster = o.Find("master.m_Value.m_Curve");
            m_RawRed = o.Find("red.m_Value.m_Curve");
            m_RawGreen = o.Find("green.m_Value.m_Curve");
            m_RawBlue = o.Find("blue.m_Value.m_Curve");
            m_RawHueVsHue = o.Find("hueVsHue.m_Value.m_Curve");
            m_RawHueVsSat = o.Find("hueVsSat.m_Value.m_Curve");
            m_RawSatVsSat = o.Find("satVsSat.m_Value.m_Curve");
            m_RawLumVsSat = o.Find("lumVsSat.m_Value.m_Curve");
            m_SelectedCurve = o.Find("m_SelectedCurve");

            m_CurveEditor = new InspectorCurveEditor();
            m_CurveColors = new Dictionary<SerializedProperty, Color>();

            SetupCurve(m_RawMaster, Color.white, 2, false);
            SetupCurve(m_RawRed, Color.red, 2, false);
            SetupCurve(m_RawGreen, Color.green, 2, false);
            SetupCurve(m_RawBlue, new Color(0f, 0.5f, 1f), 2, false);
            SetupCurve(m_RawHueVsHue, Color.white, 0, true);
            SetupCurve(m_RawHueVsSat, Color.white, 0, true);
            SetupCurve(m_RawSatVsSat, Color.white, 0, false);
            SetupCurve(m_RawLumVsSat, Color.white, 0, false);
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space();
            ResetVisibleCurves();

            using (new EditorGUI.DisabledGroupScope(serializedObject.isEditingMultipleObjects))
            {
                var curveEditingId = Mathf.Clamp(m_SelectedCurve.intValue, 0, CurveNames.Length - 1);
                SerializedProperty currentCurveRawProperty = null;

                using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    curveEditingId = DoCurveSelectionPopup(curveEditingId);
                    curveEditingId = Mathf.Clamp(curveEditingId, 0, CurveNames.Length - 1);

                    EditorGUILayout.Space();

                    switch (curveEditingId)
                    {
                        case 0:
                            CurveOverrideToggle(m_Master.overrideState);
                            SetCurveVisible(m_RawMaster, m_Master.overrideState);
                            currentCurveRawProperty = m_RawMaster;
                            break;
                        case 1:
                            CurveOverrideToggle(m_Red.overrideState);
                            SetCurveVisible(m_RawRed, m_Red.overrideState);
                            currentCurveRawProperty = m_RawRed;
                            break;
                        case 2:
                            CurveOverrideToggle(m_Green.overrideState);
                            SetCurveVisible(m_RawGreen, m_Green.overrideState);
                            currentCurveRawProperty = m_RawGreen;
                            break;
                        case 3:
                            CurveOverrideToggle(m_Blue.overrideState);
                            SetCurveVisible(m_RawBlue, m_Blue.overrideState);
                            currentCurveRawProperty = m_RawBlue;
                            break;
                        case 4:
                            CurveOverrideToggle(m_HueVsHue.overrideState);
                            SetCurveVisible(m_RawHueVsHue, m_HueVsHue.overrideState);
                            currentCurveRawProperty = m_RawHueVsHue;
                            break;
                        case 5:
                            CurveOverrideToggle(m_HueVsSat.overrideState);
                            SetCurveVisible(m_RawHueVsSat, m_HueVsSat.overrideState);
                            currentCurveRawProperty = m_RawHueVsSat;
                            break;
                        case 6:
                            CurveOverrideToggle(m_SatVsSat.overrideState);
                            SetCurveVisible(m_RawSatVsSat, m_SatVsSat.overrideState);
                            currentCurveRawProperty = m_RawSatVsSat;
                            break;
                        case 7:
                            CurveOverrideToggle(m_LumVsSat.overrideState);
                            SetCurveVisible(m_RawLumVsSat, m_LumVsSat.overrideState);
                            currentCurveRawProperty = m_RawLumVsSat;
                            break;
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
                        ResetSelectedCurve(curveEditingId);

                    m_SelectedCurve.intValue = curveEditingId;
                }

                var rect = GUILayoutUtility.GetAspectRect(2f);
                var innerRect = new RectOffset(10, 10, 10, 10).Remove(rect);

                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));

                    if (curveEditingId == 4 || curveEditingId == 5)
                        DrawBackgroundTexture(innerRect, 0);
                    else if (curveEditingId == 6 || curveEditingId == 7)
                        DrawBackgroundTexture(innerRect, 1);

                    Handles.color = Color.white * (GUI.enabled ? 1f : 0.5f);
                    Handles.DrawSolidRectangleWithOutline(innerRect, Color.clear, new Color(0.8f, 0.8f, 0.8f, 0.5f));

                    Handles.color = new Color(1f, 1f, 1f, 0.05f);
                    var horizontalLines = Mathf.Max(1, (int)Mathf.Sqrt(innerRect.width));
                    var verticalLines = Mathf.Max(1, (int)(horizontalLines / (innerRect.width / innerRect.height)));

                    var gridOffset = Mathf.FloorToInt(innerRect.width / horizontalLines);
                    var gridPadding = ((int)innerRect.width % horizontalLines) / 2;

                    for (var i = 1; i < horizontalLines; i++)
                    {
                        var offset = i * gridOffset * Vector2.right;
                        offset.x += gridPadding;
                        Handles.DrawLine(innerRect.position + offset,
                            new Vector2(innerRect.x, innerRect.yMax - 1f) + offset);
                    }

                    gridOffset = Mathf.FloorToInt(innerRect.height / verticalLines);
                    gridPadding = ((int)innerRect.height % verticalLines) / 2;

                    for (var i = 1; i < verticalLines; i++)
                    {
                        var offset = i * gridOffset * Vector2.up;
                        offset.y += gridPadding;
                        Handles.DrawLine(innerRect.position + offset,
                            new Vector2(innerRect.xMax - 1f, innerRect.y) + offset);
                    }
                }

                using (new GUI.ClipScope(innerRect))
                {
                    if (m_CurveEditor.OnGUI(new Rect(0f, 0f, innerRect.width - 1f, innerRect.height - 1f)))
                    {
                        Repaint();
                        GUI.changed = true;
                        MarkTextureCurveAsDirty(curveEditingId);
                    }
                }

                if (Event.current.type != EventType.Repaint)
                    return;

                Handles.color = Color.black;
                Handles.DrawLine(new Vector2(rect.x, rect.y - 20f), new Vector2(rect.xMax, rect.y - 20f));
                Handles.DrawLine(new Vector2(rect.x, rect.y - 21f), new Vector2(rect.x, rect.yMax));
                Handles.DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.xMax, rect.yMax));
                Handles.DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.y - 20f));

                var editable = m_CurveEditor.GetCurveState(currentCurveRawProperty).editable;
                var editableLabel = editable ? string.Empty : "(Not Overriding)\n";

                var selection = m_CurveEditor.GetSelection();
                var infoRect = innerRect;
                infoRect.x += 5f;
                infoRect.width = 100f;
                infoRect.height = 30f;

                s_PreLabel ??= new GUIStyle("ShurikenLabel");

                if (selection.curve != null && selection.keyframeIndex > -1)
                {
                    var key = selection.keyframe.Value;
                    GUI.Label(infoRect, $"{key.time:F3}\n{key.value:F3}", s_PreLabel);
                }
                else
                {
                    GUI.Label(infoRect, editableLabel, s_PreLabel);
                }
            }
        }

        private void SetupCurve(SerializedProperty property, Color color, uint minPointCount, bool loop)
        {
            var state = InspectorCurveEditor.CurveState.defaultState;
            state.color = color;
            state.visible = false;
            state.minPointCount = minPointCount;
            state.onlyShowHandlesOnSelection = true;
            state.zeroKeyConstantValue = 0.5f;
            state.loopInBounds = loop;
            m_CurveEditor.Add(property, state);
            m_CurveColors.Add(property, color);
        }

        private void ResetVisibleCurves()
        {
            foreach (var curve in m_CurveColors)
            {
                var state = m_CurveEditor.GetCurveState(curve.Key);
                state.visible = false;
                m_CurveEditor.SetCurveState(curve.Key, state);
            }
        }

        private void SetCurveVisible(SerializedProperty rawProperty, SerializedProperty overrideProperty)
        {
            var state = m_CurveEditor.GetCurveState(rawProperty);
            state.visible = true;
            state.editable = overrideProperty.boolValue;
            m_CurveEditor.SetCurveState(rawProperty, state);
        }

        private static void CurveOverrideToggle(SerializedProperty overrideProperty)
        {
            overrideProperty.boolValue = GUILayout.Toggle(
                overrideProperty.boolValue,
                EditorGUIUtility.TrTextContent("Override"),
                EditorStyles.toolbarButton);
        }

        private string MakeCurveSelectionPopupLabel(int id)
        {
            var label = CurveNames[id];
            const string overrideSuffix = " (Overriding)";

            switch (id)
            {
                case 0:
                    if (m_Master.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 1:
                    if (m_Red.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 2:
                    if (m_Green.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 3:
                    if (m_Blue.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 4:
                    if (m_HueVsHue.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 5:
                    if (m_HueVsSat.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 6:
                    if (m_SatVsSat.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
                case 7:
                    if (m_LumVsSat.overrideState.boolValue)
                        label += overrideSuffix;
                    break;
            }

            return label;
        }

        private int DoCurveSelectionPopup(int id)
        {
            GUILayout.Label(MakeCurveSelectionPopupLabel(id), EditorStyles.toolbarPopup, GUILayout.MaxWidth(150f));

            var lastRect = GUILayoutUtility.GetLastRect();
            var currentEvent = Event.current;

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 ||
                !lastRect.Contains(currentEvent.mousePosition))
                return id;

            var menu = new GenericMenu();

            for (var i = 0; i < CurveNames.Length; i++)
            {
                if (i == 4)
                    menu.AddSeparator(string.Empty);

                var index = i;
                menu.AddItem(new GUIContent(MakeCurveSelectionPopupLabel(i)), index == id, () =>
                {
                    m_SelectedCurve.intValue = index;
                    serializedObject.ApplyModifiedProperties();
                });
            }

            menu.DropDown(new Rect(lastRect.xMin, lastRect.yMax, 1f, 1f));
            currentEvent.Use();
            return id;
        }

        private static Texture2D GetHueBackgroundTexture()
        {
            s_HueBackgroundTexture ??= CreateGradientTexture(t => Color.HSVToRGB(t, 1f, 1f));
            return s_HueBackgroundTexture;
        }

        private static Texture2D GetGrayscaleBackgroundTexture()
        {
            s_GrayscaleBackgroundTexture ??= CreateGradientTexture(t => new Color(t, t, t, 1f));
            return s_GrayscaleBackgroundTexture;
        }

        private static Texture2D CreateGradientTexture(System.Func<float, Color> evaluator)
        {
            const int width = 256;
            const int height = 2;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "VividRP.ColorCurvesBackground",
            };

            for (var x = 0; x < width; x++)
            {
                var color = evaluator(x / (width - 1f));
                for (var y = 0; y < height; y++)
                    texture.SetPixel(x, y, color);
            }

            texture.Apply(false, false);
            return texture;
        }

        private static void DrawBackgroundTexture(Rect rect, int pass)
        {
            if (s_CurveBackgroundMaterial == null)
            {
                var shader = Shader.Find(CurveBackgroundShaderName);
                if (shader != null)
                    s_CurveBackgroundMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (s_CurveBackgroundMaterial == null)
            {
                DrawBackgroundTexture(rect, pass == 0 ? GetHueBackgroundTexture() : GetGrayscaleBackgroundTexture());
                return;
            }

            var scale = EditorGUIUtility.pixelsPerPoint;
            var oldRenderTarget = RenderTexture.active;
            var renderTarget = RenderTexture.GetTemporary(
                Mathf.CeilToInt(rect.width * scale),
                Mathf.CeilToInt(rect.height * scale),
                0,
                GraphicsFormat.R8G8B8A8_SRGB);

            s_CurveBackgroundMaterial.SetFloat(DisabledStateShaderId, GUI.enabled ? 1f : 0.5f);
            Graphics.Blit(null, renderTarget, s_CurveBackgroundMaterial, pass);
            RenderTexture.active = oldRenderTarget;

            GUI.DrawTexture(rect, renderTarget);
            RenderTexture.ReleaseTemporary(renderTarget);
        }

        private static void DrawBackgroundTexture(Rect rect, Texture texture)
        {
            var previousColor = GUI.color;
            if (!GUI.enabled)
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.5f);

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
            GUI.color = previousColor;
        }

        private void ResetSelectedCurve(int curveId)
        {
            MarkTextureCurveAsDirty(curveId);

            switch (curveId)
            {
                case 0:
                    m_RawMaster.animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                    break;
                case 1:
                    m_RawRed.animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                    break;
                case 2:
                    m_RawGreen.animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                    break;
                case 3:
                    m_RawBlue.animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                    break;
                case 4:
                    m_RawHueVsHue.animationCurveValue = new AnimationCurve();
                    break;
                case 5:
                    m_RawHueVsSat.animationCurveValue = new AnimationCurve();
                    break;
                case 6:
                    m_RawSatVsSat.animationCurveValue = new AnimationCurve();
                    break;
                case 7:
                    m_RawLumVsSat.animationCurveValue = new AnimationCurve();
                    break;
            }
        }

        private void MarkTextureCurveAsDirty(int curveId)
        {
            if (target is not ColorCurves colorCurves)
                return;

            switch (curveId)
            {
                case 0:
                    colorCurves.master.value.SetDirty();
                    break;
                case 1:
                    colorCurves.red.value.SetDirty();
                    break;
                case 2:
                    colorCurves.green.value.SetDirty();
                    break;
                case 3:
                    colorCurves.blue.value.SetDirty();
                    break;
                case 4:
                    colorCurves.hueVsHue.value.SetDirty();
                    break;
                case 5:
                    colorCurves.hueVsSat.value.SetDirty();
                    break;
                case 6:
                    colorCurves.satVsSat.value.SetDirty();
                    break;
                case 7:
                    colorCurves.lumVsSat.value.SetDirty();
                    break;
            }
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(Tonemapping))]
    internal sealed class TonemappingEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_UseFullAces;
        private SerializedDataParameter m_ToeStrength;
        private SerializedDataParameter m_ToeLength;
        private SerializedDataParameter m_ShoulderStrength;
        private SerializedDataParameter m_ShoulderLength;
        private SerializedDataParameter m_ShoulderAngle;
        private SerializedDataParameter m_Gamma;
        private SerializedDataParameter m_LutTexture;
        private SerializedDataParameter m_LutContribution;
        private SerializedDataParameter m_NeutralHdrRangeReductionMode;
        private SerializedDataParameter m_AcesPreset;
        private SerializedDataParameter m_FallbackMode;
        private SerializedDataParameter m_HueShiftAmount;
        private SerializedDataParameter m_DetectPaperWhite;
        private SerializedDataParameter m_PaperWhite;
        private SerializedDataParameter m_DetectBrightnessLimits;
        private SerializedDataParameter m_MinNits;
        private SerializedDataParameter m_MaxNits;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Tonemapping>(serializedObject);
            m_Mode = Unpack(o.Find(x => x.mode));
            m_UseFullAces = Unpack(o.Find(x => x.useFullACES));
            m_ToeStrength = Unpack(o.Find(x => x.toeStrength));
            m_ToeLength = Unpack(o.Find(x => x.toeLength));
            m_ShoulderStrength = Unpack(o.Find(x => x.shoulderStrength));
            m_ShoulderLength = Unpack(o.Find(x => x.shoulderLength));
            m_ShoulderAngle = Unpack(o.Find(x => x.shoulderAngle));
            m_Gamma = Unpack(o.Find(x => x.gamma));
            m_LutTexture = Unpack(o.Find(x => x.lutTexture));
            m_LutContribution = Unpack(o.Find(x => x.lutContribution));
            m_NeutralHdrRangeReductionMode = Unpack(o.Find(x => x.neutralHDRRangeReductionMode));
            m_AcesPreset = Unpack(o.Find(x => x.acesPreset));
            m_FallbackMode = Unpack(o.Find(x => x.fallbackMode));
            m_HueShiftAmount = Unpack(o.Find(x => x.hueShiftAmount));
            m_DetectPaperWhite = Unpack(o.Find(x => x.detectPaperWhite));
            m_PaperWhite = Unpack(o.Find(x => x.paperWhite));
            m_DetectBrightnessLimits = Unpack(o.Find(x => x.detectBrightnessLimits));
            m_MinNits = Unpack(o.Find(x => x.minNits));
            m_MaxNits = Unpack(o.Find(x => x.maxNits));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);

            var mode = (TonemappingMode)m_Mode.value.enumValueIndex;
            switch (mode)
            {
                case TonemappingMode.ACES:
                    if (BeginAdditionalPropertiesScope())
                    {
                        PropertyField(m_UseFullAces);
                        EndAdditionalPropertiesScope();
                    }

                    break;
                case TonemappingMode.Custom:
                    DrawHeader("Custom Curve");
                    PropertyField(m_ToeStrength);
                    PropertyField(m_ToeLength);
                    PropertyField(m_ShoulderStrength);
                    PropertyField(m_ShoulderLength);
                    PropertyField(m_ShoulderAngle);
                    PropertyField(m_Gamma);
                    break;
                case TonemappingMode.External:
                    DrawHeader("External LUT");
                    PropertyField(m_LutTexture);
                    PropertyField(m_LutContribution);
                    break;
            }

            if (!BeginAdditionalPropertiesScope())
                return;

            DrawHeader("HDR Output");
            if (mode == TonemappingMode.Neutral)
                PropertyField(m_NeutralHdrRangeReductionMode);
            else if (mode == TonemappingMode.ACES)
                PropertyField(m_AcesPreset);

            PropertyField(m_FallbackMode);
            PropertyField(m_HueShiftAmount);
            PropertyField(m_DetectPaperWhite);
            if (!m_DetectPaperWhite.value.boolValue)
                PropertyField(m_PaperWhite);

            PropertyField(m_DetectBrightnessLimits);
            if (!m_DetectBrightnessLimits.value.boolValue)
            {
                PropertyField(m_MinNits);
                PropertyField(m_MaxNits);
            }

            EndAdditionalPropertiesScope();
        }
    }
}