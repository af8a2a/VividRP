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
    sealed class ShadowsMidtonesHighlightsEditor : VolumeComponentEditor
    {
        static class Styles
        {
            public static readonly GUIContent shadowsLabel = EditorGUIUtility.TrTextContent("Shadows","Use this to control and apply a hue to the shadows.");
            public static readonly GUIContent midtonesLabel = EditorGUIUtility.TrTextContent("Midtones", "Use this to control and apply a hue to the midtones.");
            public static readonly GUIContent highlightsLabel = EditorGUIUtility.TrTextContent("Highlights", "Use this to control and apply a hue to the highlights.");
        }

        SerializedDataParameter m_Shadows;
        SerializedDataParameter m_Midtones;
        SerializedDataParameter m_Highlights;
        SerializedDataParameter m_ShadowsStart;
        SerializedDataParameter m_ShadowsEnd;
        SerializedDataParameter m_HighlightsStart;
        SerializedDataParameter m_HighlightsEnd;

        readonly TrackballUIDrawer m_TrackballUIDrawer = new TrackballUIDrawer();

        // Curve drawing utilities
        Rect m_CurveRect;
        Material m_Material;
        RenderTexture m_CurveTex;

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

            m_Material = new Material(Shader.Find("Hidden/VividRP/Editor/Shadows Midtones Highlights Curve"));
        }

        public override void OnInspectorGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                m_TrackballUIDrawer.OnGUI(m_Shadows.value, enableOverrides ? m_Shadows.overrideState : null, Styles.shadowsLabel, GetWheelValue);
                GUILayout.Space(4f);
                m_TrackballUIDrawer.OnGUI(m_Midtones.value, enableOverrides ? m_Midtones.overrideState : null, Styles.midtonesLabel, GetWheelValue);
                GUILayout.Space(4f);
                m_TrackballUIDrawer.OnGUI(m_Highlights.value, enableOverrides ? m_Highlights.overrideState : null, Styles.highlightsLabel, GetWheelValue);
            }
            EditorGUILayout.Space();

            // Reserve GUI space
            m_CurveRect = GUILayoutUtility.GetRect(128, 80);
            m_CurveRect.xMin += EditorGUI.indentLevel * 15f;

            if (Event.current.type == EventType.Repaint)
            {
                float alpha = GUI.enabled ? 1f : 0.4f;
                var limits = new Vector4(m_ShadowsStart.value.floatValue, m_ShadowsEnd.value.floatValue, m_HighlightsStart.value.floatValue, m_HighlightsEnd.value.floatValue);

                m_Material.SetVector("_ShaHiLimits", limits);
                m_Material.SetVector("_Variants", new Vector4(alpha, Mathf.Max(m_HighlightsEnd.value.floatValue, 1f), 0f, 0f));

                CheckCurveRT((int)m_CurveRect.width, (int)m_CurveRect.height);

                var oldRt = RenderTexture.active;
                Graphics.Blit(null, m_CurveTex, m_Material, EditorGUIUtility.isProSkin ? 0 : 1);
                RenderTexture.active = oldRt;

                GUI.DrawTexture(m_CurveRect, m_CurveTex);

                Handles.DrawSolidRectangleWithOutline(m_CurveRect, Color.clear, Color.white * 0.4f);
            }

            PropertyField(m_ShadowsStart, EditorGUIUtility.TrTextContent("Start"));
            m_ShadowsStart.value.floatValue = Mathf.Min(m_ShadowsStart.value.floatValue, m_ShadowsEnd.value.floatValue);
            PropertyField(m_ShadowsEnd, EditorGUIUtility.TrTextContent("End"));
            m_ShadowsEnd.value.floatValue = Mathf.Max(m_ShadowsStart.value.floatValue, m_ShadowsEnd.value.floatValue);

            PropertyField(m_HighlightsStart, EditorGUIUtility.TrTextContent("Start"));
            m_HighlightsStart.value.floatValue = Mathf.Min(m_HighlightsStart.value.floatValue, m_HighlightsEnd.value.floatValue);
            PropertyField(m_HighlightsEnd, EditorGUIUtility.TrTextContent("End"));
            m_HighlightsEnd.value.floatValue = Mathf.Max(m_HighlightsStart.value.floatValue, m_HighlightsEnd.value.floatValue);
        }

        void CheckCurveRT(int width, int height)
        {
            if (m_CurveTex == null || !m_CurveTex.IsCreated() || m_CurveTex.width != width || m_CurveTex.height != height)
            {
                CoreUtils.Destroy(m_CurveTex);
                m_CurveTex = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB);
                m_CurveTex.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        Vector3 GetWheelValue(Vector4 v)
        {
            float w = v.w * (Mathf.Sign(v.w) < 0f ? 1f : 4f);
            return new Vector3(
                Mathf.Max(v.x + w, 0f),
                Mathf.Max(v.y + w, 0f),
                Mathf.Max(v.z + w, 0f)
            );
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
    sealed class TonemappingEditor : VolumeComponentEditor
    {
        static readonly int s_GtToneMapParams0Id = Shader.PropertyToID("_GTToneMap_Params0");
        static readonly int s_GtToneMapParams1Id = Shader.PropertyToID("_GTToneMap_Params1");
        static readonly int s_LpmParams0Id = Shader.PropertyToID("_LPM_Params0");
        static readonly int s_LpmParams1Id = Shader.PropertyToID("_LPM_Params1");
        static readonly int s_LpmParams2Id = Shader.PropertyToID("_LPM_Params2");
        static readonly int s_LpmParams3Id = Shader.PropertyToID("_LPM_Params3");
        static readonly int s_LpmParams4Id = Shader.PropertyToID("_LPM_Params4");
        static readonly int s_LpmParams5Id = Shader.PropertyToID("_LPM_Params5");
        static readonly int s_LpmParams6Id = Shader.PropertyToID("_LPM_Params6");
        static readonly int s_LpmParams7Id = Shader.PropertyToID("_LPM_Params7");
        static readonly int s_LpmParams8Id = Shader.PropertyToID("_LPM_Params8");
        static readonly int s_LpmParams9Id = Shader.PropertyToID("_LPM_Params9");
        static readonly int s_LpmFlagsId = Shader.PropertyToID("_LPM_Flags");
        static readonly int s_LpmFlags2Id = Shader.PropertyToID("_LPM_Flags2");
        static readonly int s_VariantsId = Shader.PropertyToID("_Variants");

        SerializedDataParameter m_Mode;
        SerializedDataParameter m_UseFullACES;
        SerializedDataParameter m_MaxBrightness;
        SerializedDataParameter m_Contrast;
        SerializedDataParameter m_LinearSectionStart;
        SerializedDataParameter m_LinearSectionLength;
        SerializedDataParameter m_BlackPow;
        SerializedDataParameter m_BlackMin;
        SerializedDataParameter m_LpmShoulder;
        SerializedDataParameter m_LpmHdrMax;
        SerializedDataParameter m_LpmColorGamut;
        SerializedDataParameter m_LpmSoftGap;
        SerializedDataParameter m_LpmExposure;
        SerializedDataParameter m_LpmContrast;
        SerializedDataParameter m_LpmShoulderContrast;
        SerializedDataParameter m_LpmSaturation;
        SerializedDataParameter m_LpmCrosstalk;
        SerializedDataParameter m_ToeStrength;
        SerializedDataParameter m_ToeLength;
        SerializedDataParameter m_ShoulderStrength;
        SerializedDataParameter m_ShoulderLength;
        SerializedDataParameter m_ShoulderAngle;
        SerializedDataParameter m_Gamma;
        SerializedDataParameter m_LutTexture;
        SerializedDataParameter m_LutContribution;

        // HDR Mode.
        SerializedDataParameter m_NeutralHDRRangeReductionMode;
        SerializedDataParameter m_HueShiftAmount;
        SerializedDataParameter m_HDRDetectPaperWhite;
        SerializedDataParameter m_HDRPaperwhite;
        SerializedDataParameter m_HDRDetectNitLimits;
        SerializedDataParameter m_HDRMinNits;
        SerializedDataParameter m_HDRMaxNits;
        SerializedDataParameter m_HDRAcesPreset;
        SerializedDataParameter m_HDRFallbackMode;

        public override bool hasAdditionalProperties => true;

        // Curve drawing utilities
        readonly HableCurve m_HableCurve = new HableCurve();
        Rect m_CurveRect;
        Material m_Material;
        RenderTexture m_CurveTex;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Tonemapping>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));
            m_UseFullACES = Unpack(o.Find(x => x.useFullACES));
            m_MaxBrightness = Unpack(o.Find(x => x.maxBrightness));
            m_Contrast = Unpack(o.Find(x => x.contrast));
            m_LinearSectionStart = Unpack(o.Find(x => x.linearSectionStart));
            m_LinearSectionLength = Unpack(o.Find(x => x.linearSectionLength));
            m_BlackPow = Unpack(o.Find(x => x.blackPow));
            m_BlackMin = Unpack(o.Find(x => x.blackMin));
            m_LpmShoulder = Unpack(o.Find(x => x.lpmShoulder));
            m_LpmHdrMax = Unpack(o.Find(x => x.lpmHdrMax));
            m_LpmColorGamut = Unpack(o.Find(x => x.lpmColorGamut));
            m_LpmSoftGap = Unpack(o.Find(x => x.lpmSoftGap));
            m_LpmExposure = Unpack(o.Find(x => x.lpmExposure));
            m_LpmContrast = Unpack(o.Find(x => x.lpmContrast));
            m_LpmShoulderContrast = Unpack(o.Find(x => x.lpmShoulderContrast));
            m_LpmSaturation = Unpack(o.Find(x => x.lpmSaturation));
            m_LpmCrosstalk = Unpack(o.Find(x => x.lpmCrosstalk));
            m_ToeStrength = Unpack(o.Find(x => x.toeStrength));
            m_ToeLength = Unpack(o.Find(x => x.toeLength));
            m_ShoulderStrength = Unpack(o.Find(x => x.shoulderStrength));
            m_ShoulderLength = Unpack(o.Find(x => x.shoulderLength));
            m_ShoulderAngle = Unpack(o.Find(x => x.shoulderAngle));
            m_Gamma = Unpack(o.Find(x => x.gamma));
            m_LutTexture = Unpack(o.Find(x => x.lutTexture));
            m_LutContribution = Unpack(o.Find(x => x.lutContribution));

            m_NeutralHDRRangeReductionMode = Unpack(o.Find(x => x.neutralHDRRangeReductionMode));
            m_HueShiftAmount = Unpack(o.Find(x => x.hueShiftAmount));
            m_HDRDetectPaperWhite = Unpack(o.Find(x => x.detectPaperWhite));
            m_HDRPaperwhite = Unpack(o.Find(x => x.paperWhite));
            m_HDRDetectNitLimits = Unpack(o.Find(x => x.detectBrightnessLimits));
            m_HDRMinNits = Unpack(o.Find(x => x.minNits));
            m_HDRMaxNits = Unpack(o.Find(x => x.maxNits));
            m_HDRAcesPreset = Unpack(o.Find(x => x.acesPreset));
            m_HDRFallbackMode = Unpack(o.Find(x => x.fallbackMode));

            var shader = Shader.Find("Hidden/VividRP/Editor/Custom Tonemapper Curve");
            if (shader != null)
                m_Material = new Material(shader);
        }

        public override void OnDisable()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;

            CoreUtils.Destroy(m_CurveTex);
            m_CurveTex = null;
        }

        internal bool HDROutputIsActive()
        {
            return SystemInfo.hdrDisplaySupportFlags.HasFlag(HDRDisplaySupportFlags.Supported) && HDROutputSettings.main.active;
        }

        void DrawCurvePreview()
        {
            if (m_Material == null)
                return;

            EditorGUILayout.Space();
            m_CurveRect = GUILayoutUtility.GetRect(128, 80);
            m_CurveRect.xMin += EditorGUI.indentLevel * 15f;

            if (Event.current.type != EventType.Repaint)
                return;

            CheckCurveRT((int)m_CurveRect.width, (int)m_CurveRect.height);

            var oldRt = RenderTexture.active;
            Graphics.Blit(null, m_CurveTex, m_Material, EditorGUIUtility.isProSkin ? 0 : 1);
            RenderTexture.active = oldRt;

            GUI.DrawTexture(m_CurveRect, m_CurveTex);
            Handles.DrawSolidRectangleWithOutline(m_CurveRect, Color.clear, Color.white * 0.4f);
        }

        void ConfigureCustomCurvePreview()
        {
            if (m_Material == null)
                return;

            m_HableCurve.Init(
                m_ToeStrength.value.floatValue,
                m_ToeLength.value.floatValue,
                m_ShoulderStrength.value.floatValue,
                m_ShoulderLength.value.floatValue,
                m_ShoulderAngle.value.floatValue,
                m_Gamma.value.floatValue);

            float alpha = GUI.enabled ? 1f : 0.5f;

            m_Material.SetVector("_CustomToneCurve", m_HableCurve.uniforms.curve);
            m_Material.SetVector("_ToeSegmentA", m_HableCurve.uniforms.toeSegmentA);
            m_Material.SetVector("_ToeSegmentB", m_HableCurve.uniforms.toeSegmentB);
            m_Material.SetVector("_MidSegmentA", m_HableCurve.uniforms.midSegmentA);
            m_Material.SetVector("_MidSegmentB", m_HableCurve.uniforms.midSegmentB);
            m_Material.SetVector("_ShoSegmentA", m_HableCurve.uniforms.shoSegmentA);
            m_Material.SetVector("_ShoSegmentB", m_HableCurve.uniforms.shoSegmentB);
            m_Material.SetVector(s_GtToneMapParams0Id, Vector4.zero);
            m_Material.SetVector(s_GtToneMapParams1Id, Vector4.zero);
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, m_HableCurve.whitePoint, 0f, 0f));
        }

        void ConfigureGranTurismoCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_GtToneMapParams0Id, ColorGradingSettingsResolver.BuildGranTurismoParams0(
                m_MaxBrightness.value.floatValue,
                m_Contrast.value.floatValue,
                m_LinearSectionStart.value.floatValue,
                m_LinearSectionLength.value.floatValue));
            m_Material.SetVector(s_GtToneMapParams1Id, ColorGradingSettingsResolver.BuildGranTurismoParams1(
                m_BlackPow.value.floatValue,
                m_BlackMin.value.floatValue));
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 1f, 0f));
        }

        void ConfigureAgXCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 2f, 0f));
        }

        void ConfigureKhronosPbrCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 3f, 0f));
        }

        void ConfigureLpmCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            var outputGamut = (LpmColorGamut)m_LpmColorGamut.value.intValue;
            var lpmData = LpmTonemapperUtility.CreateForLinearOutput(
                LpmColorGamut.Rec709,
                outputGamut,
                m_LpmShoulder.value.boolValue,
                m_LpmSoftGap.value.floatValue,
                m_LpmHdrMax.value.floatValue,
                m_LpmExposure.value.floatValue,
                m_LpmContrast.value.floatValue,
                m_LpmShoulderContrast.value.floatValue,
                m_LpmSaturation.value.vector3Value,
                m_LpmCrosstalk.value.vector3Value);

            m_Material.SetVector(s_LpmParams0Id, lpmData.Params0);
            m_Material.SetVector(s_LpmParams1Id, lpmData.Params1);
            m_Material.SetVector(s_LpmParams2Id, lpmData.Params2);
            m_Material.SetVector(s_LpmParams3Id, lpmData.Params3);
            m_Material.SetVector(s_LpmParams4Id, lpmData.Params4);
            m_Material.SetVector(s_LpmParams5Id, lpmData.Params5);
            m_Material.SetVector(s_LpmParams6Id, lpmData.Params6);
            m_Material.SetVector(s_LpmParams7Id, lpmData.Params7);
            m_Material.SetVector(s_LpmParams8Id, lpmData.Params8);
            m_Material.SetVector(s_LpmParams9Id, lpmData.Params9);
            m_Material.SetVector(s_LpmFlagsId, lpmData.Flags);
            m_Material.SetVector(s_LpmFlags2Id, lpmData.Flags2);
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 7f, 0f));
        }
        void ConfigureACESCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 4f, 0f));
        }

        void ConfigureNeutralCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 5f, 0f));
        }

        void ConfigureNoneCurvePreview()
        {
            if (m_Material == null)
                return;

            float alpha = GUI.enabled ? 1f : 0.5f;
            m_Material.SetVector(s_VariantsId, new Vector4(alpha, 1f, 6f, 0f));
        }


        public override void OnInspectorGUI()
        {
            bool hdrInPlayerSettings = UnityEditor.PlayerSettings.allowHDRDisplaySupport;

            PropertyField(m_Mode);

            if (m_Mode.value.intValue == (int)TonemappingMode.None)
            {
                ConfigureNoneCurvePreview();
                DrawCurvePreview();
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.Neutral)
            {
                ConfigureNeutralCurvePreview();
                DrawCurvePreview();
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.GranTurismo)
            {
                ConfigureGranTurismoCurvePreview();
                DrawCurvePreview();

                PropertyField(m_MaxBrightness);
                PropertyField(m_Contrast);
                PropertyField(m_LinearSectionStart);
                PropertyField(m_LinearSectionLength);
                PropertyField(m_BlackPow);
                PropertyField(m_BlackMin);
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.AgX)
            {
                ConfigureAgXCurvePreview();
                DrawCurvePreview();
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.KhronosPBR)
            {
                ConfigureKhronosPbrCurvePreview();
                DrawCurvePreview();
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.LPM)
            {
                ConfigureLpmCurvePreview();
                DrawCurvePreview();

                PropertyField(m_LpmShoulder);
                PropertyField(m_LpmHdrMax);
                PropertyField(m_LpmColorGamut);
                PropertyField(m_LpmSoftGap);
                PropertyField(m_LpmExposure);
                PropertyField(m_LpmContrast);
                PropertyField(m_LpmShoulderContrast);
                PropertyField(m_LpmSaturation);
                PropertyField(m_LpmCrosstalk);
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.Custom)
            {
                ConfigureCustomCurvePreview();
                DrawCurvePreview();

                PropertyField(m_ToeStrength);
                PropertyField(m_ToeLength);
                PropertyField(m_ShoulderStrength);
                PropertyField(m_ShoulderLength);
                PropertyField(m_ShoulderAngle);
                PropertyField(m_Gamma);
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.External)
            {
                PropertyField(m_LutTexture, EditorGUIUtility.TrTextContent("Lookup Texture"));

                var lut = m_LutTexture.value.objectReferenceValue;
                if (lut != null && !((Tonemapping)target).ValidateLUT())
                    EditorGUILayout.HelpBox("Invalid lookup texture. It must be a 3D Texture or a Render Texture and have the same size as set in the HDRP settings.", MessageType.Warning);

                PropertyField(m_LutContribution, EditorGUIUtility.TrTextContent("Contribution"));

                EditorGUILayout.HelpBox("Use \"Edit > Rendering > Render Selected HDRP Camera to Log EXR\" to export a log-encoded frame for external grading.", MessageType.Info);
            }
            else if (m_Mode.value.intValue == (int)TonemappingMode.ACES)
            {
                ConfigureACESCurvePreview();
                DrawCurvePreview();

                PropertyField(m_UseFullACES);
            }

            if (hdrInPlayerSettings && m_Mode.value.intValue != (int)TonemappingMode.None)
            {
                EditorGUILayout.LabelField("HDR Output");

                if (!HDROutputIsActive())
                {
                    EditorGUILayout.HelpBox("HDR is not currently active. Settings will take effect when a compatible device is found.", MessageType.Info);
                }

                int hdrTonemapMode = m_Mode.value.intValue;
                if (hdrTonemapMode == (int)TonemappingMode.GranTurismo ||
                    hdrTonemapMode == (int)TonemappingMode.KhronosPBR ||
                    hdrTonemapMode == (int)TonemappingMode.Custom ||
                    hdrTonemapMode == (int)TonemappingMode.External)
                {
                    EditorGUILayout.HelpBox("The selected tonemapping mode is not supported in HDR Output mode. Select a fallback mode.", MessageType.Warning);
                    PropertyField(m_HDRFallbackMode);
                    hdrTonemapMode = (m_HDRFallbackMode.value.intValue == (int)FallbackHDRTonemap.ACES) ? (int)TonemappingMode.ACES :
                                     (m_HDRFallbackMode.value.intValue == (int)FallbackHDRTonemap.Neutral) ? (int)TonemappingMode.Neutral :
                                     (int)TonemappingMode.None;
                }

                if (hdrTonemapMode == (int)TonemappingMode.Neutral)
                {
                    PropertyField(m_NeutralHDRRangeReductionMode);
                    PropertyField(m_HueShiftAmount);

                    PropertyField(m_HDRDetectPaperWhite);
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(m_HDRDetectPaperWhite.value.boolValue))
                    {
                        PropertyField(m_HDRPaperwhite);
                    }
                    EditorGUI.indentLevel--;
                    PropertyField(m_HDRDetectNitLimits);
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(m_HDRDetectNitLimits.value.boolValue))
                    {
                        PropertyField(m_HDRMinNits);
                        PropertyField(m_HDRMaxNits);
                    }
                    EditorGUI.indentLevel--;
                }
                if (hdrTonemapMode == (int)TonemappingMode.LPM)
                {
                    PropertyField(m_HDRDetectPaperWhite);
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(m_HDRDetectPaperWhite.value.boolValue))
                    {
                        PropertyField(m_HDRPaperwhite);
                    }
                    EditorGUI.indentLevel--;
                    PropertyField(m_HDRDetectNitLimits);
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(m_HDRDetectNitLimits.value.boolValue))
                    {
                        PropertyField(m_HDRMinNits);
                        PropertyField(m_HDRMaxNits);
                    }
                    EditorGUI.indentLevel--;
                }
                if (hdrTonemapMode == (int)TonemappingMode.ACES)
                {
                    PropertyField(m_HDRAcesPreset);
                    PropertyField(m_HDRDetectPaperWhite);
                    EditorGUI.indentLevel++;
                    using (new EditorGUI.DisabledScope(m_HDRDetectPaperWhite.value.boolValue))
                    {
                        PropertyField(m_HDRPaperwhite);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        void CheckCurveRT(int width, int height)
        {
            if (m_CurveTex == null || !m_CurveTex.IsCreated() || m_CurveTex.width != width || m_CurveTex.height != height)
            {
                CoreUtils.Destroy(m_CurveTex);
                m_CurveTex = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB);
                m_CurveTex.hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(FilmGrain))]
    internal sealed class FilmGrainEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Type;
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_Response;
        SerializedDataParameter m_Texture;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<FilmGrain>(serializedObject);
            m_Type = Unpack(o.Find(x => x.type));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Response = Unpack(o.Find(x => x.response));
            m_Texture = Unpack(o.Find(x => x.texture));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Type);
            PropertyField(m_Intensity);
            PropertyField(m_Response);

            if (m_Type.value.intValue == (int)FilmGrainLookup.Custom)
            {
                PropertyField(m_Texture);

                if (m_Texture.value.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("A custom grain texture is required when Type is set to Custom.", MessageType.Warning);
            }
        }
    }

}
