using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

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

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
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
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<ClusterDebugVolume>();

            try
            {
                volume.active = true;
                volume.tileClusterDebug.overrideState = true;
                volume.tileClusterDebug.value = TileClusterDebug.Cluster;
                volume.tileClusterDebugByCategory.overrideState = true;
                volume.tileClusterDebugByCategory.value = TileClusterCategoryDebug.EnvironmentAndAreaAndPunctual;
                volume.clusterDebugMode.overrideState = true;
                volume.clusterDebugMode.value = ClusterDebugMode.VisualizeSlice;
                volume.clusterDebugDistance.overrideState = true;
                volume.clusterDebugDistance.value = 6f;

                var settings = ClusterDebugPass.ResolveSettings(volume);

                Assert.That(settings.tileClusterDebug, Is.EqualTo(TileClusterDebug.Cluster));
                Assert.That(settings.tileClusterDebugByCategory, Is.EqualTo(TileClusterCategoryDebug.EnvironmentAndAreaAndPunctual));
                Assert.That(settings.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeSlice));
                Assert.That(settings.clusterDebugDistance, Is.EqualTo(6f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ClusterDebugVolume_UsesHdrpLikeDefaults_WhenCreated()
        {
            var volume = ScriptableObject.CreateInstance<ClusterDebugVolume>();

            try
            {
                Assert.That(volume.tileClusterDebug.value, Is.EqualTo(TileClusterDebug.None));
                Assert.That(volume.tileClusterDebugByCategory.value, Is.EqualTo(TileClusterCategoryDebug.Punctual));
                Assert.That(volume.clusterDebugMode.value, Is.EqualTo(ClusterDebugMode.VisualizeOpaque));
                Assert.That(volume.clusterDebugDistance.value, Is.EqualTo(1f));
                Assert.That(volume.IsActive(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetClusterDebugVolume_ReturnsStackComponent_WhenVolumeManagerIsInitialized()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            try
            {
                var component = profile.Add<ClusterDebugVolume>(false);
                component.tileClusterDebug.overrideState = true;
                component.tileClusterDebug.value = TileClusterDebug.Cluster;

                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);

                var resolvedVolume = VividVolumeManagerUtility.GetClusterDebugVolume();

                Assert.That(resolvedVolume, Is.Not.Null);
                Assert.That(resolvedVolume.tileClusterDebug.value, Is.EqualTo(TileClusterDebug.Cluster));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ClusterDebugShader_UsesCoreDebugHeatmapOverlay()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl\""));
            Assert.That(shaderSource, Does.Contain("OverlayHeatMap("));
            Assert.That(shaderSource, Does.Contain("float2 pixelUv = (float2(pixelCoord) + 0.5) * _ClusterDebugLightViewportSize.zw;"));
            Assert.That(shaderSource, Does.Contain("SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r"));
        }

        private static RenderGraphTexture GetTextureField(ClusterDebugPass pass, string fieldName)
        {
            var field = typeof(ClusterDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
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
                "ClusterDebug.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
