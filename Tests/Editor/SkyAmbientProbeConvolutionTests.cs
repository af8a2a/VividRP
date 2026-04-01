using System.IO;
using System.Reflection;
using NUnit.Framework;
using VividRP.Runtime;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class SkyAmbientProbeConvolutionTests
    {
        [Test]
        public void SkyAmbientProbeConvolution_UsesPersistentGpuBufferWithoutAsyncReadback()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyAmbientProbeConvolution.cs"));

            Assert.That(source, Does.Contain("internal void BindGlobalBuffer(CommandBuffer cmd, bool useDefault = false)"));
            Assert.That(source, Does.Contain("useDefault || m_AmbientProbeBuffer == null ? m_DefaultAmbientProbeBuffer : m_AmbientProbeBuffer"));
            Assert.That(source, Does.Not.Contain("RequestAsyncReadback"));
            Assert.That(source, Does.Not.Contain("AsyncGPUReadbackRequest"));
            Assert.That(source, Does.Not.Contain("supportsAsyncGPUReadback"));
            Assert.That(source, Does.Not.Contain("UploadProbe("));
            Assert.That(source, Does.Not.Contain("RenderSettings.ambientProbe"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresSkyAmbientProbeConvolutionCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.SkyAmbientProbeConvolutionCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/Sky/AmbientProbeConvolution.compute"));
        }

        [Test]
        public void AmbientProbeConvolutionCompute_DeclaresKernelAndPackedGpuOutputBuffer()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "AmbientProbeConvolution.compute"));

            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolution"));
            Assert.That(source, Does.Contain("#pragma kernel SkySpecularPrefilter"));
            Assert.That(source, Does.Contain("TEXTURECUBE(_AmbientProbeInputCubemap);"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<float4> _AmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("TEXTURECUBE(_SkySpecularSourceCubemap);"));
            Assert.That(source, Does.Contain("RWTexture2DArray<float4> _SkySpecularMipOutput;"));
            Assert.That(source, Does.Contain("float4 _SkyConvolutionTint;"));
            Assert.That(source, Does.Contain("float4 _SkyConvolutionParams;"));
            Assert.That(source, Does.Contain("PackSH(_AmbientProbeOutputBuffer, outputSHCoeffs);"));
            Assert.That(source, Does.Contain("float3 IntegrateSkySpecularGGX"));
            Assert.That(source, Does.Contain("void SkySpecularPrefilter(uint3 tid : SV_DispatchThreadID)"));
        }

        [Test]
        public void BakedGI_SamplesPackedAmbientProbeFromStructuredBuffer()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "BakedGI.hlsl"));

            Assert.That(source, Does.Contain("StructuredBuffer<float4> _VividAmbientProbeData;"));
            Assert.That(source, Does.Contain("float3 VividSampleAmbientProbe(float3 normalWS)"));
            Assert.That(source, Does.Contain("return SampleSH9(_VividAmbientProbeData, normalizedNormalWS);"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
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
