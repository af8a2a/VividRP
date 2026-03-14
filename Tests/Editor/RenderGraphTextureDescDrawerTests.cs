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
    public class RenderGraphTextureDescDrawerTests
    {
        private sealed class RenderGraphTextureDescHost : ScriptableObject
        {
            [SerializeField]
            private RenderGraphTextureDesc m_Descriptor = new RenderGraphTextureDesc();
        }

        [Test]
        public void CreatePropertyGUI_BuildsEditableTextureDescriptorFoldoutSections()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphTextureDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var drawer = new RenderGraphTextureDescDrawer();

                var root = drawer.CreatePropertyGUI(property);
                var dimensionsFoldout = root.Q<Foldout>("vivid-texture-desc-section-dimensions");
                var formatFoldout = root.Q<Foldout>("vivid-texture-desc-section-format");

                Assert.That(root, Is.TypeOf<Foldout>());
                Assert.That(dimensionsFoldout, Is.Not.Null);
                Assert.That(formatFoldout, Is.Not.Null);
                Assert.That(dimensionsFoldout.Q<PropertyField>("vivid-texture-desc-field-Width"), Is.Not.Null);
                Assert.That(dimensionsFoldout.Q<PropertyField>("vivid-texture-desc-field-Height"), Is.Not.Null);
                Assert.That(formatFoldout.Q<PropertyField>("vivid-texture-desc-field-ColorFormat"), Is.Not.Null);
                Assert.That(root.Q<Foldout>("vivid-texture-desc-section-flags"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-texture-desc-field-ScaleFactor"), Is.Not.Null);
                Assert.That(root.Q<PropertyField>("vivid-texture-desc-field-Name"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BuildSectionStateKey_UsesTargetObjectEntityId_WhenSerializedObjectExists()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphTextureDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var sectionsField = typeof(RenderGraphTextureDescDrawer).GetField("s_Sections", BindingFlags.Static | BindingFlags.NonPublic);
                var buildSectionStateKeyMethod = typeof(RenderGraphTextureDescDrawer).GetMethod("BuildSectionStateKey", BindingFlags.Static | BindingFlags.NonPublic);

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
