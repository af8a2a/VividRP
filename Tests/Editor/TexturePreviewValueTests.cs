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

        [Test]
        public void TryGetConnectedPassOutput_ReturnsStoredConnectionMetadata()
        {
            var previewValue = new TexturePreviewValue();

            previewValue.SetConnectedPassOutput(typeof(TexturePreviewValueTests), "Color");

            var found = previewValue.TryGetConnectedPassOutput(out var passType, out var fieldName);

            Assert.That(found, Is.True);
            Assert.That(passType, Is.EqualTo(typeof(TexturePreviewValueTests)));
            Assert.That(fieldName, Is.EqualTo("Color"));
            Assert.That(previewValue.HasConnectedTextureInput, Is.True);
        }

        [Test]
        public void ClearConnectionMetadata_RemovesStoredConnectionMetadata()
        {
            var previewValue = new TexturePreviewValue();
            previewValue.SetConnectedPassOutput(typeof(TexturePreviewValueTests), "Color");

            previewValue.ClearConnectionMetadata();

            Assert.That(previewValue.TryGetConnectedPassOutput(out _, out _), Is.False);
            Assert.That(previewValue.HasConnectedTextureInput, Is.False);
        }
    }
}
