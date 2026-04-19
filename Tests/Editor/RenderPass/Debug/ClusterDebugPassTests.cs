using System.IO;
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
        public void Initialize_RegistersSourceDepthInputsAndColorOutput()
        {
            IRenderPass renderPass = new ClusterDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "DepthTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");
            var punctualLightsEntry = resources.Buffers.Single(entry => entry.Name == "PunctualLights");
            var areaLightsEntry = resources.Buffers.Single(entry => entry.Name == "AreaLights");
            var layeredOffsetEntry = resources.Buffers.Single(entry => entry.Name == "LayeredOffset");
            var layeredLightListEntry = resources.Buffers.Single(entry => entry.Name == "LayeredLightList");
            var logBaseBufferEntry = resources.Buffers.Single(entry => entry.Name == "LogBaseBuffer");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(resources.Buffers, Has.Length.EqualTo(5));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(punctualLightsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(areaLightsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(layeredOffsetEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(layeredLightListEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(logBaseBufferEntry.Access, Is.EqualTo(AccessFlags.Read));
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
                tileClusterDebugByCategory = TileClusterCategoryDebug.EnvironmentAndAreaAndPunctual,
                clusterDebugMode = ClusterDebugMode.VisualizeSlice,
                clusterDebugDistance = 6f,
            };

            var settings = ClusterDebugPass.ResolveSettings(data);

            Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.Cluster));
            Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.EnvironmentAndAreaAndPunctual));
            Assert.That(settings.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeSlice));
            Assert.That(settings.clusterDebugDistance, Is.EqualTo(6f));
        }

        [Test]
        public void ResolveSettings_UsesDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = ClusterDebugPass.ResolveSettings(null);

            Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.None));
            Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.Punctual));
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
                data.tileClusterDebugByCategory = TileClusterCategoryDebug.Environment;
                data.clusterDebugMode = ClusterDebugMode.VisualizeSlice;
                data.clusterDebugDistance = 4f;

                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;

                pass.Prepare(frameData);
                var settings = GetFieldValue<ClusterDebugPass.ClusterDebugSettingsData>(pass, "m_ResolvedSettings");

                Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.Cluster));
                Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.Environment));
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
            clusteredLightingData.punctualLights = CreateStructuredBuffer("PunctualLights", VividLightData.PunctualLightData.Stride);
            clusteredLightingData.areaLights = CreateStructuredBuffer("AreaLights", VividLightData.AreaLightData.Stride);
            clusteredLightingData.layeredOffset = CreateStructuredBuffer("LayeredOffset", sizeof(uint));
            clusteredLightingData.layeredLightList = CreateStructuredBuffer("LayeredLightList", sizeof(uint));
            clusteredLightingData.logBaseBuffer = CreateStructuredBuffer("LogBaseBuffer", sizeof(float));
            clusteredLightingData.clusterTileSize = 32;
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

            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_PunctualLightBuffer"), Is.SameAs(clusteredLightingData.punctualLights));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_AreaLightBuffer"), Is.SameAs(clusteredLightingData.areaLights));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LayeredOffsetBuffer"), Is.SameAs(clusteredLightingData.layeredOffset));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LayeredLightListBuffer"), Is.SameAs(clusteredLightingData.layeredLightList));
            Assert.That(GetFieldValue<RenderGraphBuffer>(pass, "m_LogBaseBuffer"), Is.SameAs(clusteredLightingData.logBaseBuffer));
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
        public void ClusterDebugPass_BindsClusteredLightingParametersDirectly()
        {
            var passSource = File.ReadAllText(GetPassSourcePath());

            Assert.That(passSource, Does.Contain("PrepareClusteredLightingParameters(frameData, cameraData, width, height);"));
            Assert.That(passSource, Does.Contain("ApplyClusteredLightingProperties();"));
            Assert.That(passSource, Does.Contain("m_Material.SetInt(ClusteredPunctualLightGridEnabledId"));
            Assert.That(passSource, Does.Contain("m_Material.SetInt(ClusterTileSizeId"));
            Assert.That(passSource, Does.Contain("m_Material.SetBuffer(LayeredOffsetId"));
            Assert.That(passSource, Does.Contain("m_Material.SetBuffer(LayeredLightListId"));
        }

        [Test]
        public void ClusterDebugShader_UsesCoreDebugHeatmapOverlay()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl\""));
            Assert.That(shaderSource, Does.Contain("OverlayHeatMap("));
            Assert.That(shaderSource, Does.Contain("VividLightingLoop::GetPunctualLightCount"));
            Assert.That(shaderSource, Does.Contain("VividLightingLoop::GetAreaLightCount"));
            Assert.That(shaderSource, Does.Contain("_ClusteredPunctualLightGridEnabled"));
            Assert.That(shaderSource, Does.Contain("_ClusteredAreaLightGridEnabled"));
            Assert.That(shaderSource, Does.Contain("float2 pixelUv = (float2(pixelCoord) + 0.5) * _ClusterDebugLightViewportSize.zw;"));
            Assert.That(shaderSource, Does.Contain("SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r"));
            Assert.That(shaderSource, Does.Not.Contain("_PunctualLightCount"));
            Assert.That(shaderSource, Does.Not.Contain("GetBruteForcePunctualLightCount"));
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

        private static string GetPassSourcePath()
        {
            var passPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                "Runtime",
                "RenderPass",
                "Debug",
                "ClusterDebugPass.cs"));

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
        }

        private static string GetShaderSourcePath()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "ClusterDebug.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
