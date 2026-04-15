using System.IO;
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
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void Source_UsesHdrpStyleWorkingUnitAndCascadePreview()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "CascadedShadowSettingsVolumeEditor.cs"));

            Assert.That(source, Does.Contain("[CustomEditor(typeof(CascadedShadowSettingsVolume))]"));
            Assert.That(source, Does.Contain("PropertyFetcher<CascadedShadowSettingsVolume>"));
            Assert.That(source, Does.Contain("EditorPrefBoolFlags<WorkingUnit>"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Working Unit\""));
            Assert.That(source, Does.Contain("DrawCascadeSplitField(splitParameters, i, activeSplitCount);"));
            Assert.That(source, Does.Contain("ShadowCascadeGUI.DrawCascades(ref cascades, useMetric, baseMetric);"));
            Assert.That(source, Does.Contain("using (var scope = new OverridablePropertyScope(parameter, title, this))"));
            Assert.That(source, Does.Contain("GUILayout.Label(\"Cascade splits\""));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Directional Light\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Per Light\")"));
            Assert.That(source, Does.Contain("Atlas Resolution, Depth Bias, Normal Bias, and Slope-Scale Depth Bias are configured on the shadow-casting directional light."));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
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
