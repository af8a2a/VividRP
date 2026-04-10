using System.IO;
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

        [Test]
        public void Source_UsesHdrpStyleSectionsAndModelOrdering()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "PhysicallyBasedSkyVolumeEditor.cs"));

            Assert.That(source, Does.Contain("[CustomEditor(typeof(PhysicallyBasedSkyVolume))]"));
            Assert.That(source, Does.Contain("PropertyFetcher<PhysicallyBasedSkyVolume>"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Earth (Simple)\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Earth (Advanced)\")"));
            Assert.That(source, Does.Contain("EditorGUIUtility.TrTextContent(\"Custom Planet\")"));
            Assert.That(source, Does.Contain("(int)PhysicallyBasedSkyModel.EarthSimple"));
            Assert.That(source, Does.Contain("(int)PhysicallyBasedSkyModel.EarthAdvanced"));
            Assert.That(source, Does.Contain("(int)PhysicallyBasedSkyModel.Custom"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Model\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Planet and Space\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Planet\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Space\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Air\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Aerosols\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Ozone\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Artistic Overrides\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Vivid Extensions\")"));
            Assert.That(source, Does.Contain("DrawSectionHeader(\"Height Fog\")"));
            Assert.That(source, Does.Contain("DrawModelTypeField();"));
            Assert.That(source, Does.Contain("EditorGUI.IntPopup"));
            Assert.That(source, Does.Contain("using (new EditorGUI.IndentLevelScope())"));
            Assert.That(source, Does.Contain("if (!isSimpleEarth && !hasCustomMaterial)"));
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
