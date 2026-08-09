using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class CascadedShadowSettingsVolumeEditorTests
    {
        [Test]
        public void CreateEditor_UsesCustomCascadedShadowSettingsVolumeEditor()
        {
            var component = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("CascadedShadowSettingsVolumeEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void OnEnable_InitializesCascadedShadowSerializedParameters()
        {
            var component = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(editorType.GetField("m_EnableCSM", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeCount", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_MaxShadowDistance", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeSplit1", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeSplit2", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeSplit3", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeBorder1", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeBorder2", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeBorder3", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_CascadeBorder4", flags)?.GetValue(editor), Is.Not.Null);
                Assert.That(editorType.GetField("m_ScreenSpaceShadowDenoise", flags)?.GetValue(editor), Is.Not.Null);
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
