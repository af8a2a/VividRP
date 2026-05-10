using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionTrace").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionResolve").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionRayInfo").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionHDRPHitPoint").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionHDRPAccum").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionHDRPOutput").IsTransient, Is.False);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionHDRPOutput").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionDebug").IsTransient, Is.False);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionDebug").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("PreviousColorPyramid"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionSkyTexture"));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRTileList").IsTransient, Is.True);
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRDispatchIndirectArgs").IsTransient, Is.False);
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("source"));
        }

        [Test]
        public void ScreenSpaceReflectionPassNode_ExportsDebugTextureOutputPort_ForVisualization()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredScreenSpaceReflectionPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetOutputPortByName("m_DebugTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DebugTexture"), Is.Null);
                Assert.That(node.GetInputPortByName("m_DebugTexture_In"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_HDRPOutputTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_HDRPOutputTexture"), Is.Null);
                Assert.That(node.TryGetExecutionPath(out var executionPath), Is.True);
                Assert.That(executionPath, Is.EqualTo(ScreenSpaceReflectionExecutionPath.Vivid));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesScreenSpaceReflectionExecutionPathEnumParameter()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredScreenSpaceReflectionPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);
                var pass = result.Passes.Single(entry => entry.PassType.Contains(nameof(ScreenSpaceReflectionPass)));
                var parameter = pass.EnumParameters.Single(entry => entry.FieldName == "m_ExecutionPath");

                Assert.That(parameter.Value, Is.EqualTo((int)ScreenSpaceReflectionExecutionPath.Vivid));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_ExecutionPath_SelectsRequestedRuntimePath()
        {
            var pass = new ScreenSpaceReflectionPass();

            try
            {
                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.Vivid);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.HDRP);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.True);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.VividAndHDRPComparison);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Compile_BindsScreenSpaceReflectionDebugTexture_WhenOverlayDebugConsumesPort()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var ssrNode = new AutoRegisteredScreenSpaceReflectionPassNode();
                var overlayNode = new AutoRegisteredOverlayDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, ssrNode);
                RenderGraphTestUtility.AddTestNode(graph, overlayNode);

                Assert.That(graph.Connect(
                    ssrNode.GetOutputPortByName("m_DebugTexture"),
                    overlayNode.GetInputPortByName("m_DebugTexture")),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var overlayPass = result.Passes.Single(pass => pass.PassType.Contains(nameof(OverlayDebugPass)));
                var binding = overlayPass.ResourceBindings.Single(entry => entry.FieldName == "m_DebugTexture");

                Assert.That(binding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(binding.SourceFieldName, Is.EqualTo("m_DebugTexture"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_UsesStandardComputePassRecording()
        {
            Assert.That(typeof(ComputePass).IsAssignableFrom(typeof(ScreenSpaceReflectionPass)), Is.True);
            Assert.That(typeof(IRenderGraphRecordingPass).IsAssignableFrom(typeof(ScreenSpaceReflectionPass)), Is.False);
        }

        [Test]
        public void ScreenSpaceReflectionPass_PrepareRenderGraph_RefreshesPreviousColorPyramidAfterColorPyramidPrepare()
        {
            var pass = new ScreenSpaceReflectionPass();
            IRenderPass renderPass = pass;
            IRenderGraphPreparePass preparePass = pass;
            var resources = renderPass.Initialize();
            var previousEntry = resources.Textures.Single(entry => entry.Name == "PreviousColorPyramid");
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);
                Assert.That(GetPrivateField<bool>(pass, "m_UseHistoryColorPyramid"), Is.False);

                var previousColorPyramid = RenderGraphTexture.CreateInput(
                    "ColorPyramidHistory",
                    GraphicsFormat.R16G16B16A16_SFloat);
                previousColorPyramid.desc.Width = 1920;
                previousColorPyramid.desc.Height = 1080;
                previousColorPyramid.desc.UseMipMap = true;
                previousColorPyramid.desc.MipCount = 12;

                var colorPyramidData = frameData.GetOrCreate<VividColorPyramidData>();
                colorPyramidData.hasValidHistory = true;
                colorPyramidData.previousColorPyramid = previousColorPyramid;
                colorPyramidData.width = 1920;
                colorPyramidData.height = 1080;
                colorPyramidData.mipCount = 12;

                preparePass.PrepareRenderGraph(frameData);

                Assert.That(GetPrivateField<bool>(pass, "m_UseHistoryColorPyramid"), Is.True);
                Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
                Assert.That(PassResourceReferenceRefreshUtility.TryRefresh(pass, resources), Is.True);
                Assert.That(previousEntry.Texture, Is.SameAs(previousColorPyramid));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_PrepareRenderGraph_BindsPreviousColorPyramidEvenBeforeHistoryIsValid()
        {
            var pass = new ScreenSpaceReflectionPass();
            IRenderPass renderPass = pass;
            IRenderGraphPreparePass preparePass = pass;
            var resources = renderPass.Initialize();
            var previousEntry = resources.Textures.Single(entry => entry.Name == "PreviousColorPyramid");
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                pass.Prepare(frameData);

                var previousColorPyramid = RenderGraphTexture.CreateInput(
                    "ColorPyramidHistory",
                    GraphicsFormat.R16G16B16A16_SFloat);
                previousColorPyramid.desc.Width = 1920;
                previousColorPyramid.desc.Height = 1080;
                previousColorPyramid.desc.UseMipMap = true;
                previousColorPyramid.desc.MipCount = 12;

                var colorPyramidData = frameData.GetOrCreate<VividColorPyramidData>();
                colorPyramidData.hasValidHistory = false;
                colorPyramidData.previousColorPyramid = previousColorPyramid;
                colorPyramidData.width = 1920;
                colorPyramidData.height = 1080;
                colorPyramidData.mipCount = 12;

                preparePass.PrepareRenderGraph(frameData);

                Assert.That(GetPrivateField<bool>(pass, "m_UseHistoryColorPyramid"), Is.False);
                Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
                Assert.That(PassResourceReferenceRefreshUtility.TryRefresh(pass, resources), Is.True);
                Assert.That(previousEntry.Texture, Is.SameAs(previousColorPyramid));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_CalculatesRoundedUpDepthPyramidMipCount_ForNonPowerOfTwoCamera()
        {
            var method = typeof(ScreenSpaceReflectionPass).GetMethod("CalculateMipCount", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(method.Invoke(null, new object[] { 1920, 1080 }), Is.EqualTo(12));
            Assert.That(method.Invoke(null, new object[] { 1024, 1024 }), Is.EqualTo(11));
            Assert.That(method.Invoke(null, new object[] { 1, 1 }), Is.EqualTo(1));
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

                var rayInfoTexture = GetPrivateField<RenderGraphTexture>(pass, "m_RayInfoTexture");
                Assert.That(rayInfoTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(rayInfoTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(rayInfoTexture.desc.Name, Is.EqualTo("ScreenSpaceReflectionRayInfo"));
                Assert.That(rayInfoTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(rayInfoTexture.desc.EnableRandomWrite, Is.True);

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
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsHDRPTracing"));
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsHDRPReprojection"));
            Assert.That(source, Does.Contain("#pragma kernel ScreenSpaceReflectionsHDRPAccumulate"));
            Assert.That(source, Does.Contain("int _SsrWriteHDRPToOutput;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _SSRTileList;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _SSRDispatchIndirectArgs;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRResolveTexture;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRRayInfoTexture;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRHDRPHitPointTexture;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRHDRPAccumTexture;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _SSRHDRPOutputTexture;"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/DebugUtil.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/BlueNoise.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl\""));
            Assert.That(source, Does.Contain("float4x4 _SsrViewProjMatrix;"));
            Assert.That(source, Does.Contain("float4x4 _SsrInvViewProjMatrix;"));
            Assert.That(source, Does.Contain("float4x4 _SsrPrevViewProjMatrix;"));
            Assert.That(source, Does.Contain("int _SsrFrameIndex;"));
            Assert.That(source, Does.Contain("float3 ComputeSsrWorldSpacePosition(float2 screenUV, float deviceDepth)"));
            Assert.That(source, Does.Contain("float3 ComputeSsrNormalizedDeviceCoordinatesWithZ(float3 positionWS)"));
            Assert.That(source, Does.Contain("void StoreSsrWorldGridDebug(uint2 coordSS, float3 positionWS)"));
            Assert.That(source, Does.Contain("Debug_WorldGrid(positionWS, 1.0, 0.02)"));
            Assert.That(source, Does.Not.Contain("UNITY_MATRIX_I_VP"));
            Assert.That(source, Does.Not.Contain("UNITY_MATRIX_VP"));
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
            Assert.That(source, Does.Contain("float2 GetSsrBlueNoiseSample(uint2 coordSS)"));
            Assert.That(source, Does.Contain("GetBNDSequenceSample(coordSS, (uint)_SsrFrameIndex, 0)"));
            Assert.That(source, Does.Contain("bool SampleSsrGGXVNDF("));
            Assert.That(source, Does.Contain("SampleGGXVisibleNormal(inputSample.xy, viewDirWS, localToWorld, roughness, localV, localH, VdotH);"));
            Assert.That(source, Does.Contain("float3 SampleSsrReflectionDir("));
            Assert.That(source, Does.Contain("float traceSampleWeight;"));
            Assert.That(source, Does.Contain("float2 traceBlueNoiseSample = GetSsrBlueNoiseSample(coordSS);"));
            Assert.That(source, Does.Contain("float3 reflectionDirWS = SampleSsrReflectionDir(normalWS, viewDirWS, perceptualRoughness, traceBlueNoiseSample, traceSampleWeight);"));
            Assert.That(source, Does.Contain("if (traceSampleWeight <= 0.0)"));
            Assert.That(source, Does.Contain("float3 reflectionDirWS,"));
            Assert.That(source, Does.Contain("reflectionDirWS,"));
            Assert.That(source, Does.Contain("float2 blueNoiseSample = GetSsrBlueNoiseSample(coordSS);"));
            Assert.That(source, Does.Contain("float3 reflectionDirWS = SampleSsrReflectionDir(normalWS, viewDirWS, perceptualRoughness, blueNoiseSample, sampleWeight);"));
            Assert.That(source, Does.Contain("BuildSsrSkyFallback("));
            Assert.That(source, Does.Contain("float ComputeHistoryColorPyramidReliability(float2 currentScreenUV, float2 historyScreenUV)"));
            Assert.That(source, Does.Contain("pixelMotion * rcp(32.0)"));
            Assert.That(source, Does.Contain("bool TryComputeHistoryPyramidUV("));
            Assert.That(source, Does.Contain("ComputeNormalizedDeviceCoordinatesWithZ(positionWS, _SsrPrevViewProjMatrix)"));
            Assert.That(source, Does.Contain("bool insideHistoryDepth = previousNDC.z >= 0.0 && previousNDC.z <= 1.0;"));
            Assert.That(source, Does.Contain("historyReliability = insideHistory"));
            Assert.That(source, Does.Contain("historyScreenUV = saturate(historyScreenUV);"));
            Assert.That(source, Does.Contain("return insideHistoryDepth;"));
            Assert.That(source, Does.Contain("bool IsHitDepthConsistent(float tracedDeviceDepth, float sceneDeviceDepth)"));
            Assert.That(source, Does.Contain("bool TryLoadValidHitNormalWS(int2 hitCoordSS, float3 reflectionDirWS, out float3 hitNormalWS)"));
            Assert.That(source, Does.Contain("bool IsSameReceiverPlaneHit(float3 receiverNormalWS, float3 hitNormalWS, float3 receiverToHitWS, float hitDistance)"));
            Assert.That(source, Does.Contain("bool IsHitPositionConsistentWithReflectionRay(float3 reflectionDirWS, float3 receiverToHitWS, float hitDistance)"));
            Assert.That(source, Does.Contain("float ResolveDepthWeight(float centerDeviceDepth, float sampleDeviceDepth)"));
            Assert.That(source, Does.Contain("bool IsValidSsrRayInfo(float4 rayInfo)"));
            Assert.That(source, Does.Contain("float4 BuildSsrRayInfo(float hitDistance, float historyReliability, float deviceDepth, float contribution)"));
            Assert.That(source, Does.Contain("float ResolveRayDistanceWeight(float centerHitDistance, float sampleHitDistance)"));
            Assert.That(source, Does.Contain("float ResolveRadianceWeight(float3 centerRadiance, float3 sampleRadiance)"));
            Assert.That(source, Does.Contain("bool IsRawFarDepth(float deviceDepth)"));
            Assert.That(source, Does.Contain("LinearEyeDepth(tracedDeviceDepth, _ZBufferParams)"));
            Assert.That(source, Does.Contain("LinearEyeDepth(centerDeviceDepth, _ZBufferParams)"));
            Assert.That(source, Does.Contain("max(sceneEyeDepth, tracedEyeDepth) * 0.03f"));
            Assert.That(source, Does.Contain("max(centerEyeDepth, sampleEyeDepth) * 0.04f"));
            Assert.That(source, Does.Contain("float3 hitRayPos = rayOrigin;"));
            Assert.That(source, Does.Contain("hitRayPos = rayPos;"));
            Assert.That(source, Does.Not.Contain("hitRayPos = rayOrigin + distFloor * rayDir;"));
            Assert.That(source, Does.Contain("hitCoordSS = ClampPixelCoord((int2)floor(hitRayPos.xy));"));
            Assert.That(source, Does.Contain("hitDeviceDepth = hitRayPos.z;"));
            Assert.That(source, Does.Contain("if (!IsHitDepthConsistent(hitDeviceDepth, hitSceneDeviceDepth))"));
            Assert.That(source, Does.Contain("if (!TryLoadValidHitNormalWS(hitCoordSS, reflectionDirWS, hitNormalWS))"));
            Assert.That(source, Does.Contain("if (!IsHitPositionConsistentWithReflectionRay(reflectionDirWS, receiverToHitWS, hitDistance))"));
            Assert.That(source, Does.Contain("if (IsSameReceiverPlaneHit(normalWS, hitNormalWS, receiverToHitWS, hitDistance))"));
            Assert.That(source, Does.Contain("out bool hitSky"));
            Assert.That(source, Does.Contain("hitSky = (_SsrReflectsSky != 0) && reachedRayBounds && !rayTowardsEye;"));
            Assert.That(source, Does.Contain(": EmptySsrResult();"));
            Assert.That(source, Does.Contain("if (!TryComputeHistoryPyramidUV(hitCoordSS, hitSceneDeviceDepth, historyScreenUV, historyReliability))"));
            Assert.That(source, Does.Contain("float3 historyColor = SanitizeSsrRadiance(SampleReflectionColor(historyScreenUV, perceptualRoughness));"));
            Assert.That(source, Does.Contain("float3 reflectedColor = historyColor;"));
            Assert.That(source, Does.Contain("float contribution = saturate(edgeFade * roughnessFade * _SsrIntensity * historyReliability);"));
            Assert.That(source, Does.Contain("float edgeFade = lerp(hitEdgeFade, historyEdgeFade, historyReliability);"));
            Assert.That(source, Does.Contain("float hitDistance = length(hitPositionWS - positionWS);"));
            Assert.That(source, Does.Contain("_SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(hitDistance, historyReliability, hitSceneDeviceDepth, contribution);"));
            Assert.That(source, Does.Contain("_SSRRayInfoTexture[coordSS] = BuildSsrRayInfo(5000.0, 1.0, deviceDepth, skyReflection.a);"));
            Assert.That(source, Does.Contain("if (centerReflection.a <= 0.0001 || !IsValidSsrRayInfo(centerRayInfo))"));
            Assert.That(source, Does.Contain("sampleReflection.rgb = SanitizeSsrRadiance(sampleReflection.rgb);"));
            Assert.That(source, Does.Contain("float4 sampleRayInfo = _SSRRayInfoTexture[sampleCoord];"));
            Assert.That(source, Does.Contain("float normalWeight = pow(saturate(dot(centerNormalWS, sampleNormalWS)), 128.0);"));
            Assert.That(source, Does.Contain("float rayDistanceWeight = ResolveRayDistanceWeight(centerRayInfo.x, sampleRayInfo.x);"));
            Assert.That(source, Does.Contain("float radianceWeight = ResolveRadianceWeight(centerReflection.rgb, sampleReflection.rgb);"));
            Assert.That(source, Does.Contain("float reliabilityWeight = min(centerRayInfo.y, sampleRayInfo.y);"));
            Assert.That(source, Does.Contain("if (weightSum <= 0.0001)"));
            Assert.That(source, Does.Contain("WaveActiveAnyTrue(shouldTracePixel)"));
            Assert.That(source, Does.Contain("WaveIsFirstLane()"));
            Assert.That(source, Does.Contain("g_SSRClassifiedTileWaves[linearThreadIndex] = 0u;"));
            Assert.That(source, Does.Contain("g_SSRClassifiedTileWaves[linearThreadIndex] = waveHasTracePixel ? 1u : 0u;"));
            Assert.That(source, Does.Not.Contain("InterlockedOr(g_SSRClassifiedTile"));
            Assert.That(source, Does.Not.Contain("InterlockedOr("));
            Assert.That(source, Does.Contain("InterlockedAdd(_SSRDispatchIndirectArgs[0], 1u, tileOffset);"));
            Assert.That(source, Does.Contain("#define SSR_TRACE_BEHIND_OBJECTS"));
            Assert.That(source, Does.Not.Contain("#define SSR_TRACE_TOWARDS_EYE"));
            Assert.That(source, Does.Contain("#ifndef SSR_TRACE_TOWARDS_EYE"));
            Assert.That(source, Does.Contain("killRay = killRay || rayTowardsEye;"));
            Assert.That(source, Does.Contain("miss = belowMip0 && insideFloor;"));
            Assert.That(source, Does.Contain("_SSRTraceTexture[coordSS] = float4(reflectedColor * fresnel, contribution);"));
            Assert.That(source, Does.Contain("_SSRResolveTexture[coordSS] = float4(colorSum * rcpWeightSum, saturate(alphaSum * rcpWeightSum));"));
            Assert.That(source, Does.Contain("_OutputColorTexture[coordSS] = _SSRResolveTexture[coordSS];"));
            Assert.That(source, Does.Contain("bool TraceScreenSpaceReflectionHDRP("));
            Assert.That(source, Does.Contain("void ScreenSpaceReflectionsHDRPTracing("));
            Assert.That(source, Does.Contain("void ScreenSpaceReflectionsHDRPReprojection("));
            Assert.That(source, Does.Contain("void ScreenSpaceReflectionsHDRPAccumulate("));
            Assert.That(source, Does.Contain("_SSRHDRPHitPointTexture[coordSS] = float4(hitScreenUV, 1.0, 1.0);"));
            Assert.That(source, Does.Contain("_SSRHDRPAccumTexture[coordSS] = float4(color, 1.0) * opacity;"));
            Assert.That(source, Does.Contain("_SSRHDRPOutputTexture[coordSS] = _SSRHDRPAccumTexture[coordSS];"));
            Assert.That(source, Does.Contain("if (_SsrWriteHDRPToOutput != 0)"));
            Assert.That(source, Does.Contain("_OutputColorTexture[coordSS] = _SSRHDRPAccumTexture[coordSS];"));
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
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsHDRPTracing\")"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsHDRPReprojection\")"));
            Assert.That(source, Does.Contain("FindKernel(\"ScreenSpaceReflectionsHDRPAccumulate\")"));
            Assert.That(source, Does.Contain("DispatchHDRPComparison(cmd, context);"));
            Assert.That(source, Does.Contain("[SerializeField]"));
            Assert.That(source, Does.Contain("private ScreenSpaceReflectionExecutionPath m_ExecutionPath = ScreenSpaceReflectionExecutionPath.Vivid;"));
            Assert.That(source, Does.Contain("private bool ShouldRunVividPath()"));
            Assert.That(source, Does.Contain("private bool ShouldRunHDRPPath()"));
            Assert.That(source, Does.Contain("private bool ShouldUseHDRPAsMainOutput()"));
            Assert.That(source, Does.Contain("cmd.SetComputeIntParam(m_ComputeShader, SsrWriteHDRPToOutputId, ShouldUseHDRPAsMainOutput() ? 1 : 0);"));
            Assert.That(source, Does.Not.Contain("BlueNoise.Instance?.Bind(cmd);"));
            Assert.That(source, Does.Contain("ResetDispatchIndirectArgs(cmd);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(dispatchIndirectArgsBuffer, s_InitialDispatchIndirectArgsData);"));
            Assert.That(source, Does.Contain("public int SsrFrameIndex;"));
            Assert.That(source, Does.Contain("SsrFrameIndex = Time.frameCount,"));
            Assert.That(source, Does.Contain("public Matrix4x4 SsrViewProjMatrix;"));
            Assert.That(source, Does.Contain("public Matrix4x4 SsrInvViewProjMatrix;"));
            Assert.That(source, Does.Contain("public Matrix4x4 SsrPrevViewProjMatrix;"));
            Assert.That(source, Does.Contain("cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);"));
            Assert.That(source, Does.Contain("ResolveSsrPrevViewProjMatrix(cameraShaderData, viewProjMatrix);"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRClassifyTilesProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRTracingProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRResolveProfilingSampler))"));
            Assert.That(source, Does.Contain("using (new ProfilingScope(cmd, s_SSRAccumulateProfilingSampler))"));
            Assert.That(source, Does.Not.Contain("ScreenSpaceReflectionDenoise"));
            Assert.That(source, Does.Not.Contain("ClearScreenSpaceReflectionTiles"));
        }

        [Test]
        public void FrameContextSystem_SourceBindsBlueNoiseGlobals_DuringFrameSetup()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "FrameContext",
                "FrameContextSystem.cs"));

            Assert.That(source, Does.Contain("private static void SetShaderGlobals("));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(cmd, shaderVariablesGlobal, ShaderVariablesGlobal.ConstantBufferShaderId);"));
            Assert.That(source, Does.Contain("BlueNoise.Instance?.Bind(cmd);"));
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

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {instance.GetType().Name}");
            field.SetValue(instance, value);
        }

        private static bool InvokePrivateBool(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Method '{methodName}' not found on {instance.GetType().Name}");
            return (bool)method.Invoke(instance, null);
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

        private sealed class AutoRegisteredScreenSpaceReflectionPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(ScreenSpaceReflectionPass);

            internal bool TryGetExecutionPath(out ScreenSpaceReflectionExecutionPath value)
            {
                return TryGetEnumParameterValue("m_ExecutionPath", out value);
            }
        }

        private sealed class AutoRegisteredOverlayDebugPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(OverlayDebugPass);
        }
    }
}
