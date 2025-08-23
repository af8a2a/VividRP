using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VividToneMapping))]
    public class VividToneMappingEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Mode;

        // HDR Mode.
        SerializedDataParameter m_NeutralHDRRangeReductionMode;
        SerializedDataParameter m_HueShiftAmount;
        SerializedDataParameter m_HDRDetectPaperWhite;
        SerializedDataParameter m_HDRPaperwhite;
        SerializedDataParameter m_HDRDetectNitLimits;
        SerializedDataParameter m_HDRMinNits;
        SerializedDataParameter m_HDRMaxNits;
        SerializedDataParameter m_HDRAcesPreset;


        // GT Tonemapping
        SerializedDataParameter m_MaxBrightness;
        SerializedDataParameter m_Contrast;
        SerializedDataParameter m_LinearSectionStart;
        SerializedDataParameter m_LinearSectionLength;
        SerializedDataParameter m_BlackPow;
        SerializedDataParameter m_BlackMin;


        Material m_Material;
        private static readonly int _GTToneMap_Params0 = Shader.PropertyToID("_GTToneMap_Params0");
        private static readonly int _GTToneMap_Params1 = Shader.PropertyToID("_GTToneMap_Params1");

        RenderTexture m_CurveTex;
        Rect m_CurveRect;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<VividToneMapping>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));
            m_NeutralHDRRangeReductionMode = Unpack(o.Find(x => x.neutralHDRRangeReductionMode));
            m_HueShiftAmount = Unpack(o.Find(x => x.hueShiftAmount));
            m_HDRDetectPaperWhite = Unpack(o.Find(x => x.detectPaperWhite));
            m_HDRPaperwhite = Unpack(o.Find(x => x.paperWhite));
            m_HDRDetectNitLimits = Unpack(o.Find(x => x.detectBrightnessLimits));
            m_HDRMinNits = Unpack(o.Find(x => x.minNits));
            m_HDRMaxNits = Unpack(o.Find(x => x.maxNits));
            m_HDRAcesPreset = Unpack(o.Find(x => x.acesPreset));


            m_MaxBrightness = Unpack(o.Find(x => x.maxBrightness));
            m_Contrast = Unpack(o.Find(x => x.contrast));
            m_LinearSectionStart = Unpack(o.Find(x => x.linearSectionStart));
            m_LinearSectionLength = Unpack(o.Find(x => x.linearSectionLength));
            m_BlackPow = Unpack(o.Find(x => x.blackPow));
            m_BlackMin = Unpack(o.Find(x => x.blackMin));


            m_Material = new Material(Shader.Find("Hidden/HD PostProcessing/Editor/Custom Tonemapper Curve"));
            base.OnEnable();
        }


        void CheckCurveRT(int width, int height)
        {
            if (m_CurveTex == null || !m_CurveTex.IsCreated() || m_CurveTex.width != width ||
                m_CurveTex.height != height)
            {
                CoreUtils.Destroy(m_CurveTex);
                m_CurveTex = new RenderTexture(width, height, 0, GraphicsFormat.R8G8B8A8_SRGB);
                m_CurveTex.hideFlags = HideFlags.HideAndDontSave;
            }
        }


        public override void OnDisable()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;

            CoreUtils.Destroy(m_CurveTex);
            m_CurveTex = null;
        }

        void DrawCurve(VividToneMapping toneMapping)
        {
            // Reserve GUI space
            m_CurveRect = GUILayoutUtility.GetRect(128, 80);
            m_CurveRect.xMin += EditorGUI.indentLevel * 15f;
            if (Event.current.type == EventType.Repaint)
            {
                var gtToneMapParams0 = new Vector4(toneMapping.maxBrightness.value, toneMapping.contrast.value,
                    toneMapping.linearSectionStart.value, toneMapping.linearSectionLength.value);
                var gtToneMapParams1 = new Vector4(toneMapping.blackPow.value, toneMapping.blackMin.value, 0.0f,
                    0.0f);

                m_Material.SetVector(_GTToneMap_Params0, gtToneMapParams0);
                m_Material.SetVector(_GTToneMap_Params1, gtToneMapParams1);

                CheckCurveRT((int)m_CurveRect.width, (int)m_CurveRect.height);

                var oldRt = RenderTexture.active;
                Graphics.Blit(null, m_CurveTex, m_Material, EditorGUIUtility.isProSkin ? 0 : 1);
                RenderTexture.active = oldRt;

                GUI.DrawTexture(m_CurveRect, m_CurveTex);

                Handles.DrawSolidRectangleWithOutline(m_CurveRect, Color.clear, Color.white * 0.4f);
            }

        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);

            // Display a warning if the user is trying to use a tonemap while rendering in LDR
            var asset = UniversalRenderPipeline.asset;
            int hdrTonemapMode = m_Mode.value.intValue;

            var toneMapping = target as VividToneMapping;

            m_Material.enabledKeywords = null;
            
            switch (toneMapping.mode.value)
            {
                case VividTonemappingMode.Neutral:
                    m_Material.EnableKeyword(ShaderKeywordStrings.TonemapNeutral);
                    break;
                case VividTonemappingMode.ACES:
                    m_Material.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                    break;
                case VividTonemappingMode.GranTurismo:
                    m_Material.EnableKeyword(ShaderKeywordStrings.TonemapGranTurismo);
                    break;
                
                case VividTonemappingMode.AgX:
                    m_Material.EnableKeyword(ShaderKeywordStrings.TonemapAgx);
                    break;
            }


            


            if (hdrTonemapMode == (int)VividTonemappingMode.GranTurismo)
            {
                PropertyField(m_MaxBrightness);
                PropertyField(m_Contrast);
                PropertyField(m_LinearSectionStart);
                PropertyField(m_LinearSectionLength);
                PropertyField(m_BlackPow);
                PropertyField(m_BlackMin);
            }

            DrawCurve(toneMapping);

            if (asset != null && !asset.supportsHDR && hdrTonemapMode != (int)VividTonemappingMode.None)
            {
                EditorGUILayout.HelpBox(
                    "Tonemapping should only be used when working with High Dynamic Range (HDR). Please enable HDR through the active Render Pipeline Asset.",
                    MessageType.Warning);
                return;
            }

            if (PlayerSettings.allowHDRDisplaySupport && hdrTonemapMode != (int)VividTonemappingMode.None)
            {
                EditorGUILayout.LabelField("HDR Output");

                if (hdrTonemapMode == (int)VividTonemappingMode.Neutral)
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

                if (hdrTonemapMode == (int)VividTonemappingMode.ACES)
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

                if (hdrTonemapMode == (int)VividTonemappingMode.GranTurismo)
                {
                }
            }
        }
    }
}