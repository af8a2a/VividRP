using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class HDRPHZBPassTests
    {
        [Test]
        public void Initialize_RegistersAtlasTextureAndOffsetBuffer()
        {
            IRenderPass renderPass = new HDRPHZBPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EqualTo(new[] { "Depth", "HZB" }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EqualTo(new[] { "HZBMipLevelOffsets" }));
        }

        [Test]
        public void Prepare_ConfiguresHDRPPackedAtlas_ForCameraSize()
        {
            var pass = new HDRPHZBPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var hzbTexture = GetTextureField(pass, "m_HzbTexture");
                Assert.That(hzbTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(hzbTexture.desc.Height, Is.EqualTo(1620));
                Assert.That(hzbTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
                Assert.That(hzbTexture.desc.EnableRandomWrite, Is.True);
                Assert.That(hzbTexture.desc.UseMipMap, Is.False);
                Assert.That(hzbTexture.desc.MipCount, Is.EqualTo(1));
                Assert.That(hzbTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));

                Assert.That(GetIntField(pass, "m_MipLevelCount"), Is.EqualTo(12));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Prepare_ComputesHDRPPackedMipOffsets()
        {
            var pass = new HDRPHZBPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var offsets = GetVector2IntArrayField(pass, "m_MipLevelOffsets");
                var offsetData = GetInt2ArrayField(pass, "m_MipLevelOffsetData");

                Assert.That(offsets[0], Is.EqualTo(new Vector2Int(0, 0)));
                Assert.That(offsets[1], Is.EqualTo(new Vector2Int(0, 1080)));
                Assert.That(offsets[2], Is.EqualTo(new Vector2Int(960, 1080)));
                Assert.That(offsets[3], Is.EqualTo(new Vector2Int(960, 1350)));
                Assert.That(offsetData[3], Is.EqualTo(new int2(960, 1350)));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_RegistersHDRPDepthPyramidOffsetInput()
        {
            IRenderPass renderPass = new ScreenSpaceReflectionPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("HZB"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("ScreenSpaceReflectionOutput"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Does.Contain("HZBMipLevelOffsets"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionTrace"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionResolve"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionSkyTexture"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Does.Not.Contain("SSRTileList"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Does.Not.Contain("SSRDispatchIndirectArgs"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("source"));
        }

        [Test]
        public void ScreenSpaceReflectionPass_Prepare_ConfiguresInternalTileResources()
        {
            var pass = new ScreenSpaceReflectionPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var traceTexture = GetPrivateField<RenderGraphTexture>(pass, "m_TraceTexture");
                Assert.That(traceTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(traceTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(traceTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(traceTexture.desc.EnableRandomWrite, Is.True);

                var resolveTexture = GetPrivateField<RenderGraphTexture>(pass, "m_ResolveTexture");
                Assert.That(resolveTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(resolveTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(resolveTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(resolveTexture.desc.EnableRandomWrite, Is.True);

                var tileListBuffer = GetPrivateField<RenderGraphBuffer>(pass, "m_TileListBuffer");
                Assert.That(tileListBuffer.desc.Name, Is.EqualTo("SSRTileList"));
                Assert.That(tileListBuffer.desc.Count, Is.EqualTo(240 * 135));
                Assert.That(tileListBuffer.desc.Stride, Is.EqualTo(sizeof(uint)));
                Assert.That(tileListBuffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));

                var dispatchIndirectArgsBuffer = GetPrivateField<RenderGraphBuffer>(pass, "m_DispatchIndirectArgsBuffer");
                Assert.That(dispatchIndirectArgsBuffer.desc.Name, Is.EqualTo("SSRDispatchIndirectArgs"));
                Assert.That(dispatchIndirectArgsBuffer.desc.Count, Is.EqualTo(4));
                Assert.That(dispatchIndirectArgsBuffer.desc.Stride, Is.EqualTo(sizeof(uint)));
                Assert.That(
                    dispatchIndirectArgsBuffer.desc.Target,
                    Is.EqualTo(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments));
                Assert.That(GetPrivateField<int>(pass, "m_TileCountX"), Is.EqualTo(240));
                Assert.That(GetPrivateField<int>(pass, "m_TileCountY"), Is.EqualTo(135));

                var skyTexture = GetPrivateField<RenderGraphTexture>(pass, "m_SkyTexture");
                Assert.That(skyTexture.desc.Name, Is.EqualTo("ScreenSpaceReflectionSkyTexture"));
                Assert.That(skyTexture.desc.Dimension, Is.EqualTo(TextureDimension.Cube));
                Assert.That(skyTexture.desc.FilterMode, Is.EqualTo(FilterMode.Trilinear));
                Assert.That(skyTexture.desc.UseMipMap, Is.True);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflectionCompute_OutputsReflectionContribution_ForDeferredBlend()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "ScreenSpaceReflection",
                "ScreenSpaceReflection.compute"));

            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsClassifyTiles"));
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsTracing"));
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsResolve"));
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsAccumulate"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _SSRTileList;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _SSRDispatchIndirectArgs;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRResolveTexture;"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl\""));
            Assert.That(source, Does.Contain("float _SsrIntensityClamp;"));
            Assert.That(source, Does.Contain("TEXTURECUBE(_SkyTexture);"));
            Assert.That(source, Does.Contain("float4 _SkyTextureTint;"));
            Assert.That(source, Does.Contain("float4 _SkyTextureParams;"));
            Assert.That(source, Does.Contain("float3 SampleSsrSkyFallback(float3 directionWS, float perceptualRoughness)"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, rotatedDirectionWS, skyMipLevel).rgb;"));
            Assert.That(source, Does.Contain("float3 exposedSkyRadiance = VividApplyPreExposure"));
            Assert.That(source, Does.Contain("float3 exposedSkyHsv = RgbToHsv(exposedSkyRadiance);"));
            Assert.That(source, Does.Contain("exposedSkyHsv.z = clamp(exposedSkyHsv.z, 0.0, _SsrIntensityClamp);"));
            Assert.That(source, Does.Contain("return ClampToFloat16Max(HsvToRgb(exposedSkyHsv));"));
            Assert.That(source, Does.Contain("_SSRTraceTexture[coordSS] = BuildSsrSkyFallback("));
            Assert.That(source, Does.Contain("bool TryComputeHistoryPyramidUV(int2 coordSS, float deviceDepth, out float2 historyScreenUV)"));
            Assert.That(source, Does.Contain("ComputeNormalizedDeviceCoordinatesWithZ(positionWS, _PrevViewProjMatrix)"));
            Assert.That(source, Does.Contain("bool insideHistoryDepth = previousNDC.z >= 0.0 && previousNDC.z <= 1.0;"));
            Assert.That(source, Does.Contain("bool IsHitDepthConsistent(float tracedDeviceDepth, float sceneDeviceDepth)"));
            Assert.That(source, Does.Contain("if (!IsHitDepthConsistent(hitDeviceDepth, hitSceneDeviceDepth))"));
            Assert.That(source, Does.Contain("if (!TryComputeHistoryPyramidUV(hitCoordSS, hitSceneDeviceDepth, historyScreenUV))"));
            Assert.That(source, Does.Contain("float3 reflectedColor = SampleReflectionColor(historyScreenUV, perceptualRoughness);"));
            Assert.That(source, Does.Contain("WaveActiveAnyTrue(shouldTracePixel)"));
            Assert.That(source, Does.Contain("WaveIsFirstLane()"));
            Assert.That(source, Does.Contain("g_SSRClassifiedTileWaves[linearThreadIndex] = 0u;"));
            Assert.That(source, Does.Contain("g_SSRClassifiedTileWaves[linearThreadIndex] = waveHasTracePixel ? 1u : 0u;"));
            Assert.That(source, Does.Not.Contain("InterlockedOr(g_SSRClassifiedTile"));
            Assert.That(source, Does.Not.Contain("InterlockedOr("));
            Assert.That(source, Does.Contain("InterlockedAdd(_SSRDispatchIndirectArgs[0], 1u, tileOffset);"));
            Assert.That(source, Does.Contain("#define SSR_TRACE_BEHIND_OBJECTS"));
            Assert.That(source, Does.Contain("#define SSR_TRACE_TOWARDS_EYE"));
            Assert.That(source, Does.Contain("#ifndef SSR_TRACE_TOWARDS_EYE"));
            Assert.That(source, Does.Contain("miss = belowMip0 && insideFloor;"));
            Assert.That(source, Does.Contain("_SSRTraceTexture[coordSS] = float4(reflectedColor * fresnel, contribution);"));
            Assert.That(source, Does.Contain("_SSRResolveTexture[coordSS] = float4(colorSum * rcpWeightSum, saturate(alphaSum * rcpWeightSum));"));
            Assert.That(source, Does.Contain("_OutputColorTexture[coordSS] = _SSRResolveTexture[coordSS];"));
            Assert.That(source, Does.Not.Contain("ClearScreenSpaceReflectionTiles"));
            Assert.That(source, Does.Not.Contain("sourceColor.rgb + reflectedColor"));
            Assert.That(source, Does.Not.Contain("_InputColorTexture"));
            Assert.That(source, Does.Contain("_OutputColorTexture[coordSS] = float4(0.0, 0.0, 0.0, 0.0);"));
        }

        [Test]
        public void ScreenSpaceReflectionPass_SourceUsesOriginalProfileScopesAndKernelNames()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "ScreenSpaceReflection",
                "ScreenSpaceReflectionPass.cs"));

            Assert.That(source, Does.Contain("private const string RenderSSRProfilerTag = \"RenderSSR\";"));
            Assert.That(source, Does.Contain("private const string SSRClassifyTilesProfilerTag = \"SSRClassifyTiles\";"));
            Assert.That(source, Does.Contain("private const string SSRTracingProfilerTag = \"SSRTracing\";"));
            Assert.That(source, Does.Contain("private const string SSRResolveProfilerTag = \"SSRResolve\";"));
            Assert.That(source, Does.Contain("private const string SSRAccumulateProfilerTag = \"SSRAccumulate\";"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsClassifyTiles\")"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsTracing\")"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsResolve\")"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsAccumulate\")"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRClassifyTilesProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRTracingProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRResolveProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRAccumulateProfilingSampler))"));
            Assert.That(source, Does.Not.Contain("ScreenSpaceReflectionDenoise"));
            Assert.That(source, Does.Not.Contain("ClearScreenSpaceReflectionTiles"));
        }

        [Test]
        public void HDRPHZBCompute_ClipsDownsampleDispatchToMipSize()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "DownSample",
                "HDRPHZB.compute"));

            Assert.That(source, Does.Contain("_DstOffsetAndSize"));
            Assert.That(source, Does.Contain("dispatchThreadId.x >= dstSize.x"));
            Assert.That(source, Does.Contain("dispatchThreadId.y >= dstSize.y"));
        }

        private static RenderGraphTexture GetTextureField(HDRPHZBPass pass, string fieldName)
        {
            var field = typeof(HDRPHZBPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on HDRPHZBPass");
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static int GetIntField(HDRPHZBPass pass, string fieldName)
        {
            var field = typeof(HDRPHZBPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on HDRPHZBPass");
            return (int)field.GetValue(pass);
        }

        private static Vector2Int[] GetVector2IntArrayField(HDRPHZBPass pass, string fieldName)
        {
            var field = typeof(HDRPHZBPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on HDRPHZBPass");
            return (Vector2Int[])field.GetValue(pass);
        }

        private static int2[] GetInt2ArrayField(HDRPHZBPass pass, string fieldName)
        {
            var field = typeof(HDRPHZBPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on HDRPHZBPass");
            return (int2[])field.GetValue(pass);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {instance.GetType().Name}");
            return (T)field.GetValue(instance);
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            var customPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "Custom_URP"));
            if (Directory.Exists(customPath))
                return Path.Combine(customPath, Path.Combine(parts));

            var vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            var legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}
