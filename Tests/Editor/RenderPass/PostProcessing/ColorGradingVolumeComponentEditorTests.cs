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
                var lpmParams0 = material.GetVector("_LPM_Params0");
                var lpmParams7 = material.GetVector("_LPM_Params7");
                var lpmFlags = material.GetVector("_LPM_Flags");
                Assert.That(variants.z, Is.EqualTo(7f));
                Assert.That(lpmParams0.w, Is.EqualTo(1.25f).Within(1e-5f));
                Assert.That(lpmFlags.x, Is.EqualTo(1f));
                Assert.That(lpmFlags.y, Is.EqualTo(1f));
                Assert.That(lpmFlags.z, Is.EqualTo(1f));
                Assert.That(lpmParams7.x, Is.EqualTo(0.05f).Within(1e-5f));
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
                Assert.That(editorType.GetField("m_MeteringMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_AdaptationMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_TargetMidGray", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ApplyPhysicalCameraExposure", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ExposureCompensation", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ExposureCompensationCurve", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CurveMap", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_HistogramLogRange", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MeterMask", flags)?.GetValue(editor), Is.Not.Null);
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

        [Test]
        public void AutoExposureEditor_UsesHdrpStyleExposureLabelsAndSections()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "AutoExposureEditor.cs"));

            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Mode\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Preset\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Apply Preset\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Fixed Exposure\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Limit Min\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Limit Max\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Compensation\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Compensation Curve\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Curve Map\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Metering Mode\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Adaptation Mode\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Target Mid Gray\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Weight Texture Mask\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Low Percent\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"High Percent\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Speed Dark to Light\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Speed Light to Dark\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Histogram Percentages\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Histogram EV100 Range\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Presets\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Metering\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Automatic\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Automatic Histogram\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Curve Mapping\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Fixed\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Physical Camera\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Adaptation\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Histogram\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Monitor\")"));
            Assert.That(source, Does.Contain("Curve Mapping now uses HDRP-style curve remapping at runtime."));
            Assert.That(source, Does.Contain("Automatic Histogram now runs through a dedicated HDRP histogram path."));
            Assert.That(source, Does.Contain("AutoExposureCommonPresets.Get(m_SelectedPreset)"));
            Assert.That(source, Does.Contain("ApplySelectedPreset()"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposurePresets.cs")),
                Does.Contain("public const float VolumeSafeMinEV100 = -5f;"));
            Assert.That(source, Does.Contain("DrawStatsPreview();"));
            Assert.That(source, Does.Contain("AutoExposureStatsReadbackBridge.TouchInspectorRequest();"));
            Assert.That(source, Does.Contain("BuildLiveStatsPreviewData(snapshot)"));
            Assert.That(source, Does.Contain("SetFloatArray(HistogramSamplesId, m_HistogramPreviewSamples);"));
            Assert.That(source, Does.Contain("Live GPU ("));
            Assert.That(source, Does.Contain("\"Pre Buffer.x\""));
            Assert.That(source, Does.Contain("resolvedPreExposure"));
            Assert.That(source, Does.Contain("Inspector Preview"));
            Assert.That(source, Does.Contain("Shader.Find(\"Hidden/VividRP/Editor/Auto Exposure Stats\")"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
