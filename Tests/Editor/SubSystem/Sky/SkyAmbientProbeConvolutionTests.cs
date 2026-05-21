using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkyAmbientProbeConvolutionTests
    {
        [Test]
        public void SkyAmbientProbeConvolution_UsesPersistentGpuBufferAndEditorDebugReadback()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Sky", "SkyAmbientProbeConvolution.cs"));

            Assert.That(source, Does.Contain("private const string DiffuseVolumetricKernelName = \"AmbientProbeConvolutionDiffuseVolumetric\";"));
            Assert.That(source, Does.Contain("private const string DiffuseKernelName = \"AmbientProbeConvolutionDiffuse\";"));
            Assert.That(source, Does.Contain("private const string LegacyKernelName = \"AmbientProbeConvolution\";"));
            Assert.That(source, Does.Contain("private const bool EnableAmbientProbeDebugReadback = true;"));
            Assert.That(source, Does.Contain("m_Kernel = FindKernel();"));
            Assert.That(source, Does.Contain("EnsureDefaultAmbientProbeBuffer();"));
            Assert.That(source, Does.Contain("if (m_ComputeShader.HasKernel(DiffuseVolumetricKernelName))"));
            Assert.That(source, Does.Contain("if (m_ComputeShader.HasKernel(DiffuseKernelName))"));
            Assert.That(source, Does.Contain("if (m_ComputeShader.HasKernel(LegacyKernelName))"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, DiffuseAmbientProbeOutputBufferId, m_AmbientProbeBuffer);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, VolumetricAmbientProbeOutputBufferId, m_VolumetricAmbientProbeBuffer);"));
            Assert.That(source, Does.Contain("cmd.SetComputeVectorParam(m_ComputeShader, FogParametersId, fogParameters);"));
            Assert.That(source, Does.Contain("cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, ScratchBufferId, m_AmbientProbeScratchBuffer);"));
            Assert.That(source, Does.Contain("internal void BindGlobalBuffer(CommandBuffer cmd, bool useDefault = false)"));
            Assert.That(source, Does.Contain("var activeBuffer = useDefault || m_AmbientProbeBuffer == null ? m_DefaultAmbientProbeBuffer : m_AmbientProbeBuffer;"));
            Assert.That(source, Does.Contain("var activeVolumetricBuffer = useDefault || m_VolumetricAmbientProbeBuffer == null"));
            Assert.That(source, Does.Contain("cmd.SetGlobalBuffer("));
            Assert.That(source, Does.Contain("VolumetricAmbientProbeBufferId"));
            Assert.That(source, Does.Contain("RequestDebugReadback(cmd, activeBuffer, useDefault);"));
            Assert.That(source, Does.Contain("SkyAmbientProbeConvolution.Convolve (MissingBuffer)"));
            Assert.That(source, Does.Contain("SkyAmbientProbeConvolution.Convolve (SkyChanged)"));
            Assert.That(source, Does.Contain("SkyAmbientProbeConvolution.Convolve (FrameRefresh)"));
            Assert.That(source, Does.Contain("rebuildReason = AmbientProbeConvolutionRebuildReason.FrameRefresh;"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, GetConvolutionSampler(rebuildReason)))"));
            Assert.That(source, Does.Contain("cmd.RequestAsyncReadback(activeBuffer, request => OnDebugReadbackCompleted(request, debugSkyHash));"));
            Assert.That(source, Does.Contain("private void OnDebugReadbackCompleted(AsyncGPUReadbackRequest request, int skyHash)"));
            Assert.That(source, Does.Contain("request.GetData<Vector4>()"));
            Assert.That(source, Does.Not.Contain("UploadProbe("));
            Assert.That(source, Does.Not.Contain("RenderSettings.ambientProbe"));
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
            Assert.That(source, Does.Contain("RWStructuredBuffer<float4> _VolumetricAmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<float4> _DiffuseAmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ScratchBuffer;"));
            Assert.That(source, Does.Contain("uniform float4 _FogParameters;"));
            Assert.That(source, Does.Contain("PackSHFromScratchBuffer(_DiffuseAmbientProbeOutputBuffer);"));
            Assert.That(source, Does.Contain("PackSHFromScratchBuffer(_VolumetricAmbientProbeOutputBuffer);"));
            Assert.That(source, Does.Contain("void KERNEL_NAME(uint dispatchThreadId : SV_DispatchThreadID)"));
            Assert.That(source, Does.Not.Contain("#define PLATFORM_SUPPORTS_WAVE_INTRINSICS"));
            Assert.That(source, Does.Not.Contain("#pragma use_dxc"));
            Assert.That(source, Does.Not.Contain("#pragma kernel SkySpecularPrefilter"));
        }

        [Test]
        public void VividRenderPipeline_InitializesHammersleyConstantBuffers()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs"));

            Assert.That(source, Does.Contain("Hammersley.Initialize();"),
                "Hammersley.Initialize() must be called in VividRenderPipeline constructor to populate " +
                "the constant buffers used by AmbientProbeConvolution.compute. Without it the compute " +
                "shader reads uninitialized GPU memory and produces garbage/-Infinity SH coefficients.");
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
            Assert.That(source, Does.Contain("var fogParameters = BuildVolumetricAmbientProbeFogParameters();"));
            Assert.That(source, Does.Contain("var ambientProbeHash = BuildVolumetricAmbientProbeHash(skyData?.ambientProbeHash ?? 0, fogParameters);"));
            Assert.That(source, Does.Contain("if (!useDefaultAmbientProbe)"));
            Assert.That(source, Does.Contain("private const bool ForceAmbientProbeConvolutionEveryFrame = true;"));
            Assert.That(source, Does.Contain("forceRebuild || ForceAmbientProbeConvolutionEveryFrame"));
            Assert.That(source, Does.Contain("forceRebuild || (skyData?.specularCubemapDirty == true)"));
            Assert.That(source, Does.Contain("skyData.specularCubemapDirty = false;"));
            Assert.That(source, Does.Not.Contain("ForceSpecularCubemapConvolutionEveryFrame"));
            Assert.That(source, Does.Contain("BuildVolumetricAmbientProbeFogParameters()"));
            Assert.That(source, Does.Contain("BuildVolumetricAmbientProbeHash(int ambientProbeHash, Vector4 fogParameters)"));
            Assert.That(source, Does.Contain("s_AmbientProbeConvolution.BindGlobalBuffer(cmd, useDefaultAmbientProbe);"));
        }

        [Test]
        public void SkyManager_HasValidSkyTexture_RequiresCreatedCubemapRenderTexture()
        {
            var method = typeof(SkyManager).GetMethod("HasValidSkyTexture", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var texture2D = new Texture2D(4, 4);
            var cubemap = new RenderTexture(16, 16, 0)
            {
                dimension = TextureDimension.Cube,
                volumeDepth = 6
            };

            try
            {
                Assert.That(InvokeHasValidSkyTexture(method, null), Is.False);
                Assert.That(InvokeHasValidSkyTexture(method, texture2D), Is.False);
                Assert.That(InvokeHasValidSkyTexture(method, cubemap), Is.False);

                cubemap.Create();

                Assert.That(InvokeHasValidSkyTexture(method, cubemap), Is.True);
            }
            finally
            {
                cubemap.Release();
                Object.DestroyImmediate(cubemap);
                Object.DestroyImmediate(texture2D);
            }
        }

        private static bool InvokeHasValidSkyTexture(MethodInfo method, Texture texture)
        {
            return (bool)method.Invoke(null, new object[] { texture });
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
                Path.Combine(projectRoot, "Packages", "Custom_URP")
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
