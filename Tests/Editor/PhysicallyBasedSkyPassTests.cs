using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyPassTests
    {
        [Test]
        public void Initialize_RegistersDepthInputAndColorOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new PhysicallyBasedSkyPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Color", "Depth" }));

            var colorEntry = textureEntries.Single(entry => entry.Name == "Color");
            var depthEntry = textureEntries.Single(entry => entry.Name == "Depth");

            Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(colorEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(colorEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));

            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.AttachmentIndex, Is.EqualTo(-1));
            Assert.That(depthEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));
        }

        [Test]
        public void Prepare_ResizesTexturesAndKeepsPassInactive_WhenSkyTypeIsNotPhysicallyBased()
        {
            var pass = new PhysicallyBasedSkyPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();

            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;
            skyData.activeSkyType = SkyType.HDRI;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_ColorTarget", 640, 360);
            AssertTextureSize(pass, "m_DepthTexture", 640, 360);
            Assert.That(GetFieldValue<bool>(pass, "m_IsActive"), Is.False);
        }

        [Test]
        public void VividRPCoreResources_DeclaresPhysicallyBasedSkyShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.PhysicallyBasedSkyShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/PhysicallyBasedSky"));
        }

        [Test]
        public void Source_UsesShaderFallbackAndCachesAtmosphereParameters()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PhysicallyBasedSkyPass.cs"));

            Assert.That(source, Does.Contain("internal const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(source, Does.Contain("shader ??= Shader.Find(PhysicallyBasedSkyShaderName);"));
            Assert.That(source, Does.Contain("skyData.activeSkyType == SkyType.PhysicallyBased"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.ResolveCameraPosition(context, volume.planetRadius.value)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.ResolveSunDirection(context)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyRenderer.ResolveSunColor(context)"));
            Assert.That(source, Does.Contain("m_SkyPlanetParams = new Vector4("));
            Assert.That(source, Does.Contain("m_SkyAirScattering = ToVector4(volume.GetAirScatteringCoefficient());"));
            Assert.That(source, Does.Contain("m_SkyAerosolExtinction = new Vector4("));
            Assert.That(source, Does.Contain("mpb.SetVector(SkyPlanetParamsId, m_SkyPlanetParams);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);"));
        }

        [Test]
        public void Shader_Source_ImplementsAtmosphereFullscreenScattering()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PhysicallyBasedSky.shader"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("ZTest LEqual"));
            Assert.That(source, Does.Contain("VIEW_SAMPLE_COUNT = 12"));
            Assert.That(source, Does.Contain("LIGHT_SAMPLE_COUNT = 6"));
            Assert.That(source, Does.Contain("GetSkyViewDirWS"));
            Assert.That(source, Does.Contain("ComputeOpticalDepthToSun"));
            Assert.That(source, Does.Contain("EvaluateSky"));
            Assert.That(source, Does.Contain("_SkySunDirection"));
            Assert.That(source, Does.Contain("_SkySunColor"));
            Assert.That(source, Does.Contain("_SkyPlanetParams"));
            Assert.That(source, Does.Contain("_SkyOzoneParams"));
        }

        private static void AssertTextureSize(PhysicallyBasedSkyPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(PhysicallyBasedSkyPass pass, string fieldName)
        {
            var field = typeof(PhysicallyBasedSkyPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
