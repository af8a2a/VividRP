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
    public class RenderGraphBufferDescDrawerTests
    {
        private sealed class RenderGraphBufferDescHost : ScriptableObject
        {
            [SerializeField]
            private RenderGraphBufferDesc m_Descriptor = new RenderGraphBufferDesc();
        }

        [Test]
        public void CreatePropertyGUI_BuildsEditableBufferDescriptorFoldoutSections()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphBufferDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var drawer = new RenderGraphBufferDescDrawer();

                var root = drawer.CreatePropertyGUI(property);
                var layoutFoldout = root.Q<Foldout>("vivid-buffer-desc-section-layout");
                var usageFoldout = root.Q<Foldout>("vivid-buffer-desc-section-usage");
                var metadataFoldout = root.Q<Foldout>("vivid-buffer-desc-section-metadata");

                Assert.That(root, Is.TypeOf<Foldout>());
                Assert.That(layoutFoldout, Is.Not.Null);
                Assert.That(usageFoldout, Is.Not.Null);
                Assert.That(metadataFoldout, Is.Not.Null);
                Assert.That(layoutFoldout.Q<PropertyField>("vivid-buffer-desc-field-Count"), Is.Not.Null);
                Assert.That(layoutFoldout.Q<PropertyField>("vivid-buffer-desc-field-Stride"), Is.Not.Null);
                Assert.That(usageFoldout.Q<PropertyField>("vivid-buffer-desc-field-Target"), Is.Not.Null);
                Assert.That(metadataFoldout.Q<PropertyField>("vivid-buffer-desc-field-Name"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BuildSectionStateKey_UsesTargetObjectEntityId_WhenSerializedObjectExists()
        {
            var host = ScriptableObject.CreateInstance<RenderGraphBufferDescHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Descriptor");
                var sectionsField = typeof(RenderGraphBufferDescDrawer).GetField("s_Sections", BindingFlags.Static | BindingFlags.NonPublic);
                var buildSectionStateKeyMethod = typeof(RenderGraphBufferDescDrawer).GetMethod("BuildSectionStateKey", BindingFlags.Static | BindingFlags.NonPublic);

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
