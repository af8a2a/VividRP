using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class LightGridPassTests
    {
        [Test]
        public void Initialize_RegistersDepthAndClusteredLightingBuffers_WhenPassIsCreated()
        {
            IRenderPass renderPass = new LightGridPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Depth" }));
            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "BigTileLightList",
                "DirectionalLights",
                "FiniteLightBounds",
                "LayeredLightList",
                "LayeredLightListCounter",
                "LayeredOffset",
                "LightVolumeData",
                "LogBaseBuffer",
                "PunctualLights",
                "ScreenSpaceBounds"
            }));
        }

        [Test]
        public void Prepare_ResizesClusteredLightingBuffersAndPublishesFrameContext_WhenLightsAreAvailable()
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

                AssertStructuredBuffer(pass, "m_DirectionalLightBuffer", 1, VividLightData.DirectionalLightData.Stride);
                AssertStructuredBuffer(pass, "m_PunctualLightBuffer", 1, VividLightData.PunctualLightData.Stride);
                AssertStructuredBuffer(pass, "m_FiniteLightBoundBuffer", 1, VividLightData.SFiniteLightBound.Stride);
                AssertStructuredBuffer(pass, "m_LightVolumeDataBuffer", 1, VividLightData.LightVolumeData.Stride);
                AssertStructuredBuffer(pass, "m_ScreenSpaceBoundsBuffer", 2, sizeof(float) * 4);
                AssertStructuredBuffer(pass, "m_BigTileLightListBuffer", 3840, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredOffsetBuffer", 15360, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredLightListBuffer", 491520, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredLightListCounterBuffer", 1, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LogBaseBuffer", 60, sizeof(float));

                AssertImportedBuffer(pass, "m_DirectionalLightBuffer", 1, VividLightData.DirectionalLightData.Stride);
                AssertImportedBuffer(pass, "m_PunctualLightBuffer", 1, VividLightData.PunctualLightData.Stride);
                AssertImportedBuffer(pass, "m_FiniteLightBoundBuffer", 1, VividLightData.SFiniteLightBound.Stride);
                AssertImportedBuffer(pass, "m_LightVolumeDataBuffer", 1, VividLightData.LightVolumeData.Stride);
                AssertImportedBuffer(pass, "m_ScreenSpaceBoundsBuffer", 2, sizeof(float) * 4);
                AssertImportedBuffer(pass, "m_BigTileLightListBuffer", 3840, sizeof(uint));
                AssertImportedBuffer(pass, "m_LayeredOffsetBuffer", 15360, sizeof(uint));
                AssertImportedBuffer(pass, "m_LayeredLightListBuffer", 491520, sizeof(uint));
                AssertImportedBuffer(pass, "m_LayeredLightListCounterBuffer", 1, sizeof(uint));
                AssertImportedBuffer(pass, "m_LogBaseBuffer", 60, sizeof(float));

                Assert.That(GetPrivateField<int>(pass, "m_DirectionalLightCount"), Is.EqualTo(1));
                Assert.That(GetPrivateField<int>(pass, "m_PunctualLightCount"), Is.EqualTo(1));
                Assert.That(GetPrivateField<int>(pass, "m_MainDirectionalLightIndex"), Is.EqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterTileCountX"), Is.EqualTo(10));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterTileCountY"), Is.EqualTo(6));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterCount"), Is.EqualTo(3840));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterBigTileCountX"), Is.EqualTo(5));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterBigTileCountY"), Is.EqualTo(3));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterLightIndexCapacity"), Is.EqualTo(491520));
                Assert.That(GetPrivateField<int>(pass, "m_ClusterBigTileLightIndexCapacity"), Is.EqualTo(3840));
                Assert.That(GetPrivateField<int>(pass, "m_LayeredOffsetCapacity"), Is.EqualTo(15360));

                var shaderVariablesLightList = GetPrivateField<object>(pass, "m_ShaderVariablesLightListCB");
                var lightListDimensions = GetStructFieldValue(shaderVariablesLightList, "g_viDimensions");
                Assert.That(GetStructFieldValue(shaderVariablesLightList, "g_iNrVisibLights"), Is.EqualTo(1));
                Assert.That(GetStructFieldValue(shaderVariablesLightList, "g_isOrthographic"), Is.EqualTo(0u));
                Assert.That(GetStructFieldValue(shaderVariablesLightList, "g_iNumSamplesMSAA"), Is.EqualTo(1));
                Assert.That(GetStructFieldValue(lightListDimensions, "x"), Is.EqualTo(320));
                Assert.That(GetStructFieldValue(lightListDimensions, "y"), Is.EqualTo(180));

                Assert.That(lightData.punctualLightBounds[0].radius, Is.GreaterThan(0.0f));
                Assert.That(lightData.punctualLightVolumeData[0].radiusSq, Is.EqualTo(36.0f).Within(1e-4f));
                Assert.That(lightData.punctualLightVolumeData[0].lightVolume, Is.EqualTo(1u));

                var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
                Assert.That(clusteredLightingData.directionalLights, Is.SameAs(GetBufferField(pass, "m_DirectionalLightBuffer")));
                Assert.That(clusteredLightingData.punctualLights, Is.SameAs(GetBufferField(pass, "m_PunctualLightBuffer")));
                Assert.That(clusteredLightingData.layeredOffset, Is.SameAs(GetBufferField(pass, "m_LayeredOffsetBuffer")));
                Assert.That(clusteredLightingData.layeredLightList, Is.SameAs(GetBufferField(pass, "m_LayeredLightListBuffer")));
                Assert.That(clusteredLightingData.logBaseBuffer, Is.SameAs(GetBufferField(pass, "m_LogBaseBuffer")));
                Assert.That(clusteredLightingData.directionalLightCount, Is.EqualTo(1));
                Assert.That(clusteredLightingData.punctualLightCount, Is.EqualTo(1));
                Assert.That(clusteredLightingData.mainDirectionalLightIndex, Is.EqualTo(0));
                Assert.That(clusteredLightingData.clusterTileSize, Is.EqualTo(LightGridPass.ClusterTileSize));
                Assert.That(clusteredLightingData.clusterSliceCount, Is.EqualTo(LightGridPass.ClusterSliceCount));
                Assert.That(clusteredLightingData.clusterTileCountX, Is.EqualTo(10));
                Assert.That(clusteredLightingData.clusterTileCountY, Is.EqualTo(6));
            }
            finally
            {
                pass.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Prepare_AllocatesFallbackClusteredLightingBuffers_WhenLightsAreMissing()
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

                AssertStructuredBuffer(pass, "m_DirectionalLightBuffer", 1, VividLightData.DirectionalLightData.Stride);
                AssertStructuredBuffer(pass, "m_PunctualLightBuffer", 1, VividLightData.PunctualLightData.Stride);
                AssertStructuredBuffer(pass, "m_FiniteLightBoundBuffer", 1, VividLightData.SFiniteLightBound.Stride);
                AssertStructuredBuffer(pass, "m_LightVolumeDataBuffer", 1, VividLightData.LightVolumeData.Stride);
                AssertStructuredBuffer(pass, "m_ScreenSpaceBoundsBuffer", 1, sizeof(float) * 4);
                AssertStructuredBuffer(pass, "m_BigTileLightListBuffer", 3840, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredOffsetBuffer", 15360, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredLightListBuffer", 491520, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LayeredLightListCounterBuffer", 1, sizeof(uint));
                AssertStructuredBuffer(pass, "m_LogBaseBuffer", 60, sizeof(float));

                Assert.That(GetPrivateField<int>(pass, "m_DirectionalLightCount"), Is.Zero);
                Assert.That(GetPrivateField<int>(pass, "m_PunctualLightCount"), Is.Zero);
                Assert.That(GetPrivateField<int>(pass, "m_MainDirectionalLightIndex"), Is.EqualTo(-1));

                var shaderVariablesLightList = GetPrivateField<object>(pass, "m_ShaderVariablesLightListCB");
                Assert.That(GetStructFieldValue(shaderVariablesLightList, "g_iNrVisibLights"), Is.Zero);
            }
            finally
            {
                pass.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsTrue_ForLightGridPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(LightGridPass)), Is.True);
        }

        private static void AssertStructuredBuffer(LightGridPass pass, string fieldName, int expectedCount, int expectedStride)
        {
            var buffer = GetBufferField(pass, fieldName);

            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.desc.Count, Is.EqualTo(expectedCount));
            Assert.That(buffer.desc.Stride, Is.EqualTo(expectedStride));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        private static void AssertImportedBuffer(LightGridPass pass, string fieldName, int expectedCount, int expectedStride)
        {
            var buffer = GetBufferField(pass, fieldName);
            var importedGraphicsBuffer = GetImportedGraphicsBuffer(buffer);

            Assert.That(importedGraphicsBuffer, Is.Not.Null);
            Assert.That(importedGraphicsBuffer.count, Is.GreaterThanOrEqualTo(expectedCount));
            Assert.That(importedGraphicsBuffer.stride, Is.EqualTo(expectedStride));
        }

        private static RenderGraphBuffer GetBufferField(LightGridPass pass, string fieldName)
        {
            return GetPrivateField<RenderGraphBuffer>(pass, fieldName);
        }

        private static GraphicsBuffer GetImportedGraphicsBuffer(RenderGraphBuffer buffer)
        {
            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);

            return (GraphicsBuffer)importedGraphicsBufferProperty.GetValue(buffer);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return (T)field.GetValue(instance);
        }

        private static object GetStructFieldValue(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return field.GetValue(instance);
        }
    }

    public class LightGridGlobalPassTests
    {
        [Test]
        public void Initialize_RegistersClusteredLightingBufferInputs_WhenPassIsCreated()
        {
            IRenderPass renderPass = new LightGridGlobalPass();

            var resources = renderPass.Initialize();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "DirectionalLights",
                "LayeredLightList",
                "LayeredOffset",
                "LogBaseBuffer",
                "PunctualLights"
            }));
        }

        [Test]
        public void Prepare_CopiesClusteredLightingState_WhenFrameContextIsAvailable()
        {
            var pass = new LightGridGlobalPass();
            var frameData = new ContextContainer();
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var directionalLights = new RenderGraphBuffer();
            var punctualLights = new RenderGraphBuffer();
            var layeredOffset = new RenderGraphBuffer();
            var layeredLightList = new RenderGraphBuffer();
            var logBaseBuffer = new RenderGraphBuffer();

            clusteredLightingData.directionalLights = directionalLights;
            clusteredLightingData.punctualLights = punctualLights;
            clusteredLightingData.layeredOffset = layeredOffset;
            clusteredLightingData.layeredLightList = layeredLightList;
            clusteredLightingData.logBaseBuffer = logBaseBuffer;
            clusteredLightingData.directionalLightCount = 2;
            clusteredLightingData.punctualLightCount = 4;
            clusteredLightingData.mainDirectionalLightIndex = 1;
            clusteredLightingData.clusterTileSize = 32;
            clusteredLightingData.clusterSliceCount = 64;
            clusteredLightingData.clusterTileCountX = 12;
            clusteredLightingData.clusterTileCountY = 7;
            clusteredLightingData.clusterNearClip = 0.3f;
            clusteredLightingData.clusterFarClip = 500.0f;
            clusteredLightingData.clusterIsOrthographic = 1;
            clusteredLightingData.clusterScale = 1.5f;
            clusteredLightingData.clusterBase = 1.02f;
            clusteredLightingData.clusterLog2SliceCount = 6;
            clusteredLightingData.supportsClusteredPunctualLights = true;
            clusteredLightingData.isLogBaseBufferEnabled = true;

            pass.Prepare(frameData);

            Assert.That(GetPrivateField<RenderGraphBuffer>(pass, "m_DirectionalLightBuffer"), Is.SameAs(directionalLights));
            Assert.That(GetPrivateField<RenderGraphBuffer>(pass, "m_PunctualLightBuffer"), Is.SameAs(punctualLights));
            Assert.That(GetPrivateField<RenderGraphBuffer>(pass, "m_LayeredOffsetBuffer"), Is.SameAs(layeredOffset));
            Assert.That(GetPrivateField<RenderGraphBuffer>(pass, "m_LayeredLightListBuffer"), Is.SameAs(layeredLightList));
            Assert.That(GetPrivateField<RenderGraphBuffer>(pass, "m_LogBaseBuffer"), Is.SameAs(logBaseBuffer));
            Assert.That(GetPrivateField<int>(pass, "m_DirectionalLightCount"), Is.EqualTo(2));
            Assert.That(GetPrivateField<int>(pass, "m_PunctualLightCount"), Is.EqualTo(4));
            Assert.That(GetPrivateField<int>(pass, "m_MainDirectionalLightIndex"), Is.EqualTo(1));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterTileSize"), Is.EqualTo(32));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterSliceCount"), Is.EqualTo(64));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterTileCountX"), Is.EqualTo(12));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterTileCountY"), Is.EqualTo(7));
            Assert.That(GetPrivateField<float>(pass, "m_ClusterNearClip"), Is.EqualTo(0.3f));
            Assert.That(GetPrivateField<float>(pass, "m_ClusterFarClip"), Is.EqualTo(500.0f));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterIsOrthographic"), Is.EqualTo(1));
            Assert.That(GetPrivateField<float>(pass, "m_ClusterScale"), Is.EqualTo(1.5f));
            Assert.That(GetPrivateField<float>(pass, "m_ClusterBase"), Is.EqualTo(1.02f));
            Assert.That(GetPrivateField<int>(pass, "m_ClusterLog2SliceCount"), Is.EqualTo(6));
            Assert.That(GetPrivateField<bool>(pass, "m_SupportsClusteredPunctualLights"), Is.True);
            Assert.That(GetPrivateField<bool>(pass, "m_IsLogBaseBufferEnabled"), Is.True);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForLightGridGlobalPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(LightGridGlobalPass)), Is.False);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return (T)field.GetValue(instance);
        }
    }
}
