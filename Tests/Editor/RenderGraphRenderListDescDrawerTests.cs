using System.Reflection;
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

        [Test]
        public void BuildSectionStateKey_UsesTargetObjectEntityId_WhenSerializedObjectExists()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphRenderListDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var sectionsField = typeof(RenderGraphRenderListDescDrawer).GetField("s_Sections", BindingFlags.Static | BindingFlags.NonPublic);
                var buildSectionStateKeyMethod = typeof(RenderGraphRenderListDescDrawer).GetMethod("BuildSectionStateKey", BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(property, Is.Not.Null);
                Assert.That(sectionsField, Is.Not.Null);
                Assert.That(buildSectionStateKeyMethod, Is.Not.Null);

                var sections = sectionsField.GetValue(null) as System.Array;
                Assert.That(sections, Is.Not.Null);
                Assert.That(sections.Length, Is.GreaterThan(0));

                var section = sections.GetValue(0);
                var title = (string)section.GetType().GetProperty("Title", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(section);
                var key = (string)buildSectionStateKeyMethod.Invoke(null, new object[] { property, section });

                Assert.That(key, Is.EqualTo($"{host.GetEntityId()}:{property.propertyPath}:{title}"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
