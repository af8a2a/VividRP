using System.Reflection;
using System.Runtime.InteropServices;
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
            lightData.punctualLights = new[]
            {
                new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(1.0f, 2.0f, 3.0f),
                    range = 10.0f,
                    color = new Vector3(0.5f, 0.25f, 1.0f),
                    lightType = 0u,
                    directionWS = Vector3.forward,
                    angleOffset = 1.0f,
                    inverseRangeSquared = 0.01f,
                    renderingLayerMask = 7u,
                }
            };
            lightData.punctualLightCount = 1;
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            try
            {
                pass.Prepare(frameData);

                var directionalLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_DirectionalLightBuffer");
                var punctualLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_PunctualLightBuffer");
                var clusterLightGridBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_ClusterLightGridBuffer");
                var clusterLightIndicesBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_ClusterLightIndicesBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var punctualLightCount = (int)GetFieldValue(pass, "m_PunctualLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");
                var clusterTileCountX = (int)GetFieldValue(pass, "m_ClusterTileCountX");
                var clusterTileCountY = (int)GetFieldValue(pass, "m_ClusterTileCountY");
                var clusterLightIndexCapacity = (int)GetFieldValue(pass, "m_ClusterLightIndexCapacity");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(punctualLightBuffer, Is.Not.Null);
                Assert.That(clusterLightGridBuffer, Is.Not.Null);
                Assert.That(clusterLightIndicesBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightBuffer.stride, Is.EqualTo(VividLightData.DirectionalLightData.Stride));
                Assert.That(punctualLightBuffer.count, Is.EqualTo(1));
                Assert.That(punctualLightBuffer.stride, Is.EqualTo(GetPunctualUploadStride()));
                Assert.That(clusterLightGridBuffer.count, Is.EqualTo(1440));
                Assert.That(clusterLightGridBuffer.stride, Is.EqualTo(sizeof(uint) * 2));
                Assert.That(clusterLightIndicesBuffer.count, Is.EqualTo(1440));
                Assert.That(clusterLightIndicesBuffer.stride, Is.EqualTo(sizeof(uint)));
                Assert.That(directionalLightCount, Is.EqualTo(1));
                Assert.That(punctualLightCount, Is.EqualTo(1));
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(clusterTileCountX, Is.EqualTo(10));
                Assert.That(clusterTileCountY, Is.EqualTo(6));
                Assert.That(clusterLightIndexCapacity, Is.EqualTo(1440));
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
                var punctualLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_PunctualLightBuffer");
                var clusterLightIndicesBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_ClusterLightIndicesBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var punctualLightCount = (int)GetFieldValue(pass, "m_PunctualLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(punctualLightBuffer, Is.Not.Null);
                Assert.That(clusterLightIndicesBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightBuffer.stride, Is.EqualTo(VividLightData.DirectionalLightData.Stride));
                Assert.That(punctualLightBuffer.count, Is.EqualTo(1));
                Assert.That(punctualLightBuffer.stride, Is.EqualTo(GetPunctualUploadStride()));
                Assert.That(clusterLightIndicesBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightCount, Is.Zero);
                Assert.That(punctualLightCount, Is.Zero);
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(-1));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForSetupPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(SetupPass)), Is.False);
        }

        private static object GetFieldValue(SetupPass pass, string fieldName)
        {
            var field = typeof(SetupPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return field.GetValue(pass);
        }

        private static int GetPunctualUploadStride()
        {
            var uploadType = typeof(SetupPass).GetNestedType("PunctualLightUploadData", BindingFlags.NonPublic);

            Assert.That(uploadType, Is.Not.Null);

            return Marshal.SizeOf(uploadType);
        }
    }
}
