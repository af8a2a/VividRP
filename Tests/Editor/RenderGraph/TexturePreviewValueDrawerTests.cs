using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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
        public void CreatePropertyGUI_ConstrainsPreviewLayoutWidth()
        {
            var host = ScriptableObject.CreateInstance<TexturePreviewValueHost>();

            try
            {
                var serializedObject = new SerializedObject(host);
                var property = serializedObject.FindProperty("m_Preview");
                var drawer = new TexturePreviewValueDrawer();

                var root = drawer.CreatePropertyGUI(property);
                var previewContainer = root.Q<VisualElement>("vivid-texture-preview-container");
                var previewImage = root.Q<Image>("vivid-texture-preview-image");

                Assert.That(root.style.width.value.value, Is.EqualTo(TexturePreviewValueDrawer.PreviewElementWidth));
                Assert.That(root.style.maxWidth.value.value, Is.EqualTo(TexturePreviewValueDrawer.PreviewElementWidth));
                Assert.That(previewContainer, Is.Not.Null);
                Assert.That(previewContainer.style.width.value.value, Is.EqualTo(TexturePreviewValueDrawer.PreviewElementWidth));
                Assert.That(previewImage, Is.Not.Null);
                Assert.That(previewImage.style.height.value.value, Is.EqualTo(120f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
