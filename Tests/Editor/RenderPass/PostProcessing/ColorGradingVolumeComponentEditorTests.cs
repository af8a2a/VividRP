using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ColorGradingVolumeComponentEditorTests
    {
        [TestCase(typeof(WhiteBalance), "WhiteBalanceEditor")]
        [TestCase(typeof(ColorAdjustments), "ColorAdjustmentsEditor")]
        [TestCase(typeof(AutoExposure), "AutoExposureEditor")]
        [TestCase(typeof(ChannelMixer), "ChannelMixerEditor")]
        [TestCase(typeof(SplitToning), "SplitToningEditor")]
        [TestCase(typeof(LiftGammaGain), "LiftGammaGainEditor")]
        [TestCase(typeof(ShadowsMidtonesHighlights), "ShadowsMidtonesHighlightsEditor")]
        [TestCase(typeof(ColorCurves), "ColorCurvesEditor")]
        [TestCase(typeof(Tonemapping), "TonemappingEditor")]
        public void CreateEditor_UsesCustomColorGradingEditor(Type componentType, string editorTypeName)
        {
            var component = ScriptableObject.CreateInstance(componentType);
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo(editorTypeName));
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [TestCase(typeof(WhiteBalance))]
        [TestCase(typeof(ColorAdjustments))]
        [TestCase(typeof(AutoExposure))]
        [TestCase(typeof(ChannelMixer))]
        [TestCase(typeof(SplitToning))]
        [TestCase(typeof(LiftGammaGain))]
        [TestCase(typeof(ShadowsMidtonesHighlights))]
        [TestCase(typeof(ColorCurves))]
        [TestCase(typeof(Tonemapping))]
        public void CreateInstance_ResolvesMonoScriptAsset(Type componentType)
        {
            var component = ScriptableObject.CreateInstance(componentType);

            try
            {
                var script = MonoScript.FromScriptableObject(component);

                Assert.That(script, Is.Not.Null);
                Assert.That(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(script)), Is.EqualTo(componentType.Name));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void ColorCurvesEditor_InitializesHdrpStyleCurveEditorState()
        {
            var component = ScriptableObject.CreateInstance<ColorCurves>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var serializedObject = new SerializedObject(component);
                Assert.That(serializedObject.FindProperty("m_SelectedCurve"), Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_CurveEditor", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_SelectedCurve", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_RawMaster", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_RawHueVsHue", flags)?.GetValue(editor), Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void ColorCurvesEditor_CurveBackgroundShaderIsAvailable()
        {
            Assert.That(Shader.Find("Hidden/VividRP PostProcessing/Editor/CurveBackground"), Is.Not.Null);
        }

        [Test]
        public void AutoExposureEditor_StatsPreviewShaderIsAvailable()
        {
            Assert.That(Shader.Find("Hidden/VividRP/Editor/Auto Exposure Stats"), Is.Not.Null);
        }

        [Test]
        public void TonemappingEditor_InitializesGranTurismoControls()
        {
            var component = ScriptableObject.CreateInstance<Tonemapping>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_MaxBrightness", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LinearSectionLength", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_BlackMin", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LpmHdrMax", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LpmColorGamut", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LpmSoftGap", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LpmSaturation", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_LpmCrosstalk", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Material", flags)?.GetValue(editor), Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void TonemappingEditor_ConfigureLpmPreview_SetsLpmVariantAndParams()
        {
            var component = ScriptableObject.CreateInstance<Tonemapping>();
            component.mode.value = TonemappingMode.LPM;
            component.lpmShoulder.value = true;
            component.lpmContrast.value = 0.25f;
            component.lpmColorGamut.value = LpmColorGamut.Rec2020;
            component.lpmSoftGap.value = 0.05f;
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configureMethod = editorType.GetMethod("ConfigureLpmCurvePreview", flags);
                var material = editorType.GetField("m_Material", flags)?.GetValue(editor) as Material;

                Assert.That(configureMethod, Is.Not.Null);
                Assert.That(material, Is.Not.Null);

                configureMethod.Invoke(editor, null);

                var variants = material.GetVector("_Variants");
                var lpmToneParams = material.GetVector("_LPM_ToneParams");
                var lpmScaleBiasSoftGap = material.GetVector("_LPM_ScaleBiasSoftGap");
                var lpmTargetLuma = material.GetVector("_LPM_TargetLuma");
                var lpmCrosstalk = material.GetVector("_LPM_Crosstalk");
                var lpmConR = material.GetVector("_LPM_ConR");
                Assert.That(variants.z, Is.EqualTo(7f));
                Assert.That(lpmToneParams.w, Is.EqualTo(1.25f).Within(1e-5f));
                Assert.That(lpmTargetLuma.w, Is.EqualTo(1f));
                Assert.That(lpmCrosstalk.w, Is.EqualTo(1f));
                Assert.That(new Vector3(lpmConR.x, lpmConR.y, lpmConR.z).sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(lpmScaleBiasSoftGap.z, Is.EqualTo(0.05f).Within(1e-5f));
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void TonemappingEditor_ConfigureAgXPreview_SetsAgXVariant()
        {
            var component = ScriptableObject.CreateInstance<Tonemapping>();
            component.mode.value = TonemappingMode.AgX;
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configureMethod = editorType.GetMethod("ConfigureAgXCurvePreview", flags);
                var material = editorType.GetField("m_Material", flags)?.GetValue(editor) as Material;

                Assert.That(configureMethod, Is.Not.Null);
                Assert.That(material, Is.Not.Null);

                configureMethod.Invoke(editor, null);

                var variants = material.GetVector("_Variants");
                Assert.That(variants.z, Is.EqualTo(2f));
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void TonemappingEditor_ConfigureKhronosPbrPreview_SetsKhronosVariant()
        {
            var component = ScriptableObject.CreateInstance<Tonemapping>();
            component.mode.value = TonemappingMode.KhronosPBR;
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var configureMethod = editorType.GetMethod("ConfigureKhronosPbrCurvePreview", flags);
                var material = editorType.GetField("m_Material", flags)?.GetValue(editor) as Material;

                Assert.That(configureMethod, Is.Not.Null);
                Assert.That(material, Is.Not.Null);

                configureMethod.Invoke(editor, null);

                var variants = material.GetVector("_Variants");
                Assert.That(variants.z, Is.EqualTo(3f));
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void AutoExposureEditor_InitializesCoreControls()
        {
            var component = ScriptableObject.CreateInstance<AutoExposure>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_Enabled", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Mode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Percent", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MinEV100", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MaxEV100", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ManualEV100", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ApplyPhysicalCameraExposure", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ExposureCompensation", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ExposureCompensationCurve", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ExposureMeteringMask", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HistogramLogRange", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPMeteringMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPAdaptationMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPTargetMidGray", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPCurveMap", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPWeightTextureMask", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPProceduralCenter", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPProceduralRadii", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HDRPProceduralSoftness", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_SelectedPreset", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_StatsPreviewMaterial", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HistogramPreviewSamples", flags)?.GetValue(editor), Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }
}
