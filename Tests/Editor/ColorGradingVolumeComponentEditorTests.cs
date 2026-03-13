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
                Assert.That(editorType.GetField("m_Material", flags)?.GetValue(editor), Is.Not.Null);
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
