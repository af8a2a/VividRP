using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class TexturePreviewValueDrawerTests
    {
        private sealed class TexturePreviewValueHost : ScriptableObject
        {
            [SerializeField]
            private TexturePreviewValue m_Preview = new TexturePreviewValue();
        }

        [Test]
        public void CreatePropertyGUI_ShowsRemovedMessage()
        {
            var host = ScriptableObject.CreateInstance<TexturePreviewValueHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Preview");
                var drawer = new TexturePreviewValueDrawer();

                var root = drawer.CreatePropertyGUI(property) as UnityEngine.UIElements.HelpBox;

                Assert.That(root, Is.Not.Null);
                Assert.That(root.text, Is.EqualTo(TexturePreviewValueDrawer.RemovedMessage));
                Assert.That(root.name, Is.EqualTo("vivid-preview-removed-help"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
