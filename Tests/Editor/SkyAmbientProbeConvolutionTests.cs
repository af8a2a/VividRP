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

            Assert.That(source, Does.Contain("private const string DiffuseKernelName = \"AmbientProbeConvolutionDiffuse\";"));
            Assert.That(source, Does.Contain("private const string LegacyKernelName = \"AmbientProbeConvolution\";"));
            Assert.That(source, Does.Contain("m_Kernel = FindKernel();"));
            Assert.That(source, Does.Contain("EnsureDefaultAmbientProbeBuffer();"));
            Assert.That(source, Does.Contain("if (m_ComputeShader.HasKernel(DiffuseKernelName))"));
            Assert.That(source, Does.Contain("if (m_ComputeShader.HasKernel(LegacyKernelName))"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, DiffuseAmbientProbeOutputBufferId, m_AmbientProbeBuffer);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, ScratchBufferId, m_AmbientProbeScratchBuffer);"));
            Assert.That(source, Does.Contain("internal void BindGlobalBuffer(CommandBuffer cmd, bool useDefault = false)"));
            Assert.That(source, Does.Contain("useDefault || m_AmbientProbeBuffer == null ? m_DefaultAmbientProbeBuffer : m_AmbientProbeBuffer"));
            Assert.That(source, Does.Contain("SkyAmbientProbeConvolution.Convolve (MissingBuffer)"));
            Assert.That(source, Does.Contain("SkyAmbientProbeConvolution.Convolve (SkyChanged)"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, GetConvolutionSampler(rebuildReason)))"));
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

            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolutionDiffuse"));
            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolutionVolumetric"));
            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolutionDiffuseVolumetric"));
            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolutionClouds"));
            Assert.That(source, Does.Contain("TEXTURECUBE(_AmbientProbeInputCubemap);"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<float> _AmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<float4> _DiffuseAmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ScratchBuffer;"));
            Assert.That(source, Does.Contain("uniform float4 _FogParameters;"));
            Assert.That(source, Does.Contain("PackSHFromScratchBuffer(_DiffuseAmbientProbeOutputBuffer);"));
            Assert.That(source, Does.Contain("void KERNEL_NAME(uint dispatchThreadId : SV_DispatchThreadID)"));
            Assert.That(source, Does.Not.Contain("#pragma kernel SkySpecularPrefilter"));
        }

        [Test]
        public void BakedGI_SamplesPackedAmbientProbeFromStructuredBuffer()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "BakedGI.hlsl"));

            Assert.That(source, Does.Contain("StructuredBuffer<float4> _VividAmbientProbeData;"));
            Assert.That(source, Does.Contain("float3 VividSampleAmbientProbe(float3 normalWS)"));
            Assert.That(source, Does.Contain("return SampleSH9(_VividAmbientProbeData, normalizedNormalWS);"));
        }

        [Test]
        public void SkyManager_BindsDefaultAmbientProbeBuffer_WhenSkyProbeIsMissing()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyManager.cs"));

            Assert.That(source, Does.Contain("var useDefaultAmbientProbe = skyData == null || skyData.ambientProbeCubemap == null;"));
            Assert.That(source, Does.Contain("if (!useDefaultAmbientProbe)"));
            Assert.That(source, Does.Contain("s_AmbientProbeConvolution.BindGlobalBuffer(cmd, useDefaultAmbientProbe);"));
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
