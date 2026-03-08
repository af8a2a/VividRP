using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class PassRecorderPreviewTests
    {
        [SetUp]
        public void SetUp()
        {
            RenderGraphPreviewRegistry.SetAvailabilityOverrideForTests(null);
        }

        [TearDown]
        public void TearDown()
        {
            RenderGraphPreviewRegistry.SetAvailabilityOverrideForTests(null);
        }

        [Test]
        public void ShouldRecordTexturePreview_ReturnsTrue_WhenFieldIsReferencedByPreview()
        {
            var passDefinition = new RenderGraphPassDefinition
            {
                PreviewTextureFields = { "m_ColorTarget" }
            };

            var entry = CreateTextureEntry("m_ColorTarget");

            var shouldRecord = PassRecorder.ShouldRecordTexturePreview(passDefinition, entry);

            Assert.That(shouldRecord, Is.True);
        }

        [Test]
        public void ShouldRecordTexturePreview_ReturnsFalse_WhenFieldIsNotReferencedByPreview()
        {
            var passDefinition = new RenderGraphPassDefinition
            {
                PreviewTextureFields = { "m_DepthTarget" }
            };

            var entry = CreateTextureEntry("m_ColorTarget");

            var shouldRecord = PassRecorder.ShouldRecordTexturePreview(passDefinition, entry);

            Assert.That(shouldRecord, Is.False);
        }

        [Test]
        public void ShouldRecordTexturePreview_ReturnsFalse_WhenPreviewRuntimeIsDisabled()
        {
            var passDefinition = new RenderGraphPassDefinition
            {
                PreviewTextureFields = { "m_ColorTarget" }
            };

            var entry = CreateTextureEntry("m_ColorTarget");
            RenderGraphPreviewRegistry.SetAvailabilityOverrideForTests(false);

            var shouldRecord = PassRecorder.ShouldRecordTexturePreview(passDefinition, entry);

            Assert.That(shouldRecord, Is.False);
        }

        private static PassResourceEntry CreateTextureEntry(string fieldName)
        {
            var field = typeof(DrawObjectPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            return new PassResourceEntry
            {
                Field = field,
                Name = fieldName,
                Access = AccessFlags.Write,
                Descriptor = new RenderGraphTexture(),
            };
        }
    }
}
