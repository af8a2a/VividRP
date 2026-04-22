using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderPassContextTests
    {
        [Test]
        public void ComputePassContext_Get_ReturnsExistingContextItem()
        {
            var frameData = new ContextContainer();
            var expected = frameData.GetOrCreate<VividCameraData>();
            var context = new ComputePassContext(default, frameData);

            Assert.That(context.Get<VividCameraData>(), Is.SameAs(expected));
        }

        [Test]
        public void RasterPassContext_TryGet_ReturnsFalse_WhenContextItemIsMissing()
        {
            var context = new RasterPassContext(default, new ContextContainer());

            var result = context.TryGet<VividCameraData>(out var cameraData);

            Assert.That(result, Is.False);
            Assert.That(cameraData, Is.Null);
        }

        [Test]
        public void UnsafePassContext_GetOrCreate_CreatesContextItem()
        {
            var frameData = new ContextContainer();
            var context = new UnsafePassContext(default, frameData);

            var cameraData = context.GetOrCreate<VividCameraData>();

            Assert.That(cameraData, Is.Not.Null);
            Assert.That(frameData.Contains<VividCameraData>(), Is.True);
            Assert.That(frameData.Get<VividCameraData>(), Is.SameAs(cameraData));
        }
    }
}
