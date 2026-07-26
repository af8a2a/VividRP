using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class AmbientOcclusionEditorTests
    {
        [Test]
        public void CreateEditor_UsesCustomAmbientOcclusionEditor()
        {
            var component = ScriptableObject.CreateInstance<AmbientOcclusion>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("AmbientOcclusionEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void OnEnable_InitializesBothImplementationPanels()
        {
            var component = ScriptableObject.CreateInstance<AmbientOcclusion>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);

                var editorType = editor.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;

                Assert.That(
                    editorType.GetField("m_Implementation", flags)?.GetValue(editor),
                    Is.Not.Null);
                Assert.That(
                    editorType.GetField("m_DenoisePasses", flags)?.GetValue(editor),
                    Is.Not.Null);
                Assert.That(
                    editorType
                        .GetField("m_CacaoAdaptiveQualityLimit", flags)
                        ?.GetValue(editor),
                    Is.Not.Null);
                Assert.That(
                    editorType
                        .GetField("m_CacaoBilateralSimilarityDistanceSigma", flags)
                        ?.GetValue(editor),
                    Is.Not.Null);
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void Source_SwitchesGtaoAndCacaoPanels()
        {
            var source = File.ReadAllText(
                GetPackageFilePath(
                    "Editor",
                    "VolumeEditor",
                    "AmbientOcclusionEditor.cs"));

            Assert.That(
                source,
                Does.Contain("[CustomEditor(typeof(AmbientOcclusion))]"));
            Assert.That(
                source,
                Does.Contain("PropertyFetcher<AmbientOcclusion>"));
            Assert.That(
                source,
                Does.Contain("AmbientOcclusionImplementation.FidelityFXCACAO"));
            Assert.That(source, Does.Contain("DrawGtaoPanel();"));
            Assert.That(source, Does.Contain("DrawCacaoPanel();"));
            Assert.That(source, Does.Contain("DrawQualityLevel(maximum: 3);"));
            Assert.That(source, Does.Contain("DrawQualityLevel(maximum: 4);"));
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
