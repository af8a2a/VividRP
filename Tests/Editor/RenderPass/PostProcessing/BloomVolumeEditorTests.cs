using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class BloomVolumeEditorTests
    {
        [Test]
        public void CreateEditor_UsesCustomBloomEditor()
        {
            var component = ScriptableObject.CreateInstance<Bloom>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("BloomEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void OnEnable_InitializesCommonAndPathSpecificParameters()
        {
            var component = ScriptableObject.CreateInstance<Bloom>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                string[] fieldNames =
                {
                    "m_Mode",
                    "m_Threshold",
                    "m_Intensity",
                    "m_Scatter",
                    "m_Tint",
                    "m_DirtTexture",
                    "m_DirtIntensity",
                    "m_Anamorphic",
                    "m_Resolution",
                    "m_HighQualityPrefiltering",
                    "m_HighQualityFiltering",
                    "m_ExperimentalSpdDownsample",
                    "m_ConvolutionKernel",
                    "m_ConvolutionSize",
                    "m_ConvolutionBufferScale",
                    "m_ConvolutionCenter",
                    "m_ConvolutionKernelClamp",
                    "m_ConvolutionResolutionScale"
                };

                foreach (string fieldName in fieldNames)
                {
                    Assert.That(
                        editorType.GetField(fieldName, flags)?.GetValue(editor),
                        Is.Not.Null,
                        fieldName);
                }
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [TestCase(BloomMode.Scattering, true, false)]
        [TestCase(BloomMode.ConvolutionFFT, false, true)]
        public void Mode_ExposesOnlyItsPathSpecificSettings(
            BloomMode mode,
            bool expectedScattering,
            bool expectedConvolution)
        {
            Assert.That(
                BloomEditor.UsesScatteringSettings(mode),
                Is.EqualTo(expectedScattering));
            Assert.That(
                BloomEditor.UsesConvolutionSettings(mode),
                Is.EqualTo(expectedConvolution));
        }
    }
}
