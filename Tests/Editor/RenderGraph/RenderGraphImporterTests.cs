using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

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

        [Test]
        public void ResolveAsyncComputeSetting_ReturnsTrue_ForSupportedPassWhenEnabled()
        {
            var enableAsyncCompute = RenderGraphImporter.ResolveAsyncComputeSetting(typeof(MaterialClassificationPass), true);

            Assert.That(enableAsyncCompute, Is.True);
        }

        [Test]
        public void ResolveAsyncComputeSetting_ReturnsFalse_ForUnsupportedPassWhenEnabled()
        {
            var enableAsyncCompute = RenderGraphImporter.ResolveAsyncComputeSetting(typeof(FinalBlitPass), true);

            Assert.That(enableAsyncCompute, Is.False);
        }

        [Test]
        public void IsAsyncComputeConfigurationValid_ReturnsTrue_WhenDisabledForUnsupportedPass()
        {
            var isValid = RenderGraphEditorValidator.IsAsyncComputeConfigurationValid(typeof(FinalBlitPass), false);

            Assert.That(isValid, Is.True);
        }

        [Test]
        public void IsAsyncComputeConfigurationValid_ReturnsFalse_WhenEnabledForUnsupportedPass()
        {
            var isValid = RenderGraphEditorValidator.IsAsyncComputeConfigurationValid(typeof(FinalBlitPass), true);

            Assert.That(isValid, Is.False);
        }
    }
}
