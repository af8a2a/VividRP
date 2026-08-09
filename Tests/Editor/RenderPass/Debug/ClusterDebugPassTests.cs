using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph.Generated;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ClusterDebugPass = VividRP.Runtime.RenderPass.Core.ClusterDebugPass;

namespace VividRP.Editor.Tests
{
    public class ClusterDebugPassTests
    {
        [Test]
        public void Initialize_RegistersSourceDepthMaterialFeatureInputsAndColorOutput()
        {
            IRenderPass renderPass = new ClusterDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "DepthTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");
            var punctualLightsEntry = resources.Buffers.Single(entry => entry.Name == "PunctualLights");
            var areaLightsEntry = resources.Buffers.Single(entry => entry.Name == "AreaLights");
            var decalDataEntry = resources.Buffers.Single(entry => entry.Name == "DecalData");
            var bigTileLightListEntry = resources.Buffers.Single(entry => entry.Name == "BigTileLightList");
            var layeredOffsetEntry = resources.Buffers.Single(entry => entry.Name == "LayeredOffset");
            var layeredLightListEntry = resources.Buffers.Single(entry => entry.Name == "LayeredLightList");
            var logBaseBufferEntry = resources.Buffers.Single(entry => entry.Name == "LogBaseBuffer");
            var materialTileFeatureFlagsEntry = resources.Buffers.Single(entry => entry.Name == "MaterialTileFeatureFlags");
            var materialFeatureTileListEntry = resources.Buffers.Single(entry => entry.Name == "MaterialFeatureTileList");
            var materialFeatureIndirectArgsEntry = resources.Buffers.Single(entry => entry.Name == "MaterialFeatureIndirectArgs");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(resources.Buffers, Has.Length.EqualTo(10));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(punctualLightsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(areaLightsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(decalDataEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(bigTileLightListEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(layeredOffsetEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(layeredLightListEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(logBaseBufferEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(materialTileFeatureFlagsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(materialFeatureTileListEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(materialFeatureIndirectArgsEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndFormat_WhenConfigured()
        {
            var pass = new ClusterDebugPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 1280;
            sourceTexture.desc.Height = 720;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                tileClusterDebug = TileClusterDebug.Cluster,
                tileClusterDebugByCategory = TileClusterCategoryDebug.Punctual | TileClusterCategoryDebug.Area | TileClusterCategoryDebug.Environment | TileClusterCategoryDebug.Decal,
                materialFeatureVariantDebug = MaterialFeatureVariantDebug.ClearCoat,
                clusterDebugMode = ClusterDebugMode.VisualizeSlice,
                clusterDebugDistance = 6f,
            };

            var settings = ClusterDebugPass.ResolveSettings(data);

            Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.Cluster));
            Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.Punctual | TileClusterCategoryDebug.Area | TileClusterCategoryDebug.Environment | TileClusterCategoryDebug.Decal));
            Assert.That(settings.materialFeatureVariantDebug, Is.EqualTo(MaterialFeatureVariantDebug.ClearCoat));
            Assert.That(settings.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeSlice));
            Assert.That(settings.clusterDebugDistance, Is.EqualTo(6f));
        }

        [Test]
        public void ResolveSettings_UsesDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = ClusterDebugPass.ResolveSettings(null);

            Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.None));
            Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.Punctual));
            Assert.That(settings.materialFeatureVariantDebug, Is.EqualTo(MaterialFeatureVariantDebug.All));
            Assert.That(settings.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeOpaque));
            Assert.That(settings.clusterDebugDistance, Is.EqualTo(1f));
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerSettings()
        {
            var pass = new ClusterDebugPass();
            var data = VividRenderingDebugDisplaySettings.Data;

            try
            {
                data.Reset();
                data.tileClusterDebug = TileClusterDebug.Cluster;
                data.tileClusterDebugByCategory = TileClusterCategoryDebug.Environment | TileClusterCategoryDebug.Decal;
                data.materialFeatureVariantDebug = MaterialFeatureVariantDebug.SSRReceive;
                data.clusterDebugMode = ClusterDebugMode.VisualizeSlice;
                data.clusterDebugDistance = 4f;

                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;

                pass.Prepare(frameData);
                var settings = GetFieldValue<ClusterDebugPass.ClusterDebugSettingsData>(pass, "m_ResolvedSettings");

                Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.Cluster));
                Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.Environment | TileClusterCategoryDebug.Decal));
                Assert.That(settings.materialFeatureVariantDebug, Is.EqualTo(MaterialFeatureVariantDebug.SSRReceive));
                Assert.That(settings.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeSlice));
                Assert.That(settings.clusterDebugDistance, Is.EqualTo(4f));
            }
            finally
            {
                data.Reset();
            }
        }

        [Test]
        public void Prepare_CachesClusteredLightingData_WhenLightGridBuffersAreAvailable()
        {
            var pass = new ClusterDebugPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.punctualLights = RenderGraphBuffer.CreateStructured("PunctualLights", VividLightData.PunctualLightData.Stride);
            clusteredLightingData.areaLights = RenderGraphBuffer.CreateStructured("AreaLights", VividLightData.AreaLightData.Stride);
            clusteredLightingData.decalData = RenderGraphBuffer.CreateStructured("DecalData", VividLightData.DecalClusterData.Stride);
            clusteredLightingData.bigTileLightList = RenderGraphBuffer.CreateStructured("BigTileLightList", sizeof(uint));
            clusteredLightingData.layeredOffset = RenderGraphBuffer.CreateStructured("LayeredOffset", sizeof(uint));
            clusteredLightingData.layeredLightList = RenderGraphBuffer.CreateStructured("LayeredLightList", sizeof(uint));
            clusteredLightingData.logBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", sizeof(float));
            clusteredLightingData.clusterTileSize = 32;
            clusteredLightingData.clusterSliceCount = 32;
            clusteredLightingData.clusterTileCountX = 10;
            clusteredLightingData.clusterTileCountY = 6;
            clusteredLightingData.bigTileCountX = 5;
            clusteredLightingData.bigTileCountY = 3;
            clusteredLightingData.clusterNearClip = 0.3f;
            clusteredLightingData.clusterFarClip = 900.0f;
            clusteredLightingData.clusterIsOrthographic = 1;
            clusteredLightingData.clusterScale = 2.5f;
            clusteredLightingData.clusterBase = 1.17f;
            clusteredLightingData.clusterLog2SliceCount = 5;
            clusteredLightingData.punctualLightCount = 2;
            clusteredLightingData.areaLightCount = 3;
            clusteredLightingData.reflectionProbeCount = 4;
            clusteredLightingData.decalCount = 1;
            clusteredLightingData.supportsClusteredPunctualLights = true;
            clusteredLightingData.isLogBaseBufferEnabled = true;

            pass.Prepare(frameData);

            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_PunctualLightBuffer"), Is.SameAs(clusteredLightingData.punctualLights));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_AreaLightBuffer"), Is.SameAs(clusteredLightingData.areaLights));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_DecalDataBuffer"), Is.SameAs(clusteredLightingData.decalData));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_BigTileLightListBuffer"), Is.SameAs(clusteredLightingData.bigTileLightList));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LayeredOffsetBuffer"), Is.SameAs(clusteredLightingData.layeredOffset));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LayeredLightListBuffer"), Is.SameAs(clusteredLightingData.layeredLightList));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LogBaseBuffer"), Is.SameAs(clusteredLightingData.logBaseBuffer));
            Assert.That(GetFieldValue<int>(pass, "m_PunctualLightCount"), Is.EqualTo(2));
            Assert.That(GetFieldValue<int>(pass, "m_AreaLightCount"), Is.EqualTo(3));
            Assert.That(GetFieldValue<int>(pass, "m_ReflectionProbeCount"), Is.EqualTo(4));
            Assert.That(GetFieldValue<int>(pass, "m_DecalCount"), Is.EqualTo(1));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileSize"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterSliceCount"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountX"), Is.EqualTo(10));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountY"), Is.EqualTo(6));
            Assert.That(GetFieldValue<int>(pass, "m_MaterialTileCountX"), Is.EqualTo(80));
            Assert.That(GetFieldValue<int>(pass, "m_MaterialTileCountY"), Is.EqualTo(45));
            Assert.That(GetFieldValue<int>(pass, "m_MaterialTileCount"), Is.EqualTo(3600));
            Assert.That(GetFieldValue<int>(pass, "m_BigTileCountX"), Is.EqualTo(5));
            Assert.That(GetFieldValue<int>(pass, "m_BigTileCountY"), Is.EqualTo(3));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterNearClip"), Is.EqualTo(0.3f));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterFarClip"), Is.EqualTo(900.0f));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterIsOrthographic"), Is.EqualTo(1));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterScale"), Is.EqualTo(2.5f));
            Assert.That(GetFieldValue<float>(pass, "m_ClusterBase"), Is.EqualTo(1.17f));
            Assert.That(GetFieldValue<int>(pass, "m_ClusterLog2SliceCount"), Is.EqualTo(5));
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredPunctualLights"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredAreaLights"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredReflectionProbes"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredDecals"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_SupportsBigTileLightList"), Is.True);
            Assert.That(GetFieldValue<bool>(pass, "m_IsLogBaseBufferEnabled"), Is.True);
        }

        private static T GetFieldValue<T>(ClusterDebugPass pass, string fieldName)
        {
            var field = typeof(ClusterDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static RenderGraphTexture GetTextureField(ClusterDebugPass pass, string fieldName)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }
    }
}
