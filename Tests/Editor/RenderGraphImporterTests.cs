using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class RenderGraphImporterTests
    {
        [Test]
        public void ShouldImportPassFieldBinding_ReturnsTrue_WhenInputIsConnectedWithoutStandaloneResource()
        {
            var shouldImport = RenderGraphImporter.ShouldImportPassFieldBinding(
                hasInputConnection: true,
                hasBoundResourceNode: false);

            Assert.That(shouldImport, Is.True);
        }

        [Test]
        public void ShouldImportPassFieldBinding_ReturnsFalse_WhenNoInputConnectionExists()
        {
            var shouldImport = RenderGraphImporter.ShouldImportPassFieldBinding(
                hasInputConnection: false,
                hasBoundResourceNode: false);

            Assert.That(shouldImport, Is.False);
        }

        [Test]
        public void ShouldImportPassFieldBinding_ReturnsFalse_WhenStandaloneResourceIsAlreadyBound()
        {
            var shouldImport = RenderGraphImporter.ShouldImportPassFieldBinding(
                hasInputConnection: true,
                hasBoundResourceNode: true);

            Assert.That(shouldImport, Is.False);
        }
    }
}
