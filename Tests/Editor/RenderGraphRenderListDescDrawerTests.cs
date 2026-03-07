using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphRenderListDescDrawerTests
    {
        private sealed class RenderGraphRenderListDescHost : ScriptableObject
        {
            [SerializeField]
            private RenderGraphRenderListDesc m_Descriptor = new RenderGraphRenderListDesc();
        }

        [Test]
        public void CreatePropertyGUI_BuildsEditableRenderListDescriptorFoldoutSections()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphRenderListDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var drawer = new RenderGraphRenderListDescDrawer();

                var root = drawer.CreatePropertyGUI(property);
                var shaderTagsFoldout = root.Q<Foldout>("vivid-renderlist-desc-section-shadertags");
                var filteringFoldout = root.Q<Foldout>("vivid-renderlist-desc-section-filtering");
                var sortingFoldout = root.Q<Foldout>("vivid-renderlist-desc-section-sorting");
                var overridesFoldout = root.Q<Foldout>("vivid-renderlist-desc-section-overrides");

                Assert.That(root, Is.TypeOf<Foldout>());
                Assert.That(shaderTagsFoldout, Is.Not.Null);
                Assert.That(filteringFoldout, Is.Not.Null);
                Assert.That(sortingFoldout, Is.Not.Null);
                Assert.That(overridesFoldout, Is.Not.Null);
                Assert.That(shaderTagsFoldout.Q<PropertyField>("vivid-renderlist-desc-field-ShaderTagNames"), Is.Not.Null);
                Assert.That(filteringFoldout.Q<PropertyField>("vivid-renderlist-desc-field-RenderQueueRange"), Is.Not.Null);
                Assert.That(filteringFoldout.Q<PropertyField>("vivid-renderlist-desc-field-LayerMask"), Is.Not.Null);
                Assert.That(sortingFoldout.Q<PropertyField>("vivid-renderlist-desc-field-SortingCriteria"), Is.Not.Null);
                Assert.That(overridesFoldout.Q<PropertyField>("vivid-renderlist-desc-field-OverrideMaterial"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
