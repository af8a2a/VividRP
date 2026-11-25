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
    }
}
