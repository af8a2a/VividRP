using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Tests
{
    public class VividRenderPipelineConstantBufferTests
    {
        [SetUp]
        public void SetUp()
        {
            VividRenderPipeline.ReleaseConstantBuffersForShutdown();
        }

        [TearDown]
        public void TearDown()
        {
            VividRenderPipeline.ReleaseConstantBuffersForShutdown();
        }

        [Test]
        public void ReleaseConstantBuffersForShutdown_ReleasesAndRecreatesSingleton_WhenLightListConstantBufferWasAllocated()
        {
            Assert.That(GetRegisteredConstantBufferCount(), Is.Zero);

            ConstantBuffer.UpdateData(default(ShaderVariablesLightList));

            Assert.That(GetRegisteredConstantBufferCount(), Is.EqualTo(1));

            VividRenderPipeline.ReleaseConstantBuffersForShutdown();

            Assert.That(GetRegisteredConstantBufferCount(), Is.Zero);

            ConstantBuffer.UpdateData(default(ShaderVariablesLightList));

            Assert.That(GetRegisteredConstantBufferCount(), Is.EqualTo(1));
        }

        private static int GetRegisteredConstantBufferCount()
        {
            var field = typeof(ConstantBuffer).GetField(
                "m_RegisteredConstantBuffers",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var registeredBuffers = field.GetValue(null) as ICollection;
            Assert.That(registeredBuffers, Is.Not.Null);
            return registeredBuffers.Count;
        }
    }
}
