using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SetupPassTests
    {
        [Test]
        public void Prepare_AllocatesDirectionalLightBuffer_WhenDirectionalLightsAreAvailable()
        {
            var pass = new SetupPass();
            var frameData = new ContextContainer();
            var lightData = frameData.GetOrCreate<VividLightData>();

            lightData.directionalLights = new[]
            {
                new VividLightData.DirectionalLightData
                {
                    directionWS = Vector3.down,
                    shadowStrength = 0.75f,
                    color = new Vector3(1.0f, 0.5f, 0.25f),
                    renderingLayerMask = 3u,
                }
            };
            lightData.directionalLightCount = 1;
            lightData.mainDirectionalLightIndex = 0;

            try
            {
                pass.Prepare(frameData);

                var directionalLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_DirectionalLightBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightBuffer.stride, Is.EqualTo(VividLightData.DirectionalLightData.Stride));
                Assert.That(directionalLightCount, Is.EqualTo(1));
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(0));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Prepare_UsesSingleElementFallbackBuffer_WhenDirectionalLightsAreMissing()
        {
            var pass = new SetupPass();
            var frameData = new ContextContainer();

            try
            {
                pass.Prepare(frameData);

                var directionalLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_DirectionalLightBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightBuffer.stride, Is.EqualTo(VividLightData.DirectionalLightData.Stride));
                Assert.That(directionalLightCount, Is.Zero);
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(-1));
            }
            finally
            {
                pass.Dispose();
            }
        }

        private static object GetFieldValue(SetupPass pass, string fieldName)
        {
            var field = typeof(SetupPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return field.GetValue(pass);
        }
    }
}
