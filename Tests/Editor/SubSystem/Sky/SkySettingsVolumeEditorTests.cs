using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkySettingsVolumeEditorTests
    {
        [Test]
        public void CreateEditor_UsesCustomSkySettingsVolumeEditor()
        {
            var component = ScriptableObject.CreateInstance<SkySettingsVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("SkySettingsVolumeEditor"));
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
            var component = ScriptableObject.CreateInstance<SkySettingsVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_SkyType", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_UpdateMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_UpdatePeriod", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_IncludeSunInBaking", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_GeneratedCubemapQuality", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_RenderingSpace", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CenterMode", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_PlanetCenter", flags)?.GetValue(editor), Is.Not.Null);
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
