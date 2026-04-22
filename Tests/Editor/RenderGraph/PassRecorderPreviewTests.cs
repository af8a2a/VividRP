using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphResourceCollectionNameTests
    {
        [Test]
        public void GetRenderGraphResourceCollectionName_UsesAttachmentIndex_WhenProvided()
        {
            var resourceName = RenderGraphPassReflectionUtility.GetRenderGraphResourceCollectionName("Color", 3, 0);

            Assert.That(resourceName, Is.EqualTo("Color3"));
        }

        [Test]
        public void GetRenderGraphResourceCollectionName_UsesCollectionIndex_WhenAttachmentIndexIsMissing()
        {
            var resourceName = RenderGraphPassReflectionUtility.GetRenderGraphResourceCollectionName("Color", -1, 2);

            Assert.That(resourceName, Is.EqualTo("Color2"));
        }
    }
}
