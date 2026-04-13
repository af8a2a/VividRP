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

#pragma warning disable CS0618
namespace VividRP.Editor.Tests
{
    public class AtmosphereLUTPassTests
    {
        [Test]
        public void Initialize_RegistersSkyLutOutputs_WhenPassIsCreated()
        {
            IRenderPass renderPass = new AtmosphereLUTPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "AtmosphericScatteringLUT",
                "MultiScatteringLUT",
                "SkyViewLUT"
            }));
            Assert.That(textureEntries.Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Write }));
            Assert.That(resources.Buffers, Is.Empty);
        }

        [Test]
        public void AtmosphereLUTPass_RemainsComputePassCompatibilityWrapper()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(AtmosphereLUTPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresFixedHdrpAlignedLutDimensionsAndRandomWrite()
        {
            var pass = new AtmosphereLUTPass();

            pass.Prepare(new ContextContainer());

            AssertTexture(
                pass,
                "m_MultiScatteringLUT",
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringHeight);
            AssertTexture(
                pass,
                "m_SkyViewLUT",
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewWidth,
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewHeight);
            AssertVolumeTexture(
                pass,
                "m_AtmosphericScatteringLUT",
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringHeight,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringDepth);
        }

        [Test]
        public void VividRPCoreResources_DeclaresAtmosphereLUTCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.AtmosphereLUTCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/Sky/SkyLUTGenerator.compute"));
        }

        [Test]
        public void Source_MovesAtmosphereLutUpdatesIntoSkyManagerAndUsesImportOnlyPassWrapper()
        {
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "AtmosphereLUTPass.cs"));
            var cacheSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "PhysicallyBasedSky", "PhysicallyBasedSkyAtmosphereLutCache.cs"));
            var managerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));

            Assert.That(passSource, Does.Contain("[System.Obsolete(\"AtmosphereLUTPass is deprecated. Atmosphere LUTs are updated by SkyManager.Update().\")]"));
            Assert.That(passSource, Does.Contain("SkyManager.ImportMultiScatteringLut(m_MultiScatteringLUT);"));
            Assert.That(passSource, Does.Contain("SkyManager.ImportSkyViewLut(m_SkyViewLUT);"));
            Assert.That(passSource, Does.Contain("SkyManager.ImportAtmosphericScatteringLut(m_AtmosphericScatteringLUT);"));
            Assert.That(passSource, Does.Not.Contain("cmd.DispatchCompute("));

            Assert.That(managerSource, Does.Contain("private static readonly PhysicallyBasedSkyAtmosphereLutCache s_PhysicallyBasedSkyAtmosphereLutCache = new();"));
            Assert.That(managerSource, Does.Contain("s_PhysicallyBasedSkyAtmosphereLutCache.Build(resources);"));
            Assert.That(managerSource, Does.Contain("s_PhysicallyBasedSkyAtmosphereLutCache.Update(context, cmd);"));
            Assert.That(managerSource, Does.Contain("internal static void ImportSkyViewLut(RenderGraphTexture texture)"));
            Assert.That(managerSource, Does.Contain("internal static void ImportAtmosphericScatteringLut(RenderGraphTexture texture)"));
            Assert.That(managerSource, Does.Contain("internal static bool TryGetSkyViewLut(int skyViewHash, out Texture skyViewTexture)"));

            Assert.That(cacheSource, Does.Contain("internal sealed class PhysicallyBasedSkyAtmosphereLutCache : IDisposable"));
            Assert.That(cacheSource, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildForCamera(volume, context, out m_Parameters)"));
            Assert.That(cacheSource, Does.Contain("PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_ComputeShader, m_Parameters, m_MaterialParameters);"));
            Assert.That(cacheSource, Does.Contain("cmd.SetComputeTextureParam("));
            Assert.That(cacheSource, Does.Contain("m_CachedSkyViewTexture"));
            Assert.That(cacheSource, Does.Contain("m_CachedAtmosphericScatteringTexture"));
            Assert.That(cacheSource, Does.Contain("internal static int ComputeSkyViewLutHash("));
            Assert.That(cacheSource, Does.Contain("internal bool TryGetSkyViewLut(int skyViewHash, out Texture skyViewTexture)"));
        }

        [Test]
        public void Shader_Source_DeclaresRequiredKernelsAndAtmosphericScatteringOutputs()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "SkyLUTGenerator.compute"));

            Assert.That(source, Does.Contain("#pragma kernel MultiScatteringLUT"));
            Assert.That(source, Does.Contain("#pragma kernel SkyViewLUT"));
            Assert.That(source, Does.Contain("#pragma kernel AtmosphericScatteringLUTCamera"));
            Assert.That(source, Does.Contain("#pragma kernel AtmosphericScatteringLUTWorld"));
            Assert.That(source, Does.Contain("#pragma kernel AtmosphericScatteringBlur"));
            Assert.That(source, Does.Contain("#include \"CelestialBodyData.hlsl\""));
            Assert.That(source, Does.Contain("RW_TEXTURE2D(float3, _MultiScatteringLUT_RW);"));
            Assert.That(source, Does.Contain("RW_TEXTURE2D(float3, _SkyViewLUT_RW);"));
            Assert.That(source, Does.Contain("RW_TEXTURE3D(float3, _AtmosphericScatteringLUT_RW);"));
            Assert.That(source, Does.Contain("for (uint i = 0; i < _CelestialLightCount; i++)"));
            Assert.That(source, Does.Contain("CelestialBodyData light = _CelestialBodyDatas[i];"));
        }

        private static void AssertTexture(AtmosphereLUTPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
            Assert.That(texture.desc.EnableRandomWrite, Is.True);
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2D));
        }

        private static void AssertVolumeTexture(
            AtmosphereLUTPass pass,
            string fieldName,
            int expectedWidth,
            int expectedHeight,
            int expectedSlices)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
            Assert.That(texture.desc.Slices, Is.EqualTo(expectedSlices));
            Assert.That(texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(texture.desc.EnableRandomWrite, Is.True);
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        private static T GetFieldValue<T>(AtmosphereLUTPass pass, string fieldName)
        {
            var field = typeof(AtmosphereLUTPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

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
#pragma warning restore CS0618
