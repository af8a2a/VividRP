using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyVolumeEditorTests
    {
        [Test]
        public void CreateEditor_UsesCustomPhysicallyBasedSkyVolumeEditor()
        {
            var component = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("PhysicallyBasedSkyVolumeEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void OnEnable_InitializesHdrpStyleSerializedParameters()
        {
            var component = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_Type", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_AtmosphericScattering", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_RenderingMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Material", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_PlanetRadius", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_GroundTint", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_AirMaximumAltitude", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_AerosolDensity", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_OzoneDensityDimmer", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_Exposure", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ColorSaturation", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_RenderSunDisk", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_EnableHeightFog", flags)?.GetValue(editor), Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }
    }
}
