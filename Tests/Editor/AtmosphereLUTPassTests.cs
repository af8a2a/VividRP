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
    public class AtmosphereLUTPassTests
    {
        [Test]
        public void Initialize_RegistersSkyLutOutputsWithoutLegacyHistoryResources()
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
        public void AtmosphereLUTPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(AtmosphereLUTPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresFixedHdrpAlignedLutDimensionsAndRandomWrite()
        {
            var pass = new AtmosphereLUTPass();

            pass.Prepare(new ContextContainer());

            AssertTexture(pass, "m_MultiScatteringLUT", AtmosphereLUTPass.MultiScatteringWidth, AtmosphereLUTPass.MultiScatteringHeight);
            AssertTexture(pass, "m_SkyViewLUT", AtmosphereLUTPass.SkyViewWidth, AtmosphereLUTPass.SkyViewHeight);
            AssertVolumeTexture(
                pass,
                "m_AtmosphericScatteringLUT",
                AtmosphereLUTPass.AtmosphericScatteringWidth,
                AtmosphereLUTPass.AtmosphericScatteringHeight,
                AtmosphereLUTPass.AtmosphericScatteringDepth);
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
        public void Source_UsesSkyLutGeneratorAndHdrpStyleBindings()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("public sealed class AtmosphereLUTPass : ComputePass"));
            Assert.That(source, Does.Contain("m_ComputeShader = resources?.AtmosphereLUTCompute;"));
            Assert.That(source, Does.Contain("m_MultiScatteringKernel = FindKernel(MultiScatteringKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewKernel = FindKernel(SkyViewKernelName);"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringCameraKernel = FindKernel(AtmosphericScatteringCameraKernelName);"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringBlurKernel = FindKernel(AtmosphericScatteringBlurKernelName);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters)"));
            Assert.That(source, Does.Contain("var skyContext = new SkyRendererContext(cameraData, lightData);"));
            Assert.That(source, Does.Contain("m_CelestialBodyBuffer.Update(skyContext);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyComputeParameterBinder.Apply(cmd, m_ComputeShader, m_Parameters, m_MaterialParameters);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_MultiScatteringLUT, m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_SkyViewLUT, m_CachedSkyViewHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_AtmosphericScatteringLUT, m_CachedAtmosphericScatteringHandle);"));
            Assert.That(source, Does.Contain("ComputeMultiScatteringHash(m_MaterialParameters)"));
            Assert.That(source, Does.Contain("ComputeSkyViewHash("));
            Assert.That(source, Does.Contain("m_CelestialBodyBuffer.CelestialLightHash"));
            Assert.That(source, Does.Contain("internal static int ComputeSkyViewLutHash("));
            Assert.That(source, Does.Contain("in SkyRendererContext context)"));
            Assert.That(source, Does.Contain("var celestialLightHash = PhysicallyBasedSkyCelestialBodyUtility.ComputeCelestialLightHash(context);"));
            Assert.That(source, Does.Contain("internal static bool TryGetCachedSkyViewLut(int skyViewHash, out Texture skyViewTexture)"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, MultiScatteringLutRwId, m_MultiScatteringLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, MultiScatteringLutId, m_MultiScatteringLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewLutRwId, m_SkyViewLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_SkyViewKernel, CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);"));
            Assert.That(source, Does.Contain("PublishCachedSkyViewLut();"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam("));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringCameraKernel,"));
            Assert.That(source, Does.Contain("AtmosphericScatteringLutRwId,"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam("));
            Assert.That(source, Does.Contain("CelestialBodyDatasId,"));
            Assert.That(source, Does.Contain("m_CelestialBodyBuffer.Buffer);"));
            Assert.That(source, Does.Not.Contain("TransmittanceKernelName"));
            Assert.That(source, Does.Not.Contain("TransmittanceWidth"));
            Assert.That(source, Does.Not.Contain("m_CompatibilityTransmittanceTexture"));
            Assert.That(source, Does.Not.Contain("SkyViewLUTSelectHistoryLayer"));
            Assert.That(source, Does.Not.Contain("SkyViewLUTStoreHistory"));
            Assert.That(source, Does.Not.Contain("SkyViewHistory"));
        }

        [Test]
        public void Source_ProfilesRebuildsAndReleasesPersistentResources()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RenderAtmosphericScatteringLUT"));
            Assert.That(source, Does.Contain("ReleaseCachedLutResources();"));
            Assert.That(source, Does.Contain("UnpublishCachedSkyViewLut();"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedMultiScatteringTexture, ref m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedSkyViewTexture, ref m_CachedSkyViewHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedAtmosphericScatteringTexture, ref m_CachedAtmosphericScatteringHandle);"));
            Assert.That(source, Does.Not.Contain("ReleaseCompatibilityTransmittanceResource();"));
        }

        [Test]
        public void Source_RemovesCompatibilityTransmittanceFallback()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Not.Contain("m_CompatibilityTransmittanceTexture"));
            Assert.That(source, Does.Not.Contain("m_CompatibilityTransmittanceHandle"));
            Assert.That(source, Does.Not.Contain("TransmittanceWidth"));
            Assert.That(source, Does.Not.Contain("TransmittanceHeight"));
            Assert.That(source, Does.Not.Contain("TransmittanceLUT"));
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
            Assert.That(source, Does.Not.Contain("#pragma kernel TransmittanceLUT"));
            Assert.That(source, Does.Not.Contain("SkyViewLUTSelectHistoryLayer"));
            Assert.That(source, Does.Not.Contain("SkyViewLUTStoreHistory"));
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
