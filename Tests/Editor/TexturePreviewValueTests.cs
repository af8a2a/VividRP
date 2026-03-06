using NUnit.Framework;
using UnityEngine;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class TexturePreviewValueTests
    {
        [Test]
        public void HasTexture_ReturnsFalse_WhenTextureIsNotAssigned()
        {
            var previewValue = new TexturePreviewValue();

            Assert.That(previewValue.HasTexture, Is.False);
            Assert.That(previewValue.Texture, Is.Null);
        }

        [Test]
        public void HasTexture_ReturnsTrue_WhenTextureIsAssigned()
        {
            var previewValue = new TexturePreviewValue();
            var texture = new Texture2D(4, 4);

            try
            {
                previewValue.Texture = texture;

                Assert.That(previewValue.HasTexture, Is.True);
                Assert.That(previewValue.Texture, Is.SameAs(texture));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
