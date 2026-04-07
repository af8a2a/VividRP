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
        public void Initialize_RegistersThreeWritableLuts()
        {
            IRenderPass renderPass = new AtmosphereLUTPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "MultiScatteringLUT",
                "SkyViewLUT",
                "TransmittanceLUT"
            }));
            Assert.That(textureEntries.Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Write }));
        }

        [Test]
        public void AtmosphereLUTPass_InheritsFromComputePass()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(AtmosphereLUTPass)), Is.True);
        }

        [Test]
        public void Prepare_ConfiguresFixedLutDimensionsAndRandomWrite()
        {
            var pass = new AtmosphereLUTPass();

            pass.Prepare(new ContextContainer());

            AssertTexture(pass, "m_TransmittanceLUT", AtmosphereLUTPass.TransmittanceWidth, AtmosphereLUTPass.TransmittanceHeight);
            AssertTexture(pass, "m_MultiScatteringLUT", AtmosphereLUTPass.MultiScatteringWidth, AtmosphereLUTPass.MultiScatteringHeight);
            AssertTexture(pass, "m_SkyViewLUT", AtmosphereLUTPass.SkyViewWidth, AtmosphereLUTPass.SkyViewHeight);
        }

        [Test]
        public void VividRPCoreResources_DeclaresAtmosphereLUTCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.AtmosphereLUTCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/AtmosphereLUT.compute"));
        }

        [Test]
        public void Source_UsesCommonSkyParametersAndDispatchesAllKernels()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("public sealed class AtmosphereLUTPass : ComputePass"));
            Assert.That(source, Does.Contain("m_ComputeShader = resources?.AtmosphereLUTCompute;"));
            Assert.That(source, Does.Contain("m_TransmittanceKernel = m_ComputeShader.FindKernel(TransmittanceKernelName);"));
            Assert.That(source, Does.Contain("m_MultiScatteringKernel = m_ComputeShader.FindKernel(MultiScatteringKernelName);"));
            Assert.That(source, Does.Contain("m_SkyViewKernel = m_ComputeShader.FindKernel(SkyViewKernelName);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_TransmittanceLUT, m_CachedTransmittanceHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_MultiScatteringLUT, m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture(m_SkyViewLUT, m_CachedSkyViewHandle);"));
            Assert.That(source, Does.Contain("ComputeTransmittanceHash(m_Parameters)"));
            Assert.That(source, Does.Contain("ComputeMultiScatteringHash(m_Parameters, transmittanceHash)"));
            Assert.That(source, Does.Contain("ComputeSkyViewHash(m_Parameters, multiScatteringHash)"));
            Assert.That(source, Does.Contain("ResolveRebuildReason(m_TransmittanceCacheRecreated, m_CachedTransmittanceHash, transmittanceHash)"));
            Assert.That(source, Does.Contain("ResolveRebuildReason(m_MultiScatteringCacheRecreated, m_CachedMultiScatteringHash, multiScatteringHash)"));
            Assert.That(source, Does.Contain("ResolveRebuildReason(m_SkyViewCacheRecreated, m_CachedSkyViewHash, skyViewHash)"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_TransmittanceKernel, TransmittanceLutOutputId, m_TransmittanceLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);"));
            Assert.That(source, Does.Contain("cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewLutOutputId, m_SkyViewLUT.innerHandle);"));
        }

        [Test]
        public void Source_ProfilesRebuildReasonsAndReleasesPersistentResources()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AtmosphereLUTPass.cs"));

            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildTransmittance (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildTransmittance (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildMultiScattering (ParametersChanged)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (MissingTexture)"));
            Assert.That(source, Does.Contain("AtmosphereLUTPass.RebuildSkyView (ParametersChanged)"));
            Assert.That(source, Does.Contain("ReleaseCachedLutResources();"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedTransmittanceTexture, ref m_CachedTransmittanceHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedMultiScatteringTexture, ref m_CachedMultiScatteringHandle);"));
            Assert.That(source, Does.Contain("ReleaseLutResource(ref m_CachedSkyViewTexture, ref m_CachedSkyViewHandle);"));
        }

        [Test]
        public void Shader_Source_DeclaresRequiredKernelsAndSkyViewEvaluation()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphereLUT.compute"));

            Assert.That(source, Does.Contain("#pragma kernel TransmittanceLUT"));
            Assert.That(source, Does.Contain("#pragma kernel MultiScatteringLUT"));
            Assert.That(source, Does.Contain("#pragma kernel SkyViewLUT"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _TransmittanceLUTOutput;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _MultiScatteringLUTOutput;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SkyViewLUTOutput;"));
            Assert.That(source, Does.Contain("SampleTransmittanceLut"));
            Assert.That(source, Does.Contain("SampleMultiScatteringLut"));
            Assert.That(source, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(source, Does.Not.Contain("float3 sunDiskTransmittance = SampleTransmittanceLut(cameraHeight, sunAtCamera, planetRadius, atmosphereRadius);"));
            Assert.That(source, Does.Contain("EvaluateSkyView"));
        }

        private static void AssertTexture(AtmosphereLUTPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
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
