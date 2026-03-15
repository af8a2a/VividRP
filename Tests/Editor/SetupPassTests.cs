using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class LightGridPassTests
    {
        [Test]
        public void Prepare_AllocatesHdrpClusteredBuffers_WhenLightsAreAvailable()
        {
            var pass = new LightGridPass();
            var frameData = new ContextContainer();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var cameraObject = new GameObject("Light Grid Pass Camera");

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
                    positionWS = new Vector3(0.0f, 0.0f, 5.0f),
                    range = 6.0f,
                    color = new Vector3(0.5f, 0.25f, 1.0f),
                    lightType = 0u,
                    directionWS = Vector3.forward,
                    angleOffset = 1.0f,
                    inverseRangeSquared = 1.0f / 36.0f,
                    renderingLayerMask = 7u,
                }
            };
            lightData.punctualLightCullData = new[]
            {
                new VividLightData.PunctualLightCullData
                {
                    positionWS = new Vector3(0.0f, 0.0f, 5.0f),
                    range = 6.0f,
                    directionWS = Vector3.forward,
                    lightType = 0u,
                    cosOuterAngle = 1.0f,
                    radiusAtRange = 0.0f,
                    cullingCenterWS = new Vector3(0.0f, 0.0f, 5.0f),
                    cullingRadius = 6.0f,
                }
            };
            lightData.punctualLightCount = 1;
            cameraData.camera = cameraObject.AddComponent<Camera>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            try
            {
                pass.Prepare(frameData);

                var directionalLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_DirectionalLightBuffer");
                var punctualLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_PunctualLightBuffer");
                var finiteLightBoundBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_FiniteLightBoundBuffer");
                var lightVolumeDataBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LightVolumeDataBuffer");
                var screenSpaceBoundsBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_ScreenSpaceBoundsBuffer");
                var bigTileLightListBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_BigTileLightListBuffer");
                var layeredOffsetBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredOffsetBuffer");
                var layeredLightListBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredLightListBuffer");
                var layeredLightListCounterBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredLightListCounterBuffer");
                var logBaseBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LogBaseBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var punctualLightCount = (int)GetFieldValue(pass, "m_PunctualLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");
                var clusterTileCountX = (int)GetFieldValue(pass, "m_ClusterTileCountX");
                var clusterTileCountY = (int)GetFieldValue(pass, "m_ClusterTileCountY");
                var clusterCount = (int)GetFieldValue(pass, "m_ClusterCount");
                var clusterBigTileCountX = (int)GetFieldValue(pass, "m_ClusterBigTileCountX");
                var clusterBigTileCountY = (int)GetFieldValue(pass, "m_ClusterBigTileCountY");
                var clusterLightIndexCapacity = (int)GetFieldValue(pass, "m_ClusterLightIndexCapacity");
                var clusterBigTileLightIndexCapacity = (int)GetFieldValue(pass, "m_ClusterBigTileLightIndexCapacity");
                var layeredOffsetCapacity = (int)GetFieldValue(pass, "m_LayeredOffsetCapacity");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(punctualLightBuffer, Is.Not.Null);
                Assert.That(finiteLightBoundBuffer, Is.Not.Null);
                Assert.That(lightVolumeDataBuffer, Is.Not.Null);
                Assert.That(screenSpaceBoundsBuffer, Is.Not.Null);
                Assert.That(bigTileLightListBuffer, Is.Not.Null);
                Assert.That(layeredOffsetBuffer, Is.Not.Null);
                Assert.That(layeredLightListBuffer, Is.Not.Null);
                Assert.That(layeredLightListCounterBuffer, Is.Not.Null);
                Assert.That(logBaseBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(directionalLightBuffer.stride, Is.EqualTo(VividLightData.DirectionalLightData.Stride));
                Assert.That(punctualLightBuffer.count, Is.EqualTo(1));
                Assert.That(punctualLightBuffer.stride, Is.EqualTo(VividLightData.PunctualLightData.Stride));
                Assert.That(finiteLightBoundBuffer.count, Is.EqualTo(1));
                Assert.That(finiteLightBoundBuffer.stride, Is.EqualTo(VividLightData.SFiniteLightBound.Stride));
                Assert.That(lightVolumeDataBuffer.count, Is.EqualTo(1));
                Assert.That(lightVolumeDataBuffer.stride, Is.EqualTo(VividLightData.LightVolumeData.Stride));
                Assert.That(screenSpaceBoundsBuffer.count, Is.EqualTo(2));
                Assert.That(screenSpaceBoundsBuffer.stride, Is.EqualTo(sizeof(float) * 4));
                Assert.That(bigTileLightListBuffer.count, Is.EqualTo(3840));
                Assert.That(bigTileLightListBuffer.stride, Is.EqualTo(sizeof(uint)));
                Assert.That(layeredOffsetBuffer.count, Is.EqualTo(15360));
                Assert.That(layeredOffsetBuffer.stride, Is.EqualTo(sizeof(uint)));
                Assert.That(layeredLightListBuffer.count, Is.EqualTo(491520));
                Assert.That(layeredLightListBuffer.stride, Is.EqualTo(sizeof(uint)));
                Assert.That(layeredLightListCounterBuffer.count, Is.EqualTo(1));
                Assert.That(layeredLightListCounterBuffer.stride, Is.EqualTo(sizeof(uint)));
                Assert.That(logBaseBuffer.count, Is.EqualTo(60));
                Assert.That(logBaseBuffer.stride, Is.EqualTo(sizeof(float)));
                Assert.That(directionalLightCount, Is.EqualTo(1));
                Assert.That(punctualLightCount, Is.EqualTo(1));
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(clusterTileCountX, Is.EqualTo(10));
                Assert.That(clusterTileCountY, Is.EqualTo(6));
                Assert.That(clusterCount, Is.EqualTo(3840));
                Assert.That(clusterBigTileCountX, Is.EqualTo(5));
                Assert.That(clusterBigTileCountY, Is.EqualTo(3));
                Assert.That(clusterLightIndexCapacity, Is.EqualTo(491520));
                Assert.That(clusterBigTileLightIndexCapacity, Is.EqualTo(3840));
                Assert.That(layeredOffsetCapacity, Is.EqualTo(15360));
                Assert.That(lightData.punctualLightBounds[0].radius, Is.GreaterThan(0.0f));
                Assert.That(lightData.punctualLightVolumeData[0].radiusSq, Is.EqualTo(36.0f).Within(1e-4f));
                Assert.That(lightData.punctualLightVolumeData[0].lightVolume, Is.EqualTo(1u));
            }
            finally
            {
                pass.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Prepare_AllocatesHdrpClusteredFallbackBuffers_WhenLightsAreMissing()
        {
            var pass = new LightGridPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var cameraObject = new GameObject("Light Grid Pass Camera");
            cameraData.camera = cameraObject.AddComponent<Camera>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            try
            {
                pass.Prepare(frameData);

                var directionalLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_DirectionalLightBuffer");
                var punctualLightBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_PunctualLightBuffer");
                var finiteLightBoundBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_FiniteLightBoundBuffer");
                var lightVolumeDataBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LightVolumeDataBuffer");
                var screenSpaceBoundsBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_ScreenSpaceBoundsBuffer");
                var bigTileLightListBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_BigTileLightListBuffer");
                var layeredOffsetBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredOffsetBuffer");
                var layeredLightListBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredLightListBuffer");
                var layeredLightListCounterBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LayeredLightListCounterBuffer");
                var logBaseBuffer = (GraphicsBuffer)GetFieldValue(pass, "m_LogBaseBuffer");
                var directionalLightCount = (int)GetFieldValue(pass, "m_DirectionalLightCount");
                var punctualLightCount = (int)GetFieldValue(pass, "m_PunctualLightCount");
                var mainDirectionalLightIndex = (int)GetFieldValue(pass, "m_MainDirectionalLightIndex");

                Assert.That(directionalLightBuffer, Is.Not.Null);
                Assert.That(punctualLightBuffer, Is.Not.Null);
                Assert.That(finiteLightBoundBuffer, Is.Not.Null);
                Assert.That(lightVolumeDataBuffer, Is.Not.Null);
                Assert.That(screenSpaceBoundsBuffer, Is.Not.Null);
                Assert.That(bigTileLightListBuffer, Is.Not.Null);
                Assert.That(layeredOffsetBuffer, Is.Not.Null);
                Assert.That(layeredLightListBuffer, Is.Not.Null);
                Assert.That(layeredLightListCounterBuffer, Is.Not.Null);
                Assert.That(logBaseBuffer, Is.Not.Null);
                Assert.That(directionalLightBuffer.count, Is.EqualTo(1));
                Assert.That(punctualLightBuffer.count, Is.EqualTo(1));
                Assert.That(finiteLightBoundBuffer.count, Is.EqualTo(1));
                Assert.That(lightVolumeDataBuffer.count, Is.EqualTo(1));
                Assert.That(screenSpaceBoundsBuffer.count, Is.EqualTo(1));
                Assert.That(bigTileLightListBuffer.count, Is.EqualTo(3840));
                Assert.That(layeredOffsetBuffer.count, Is.EqualTo(15360));
                Assert.That(layeredLightListBuffer.count, Is.EqualTo(491520));
                Assert.That(layeredLightListCounterBuffer.count, Is.EqualTo(1));
                Assert.That(logBaseBuffer.count, Is.EqualTo(60));
                Assert.That(directionalLightCount, Is.Zero);
                Assert.That(punctualLightCount, Is.Zero);
                Assert.That(mainDirectionalLightIndex, Is.EqualTo(-1));
            }
            finally
            {
                pass.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForSetupPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(LightGridPass)), Is.False);
        }

        private static object GetFieldValue(LightGridPass pass, string fieldName)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return field.GetValue(pass);
        }
    }
}
