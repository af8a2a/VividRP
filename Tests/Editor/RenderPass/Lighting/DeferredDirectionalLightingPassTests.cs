using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph.Generated;
using VividRP.Runtime;
using DeferredLightingPass = VividRP.Runtime.RenderPass.Core.DeferredLightingPass;

namespace VividRP.Editor.Tests
{
    public class DeferredDirectionalLightingPassTests
    {
        [Test]
        public void Initialize_RegistersDeferredLightBuffersAndIndirectLightingDependencies_WhenPassIsCreated()
        {
            IRenderPass renderPass = new DeferredLightingPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "Color",
                "Depth",
                "DirectionalShadowTexture",
                "GBuffer0",
                "GBuffer1",
                "GBuffer2",
                "GBuffer3",
                "GBuffer4",
                "GTAOTexture",
                "PreIntegratedFGD_CharlieAndFabric",
                "PreIntegratedFGD_GGXDisneyDiffuse",
                "SkyIBLCubemap"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").AttachmentIndex, Is.EqualTo(0));
            Assert.That(textureEntries.Single(entry => entry.Name == "SkyIBLCubemap").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "PreIntegratedFGD_GGXDisneyDiffuse").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "PreIntegratedFGD_CharlieAndFabric").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                textureEntries
                    .Where(entry => entry.Name != "Color")
                    .Select(entry => entry.Access)
                    .Distinct(),
                Is.EqualTo(new[] { AccessFlags.Read }));

            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "AreaLights",
                "ClearCoatIndirectArgs",
                "ClearCoatMaterialIndices",
                "DirectionalLights",
                "FabricIndirectArgs",
                "FabricMaterialIndices",
                "LayeredLightList",
                "LayeredOffset",
                "LogBaseBuffer",
                "PunctualLights",
                "StandardIndirectArgs",
                "StandardMaterialIndices"
            }));
            Assert.That(bufferEntries.Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Read }));
        }

        [Test]
        public void Prepare_ResizesInputAndOutputTextures_WhenCameraSizeChanges()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 511;
            cameraData.actualHeight = 257;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_GBuffer0", 511, 257);
            AssertTextureSize(pass, "m_GBuffer1", 511, 257);
            AssertTextureSize(pass, "m_GBuffer2", 511, 257);
            AssertTextureSize(pass, "m_GBuffer3", 511, 257);
            AssertTextureSize(pass, "m_GBuffer4", 511, 257);
            AssertTextureSize(pass, "m_DepthTexture", 511, 257);
            AssertTextureSize(pass, "m_GTAOTexture", 511, 257);
            AssertTextureSize(pass, "m_ColorTexture", 511, 257);
            AssertTextureSize(pass, "m_PreIntegratedFGDGGXDisneyDiffuseTexture", 64, 64);
            AssertTextureSize(pass, "m_PreIntegratedFGDCharlieAndFabricTexture", 64, 64);

            Assert.That(GetFieldValue<int>(pass, "m_LightingWidth"), Is.EqualTo(511));
            Assert.That(GetFieldValue<int>(pass, "m_LightingHeight"), Is.EqualTo(257));
            Assert.That(GetFieldValue<int>(pass, "m_ClearDispatchGroupCountX"), Is.EqualTo(64));
            Assert.That(GetFieldValue<int>(pass, "m_ClearDispatchGroupCountY"), Is.EqualTo(33));
            Assert.That(GetFieldValue<int>(pass, "m_DirectionalLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<int>(pass, "m_PunctualLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<int>(pass, "m_AreaLightCount"), Is.EqualTo(0));

            var outputTexture = GetFieldValue<RenderGraphTexture>(pass, "m_ColorTexture");
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputTexture.desc.ClearBuffer, Is.True);
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));

            var gbuffer1Texture = GetFieldValue<RenderGraphTexture>(pass, "m_GBuffer1");
            var gbuffer2Texture = GetFieldValue<RenderGraphTexture>(pass, "m_GBuffer2");
            Assert.That(gbuffer1Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(gbuffer2Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));

            var skyCubemap = GetFieldValue<RenderGraphTexture>(pass, "m_SkyIBLCubemap");
            Assert.That(skyCubemap.desc.Dimension, Is.EqualTo(TextureDimension.Cube));
        }

        [Test]
        public void Prepare_KeepsLocalDirectionalShadowFallback_WhenGraphDoesNotBindOne()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 256;
            cameraData.actualHeight = 144;

            pass.Prepare(frameData);

            var localDirectionalShadowTexture = GetFieldValue<RenderGraphTexture>(pass, "m_LocalDirectionalShadowTexture");
            var directionalShadowTexture = GetFieldValue<RenderGraphTexture>(pass, "m_DirectionalShadowTexture");

            Assert.That(directionalShadowTexture, Is.SameAs(localDirectionalShadowTexture));
            Assert.That(directionalShadowTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
            Assert.That(directionalShadowTexture.desc.ClearColor, Is.EqualTo(Color.white));
        }

        [Test]
        public void Prepare_KeepsLocalGTAOFallback_WhenGraphDoesNotBindOne()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 256;
            cameraData.actualHeight = 144;

            pass.Prepare(frameData);

            var localGtaoTexture = GetFieldValue<RenderGraphTexture>(pass, "m_LocalGTAOTexture");
            var gtaoTexture = GetFieldValue<RenderGraphTexture>(pass, "m_GTAOTexture");

            Assert.That(gtaoTexture, Is.SameAs(localGtaoTexture));
            Assert.That(gtaoTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8_UNorm));
            Assert.That(gtaoTexture.desc.ClearColor, Is.EqualTo(Color.white));
        }

        [Test]
        public void Prepare_CachesClusteredLightingMetadata_WhenLightGridBuffersAreBound()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            SetFieldValue(pass, "m_DirectionalLightBuffer", CreateStructuredBuffer("DirectionalLights", VividLightData.DirectionalLightData.Stride));
            SetFieldValue(pass, "m_PunctualLightBuffer", CreateStructuredBuffer("PunctualLights", VividLightData.PunctualLightData.Stride));
            SetFieldValue(pass, "m_AreaLightBuffer", CreateStructuredBuffer("AreaLights", VividLightData.AreaLightData.Stride));
            SetFieldValue(pass, "m_LayeredOffsetBuffer", CreateStructuredBuffer("LayeredOffset", sizeof(uint)));
            SetFieldValue(pass, "m_LayeredLightListBuffer", CreateStructuredBuffer("LayeredLightList", sizeof(uint)));
            SetFieldValue(pass, "m_LogBaseBuffer", CreateStructuredBuffer("LogBaseBuffer", sizeof(float)));

            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.directionalLightCount = 3;
            clusteredLightingData.punctualLightCount = 7;
            clusteredLightingData.areaLightCount = 2;
            clusteredLightingData.mainDirectionalLightIndex = 1;
            clusteredLightingData.clusterTileSize = 64;
            clusteredLightingData.clusterSliceCount = 32;
            clusteredLightingData.clusterTileCountX = 10;
            clusteredLightingData.clusterTileCountY = 6;
            clusteredLightingData.clusterNearClip = 0.3f;
            clusteredLightingData.clusterFarClip = 900.0f;
            clusteredLightingData.clusterIsOrthographic = 1;
            clusteredLightingData.clusterScale = 2.5f;
            clusteredLightingData.clusterBase = 1.17f;
            clusteredLightingData.clusterLog2SliceCount = 5;
            clusteredLightingData.supportsClusteredPunctualLights = true;
            clusteredLightingData.isLogBaseBufferEnabled = true;

            pass.Prepare(frameData);

            Assert.That(GetFieldValue<int>(pass, "m_DirectionalLightCount"), Is.EqualTo(3));
            Assert.That(GetFieldValue<int>(pass, "m_PunctualLightCount"), Is.EqualTo(7));
            Assert.That(GetFieldValue<int>(pass, "m_AreaLightCount"), Is.EqualTo(2));
            Assert.That(GetFieldValue<int>(pass, "m_MainDirectionalLightIndex"), Is.EqualTo(1));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileSize"), Is.EqualTo(64));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterSliceCount"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountX"), Is.EqualTo(10));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountY"), Is.EqualTo(6));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterNearClip"), Is.EqualTo(0.3f));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterFarClip"), Is.EqualTo(900.0f));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterIsOrthographic"), Is.EqualTo(1));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterScale"), Is.EqualTo(2.5f));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterBase"), Is.EqualTo(1.17f));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterLog2SliceCount"), Is.EqualTo(5));
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredPunctualLights"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredAreaLights"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_IsLogBaseBufferEnabled"), Is.True);
        }

        [Test]
        public void Prepare_ZeroesAreaLightCount_WhenAreaBufferIsBoundWithoutClusteredLists()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 200;

            SetFieldValue(pass, "m_AreaLightBuffer", CreateStructuredBuffer("AreaLights", VividLightData.AreaLightData.Stride));

            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.areaLightCount = 4;
            clusteredLightingData.clusterTileSize = 32;
            clusteredLightingData.clusterSliceCount = 64;

            pass.Prepare(frameData);

            Assert.That(GetFieldValue<int>(pass, "m_AreaLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredAreaLights"), Is.False);
        }

        [Test]
        public void Prepare_ZeroesLightCounts_WhenClusteredBuffersAreNotBound()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 200;

            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.directionalLightCount = 2;
            clusteredLightingData.punctualLightCount = 5;
            clusteredLightingData.areaLightCount = 4;
            clusteredLightingData.mainDirectionalLightIndex = 0;
            clusteredLightingData.clusterTileSize = 32;
            clusteredLightingData.clusterSliceCount = 64;
            clusteredLightingData.clusterTileCountX = 10;
            clusteredLightingData.clusterTileCountY = 7;
            clusteredLightingData.clusterNearClip = 0.5f;
            clusteredLightingData.clusterFarClip = 500.0f;
            clusteredLightingData.clusterScale = 1.5f;
            clusteredLightingData.clusterBase = 1.1f;
            clusteredLightingData.clusterLog2SliceCount = 6;
            clusteredLightingData.supportsClusteredPunctualLights = true;
            clusteredLightingData.isLogBaseBufferEnabled = true;

            pass.Prepare(frameData);

            Assert.That(GetFieldValue<int>(pass, "m_DirectionalLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<int>(pass, "m_PunctualLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<int>(pass, "m_AreaLightCount"), Is.EqualTo(0));
            Assert.That(GetFieldValue<int>(pass, "m_MainDirectionalLightIndex"), Is.EqualTo(-1));
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredPunctualLights"), Is.False);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredAreaLights"), Is.False);
            Assert.That(GetFieldValue<bool>(pass, "m_IsLogBaseBufferEnabled"), Is.False);
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileSize"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterSliceCount"), Is.EqualTo(64));
        }

        [Test]
        public void Prepare_UsesSkyDataFromFrameContext_WhenSkySystemPopulatesIt()
        {
            var pass = new DeferredLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var cubemap = new Cubemap(4, TextureFormat.RGBA32, false);

            try
            {
                cameraData.actualWidth = 256;
                cameraData.actualHeight = 144;
                skyData.activeSkyType = SkyType.HDRI;
                skyData.specularCubemap = cubemap;
                skyData.tint = Color.cyan;
                skyData.exposure = 1.75f;
                skyData.rotation = 30.0f;

                pass.Prepare(frameData);

                Assert.That(GetFieldValue<Color>(pass, "m_SkyTextureTint"), Is.EqualTo(Color.cyan));
                Assert.That(
                    GetFieldValue<Vector4>(pass, "m_SkyTextureParams"),
                    Is.EqualTo(DeferredLightingPass.BuildSkyTextureParams(cubemap, 1.75f, 30.0f)));
                Assert.That(GetFieldValue<Vector4>(pass, "m_SkyTextureParams").x, Is.EqualTo(1.75f).Within(1e-5f));
                Assert.That(GetFieldValue<Vector4>(pass, "m_SkyTextureParams").y, Is.EqualTo(30.0f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cubemap);
            }
        }


        private static void AssertTextureSize(DeferredLightingPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(DeferredLightingPass pass, string fieldName)
        {
            var field = GetField(fieldName);
            return (T)field.GetValue(pass);
        }

        private static void SetFieldValue<T>(DeferredLightingPass pass, string fieldName, T value)
        {
            var field = GetField(fieldName);
            field.SetValue(pass, value);
        }

        private static FieldInfo GetField(string fieldName)
        {
            var field = typeof(DeferredLightingPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' on {nameof(DeferredLightingPass)}.");
            return field;
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }
    }
}
