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

#pragma warning disable CS0618
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
        public void ScreenSpaceReflectionPass_RegistersMippedHzbInputWithoutOffsetBuffer()
        {
            IRenderPass renderPass = new ScreenSpaceReflectionPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("HZB"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Contain("ScreenSpaceReflectionOutput"));
            Assert.That(resources.Textures.Single(entry => entry.Name == "MotionVectors").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Does.Not.Contain("HZBMipLevelOffsets"));
            Assert.That(resources.AccelerationStructures.Single(entry => entry.Name == "SceneRTAS").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionTrace").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionResolve").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionRayInfo").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionResolveAccum").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionAvgRadiance").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ReBlurLightingDistance").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ReBlurLightingDistanceIntermediate").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ReBlurMipChain").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ReBlurAccumulation").IsTransient, Is.True);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionHDRPHitPoint").IsTransient, Is.True);
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionHDRPAccum"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionHDRPOutput"));
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionDebug").IsTransient, Is.False);
            Assert.That(resources.Textures.Single(entry => entry.Name == "ScreenSpaceReflectionDebug").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurLightingDistanceHistory"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurLightingDistanceHistoryTexture"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurAccumulationHistory"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurAccumulationHistoryTexture"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurStabilizationHistory"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ReBlurStabilizationHistoryTexture"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionHDRPAccumPrev"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionHDRPAccumTexture"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionAccumPrev"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionAccumTexture"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionPrevNumFramesAccum"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionNumFramesAccum"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("PreviousColorPyramid"));
            Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("ScreenSpaceReflectionSkyTexture"));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRTileList").IsTransient, Is.False);
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRDispatchIndirectArgs").IsTransient, Is.False);
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRHybridCandidateBuffer").IsTransient, Is.True);
            Assert.That(resources.Buffers.Single(entry => entry.Name == "SSRHybridDispatchIndirectArgs").IsTransient, Is.True);
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
                Assert.That(node.GetOutputPortByName("m_TileListBuffer_Out"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_TileListBuffer_In"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_DispatchIndirectArgsBuffer_Out"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DispatchIndirectArgsBuffer_In"), Is.Null);
                Assert.That(node.TryGetExecutionPath(out _), Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_DoesNotIncludeScreenSpaceReflectionExecutionPathEnumParameter()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredScreenSpaceReflectionPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                var result = RenderGraphCompiler.Compile(graph);
                var pass = result.Passes.Single(entry => entry.PassType.Contains(nameof(ScreenSpaceReflectionPass)));

                Assert.That(pass.EnumParameters.Select(entry => entry.FieldName), Does.Not.Contain("m_ExecutionPath"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_BindsScreenSpaceReflectionTileBuffers_WhenTileDebugConsumesPorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var ssrNode = new AutoRegisteredScreenSpaceReflectionPassNode();
                var tileDebugNode = new AutoRegisteredTileDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, ssrNode);
                RenderGraphTestUtility.AddTestNode(graph, tileDebugNode);

                Assert.That(graph.Connect(
                    ssrNode.GetOutputPortByName("m_TileListBuffer_Out"),
                    tileDebugNode.GetInputPortByName("m_TileIndices")),
                    Is.True);
                Assert.That(graph.Connect(
                    ssrNode.GetOutputPortByName("m_DispatchIndirectArgsBuffer_Out"),
                    tileDebugNode.GetInputPortByName("m_IndirectArgs")),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var tileDebugPass = result.Passes.Single(pass => pass.PassType.Contains(nameof(TileDebugPass)));
                var tileIndicesBinding = tileDebugPass.ResourceBindings.Single(entry => entry.FieldName == "m_TileIndices");
                var indirectArgsBinding = tileDebugPass.ResourceBindings.Single(entry => entry.FieldName == "m_IndirectArgs");

                Assert.That(tileIndicesBinding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(tileIndicesBinding.SourceFieldName, Is.EqualTo("m_TileListBuffer"));
                Assert.That(indirectArgsBinding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(indirectArgsBinding.SourceFieldName, Is.EqualTo("m_DispatchIndirectArgsBuffer"));
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
                Assert.That(InvokePrivateBool(pass, "ShouldRunHybridPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunRayTracingPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.HDRP);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHybridPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunRayTracingPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.True);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.VividAndHDRPComparison);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHybridPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunRayTracingPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.Hybrid);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHybridPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunRayTracingPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);

                SetPrivateField(pass, "m_ExecutionPath", ScreenSpaceReflectionExecutionPath.RayTracing);
                Assert.That(InvokePrivateBool(pass, "ShouldRunVividPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHybridPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldRunRayTracingPath"), Is.True);
                Assert.That(InvokePrivateBool(pass, "ShouldRunHDRPPath"), Is.False);
                Assert.That(InvokePrivateBool(pass, "ShouldUseHDRPAsMainOutput"), Is.False);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflection_ExecutionPath_DefaultsToVivid()
        {
            var component = ScriptableObject.CreateInstance<ScreenSpaceReflection>();

            try
            {
                Assert.That(component.executionPath.value, Is.EqualTo(ScreenSpaceReflectionExecutionPath.Vivid));
                Assert.That(component.debugMode.value, Is.EqualTo(ScreenSpaceReflectionDebugMode.WorldPosition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void ScreenSpaceReflectionSettingsResolver_Resolve_ReturnsExecutionPath_WhenComponentActive()
        {
            var cameraObject = new GameObject("SSR Volume Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ssr = profile.Add<ScreenSpaceReflection>(true);

            ssr.enabled.value = true;
            ssr.executionPath.value = ScreenSpaceReflectionExecutionPath.Hybrid;
            ssr.debugMode.value = ScreenSpaceReflectionDebugMode.HitDelta;
            ssr.reBlurDenoiserRadius.value = 0.25f;
            ssr.reBlurAntiFlickeringStrength.value = 0.75f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = ScreenSpaceReflectionSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.executionPath, Is.EqualTo(ScreenSpaceReflectionExecutionPath.Hybrid));
                Assert.That(settings.debugMode, Is.EqualTo(ScreenSpaceReflectionDebugMode.HitDelta));
                Assert.That(settings.reBlurDenoiserRadius, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(settings.reBlurAntiFlickeringStrength, Is.EqualTo(0.75f).Within(0.0001f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_Prepare_UsesExecutionPathFromVolume()
        {
            var cameraObject = new GameObject("SSR Prepare Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ssr = profile.Add<ScreenSpaceReflection>(true);
            var pass = new ScreenSpaceReflectionPass();
            var frameData = new ContextContainer();

            ssr.enabled.value = true;
            ssr.executionPath.value = ScreenSpaceReflectionExecutionPath.Hybrid;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                pass.Prepare(frameData);

                Assert.That(
                    GetPrivateField<ScreenSpaceReflectionExecutionPath>(pass, "m_ExecutionPath"),
                    Is.EqualTo(ScreenSpaceReflectionExecutionPath.Hybrid));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                pass.Dispose();
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_Create_LoadsHybridCandidateKernelAndRayTracingShader()
        {
            var pass = new ScreenSpaceReflectionPass();

            try
            {
                pass.Create();

                Assert.That(GetPrivateField<ComputeShader>(pass, "m_ComputeShader"), Is.Not.Null);
                Assert.That(GetPrivateField<int>(pass, "m_SSRHybridCandidatesKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_SSRRayTracingTemporalKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_SSRRayTracingDenoiseHKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_SSRRayTracingDenoiseVKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurPreBlurKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurTemporalAccumulationKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurMipGenerationKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurHistoryFixKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurBlurKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurCopyHistoryAccumulationKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurCopyHistoryKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurTemporalStabilizationKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<int>(pass, "m_ReBlurPostBlurKernel"), Is.GreaterThanOrEqualTo(0));
                Assert.That(GetPrivateField<RayTracingShader>(pass, "m_HybridTraceRayTracingShader"), Is.Not.Null);
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
        public void ScreenSpaceReflectionPass_PrepareRenderGraph_KeepsInvalidColorPyramidHistoryDisabled()
        {
            var pass = new ScreenSpaceReflectionPass();
            IRenderGraphPreparePass preparePass = pass;
            var resources = ((IRenderPass)pass).Initialize();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            var colorPyramidData = frameData.GetOrCreate<VividColorPyramidData>();
            colorPyramidData.hasValidHistory = false;
            colorPyramidData.width = 1920;
            colorPyramidData.height = 1080;
            colorPyramidData.mipCount = 12;

            try
            {
                pass.Prepare(frameData);
                preparePass.PrepareRenderGraph(frameData);

                Assert.That(GetPrivateField<bool>(pass, "m_UseHistoryColorPyramid"), Is.False);
                Assert.That(pass.IsPassResourceLayoutDirty, Is.False);
                Assert.That(resources.Textures.Select(entry => entry.Name), Does.Not.Contain("PreviousColorPyramid"));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ScreenSpaceReflectionPass_CalculatesTextureMipCount_ForNonPowerOfTwoCamera()
        {
            var method = typeof(ScreenSpaceReflectionPass).GetMethod("CalculateMipCount", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(method.Invoke(null, new object[] { 1920, 1080 }), Is.EqualTo(11));
            Assert.That(method.Invoke(null, new object[] { 1024, 1024 }), Is.EqualTo(11));
            Assert.That(method.Invoke(null, new object[] { 1, 1 }), Is.EqualTo(1));
        }

        [Test]
        public void ScreenSpaceReflectionPass_ResolvesDepthPyramidMaxMip_FromHzbDescriptor()
        {
            var hzbTexture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(8192, 8192, GraphicsFormat.R16_SFloat)
            };

            hzbTexture.desc.UseMipMap = true;
            hzbTexture.desc.MipCount = 4;
            Assert.That(
                InvokePrivateStatic<int>(
                    typeof(ScreenSpaceReflectionPass),
                    "ResolveDepthPyramidMaxMip",
                    hzbTexture,
                    8192,
                    8192),
                Is.EqualTo(3));

            hzbTexture.desc.MipCount = 0;
            Assert.That(
                InvokePrivateStatic<int>(
                    typeof(ScreenSpaceReflectionPass),
                    "ResolveDepthPyramidMaxMip",
                    hzbTexture,
                    8192,
                    8192),
                Is.EqualTo(12));

            Assert.That(
                InvokePrivateStatic<int>(
                    typeof(ScreenSpaceReflectionPass),
                    "ResolveDepthPyramidMaxMip",
                    hzbTexture,
                    1920,
                    1080),
                Is.EqualTo(10));

            hzbTexture.desc.UseMipMap = false;
            hzbTexture.desc.MipCount = 1;
            Assert.That(
                InvokePrivateStatic<int>(
                    typeof(ScreenSpaceReflectionPass),
                    "ResolveDepthPyramidMaxMip",
                    hzbTexture,
                    8192,
                    8192),
                Is.EqualTo(0));
        }

        [Test]
        public void ScreenSpaceReflectionPass_UsesFrameContextViewProjectionMatrices_WhenAvailable()
        {
            var cameraData = new VividCameraData();
            var viewProj = Matrix4x4.TRS(new Vector3(1.0f, 2.0f, 3.0f), Quaternion.Euler(10.0f, 20.0f, 30.0f), new Vector3(2.0f, 3.0f, 4.0f));
            var invViewProj = viewProj.inverse;
            var cameraPosition = new Vector4(5.0f, 6.0f, 7.0f, 1.0f);
            cameraData.shaderVariablesGlobal = new ShaderVariablesGlobal
            {
                _VividViewProjMatrix = viewProj,
                _VividInvViewProjMatrix = invViewProj,
                _VividWorldSpaceCameraPos = cameraPosition
            };
            cameraData.hasShaderVariablesGlobal = true;

            var resolvedViewProj = InvokePrivateStatic<Matrix4x4>(
                typeof(ScreenSpaceReflectionPass),
                "ResolveSsrViewProjMatrix",
                cameraData);
            var resolvedInvViewProj = InvokePrivateStatic<Matrix4x4>(
                typeof(ScreenSpaceReflectionPass),
                "ResolveSsrInvViewProjMatrix",
                cameraData,
                Matrix4x4.identity);
            var resolvedCameraPosition = InvokePrivateStatic<Vector4>(
                typeof(ScreenSpaceReflectionPass),
                "ResolveSsrWorldSpaceCameraPos",
                cameraData);

            Assert.That(MaxAbsDiff(resolvedViewProj, viewProj), Is.LessThan(0.0001f));
            Assert.That(MaxAbsDiff(resolvedInvViewProj, invViewProj), Is.LessThan(0.0001f));
            Assert.That(resolvedCameraPosition, Is.EqualTo(cameraPosition));
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

                var hzbTexture = GetPrivateField<RenderGraphTexture>(pass, "m_HZBTexture");
                Assert.That(hzbTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(hzbTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(hzbTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
                Assert.That(hzbTexture.desc.UseMipMap, Is.True);
                Assert.That(hzbTexture.desc.AutoGenerateMips, Is.False);
                Assert.That(hzbTexture.desc.MipCount, Is.EqualTo(11));
                Assert.That(hzbTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));

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

                var resolveAccumTexture = GetPrivateField<RenderGraphTexture>(pass, "m_ResolveAccumTexture");
                Assert.That(resolveAccumTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(resolveAccumTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(resolveAccumTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(resolveAccumTexture.desc.EnableRandomWrite, Is.True);

                var avgRadianceTexture = GetPrivateField<RenderGraphTexture>(pass, "m_AvgRadianceTexture");
                Assert.That(avgRadianceTexture.desc.Width, Is.EqualTo(240));
                Assert.That(avgRadianceTexture.desc.Height, Is.EqualTo(135));
                Assert.That(avgRadianceTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(avgRadianceTexture.desc.EnableRandomWrite, Is.True);

                var hdrpHitPointTexture = GetPrivateField<RenderGraphTexture>(pass, "m_HDRPHitPointTexture");
                Assert.That(hdrpHitPointTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(hdrpHitPointTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(hdrpHitPointTexture.desc.Name, Is.EqualTo("ScreenSpaceReflectionHDRPHitPoint"));
                Assert.That(hdrpHitPointTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_UNorm));
                Assert.That(hdrpHitPointTexture.desc.EnableRandomWrite, Is.True);

                var accumulationHistory = GetPrivateField<RenderGraphTexture>(pass, "m_AccumulationHistoryCurrent");
                Assert.That(accumulationHistory.desc.Width, Is.EqualTo(1920));
                Assert.That(accumulationHistory.desc.Height, Is.EqualTo(1080));
                Assert.That(accumulationHistory.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(accumulationHistory.desc.EnableRandomWrite, Is.True);

                var frameCountHistory = GetPrivateField<RenderGraphTexture>(pass, "m_NumFramesHistoryCurrent");
                Assert.That(frameCountHistory.desc.Width, Is.EqualTo(1920));
                Assert.That(frameCountHistory.desc.Height, Is.EqualTo(1080));
                Assert.That(frameCountHistory.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
                Assert.That(frameCountHistory.desc.EnableRandomWrite, Is.True);

                var reBlurMipTexture = GetPrivateField<RenderGraphTexture>(pass, "m_ReBlurMipTexture");
                Assert.That(reBlurMipTexture.desc.Name, Is.EqualTo("ReBlurMipChain"));
                Assert.That(reBlurMipTexture.desc.Width, Is.EqualTo(1920));
                Assert.That(reBlurMipTexture.desc.Height, Is.EqualTo(1080));
                Assert.That(reBlurMipTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                Assert.That(reBlurMipTexture.desc.UseMipMap, Is.True);
                Assert.That(reBlurMipTexture.desc.AutoGenerateMips, Is.False);
                Assert.That(reBlurMipTexture.desc.MipCount, Is.EqualTo(4));
                Assert.That(reBlurMipTexture.desc.EnableRandomWrite, Is.True);

                var reBlurAccumulation = GetPrivateField<RenderGraphTexture>(pass, "m_ReBlurAccumulationTexture");
                Assert.That(reBlurAccumulation.desc.Name, Is.EqualTo("ReBlurAccumulation"));
                Assert.That(reBlurAccumulation.desc.Width, Is.EqualTo(1920));
                Assert.That(reBlurAccumulation.desc.Height, Is.EqualTo(1080));
                Assert.That(reBlurAccumulation.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8_UInt));
                Assert.That(reBlurAccumulation.desc.FilterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(reBlurAccumulation.desc.EnableRandomWrite, Is.True);

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

                var hybridCandidateBuffer = GetPrivateField<RenderGraphBuffer>(pass, "m_HybridCandidateBuffer");
                Assert.That(hybridCandidateBuffer.desc.Name, Is.EqualTo("SSRHybridCandidateBuffer"));
                Assert.That(hybridCandidateBuffer.desc.Count, Is.EqualTo(1920 * 1080));
                Assert.That(hybridCandidateBuffer.desc.Stride, Is.EqualTo(sizeof(uint)));
                Assert.That(hybridCandidateBuffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));

                var hybridDispatchIndirectArgsBuffer = GetPrivateField<RenderGraphBuffer>(pass, "m_HybridDispatchIndirectArgsBuffer");
                Assert.That(hybridDispatchIndirectArgsBuffer.desc.Name, Is.EqualTo("SSRHybridDispatchIndirectArgs"));
                Assert.That(hybridDispatchIndirectArgsBuffer.desc.Count, Is.EqualTo(3));
                Assert.That(hybridDispatchIndirectArgsBuffer.desc.Stride, Is.EqualTo(sizeof(uint)));
                Assert.That(
                    hybridDispatchIndirectArgsBuffer.desc.Target,
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

        private static T InvokePrivateStatic<T>(System.Type type, string methodName, params object[] args)
        {
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Method '{methodName}' not found on {type.Name}");
            return (T)method.Invoke(null, args);
        }

        private static float MaxAbsDiff(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            var max = 0.0f;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                    max = Mathf.Max(max, Mathf.Abs(lhs[row, column] - rhs[row, column]));
            }

            return max;
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

        private sealed class AutoRegisteredTileDebugPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(TileDebugPass);
        }
    }
}
#pragma warning restore CS0618
