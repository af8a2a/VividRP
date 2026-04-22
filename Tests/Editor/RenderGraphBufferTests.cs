using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphBufferTests
    {
        [Test]
        public void CreateStructured_UsesSingleElementStructuredDescriptor_WhenStrideOnlyIsProvided()
        {
            var buffer = RenderGraphBuffer.CreateStructured("TestBuffer", sizeof(uint));

            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.desc, Is.Not.Null);
            Assert.That(buffer.desc.Name, Is.EqualTo("TestBuffer"));
            Assert.That(buffer.desc.Count, Is.EqualTo(1));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        [Test]
        public void CreateStructured_PreservesCountStrideAndTarget_WhenExplicitValuesAreProvided()
        {
            var buffer = RenderGraphBuffer.CreateStructured(
                "IndirectArgs",
                4,
                sizeof(uint),
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments);

            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.desc, Is.Not.Null);
            Assert.That(buffer.desc.Name, Is.EqualTo("IndirectArgs"));
            Assert.That(buffer.desc.Count, Is.EqualTo(4));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(
                buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments));
        }
    }
}
