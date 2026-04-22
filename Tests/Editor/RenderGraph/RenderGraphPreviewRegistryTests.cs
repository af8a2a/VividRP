using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPreviewRegistryTests
    {
        [Test]
        public void IsAvailable_ReturnsFalse_AfterPreviewRemoval()
        {
            Assert.That(RenderGraphPreviewRegistry.IsAvailable, Is.False);
        }

        [Test]
        public void TryGetPreview_ReturnsFalse_WhenLegacyCallersAttemptToRegisterPreview()
        {
            RenderGraphPreviewRegistry.SetPreview(typeof(RenderGraphPreviewRegistryTests), "Color", null);

            var found = RenderGraphPreviewRegistry.TryGetPreview(typeof(RenderGraphPreviewRegistryTests), "Color", out var previewTexture);

            Assert.That(found, Is.False);
            Assert.That(previewTexture, Is.Null);
        }

        [Test]
        public void TryGetSinglePreview_ReturnsFalse_AfterPreviewRemoval()
        {
            var found = RenderGraphPreviewRegistry.TryGetSinglePreview(out _, out _, out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void GetOrCreatePreviewTarget_ReturnsNull_AfterPreviewRemoval()
        {
            var target = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(
                typeof(RenderGraphPreviewRegistryTests),
                "Color",
                default,
                null);

            Assert.That(target, Is.Null);
        }
    }
}
