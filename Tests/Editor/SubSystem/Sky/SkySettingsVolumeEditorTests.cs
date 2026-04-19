using System.IO;
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

        [Test]
        public void Source_CombinesHdrpStyleSkyAndPlanetControls()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "SkySettingsVolumeEditor.cs"));

            Assert.That(source, Does.Contain("[CustomEditor(typeof(SkySettingsVolume))]"));
            Assert.That(source, Does.Contain("PropertyFetcher<SkySettingsVolume>"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Sky Type\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Update Mode\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Include Sun In Baking\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Rendering Space\""));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Center\""));
            Assert.That(source, Does.Contain("UpdateSkyTypePopupData();"));
            Assert.That(source, Does.Contain("Enum.GetValues(typeof(SkyType))"));
            Assert.That(source, Does.Contain("SkyType.HDRI => \"HDRI Sky\""));
            Assert.That(source, Does.Contain("SkyType.PhysicallyBased => \"Physically Based Sky\""));
            Assert.That(source, Does.Contain("EditorGUI.IntPopup(rect, s_SkyTypeLabel"));
            Assert.That(source, Does.Not.Contain("DrawIntensitySettings();"));
            Assert.That(source, Does.Not.Contain("EditorGUIUtility.TrTextContent(\"Intensity Mode\""));
            Assert.That(source, Does.Not.Contain("EditorGUIUtility.TrTextContent(\"Exposure Compensation\""));
            Assert.That(source, Does.Contain("PropertyField(m_UpdateMode, s_UpdateModeLabel);"));
            Assert.That(source, Does.Contain("m_UpdateMode.value.intValue == (int)SkyUpdateMode.Realtime"));
            Assert.That(source, Does.Contain("PropertyField(m_IncludeSunInBaking, s_IncludeSunInBakingLabel);"));
            Assert.That(source, Does.Contain("PropertyField(m_RenderingSpace, s_RenderingSpaceLabel);"));
            Assert.That(source, Does.Contain("BeginAdditionalPropertiesScope()"));
            Assert.That(source, Does.Contain("PropertyField(m_CenterMode, s_CenterModeLabel);"));
            Assert.That(source, Does.Contain("PropertyField(m_PlanetCenter, s_PlanetCenterLabel);"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Planet\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Vivid Extensions\")"));
            Assert.That(source, Does.Contain("PropertyField(m_GeneratedCubemapQuality, s_GeneratedCubemapQualityLabel);"));
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
