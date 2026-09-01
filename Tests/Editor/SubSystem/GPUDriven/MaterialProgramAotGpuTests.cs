using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    [Parallelizable(ParallelScope.None)]
    public sealed class MaterialProgramAotGpuTests
    {
        private const string TemporaryFolder = "Assets/VividMaterialProgramAotGpuTests";
        private const string CoverageIncludePath =
            TemporaryFolder + "/VividMaterialCoverageAOT.generated.hlsl";
        private const string SurfaceIncludePath =
            TemporaryFolder + "/VividMaterialSurfaceAOT.generated.hlsl";
        private const string CatalogAssetPath =
            TemporaryFolder + "/VividMaterialProgramCatalog.asset";
        private const string CapabilityShaderPath =
            TemporaryFolder + "/MaterialProgramAotGpuCapability.shader";
        private const string ShaderPath =
            TemporaryFolder + "/MaterialProgramAotGpuTest.shader";
        private const string VisibilityInputShaderPath =
            TemporaryFolder + "/VisibilityDeferredPixelLoopInput.shader";
        private const string VisibilityShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/VisibilityBufferPass.shader";
        private const string ResolveShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/VisibilityBufferGBufferResolve.shader";
        private const string ClassificationComputePath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/MaterialClassification.compute";
        private const string DeferredLitComputePath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/DeferredLit.compute";
        private const uint GenericProofProgramIndex =
            (uint) MaterialProgramContract.BuiltinProgramCount;

        [TearDown]
        public void TearDown()
        {
            RenderTexture.active = null;
            AssetDatabase.DeleteAsset(TemporaryFolder);
        }

        [Test]
        public void FrozenCatalog_NonBuiltinCoverageAndSurface_DispatchEndToEndOnGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for the Material Program AOT GPU validation.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Assert.Ignore(
                    "The Material Program AOT GPU validation requires Direct3D 12, DXC, and Shader Model 6.6.");
            }
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                Assert.Ignore(
                    "The Material Program AOT GPU validation requires RGBA32F render and readback formats.");
            }

            EnsureTemporaryFolder();
            RequireShaderModel66();
            CompiledMaterialProgram customProgram =
                GPUDrivenMaterialCompiler.GetMaterialProgram(
                    (VividMaterialProgramID) GenericProofProgramIndex);
            CompiledMaterialProgram builtinProgram =
                GPUDrivenMaterialCompiler.GetMaterialProgram(
                    VividMaterialProgramID.StandardSingleSlab);
            Assert.That(
                customProgram.CoverageHlsl.EntryPoint,
                Is.Not.EqualTo(builtinProgram.CoverageHlsl.EntryPoint));
            Assert.That(
                customProgram.SurfaceHlsl.EntryPoint,
                Is.Not.EqualTo(builtinProgram.SurfaceHlsl.EntryPoint));
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.Reserved("P0.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.Reserved("P1.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.Reserved("P2.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P3.NonBuiltinCoverageAndSurfaceGpuTest",
                    customProgram));
            var customProgramID =
                (VividMaterialProgramID) GenericProofProgramIndex;
            MaterialProgramCatalog.ManifestEntry catalogEntry =
                catalog.GetEntry(customProgramID);
            Assert.That(catalogEntry.ProgramID, Is.EqualTo(customProgramID));

            MaterialProgramCatalogBaker.Bake(
                catalog,
                CatalogAssetPath,
                SurfaceIncludePath,
                CoverageIncludePath);
            File.WriteAllText(ShaderPath, BuildTestShaderSource());
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The generated non-builtin Coverage/Surface dispatcher shader failed to compile.");

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            GraphicsBuffer runtimeHeaderBuffer = null;
            GraphicsBuffer programBuffer = null;
            GraphicsBuffer materialParameterBuffer = null;
            GraphicsBuffer materialResourceBuffer = null;
            var target = new RenderTexture(
                3,
                1,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            var readback = new Texture2D(
                3,
                1,
                TextureFormat.RGBAFloat,
                mipChain: false,
                linear: true);
            var commandBuffer = new CommandBuffer
            {
                name = "Vivid Material Program AOT GPU Test",
            };
            try
            {
                VividMaterialRuntimeHeader[] runtimeHeaders =
                {
                    new VividMaterialRuntimeHeader
                    {
                        ProgramID = customProgramID,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                        Flags = VividMaterialRuntimeFlags.AlphaClip,
                    },
                };
                VividMaterialProgramData[] runtimePrograms =
                    catalog.CreateRuntimeProgramTable();
                var materialParameters = new VividMaterialData
                {
                    AlbedoColor = new float4(0.8f, 0.6f, 0.4f, 0.8f),
                    TextureTilingOffset = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                    Emission = new float4(0.1f, 0.2f, 0.3f, 0.0f),
                    MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                    AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                    NormalsStrength = 1.0f,
                    Roughness = 0.25f,
                    Metallic = 0.6f,
                    AlphaClipThreshold = 0.3f,
                };
                var surfaceBinding = new VividSurfaceBindingData
                {
                    BaseColorResource = VividSurfaceBindingData.InvalidResource,
                    NormalResource = VividSurfaceBindingData.InvalidResource,
                    MaskResource = VividSurfaceBindingData.InvalidResource,
                    Flags = VividSurfaceBindingFlags.None,
                    UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                };
                uint4[] parameterLanes = CreateGenericParameterLanes(
                    customProgramID,
                    materialParameters);
                VividMaterialResourceData[] materialResources =
                {
                    CreateMaterialResource(materialParameters, surfaceBinding),
                };

                runtimeHeaderBuffer = CreateStructuredBuffer(runtimeHeaders);
                programBuffer = CreateStructuredBuffer(runtimePrograms);
                materialParameterBuffer = CreateStructuredBuffer(parameterLanes);
                materialResourceBuffer = CreateStructuredBuffer(materialResources);
                material.SetBuffer("_MaterialRuntimeHeaders", runtimeHeaderBuffer);
                material.SetInt("_MaterialRuntimeHeaderCount", runtimeHeaders.Length);
                material.SetBuffer("_MaterialPrograms", programBuffer);
                material.SetInt("_MaterialProgramCount", runtimePrograms.Length);
                material.SetBuffer("_MaterialParameterData", materialParameterBuffer);
                material.SetInt("_MaterialParameterDataCount", parameterLanes.Length);
                material.SetBuffer("_MaterialResourceData", materialResourceBuffer);
                material.SetInt("_MaterialResourceDataCount", materialResources.Length);

                target.Create();
                commandBuffer.SetRenderTarget(target);
                commandBuffer.ClearRenderTarget(
                    clearDepth: false,
                    clearColor: true,
                    backgroundColor: Color.black);
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    material,
                    shaderPass: 0,
                    topology: MeshTopology.Triangles,
                    vertexCount: 3,
                    instanceCount: 1);
                Graphics.ExecuteCommandBuffer(commandBuffer);

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 3, 1), 0, 0);
                readback.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                AssertColor(
                    readback.GetPixel(0, 0),
                    new Color(0.4f, 0.3f, 1.0f, 1.0f),
                    "Coverage, alpha threshold, and dispatcher success");
                AssertColor(
                    readback.GetPixel(1, 0),
                    new Color(0.4f, 0.15f, 0.3f, 0.75f),
                    "Surface base color and transformed roughness");
                AssertColor(
                    readback.GetPixel(2, 0),
                    new Color(0.3f, 0.15f, 0.3f, 0.45f),
                    "Surface transformed metallic and emission");
            }
            finally
            {
                commandBuffer.Dispose();
                runtimeHeaderBuffer?.Dispose();
                programBuffer?.Dispose();
                materialParameterBuffer?.Dispose();
                materialResourceBuffer?.Dispose();
                RenderTexture.active = null;
                target.Release();
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ProductionRenderGraph_VisibilityResolveClassifyAndLight_EndToEndOnGpu()
        {
            const int width = 8;
            const int height = 8;
            const int variantCount = 4;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for the production RenderGraph pixel-loop validation.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Assert.Ignore(
                    "The production RenderGraph pixel-loop validation requires Direct3D 12, DXC, and Shader Model 6.6.");
            }
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                Assert.Ignore(
                    "The production RenderGraph pixel-loop validation requires RGBA32F readback support.");
            }

            EnsureTemporaryFolder();
            RequireShaderModel66();
            AssertUsableShader(
                AssetDatabase.LoadAssetAtPath<Shader>(VisibilityShaderPath),
                "production Visibility Buffer");
            AssertUsableShader(
                AssetDatabase.LoadAssetAtPath<Shader>(ResolveShaderPath),
                "production Visibility Buffer GBuffer Resolve");

            VividRPCoreResources resources =
                PipelineResourceManager.Get<VividRPCoreResources>();
            AssertUsableComputeShader(
                resources.MaterialClassificationCompute,
                "production Material Classification resource");
            AssertUsableComputeShader(
                resources.DeferredLitCompute,
                "production Deferred Lit resource");

            var buffers = new List<GraphicsBuffer>();
            var objects = new List<Object>();
            CommandBuffer commandBuffer = null;
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph = null;
            RenderGraphData graphAsset = null;
            RTHandle gbuffer0CaptureHandle = null;
            RTHandle lightingCaptureHandle = null;
            RTHandle debugCaptureHandle = null;
            bool frameCommitted = false;
            int previousEnableProbeVolumes = Shader.GetGlobalInt(
                "_EnableProbeVolumes");

            GraphicsBuffer TrackBuffer(GraphicsBuffer buffer)
            {
                buffers.Add(buffer);
                return buffer;
            }

            T TrackObject<T>(T value)
                where T : Object
            {
                objects.Add(value);
                return value;
            }

            try
            {
                PassRecorder.Dispose();
                VividGPUDrivenSystem.Shutdown();

                VividMaterialProgramData[] runtimePrograms =
                    GPUDrivenMaterialCompiler.CreateRuntimeProgramTable();
                VividMaterialRuntimeHeader[] runtimeHeaders =
                {
                    new()
                    {
                        ProgramID = VividMaterialProgramID.StandardSingleSlab,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                        Flags = VividMaterialRuntimeFlags.None,
                    },
                };
                VividMaterialData[] materialData =
                {
                    CreatePixelLoopMaterialData(
                        new float4(1.0f),
                        new float3(0.125f, 0.25f, 0.5f),
                        metallic: 0.0f),
                };
                VividSurfaceBindingData[] surfaceBindings =
                {
                    CreateUnboundSurfaceBinding(),
                };
                uint4[] materialParameterLanes = CreateGenericParameterLanes(
                    VividMaterialProgramID.StandardSingleSlab,
                    materialData[0]);
                VividMaterialResourceData[] materialResources =
                {
                    CreateMaterialResource(materialData[0], surfaceBindings[0]),
                };
                VividInstanceData[] instances =
                {
                    CreatePixelLoopInstance(0u),
                };
                var meshlet = new VividMeshlet
                {
                    VertexOffset = 0u,
                    TriangleOffset = 0u,
                    BoundingSphere = new float4(0.0f, 0.0f, 0.0f, 4.0f),
                };
                meshlet.VertexCount = 3u;
                meshlet.TriangleCount = 1u;
                VividMeshletVertex[] vertices =
                {
                    VividMeshletVertexPacking.Pack(
                        new float3(-1.0f, -1.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(0.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new float3(3.0f, -1.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(2.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new float3(-1.0f, 3.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(0.0f, 2.0f)),
                };

                GraphicsBuffer instanceBuffer = TrackBuffer(
                    CreateStructuredBuffer(instances));
                GraphicsBuffer materialBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialData));
                GraphicsBuffer dualSlabMaterialBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividDualSlabMaterialData[1]));
                GraphicsBuffer materialParameterBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialParameterLanes));
                GraphicsBuffer materialResourceBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialResources));
                GraphicsBuffer runtimeHeaderBuffer = TrackBuffer(
                    CreateStructuredBuffer(runtimeHeaders));
                GraphicsBuffer programBuffer = TrackBuffer(
                    CreateStructuredBuffer(runtimePrograms));
                GraphicsBuffer surfaceBindingBuffer = TrackBuffer(
                    CreateStructuredBuffer(surfaceBindings));
                GraphicsBuffer terrainMaterialBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividTerrainMaterialData[1]));
                GraphicsBuffer terrainLayerBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividTerrainLayerGPUData[1]));
                GraphicsBuffer meshLodNodeBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividMeshLODNode[1]));
                GraphicsBuffer meshletBuffer = TrackBuffer(
                    CreateStructuredBuffer(new[] { meshlet }));
                GraphicsBuffer vertexBuffer = TrackBuffer(
                    CreateStructuredBuffer(vertices));
                GraphicsBuffer indexBuffer = TrackBuffer(CreateRawBuffer(
                    new[] { 0x00020100u }));
                GraphicsBuffer visibleRequests = TrackBuffer(
                    CreateStructuredBuffer(new[]
                    {
                        new VividMeshletRenderRequestPacked
                        {
                            InstanceID_LOD = 0u,
                            MeshletID = 0u,
                        },
                    }));
                GraphicsBuffer indirectDrawArgs = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Raw
                            | GraphicsBuffer.Target.IndirectArguments,
                        (int) VividRendererListID.Count * 4,
                        sizeof(uint)));
                var drawArgs = new uint[(int) VividRendererListID.Count * 4];
                int cullOffArgsOffset = (int) VividRendererListID.CullOff * 4;
                drawArgs[cullOffArgsOffset] = 3u;
                drawArgs[cullOffArgsOffset + 1] = 1u;
                indirectDrawArgs.SetData(drawArgs);
                GraphicsBuffer preExposureBuffer = TrackBuffer(
                    CreateStructuredBuffer(new[]
                    {
                        new float4(1.0f, 0.0f, 0.0f, 0.0f),
                    }));
                GraphicsBuffer ambientProbeBuffer = TrackBuffer(
                    CreateStructuredBuffer(new float4[7]));

                RenderTexture gbuffer0Capture = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    "RenderGraphGBuffer0Capture"));
                RenderTexture lightingCapture = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "RenderGraphLightingCapture",
                    enableRandomWrite: true));
                RenderTexture debugCapture = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "RenderGraphLightingDebugCapture",
                    enableRandomWrite: true));
                gbuffer0CaptureHandle = RTHandles.Alloc(gbuffer0Capture);
                lightingCaptureHandle = RTHandles.Alloc(lightingCapture);
                debugCaptureHandle = RTHandles.Alloc(debugCapture);

                graphAsset = CreateProductionPixelLoopRenderGraph();
                var cameraObject = TrackObject(new GameObject(
                    "Vivid Production RenderGraph Pixel Loop Camera"));
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = lightingCapture;
                camera.transform.position = new Vector3(0.0f, 0.0f, -1.0f);
                camera.transform.rotation = Quaternion.identity;
                camera.orthographic = true;
                camera.orthographicSize = 1.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10.0f;

                commandBuffer = new CommandBuffer
                {
                    name = "Vivid Production RenderGraph Pixel Loop GPU Test",
                };
                renderGraph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph(
                    "Vivid Production RenderGraph Pixel Loop GPU Test");

                PassRecorder.InitializeContext(
                    default,
                    camera,
                    default,
                    graphAsset,
                    pipelineFrameIndex: 1);
                PassRecorder.PrepareFrame(graphAsset, commandBuffer);
                Graphics.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();

                IReadOnlyList<IRenderPass> compiledPasses =
                    GetCompiledRenderPasses();
                Assert.That(compiledPasses, Has.Count.EqualTo(4));
                Assert.That(compiledPasses[0], Is.TypeOf<VisibilityBufferPass>());
                Assert.That(
                    compiledPasses[1],
                    Is.TypeOf<VisibilityBufferGBufferResolvePass>());
                Assert.That(
                    compiledPasses[2],
                    Is.TypeOf<MaterialClassificationPass>());
                Assert.That(compiledPasses[3], Is.TypeOf<DeferredLightingPass>());
                PassRecorder.GetFrameData().GetOrCreate<VividSkyData>().Reset();
                PassRecorder.GetFrameData()
                    .GetOrCreate<VividScreenSpaceReflectionData>()
                    .Reset();
                PassRecorder.GetFrameData()
                    .GetOrCreate<VividClusteredLightingData>()
                    .Reset();
                PassRecorder.ImportTexture(
                    GetPrivateField<RenderGraphTexture>(
                        compiledPasses[1],
                        "m_GBuffer0"),
                    gbuffer0CaptureHandle);
                PassRecorder.ImportTexture(
                    GetPrivateField<RenderGraphTexture>(
                        compiledPasses[3],
                        "m_ColorTexture"),
                    lightingCaptureHandle);
                PassRecorder.ImportTexture(
                    GetPrivateField<RenderGraphTexture>(
                        compiledPasses[3],
                        "m_DebugTexture"),
                    debugCaptureHandle);

                PassRecorder.SetGPUDrivenFrameData(
                    visibleRequests,
                    indirectDrawArgs);
                BindProductionPixelLoopGlobals(
                    commandBuffer,
                    instanceBuffer,
                    materialBuffer,
                    dualSlabMaterialBuffer,
                    materialParameterBuffer,
                    materialResourceBuffer,
                    runtimeHeaderBuffer,
                    programBuffer,
                    surfaceBindingBuffer,
                    terrainMaterialBuffer,
                    terrainLayerBuffer,
                    meshLodNodeBuffer,
                    meshletBuffer,
                    vertexBuffer,
                    indexBuffer,
                    preExposureBuffer,
                    ambientProbeBuffer,
                    runtimePrograms.Length,
                    materialParameterLanes.Length,
                    materialResources.Length);
                Graphics.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();

                var renderGraphParameters = new RenderGraphParameters
                {
                    commandBuffer = commandBuffer,
                    currentFrameIndex = 1,
                    executionId = default,
                    invalidContextForTesting = true,
                };
                bool recorded = VividRenderPipeline.TryRecordAndExecuteRenderGraph(
                    renderGraph,
                    renderGraphParameters,
                    () => PassRecorder.RecordRenderGraph(
                        renderGraph,
                        default,
                        graphAsset,
                        enableAsyncCompute: false),
                    PassRecorder.AbortFrame);
                Assert.That(recorded, Is.True, "The production RenderGraph failed to record.");

                Graphics.ExecuteCommandBuffer(commandBuffer);
                PassRecorder.CommitFrame();
                frameCommitted = true;

                var classificationPass =
                    (MaterialClassificationPass) compiledPasses[2];
                GraphicsBuffer featureFlagsBuffer = GetPrivateField<GraphicsBuffer>(
                    classificationPass,
                    "m_MaterialTileFeatureFlagsBuffer");
                GraphicsBuffer tileListBuffer = GetPrivateField<GraphicsBuffer>(
                    classificationPass,
                    "m_MaterialFeatureTileListBuffer");
                GraphicsBuffer indirectArgsBuffer = GetPrivateField<GraphicsBuffer>(
                    classificationPass,
                    "m_MaterialFeatureIndirectArgsBuffer");
                var featureFlags = new uint[1];
                var tileList = new uint[variantCount];
                var indirectArgs = new uint[variantCount * 4];
                featureFlagsBuffer.GetData(featureFlags);
                tileListBuffer.GetData(tileList);
                indirectArgsBuffer.GetData(indirectArgs);
                Assert.That(featureFlags, Is.EqualTo(new uint[] { 1u }));
                Assert.That(tileList[0], Is.EqualTo(0u));
                Assert.That(
                    indirectArgs,
                    Is.EqualTo(new uint[]
                    {
                        1u, 1u, 1u, 0u,
                        0u, 1u, 1u, 0u,
                        0u, 1u, 1u, 0u,
                        0u, 1u, 1u, 0u,
                    }));

                Texture2D gbuffer0Readback = TrackObject(ReadRenderTexture(
                    gbuffer0Capture,
                    TextureFormat.RGBAFloat));
                Texture2D lightingReadback = TrackObject(ReadRenderTexture(
                    lightingCapture,
                    TextureFormat.RGBAFloat));
                Texture2D debugReadback = TrackObject(ReadRenderTexture(
                    debugCapture,
                    TextureFormat.RGBAFloat));
                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(4, 4),
                    0xC2,
                    "Production Visibility and Resolve must emit the StandardLit Deferred Export header.");
                AssertColor(
                    lightingReadback.GetPixel(4, 4),
                    new Color(0.125f, 0.25f, 0.5f, 1.0f),
                    "Production Deferred must preserve emission after classifying the lit tile.");
                AssertColor(
                    debugReadback.GetPixel(4, 4),
                    new Color(0.0f, 0.0f, 0.0f, 1.0f),
                    "Production Deferred FastSlab dispatch must shade the classified tile.");
            }
            finally
            {
                if (!frameCommitted)
                    PassRecorder.AbortFrame();
                PassRecorder.Dispose();
                ClearProductionPixelLoopGlobals();
                Shader.SetGlobalInt(
                    "_EnableProbeVolumes",
                    previousEnableProbeVolumes);
                VividGPUDrivenSystem.Shutdown();
                renderGraph?.Cleanup();
                commandBuffer?.Dispose();
                gbuffer0CaptureHandle?.Release();
                lightingCaptureHandle?.Release();
                debugCaptureHandle?.Release();
                foreach (GraphicsBuffer buffer in buffers)
                    buffer?.Dispose();
                RenderTexture.active = null;
                foreach (Object value in objects)
                {
                    if (value is RenderTexture renderTexture
                        && renderTexture.IsCreated())
                    {
                        renderTexture.Release();
                    }
                    Object.DestroyImmediate(value);
                }
                if (graphAsset != null)
                    Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void ProductionShaders_SyntheticVisibility_ResolveClassifyAndLightGpuContract()
        {
            const int width = 40;
            const int height = 8;
            const int tileCount = 5;
            const int variantCount = 4;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for the deferred pixel-loop validation.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Assert.Ignore(
                    "The deferred pixel-loop validation requires Direct3D 12, DXC, and Shader Model 6.6.");
            }
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                Assert.Ignore(
                    "The deferred pixel-loop validation requires RGBA32F readback support.");
            }

            EnsureTemporaryFolder();
            RequireShaderModel66();
            File.WriteAllText(
                VisibilityInputShaderPath,
                BuildVisibilityDeferredPixelLoopInputShaderSource());
            AssetDatabase.ImportAsset(
                VisibilityInputShaderPath,
                ImportAssetOptions.ForceSynchronousImport);

            Shader inputShader = AssetDatabase.LoadAssetAtPath<Shader>(
                VisibilityInputShaderPath);
            Shader resolveShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ResolveShaderPath);
            ComputeShader classificationCompute =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    ClassificationComputePath);
            ComputeShader deferredLitCompute =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    DeferredLitComputePath);
            AssertUsableShader(inputShader, "Visibility Buffer pixel-loop input");
            AssertUsableShader(resolveShader, "production Visibility Buffer GBuffer Resolve");
            AssertUsableComputeShader(
                classificationCompute,
                "production Material Classification");
            AssertUsableComputeShader(
                deferredLitCompute,
                "production Deferred Lit");

            var buffers = new List<GraphicsBuffer>();
            var objects = new List<Object>();
            var commandBuffer = new CommandBuffer
            {
                name = "Vivid Visibility Deferred Pixel Loop GPU Test",
            };

            Vector4 previousScreenSize = Shader.GetGlobalVector(
                "_VividScreenSize");
            Vector4 previousScreenParams = Shader.GetGlobalVector(
                "_VividScreenParams");
            Vector4 previousScaledScreenParams = Shader.GetGlobalVector(
                "_VividScaledScreenParams");
            Vector4 previousCameraPosition = Shader.GetGlobalVector(
                "_VividWorldSpaceCameraPos");
            Matrix4x4 previousInvViewProjection = Shader.GetGlobalMatrix(
                "_VividMatrixInvVP");
            Matrix4x4 previousViewMatrix = Shader.GetGlobalMatrix(
                "_VividMatrixV");
            int previousEnableProbeVolumes = Shader.GetGlobalInt(
                "_EnableProbeVolumes");

            GraphicsBuffer TrackBuffer(GraphicsBuffer buffer)
            {
                buffers.Add(buffer);
                return buffer;
            }

            T TrackObject<T>(T value)
                where T : Object
            {
                objects.Add(value);
                return value;
            }

            try
            {
                var inputMaterial = TrackObject(new Material(inputShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                });
                var resolveMaterial = TrackObject(new Material(resolveShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                });
                var sidecarMaterial = TrackObject(new Material(resolveShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                });
                sidecarMaterial.EnableKeyword("VIVID_DUAL_SLAB_SIDECAR_OUTPUT");
                sidecarMaterial.EnableKeyword("VIVID_DUAL_SLAB_SIDECAR_TILED");

                RenderTexture visibility = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R32G32_UInt,
                    "Visibility"));
                RenderTexture attributes0 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "VisibilityAttributes0"));
                RenderTexture attributes1 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "VisibilityAttributes1"));
                RenderTexture barycentrics = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16_SFloat,
                    "VisibilityBarycentrics"));
                RenderTexture depth = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R32_SFloat,
                    "Depth"));
                RenderTexture gbuffer0 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    "GBuffer0"));
                RenderTexture gbuffer1 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.A2B10G10R10_UNormPack32,
                    "GBuffer1"));
                RenderTexture gbuffer2 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    "GBuffer2"));
                RenderTexture gbuffer3 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.B10G11R11_UFloatPack32,
                    "GBuffer3"));
                RenderTexture diffuseIrradiance = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.B10G11R11_UFloatPack32,
                    "DiffuseIrradiance"));
                RenderTexture layerAux0 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8G8B8A8_SRGB,
                    "LayerAux0"));
                RenderTexture layerAux1 = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    "LayerAux1"));
                RenderTexture screenSpaceReflection = TrackObject(
                    CreateRenderTexture(
                        width,
                        height,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "ScreenSpaceReflection"));
                RenderTexture gtao = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R8_UNorm,
                    "GTAO"));
                RenderTexture directionalShadow = TrackObject(
                    CreateRenderTexture(
                        1,
                        1,
                        GraphicsFormat.R8_UNorm,
                        "DirectionalShadow"));
                RenderTexture lighting = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "Lighting",
                    enableRandomWrite: true));
                RenderTexture lightingDebug = TrackObject(CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "LightingDebug",
                    enableRandomWrite: true));

                Texture2D ggxFgd = TrackObject(CreateSolidTexture2D(
                    64,
                    64,
                    new Color(0.0f, 1.0f, 0.0f, 0.0f),
                    "GGXDisneyDiffuseFGD"));
                Texture2D charlieFgd = TrackObject(CreateSolidTexture2D(
                    1,
                    1,
                    Color.clear,
                    "CharlieFabricFGD"));
                Texture2DArray ltcData = TrackObject(CreateSolidTexture2DArray(
                    1,
                    1,
                    3,
                    Color.clear,
                    "LtcData"));
                Texture2DArray reflectionAtlas = TrackObject(
                    CreateSolidTexture2DArray(
                        1,
                        1,
                        1,
                        Color.clear,
                        "ReflectionAtlas"));
                Cubemap skyTexture = TrackObject(CreateSolidCubemap(
                    Color.clear,
                    "SkyTexture"));

                // Five 8x8 tiles exercise lit P0, unlit P0, the production
                // catalog's generic P3 payload, a table-known P4 that is
                // deliberately absent from the frozen dispatcher, and P1
                // through the tile-adaptive Dual Slab sidecar path.
                VividMaterialProgramData[] runtimePrograms =
                    GPUDrivenMaterialCompiler.CreateRuntimeProgramTable();
                Assert.That(
                    runtimePrograms,
                    Has.Length.EqualTo(
                        MaterialProgramContract.ProductionCatalogProgramCount));
                uint dispatcherMissProgramIndex = (uint) runtimePrograms.Length;
                Array.Resize(ref runtimePrograms, runtimePrograms.Length + 1);
                runtimePrograms[dispatcherMissProgramIndex] = runtimePrograms[0];

                VividMaterialRuntimeHeader[] runtimeHeaders =
                {
                    new()
                    {
                        ProgramID = VividMaterialProgramID.StandardSingleSlab,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                        Flags = VividMaterialRuntimeFlags.None,
                    },
                    new()
                    {
                        ProgramID = VividMaterialProgramID.StandardSingleSlab,
                        ParameterAddress = 4u,
                        ResourceBindingAddress = 1u,
                        Flags = VividMaterialRuntimeFlags.Unlit,
                    },
                    new()
                    {
                        ProgramID =
                            (VividMaterialProgramID) GenericProofProgramIndex,
                        ParameterAddress = 8u,
                        ResourceBindingAddress = 2u,
                        Flags = VividMaterialRuntimeFlags.None,
                    },
                    new()
                    {
                        ProgramID =
                            (VividMaterialProgramID) dispatcherMissProgramIndex,
                        ParameterAddress = 12u,
                        ResourceBindingAddress = 3u,
                        Flags = VividMaterialRuntimeFlags.None,
                    },
                    new()
                    {
                        ProgramID =
                            VividMaterialProgramID.DualSlabHorizontalMix,
                        ParameterAddress = 16u,
                        ResourceBindingAddress = 4u,
                        Flags = VividMaterialRuntimeFlags.None,
                    },
                };
                VividMaterialData[] materialData =
                {
                    CreatePixelLoopMaterialData(
                        new float4(1.0f),
                        new float3(0.125f, 0.25f, 0.5f),
                        metallic: 1.0f),
                    CreatePixelLoopMaterialData(
                        new float4(0.25f, 0.5f, 1.0f, 1.0f),
                        float3.zero,
                        metallic: 0.0f),
                    CreatePixelLoopMaterialData(
                        new float4(1.0f),
                        float3.zero,
                        metallic: 1.0f),
                    CreatePixelLoopMaterialData(
                        new float4(1.0f),
                        float3.zero,
                        metallic: 1.0f),
                    CreatePixelLoopMaterialData(
                        new float4(1.0f),
                        float3.zero,
                        metallic: 0.0f),
                };
                VividSurfaceBindingData[] surfaceBindings =
                {
                    CreateUnboundSurfaceBinding(),
                    CreateUnboundSurfaceBinding(),
                    CreateUnboundSurfaceBinding(),
                    CreateUnboundSurfaceBinding(),
                    CreateUnboundSurfaceBinding(),
                    CreateUnboundSurfaceBinding(),
                };
                var dualSlabMaterialData = new VividDualSlabMaterialData
                {
                    BaseAlbedoColor = new float4(
                        0.8f,
                        0.2f,
                        0.1f,
                        1.0f),
                    BaseRoughness = 0.75f,
                    BaseMetallic = 0.0f,
                    TopAlbedoColor = new float4(
                        0.2f,
                        0.8f,
                        0.4f,
                        1.0f),
                    TopRoughness = 0.25f,
                    TopMetallic = 0.0f,
                    Emission = new float4(
                        0.02f,
                        0.03f,
                        0.04f,
                        0.0f),
                    LayerOperator = VividDualSlabOperator.HorizontalMix,
                    LayerWeight = 0.5f,
                };
                var materialParameterLanes = new List<uint4>();
                materialParameterLanes.AddRange(CreateGenericParameterLanes(
                    VividMaterialProgramID.StandardSingleSlab,
                    materialData[0]));
                materialParameterLanes.AddRange(CreateGenericParameterLanes(
                    VividMaterialProgramID.StandardSingleSlab,
                    materialData[1]));
                materialParameterLanes.AddRange(CreateGenericParameterLanes(
                    (VividMaterialProgramID) GenericProofProgramIndex,
                    materialData[2]));
                materialParameterLanes.AddRange(CreateGenericParameterLanes(
                    VividMaterialProgramID.StandardSingleSlab,
                    materialData[3]));
                materialParameterLanes.AddRange(CreateGenericParameterLanes(
                    VividMaterialProgramID.DualSlabHorizontalMix,
                    materialData[4],
                    dualSlabMaterialData));
                VividMaterialResourceData[] materialResources =
                {
                    CreateMaterialResource(materialData[0], surfaceBindings[0]),
                    CreateMaterialResource(materialData[1], surfaceBindings[1]),
                    CreateMaterialResource(materialData[2], surfaceBindings[2]),
                    CreateMaterialResource(materialData[3], surfaceBindings[3]),
                    CreateMaterialResource(
                        surfaceBindings[4],
                        dualSlabMaterialData.BaseTextureTilingOffset,
                        dualSlabMaterialData.BaseMetallicSmoothnessRemap,
                        dualSlabMaterialData.BaseAmbientOcclusionRemap,
                        dualSlabMaterialData.BaseNormalsStrength,
                        dualSlabMaterialData.BaseMaskMode),
                    CreateMaterialResource(
                        surfaceBindings[5],
                        dualSlabMaterialData.TopTextureTilingOffset,
                        dualSlabMaterialData.TopMetallicSmoothnessRemap,
                        dualSlabMaterialData.TopAmbientOcclusionRemap,
                        dualSlabMaterialData.TopNormalsStrength,
                        dualSlabMaterialData.TopMaskMode),
                };
                VividInstanceData[] instances =
                {
                    CreatePixelLoopInstance(0u),
                    CreatePixelLoopInstance(1u),
                    CreatePixelLoopInstance(2u),
                    CreatePixelLoopInstance(3u),
                    CreatePixelLoopInstance(4u),
                };
                var meshlet = new VividMeshlet
                {
                    VertexOffset = 0u,
                    TriangleOffset = 0u,
                    BoundingSphere = new float4(0.0f, 0.0f, 0.0f, 2.0f),
                };
                meshlet.VertexCount = 3u;
                meshlet.TriangleCount = 1u;
                VividMeshletVertex[] vertices =
                {
                    VividMeshletVertexPacking.Pack(
                        new float3(-1.0f, -1.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(0.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new float3(1.0f, -1.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(1.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new float3(0.0f, 1.0f, 0.0f),
                        new float3(0.0f, 0.0f, 1.0f),
                        new float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new float2(0.5f, 1.0f)),
                };

                GraphicsBuffer instanceBuffer = TrackBuffer(
                    CreateStructuredBuffer(instances));
                GraphicsBuffer materialBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialData));
                GraphicsBuffer dualSlabMaterialBuffer = TrackBuffer(
                    CreateStructuredBuffer(new[] { dualSlabMaterialData }));
                GraphicsBuffer materialParameterBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialParameterLanes.ToArray()));
                GraphicsBuffer materialResourceBuffer = TrackBuffer(
                    CreateStructuredBuffer(materialResources));
                GraphicsBuffer runtimeHeaderBuffer = TrackBuffer(
                    CreateStructuredBuffer(runtimeHeaders));
                GraphicsBuffer programBuffer = TrackBuffer(
                    CreateStructuredBuffer(runtimePrograms));
                GraphicsBuffer surfaceBindingBuffer = TrackBuffer(
                    CreateStructuredBuffer(surfaceBindings));
                GraphicsBuffer terrainMaterialBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividTerrainMaterialData[1]));
                GraphicsBuffer terrainLayerBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividTerrainLayerGPUData[1]));
                GraphicsBuffer meshLodNodeBuffer = TrackBuffer(
                    CreateStructuredBuffer(new VividMeshLODNode[1]));
                GraphicsBuffer meshletBuffer = TrackBuffer(
                    CreateStructuredBuffer(new[] { meshlet }));
                GraphicsBuffer vertexBuffer = TrackBuffer(
                    CreateStructuredBuffer(vertices));
                GraphicsBuffer indexBuffer = TrackBuffer(CreateRawBuffer(
                    new[] { 0x00020100u }));
                GraphicsBuffer materialTileFeatureFlags = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        tileCount,
                        sizeof(uint)));
                GraphicsBuffer materialFeatureTileList = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        tileCount * variantCount,
                        sizeof(uint)));
                GraphicsBuffer materialFeatureIndirectArgs = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured
                            | GraphicsBuffer.Target.IndirectArguments,
                        variantCount * 4,
                        sizeof(uint)));
                GraphicsBuffer dualSlabSidecarTileList = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        tileCount,
                        sizeof(uint)));
                GraphicsBuffer dualSlabSidecarIndirectArgs = TrackBuffer(
                    new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured
                            | GraphicsBuffer.Target.IndirectArguments,
                        4,
                        sizeof(uint)));
                GraphicsBuffer preExposureBuffer = TrackBuffer(
                    CreateStructuredBuffer(new[]
                    {
                        new float4(1.0f, 0.0f, 0.0f, 0.0f),
                    }));
                GraphicsBuffer ambientProbeBuffer = TrackBuffer(
                    CreateStructuredBuffer(new float4[7]));
                GraphicsBuffer directionalLights = TrackBuffer(
                    CreateStructuredBuffer(new VividRP.Runtime.VividLightData.DirectionalLightData[1]));
                GraphicsBuffer punctualLights = TrackBuffer(
                    CreateStructuredBuffer(new VividRP.Runtime.VividLightData.PunctualLightData[1]));
                GraphicsBuffer areaLights = TrackBuffer(
                    CreateStructuredBuffer(new VividRP.Runtime.VividLightData.AreaLightData[1]));
                GraphicsBuffer reflectionProbes = TrackBuffer(
                    CreateStructuredBuffer(new VividRP.Runtime.VividLightData.ReflectionProbeData[1]));
                GraphicsBuffer layeredOffset = TrackBuffer(
                    CreateStructuredBuffer(new uint[1]));
                GraphicsBuffer layeredLightList = TrackBuffer(
                    CreateStructuredBuffer(new uint[1]));
                GraphicsBuffer logBaseBuffer = TrackBuffer(
                    CreateStructuredBuffer(new float[1]));

                BindPixelLoopResolveMaterial(
                    resolveMaterial,
                    visibility,
                    attributes0,
                    attributes1,
                    barycentrics,
                    instanceBuffer,
                    materialBuffer,
                    dualSlabMaterialBuffer,
                    materialParameterBuffer,
                    materialResourceBuffer,
                    runtimeHeaderBuffer,
                    programBuffer,
                    surfaceBindingBuffer,
                    terrainMaterialBuffer,
                    terrainLayerBuffer,
                    meshLodNodeBuffer,
                    meshletBuffer,
                    vertexBuffer,
                    indexBuffer,
                    instances.Length,
                    materialData.Length,
                    runtimePrograms.Length,
                    surfaceBindings.Length,
                    materialParameterLanes.Count,
                    materialResources.Length);
                BindPixelLoopResolveMaterial(
                    sidecarMaterial,
                    visibility,
                    attributes0,
                    attributes1,
                    barycentrics,
                    instanceBuffer,
                    materialBuffer,
                    dualSlabMaterialBuffer,
                    materialParameterBuffer,
                    materialResourceBuffer,
                    runtimeHeaderBuffer,
                    programBuffer,
                    surfaceBindingBuffer,
                    terrainMaterialBuffer,
                    terrainLayerBuffer,
                    meshLodNodeBuffer,
                    meshletBuffer,
                    vertexBuffer,
                    indexBuffer,
                    instances.Length,
                    materialData.Length,
                    runtimePrograms.Length,
                    surfaceBindings.Length,
                    materialParameterLanes.Count,
                    materialResources.Length);

                commandBuffer.SetGlobalVector(
                    Shader.PropertyToID("_VividScreenSize"),
                    new Vector4(width, height, 1.0f / width, 1.0f / height));
                commandBuffer.SetGlobalVector(
                    Shader.PropertyToID("_VividScreenParams"),
                    new Vector4(width, height, 1.0f + 1.0f / width, 1.0f + 1.0f / height));
                commandBuffer.SetGlobalVector(
                    Shader.PropertyToID("_VividScaledScreenParams"),
                    new Vector4(width, height, 1.0f / width, 1.0f / height));
                commandBuffer.SetGlobalVector(
                    Shader.PropertyToID("_VividWorldSpaceCameraPos"),
                    new Vector4(0.0f, 0.0f, 1.0f, 1.0f));
                commandBuffer.SetGlobalMatrix(
                    Shader.PropertyToID("_VividMatrixInvVP"),
                    Matrix4x4.identity);
                commandBuffer.SetGlobalMatrix(
                    Shader.PropertyToID("_VividMatrixV"),
                    Matrix4x4.identity);
                commandBuffer.SetGlobalInt(
                    Shader.PropertyToID("_EnableProbeVolumes"),
                    0);

                commandBuffer.SetRenderTarget(
                    new RenderTargetIdentifier[]
                    {
                        visibility,
                        attributes0,
                        attributes1,
                        barycentrics,
                    },
                    BuiltinRenderTextureType.None);
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    inputMaterial,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1);
                ClearRenderTexture(
                    commandBuffer,
                    depth,
                    new Color(0.5f, 0.0f, 0.0f, 0.0f));
                // A uniform SSR sample plus FGD=1 gives an exact lighting
                // delta over Resolve's emissive clear, without scene lights.
                ClearRenderTexture(
                    commandBuffer,
                    screenSpaceReflection,
                    new Color(0.5f, 0.25f, 1.0f, 1.0f));
                ClearRenderTexture(commandBuffer, gtao, Color.white);
                ClearRenderTexture(commandBuffer, directionalShadow, Color.white);

                commandBuffer.SetRenderTarget(
                    new RenderTargetIdentifier[]
                    {
                        gbuffer0,
                        gbuffer1,
                        gbuffer2,
                        gbuffer3,
                        diffuseIrradiance,
                    },
                    BuiltinRenderTextureType.None);
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    resolveMaterial,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1);
                DispatchTileAdaptiveDualSlabSidecar(
                    commandBuffer,
                    classificationCompute,
                    sidecarMaterial,
                    width,
                    height,
                    gbuffer0,
                    gbuffer1,
                    layerAux0,
                    layerAux1,
                    dualSlabSidecarTileList,
                    dualSlabSidecarIndirectArgs);

                DispatchMaterialClassification(
                    commandBuffer,
                    classificationCompute,
                    width,
                    height,
                    tileCount,
                    gbuffer0,
                    gbuffer1,
                    depth,
                    materialTileFeatureFlags,
                    materialFeatureTileList,
                    materialFeatureIndirectArgs);
                DispatchDeferredLighting(
                    commandBuffer,
                    deferredLitCompute,
                    width,
                    height,
                    tileCount,
                    gbuffer0,
                    gbuffer1,
                    gbuffer2,
                    gbuffer3,
                    diffuseIrradiance,
                    layerAux0,
                    layerAux1,
                    depth,
                    directionalShadow,
                    gtao,
                    screenSpaceReflection,
                    lighting,
                    lightingDebug,
                    materialTileFeatureFlags,
                    materialFeatureTileList,
                    materialFeatureIndirectArgs,
                    preExposureBuffer,
                    ambientProbeBuffer,
                    directionalLights,
                    punctualLights,
                    areaLights,
                    reflectionProbes,
                    layeredOffset,
                    layeredLightList,
                    logBaseBuffer,
                    ggxFgd,
                    charlieFgd,
                    ltcData,
                    reflectionAtlas,
                    skyTexture);

                Graphics.ExecuteCommandBuffer(commandBuffer);

                uint[] featureFlags = new uint[tileCount];
                uint[] tileList = new uint[tileCount * variantCount];
                uint[] indirectArgs = new uint[variantCount * 4];
                uint[] sidecarTileList = new uint[tileCount];
                uint[] sidecarIndirectArgs = new uint[4];
                materialTileFeatureFlags.GetData(featureFlags);
                materialFeatureTileList.GetData(tileList);
                materialFeatureIndirectArgs.GetData(indirectArgs);
                dualSlabSidecarTileList.GetData(sidecarTileList);
                dualSlabSidecarIndirectArgs.GetData(sidecarIndirectArgs);

                Assert.That(
                    sidecarIndirectArgs,
                    Is.EqualTo(new uint[] { 3u, 1u, 0u, 0u }),
                    "Tile-adaptive sidecar must issue one triangle instance.");
                Assert.That(
                    sidecarTileList[0],
                    Is.EqualTo(4u),
                    "The production classifier must append the fifth tile.");

                Assert.That(
                    featureFlags,
                    Is.EqualTo(new uint[] { 1u, 0u, 1u, 8u, 4u }));
                Assert.That(
                    indirectArgs,
                    Is.EqualTo(new uint[]
                    {
                        2u, 1u, 1u, 0u,
                        0u, 1u, 1u, 0u,
                        1u, 1u, 1u, 0u,
                        1u, 1u, 1u, 0u,
                    }));
                Assert.That(
                    new[] { tileList[0], tileList[1] },
                    Is.EquivalentTo(new uint[] { 0u, 2u }));
                Assert.That(tileList[tileCount * 2], Is.EqualTo(4u));
                Assert.That(tileList[tileCount * 3], Is.EqualTo(3u));

                Texture2D gbuffer0Readback = TrackObject(ReadRenderTexture(
                    gbuffer0,
                    TextureFormat.RGBAFloat));
                Texture2D gbuffer1Readback = TrackObject(ReadRenderTexture(
                    gbuffer1,
                    TextureFormat.RGBAFloat));
                Texture2D gbuffer3Readback = TrackObject(ReadRenderTexture(
                    gbuffer3,
                    TextureFormat.RGBAFloat));
                Texture2D layerAux0Readback = TrackObject(ReadRenderTexture(
                    layerAux0,
                    TextureFormat.RGBAFloat));
                Texture2D layerAux1Readback = TrackObject(ReadRenderTexture(
                    layerAux1,
                    TextureFormat.RGBAFloat));
                Texture2D lightingReadback = TrackObject(ReadRenderTexture(
                    lighting,
                    TextureFormat.RGBAFloat));
                Texture2D debugReadback = TrackObject(ReadRenderTexture(
                    lightingDebug,
                    TextureFormat.RGBAFloat));

                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(4, 4),
                    0xC2,
                    "StandardLit Deferred Export header");
                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(12, 4),
                    0x81,
                    "Unlit Deferred Export header");
                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(20, 4),
                    0xC2,
                    "generic P3 Deferred Export header");
                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(28, 4),
                    0x0F,
                    "dispatcher-miss Deferred Export header");
                AssertDeferredExportHeader(
                    gbuffer0Readback.GetPixel(36, 4),
                    0x84,
                    "Dual Slab Deferred Export header");
                Assert.That(
                    gbuffer1Readback.GetPixel(4, 4).a,
                    Is.EqualTo(1.0f).Within(0.001f),
                    "Resolve must emit a valid Surface Summary GBuffer ABI tag.");
                Assert.That(
                    gbuffer1Readback.GetPixel(20, 4).a,
                    Is.EqualTo(1.0f).Within(0.001f),
                    "The generic P3 must emit the same frozen Surface Summary ABI tag.");
                AssertColor(
                    gbuffer3Readback.GetPixel(20, 4),
                    new Color(0.05f, 0.1f, 0.15f, 1.0f),
                    "generic P3 IR emission reached the production GBuffer");
                AssertColor(
                    layerAux0Readback.GetPixel(31, 4),
                    Color.clear,
                    "Tile-adaptive sidecar must not touch a non-Dual tile");
                Assert.That(
                    layerAux0Readback.GetPixel(32, 4).a,
                    Is.EqualTo(0.5f).Within(0.01f),
                    "The indirect sidecar draw must cover the first Dual tile pixel.");
                Assert.That(
                    layerAux0Readback.GetPixel(39, 4).a,
                    Is.EqualTo(0.5f).Within(0.01f),
                    "The indirect sidecar draw must cover the resolve boundary.");
                Assert.That(
                    layerAux1Readback.GetPixel(36, 4).a,
                    Is.EqualTo(0.25f).Within(0.01f),
                    "The indirect sidecar draw must export top-slab roughness.");

                AssertColor(
                    lightingReadback.GetPixel(4, 4),
                    new Color(0.625f, 0.5f, 1.5f, 1.0f),
                    "StandardLit emission plus Deferred SSR lighting");
                AssertColor(
                    lightingReadback.GetPixel(12, 4),
                    new Color(0.25f, 0.5f, 1.0f, 1.0f),
                    "Unlit Resolve export preserved by Deferred clear");
                AssertColor(
                    lightingReadback.GetPixel(20, 4),
                    new Color(0.185f, 0.13625f, 0.545f, 1.0f),
                    "generic P3 emission plus transformed Surface deferred lighting");
                AssertColor(
                    lightingReadback.GetPixel(28, 4),
                    new Color(1.0f, 0.0f, 1.0f, 1.0f),
                    "dispatcher miss classified and lit through CatchAll");
                AssertColor(
                    lightingReadback.GetPixel(36, 4),
                    new Color(0.02f, 0.03f, 0.04f, 1.0f),
                    "Deferred Dual Slab must consume the tile-adaptive sidecar");
                AssertColor(
                    debugReadback.GetPixel(4, 4),
                    new Color(0.0f, 0.0f, 0.0f, 1.0f),
                    "StandardLit Deferred variant debug output");
                AssertColor(
                    debugReadback.GetPixel(12, 4),
                    Color.clear,
                    "Unlit tile must not enter a Deferred lighting variant");
                AssertColor(
                    debugReadback.GetPixel(20, 4),
                    new Color(0.0f, 0.0f, 0.0f, 1.0f),
                    "generic P3 shares FastSlab lighting without a handwritten branch");
                AssertColor(
                    debugReadback.GetPixel(28, 4),
                    new Color(1.0f, 0.0f, 1.0f, 1.0f),
                    "CatchAll must fail closed in Deferred lighting");
            }
            finally
            {
                commandBuffer.Dispose();
                foreach (GraphicsBuffer buffer in buffers)
                    buffer?.Dispose();
                RenderTexture.active = null;
                foreach (Object value in objects)
                {
                    if (value is RenderTexture renderTexture
                        && renderTexture.IsCreated())
                    {
                        renderTexture.Release();
                    }
                    Object.DestroyImmediate(value);
                }

                Shader.SetGlobalVector("_VividScreenSize", previousScreenSize);
                Shader.SetGlobalVector("_VividScreenParams", previousScreenParams);
                Shader.SetGlobalVector(
                    "_VividScaledScreenParams",
                    previousScaledScreenParams);
                Shader.SetGlobalVector(
                    "_VividWorldSpaceCameraPos",
                    previousCameraPosition);
                Shader.SetGlobalMatrix(
                    "_VividMatrixInvVP",
                    previousInvViewProjection);
                Shader.SetGlobalMatrix("_VividMatrixV", previousViewMatrix);
                Shader.SetGlobalInt(
                    "_EnableProbeVolumes",
                    previousEnableProbeVolumes);
            }
        }

        private static RenderGraphData CreateProductionPixelLoopRenderGraph()
        {
            RenderGraphPassResourceBinding Input(
                string fieldName,
                RenderGraphResourceKind resourceKind,
                int sourcePassIndex,
                string sourceFieldName)
            {
                return new RenderGraphPassResourceBinding
                {
                    FieldName = fieldName,
                    ResourceKind = resourceKind,
                    SourceKind = RenderGraphPassBindingSourceKind.PassField,
                    ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    SourcePassIndex = sourcePassIndex,
                    SourceFieldName = sourceFieldName,
                };
            }

            RenderGraphPassDefinition Pass<T>(string name)
                where T : IRenderPass
            {
                return new RenderGraphPassDefinition
                {
                    PassType = typeof(T).AssemblyQualifiedName,
                    PassName = name,
                };
            }

            void AddInputs(
                RenderGraphPassDefinition pass,
                RenderGraphResourceKind resourceKind,
                int sourcePassIndex,
                params (string Field, string SourceField)[] fields)
            {
                foreach ((string field, string sourceField) in fields)
                {
                    pass.ResourceBindings.Add(Input(
                        field,
                        resourceKind,
                        sourcePassIndex,
                        sourceField));
                }
            }

            var graph = ScriptableObject.CreateInstance<RenderGraphData>();
            graph.hideFlags = HideFlags.HideAndDontSave;
            graph.ImportVersion = 1;

            graph.Passes.Add(Pass<VisibilityBufferPass>(
                "Production Visibility Buffer"));

            RenderGraphPassDefinition resolve =
                Pass<VisibilityBufferGBufferResolvePass>(
                    "Production Visibility GBuffer Resolve");
            AddInputs(
                resolve,
                RenderGraphResourceKind.Texture,
                0,
                ("m_VisibilityBuffer", "m_VisibilityBuffer"),
                ("m_Attributes0", "m_Attributes0"),
                ("m_Attributes1", "m_Attributes1"),
                ("m_Barycentrics", "m_Barycentrics"));
            graph.Passes.Add(resolve);

            RenderGraphPassDefinition classification =
                Pass<MaterialClassificationPass>(
                    "Production Material Classification");
            AddInputs(
                classification,
                RenderGraphResourceKind.Texture,
                1,
                ("m_GBuffer0", "m_GBuffer0"),
                ("m_GBuffer1", "m_GBuffer1"));
            classification.ResourceBindings.Add(Input(
                "m_DepthTexture",
                RenderGraphResourceKind.Texture,
                0,
                "m_Depth"));
            graph.Passes.Add(classification);

            RenderGraphPassDefinition deferred = Pass<DeferredLightingPass>(
                "Production Deferred Lighting");
            AddInputs(
                deferred,
                RenderGraphResourceKind.Texture,
                1,
                ("m_GBuffer0", "m_GBuffer0"),
                ("m_GBuffer1", "m_GBuffer1"),
                ("m_GBuffer2", "m_GBuffer2"),
                ("m_GBuffer3", "m_GBuffer3"),
                ("m_GBuffer4", "m_GBuffer4"),
                ("m_LayerAux0", "m_LayerAux0"),
                ("m_LayerAux1", "m_LayerAux1"));
            deferred.ResourceBindings.Add(Input(
                "m_DepthTexture",
                RenderGraphResourceKind.Texture,
                0,
                "m_Depth"));
            AddInputs(
                deferred,
                RenderGraphResourceKind.Buffer,
                2,
                ("m_MaterialTileFeatureFlags", "m_MaterialTileFeatureFlags"),
                ("m_MaterialFeatureTileList", "m_MaterialFeatureTileList"),
                ("m_MaterialFeatureIndirectArgs", "m_MaterialFeatureIndirectArgs"));
            graph.Passes.Add(deferred);
            return graph;
        }

        private static void BindProductionPixelLoopGlobals(
            CommandBuffer commandBuffer,
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer materialBuffer,
            GraphicsBuffer dualSlabMaterialBuffer,
            GraphicsBuffer materialParameterBuffer,
            GraphicsBuffer materialResourceBuffer,
            GraphicsBuffer runtimeHeaderBuffer,
            GraphicsBuffer programBuffer,
            GraphicsBuffer surfaceBindingBuffer,
            GraphicsBuffer terrainMaterialBuffer,
            GraphicsBuffer terrainLayerBuffer,
            GraphicsBuffer meshLodNodeBuffer,
            GraphicsBuffer meshletBuffer,
            GraphicsBuffer vertexBuffer,
            GraphicsBuffer indexBuffer,
            GraphicsBuffer preExposureBuffer,
            GraphicsBuffer ambientProbeBuffer,
            int programCount,
            int materialParameterLaneCount,
            int materialResourceCount)
        {
            commandBuffer.SetGlobalBuffer("_InstanceData", instanceBuffer);
            commandBuffer.SetGlobalInt("_InstanceDataCount", 1);
            commandBuffer.SetGlobalBuffer("_MaterialData", materialBuffer);
            commandBuffer.SetGlobalInt("_MaterialDataCount", 1);
            commandBuffer.SetGlobalBuffer(
                "_DualSlabMaterialData",
                dualSlabMaterialBuffer);
            commandBuffer.SetGlobalInt("_DualSlabMaterialDataCount", 1);
            commandBuffer.SetGlobalBuffer(
                "_MaterialParameterData",
                materialParameterBuffer);
            commandBuffer.SetGlobalInt(
                "_MaterialParameterDataCount",
                materialParameterLaneCount);
            commandBuffer.SetGlobalBuffer(
                "_MaterialResourceData",
                materialResourceBuffer);
            commandBuffer.SetGlobalInt(
                "_MaterialResourceDataCount",
                materialResourceCount);
            commandBuffer.SetGlobalBuffer(
                "_MaterialRuntimeHeaders",
                runtimeHeaderBuffer);
            commandBuffer.SetGlobalInt("_MaterialRuntimeHeaderCount", 1);
            commandBuffer.SetGlobalBuffer("_MaterialPrograms", programBuffer);
            commandBuffer.SetGlobalInt("_MaterialProgramCount", programCount);
            commandBuffer.SetGlobalBuffer(
                "_SurfaceBindingData",
                surfaceBindingBuffer);
            commandBuffer.SetGlobalInt("_SurfaceBindingDataCount", 1);
            commandBuffer.SetGlobalBuffer(
                "_TerrainMaterialData",
                terrainMaterialBuffer);
            commandBuffer.SetGlobalInt("_TerrainMaterialDataCount", 1);
            commandBuffer.SetGlobalBuffer("_TerrainLayerData", terrainLayerBuffer);
            commandBuffer.SetGlobalInt("_TerrainLayerDataCount", 1);
            commandBuffer.SetGlobalBuffer("_MeshLODNodes", meshLodNodeBuffer);
            commandBuffer.SetGlobalInt("_MeshLODNodeCount", 1);
            commandBuffer.SetGlobalBuffer("_Meshlets", meshletBuffer);
            commandBuffer.SetGlobalInt("_MeshletCount", 1);
            commandBuffer.SetGlobalBuffer("_SharedVertexBuffer", vertexBuffer);
            commandBuffer.SetGlobalBuffer("_SharedIndexBuffer", indexBuffer);
            commandBuffer.SetGlobalBuffer(
                "_VividAutoExposurePreExposureBuffer",
                preExposureBuffer);
            commandBuffer.SetGlobalBuffer(
                "_VividAmbientProbeData",
                ambientProbeBuffer);
            commandBuffer.SetGlobalInt("_EnableProbeVolumes", 0);
        }

        private static void ClearProductionPixelLoopGlobals()
        {
            foreach (string name in new[]
                     {
                         "_InstanceData",
                         "_MaterialData",
                         "_DualSlabMaterialData",
                         "_MaterialParameterData",
                         "_MaterialResourceData",
                         "_MaterialRuntimeHeaders",
                         "_MaterialPrograms",
                         "_SurfaceBindingData",
                         "_TerrainMaterialData",
                         "_TerrainLayerData",
                         "_MeshLODNodes",
                         "_Meshlets",
                         "_SharedVertexBuffer",
                         "_SharedIndexBuffer",
                         "_VividAutoExposurePreExposureBuffer",
                         "_VividAmbientProbeData",
                     })
            {
                Shader.SetGlobalBuffer(name, (GraphicsBuffer) null);
            }
            foreach (string name in new[]
                     {
                         "_InstanceDataCount",
                         "_MaterialDataCount",
                         "_DualSlabMaterialDataCount",
                         "_MaterialParameterDataCount",
                         "_MaterialResourceDataCount",
                         "_MaterialRuntimeHeaderCount",
                         "_MaterialProgramCount",
                         "_SurfaceBindingDataCount",
                         "_TerrainMaterialDataCount",
                         "_TerrainLayerDataCount",
                         "_MeshLODNodeCount",
                         "_MeshletCount",
                     })
            {
                Shader.SetGlobalInt(name, 0);
            }
        }

        private static IReadOnlyList<IRenderPass> GetCompiledRenderPasses()
        {
            FieldInfo field = typeof(PassRecorder).GetField(
                "s_RenderPasses",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IReadOnlyList<IRenderPass>) field.GetValue(null);
        }

        private static T GetPrivateField<T>(object owner, string fieldName)
            where T : class
        {
            FieldInfo field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            var value = field.GetValue(owner) as T;
            Assert.That(value, Is.Not.Null, fieldName);
            return value;
        }

        private static GraphicsBuffer CreateStructuredBuffer<T>(T[] data)
            where T : struct
        {
            var buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                data.Length,
                Marshal.SizeOf<T>());
            buffer.SetData(data);
            return buffer;
        }

        private static GraphicsBuffer CreateRawBuffer(uint[] data)
        {
            var buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw,
                data.Length,
                sizeof(uint));
            buffer.SetData(data);
            return buffer;
        }

        private static VividMaterialData CreatePixelLoopMaterialData(
            float4 albedo,
            float3 emission,
            float metallic)
        {
            return new VividMaterialData
            {
                AlbedoColor = albedo,
                TextureTilingOffset = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                Emission = new float4(emission, 0.0f),
                MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                NormalsStrength = 1.0f,
                Roughness = 1.0f,
                Metallic = metallic,
                RendererListID = VividRendererListID.Default,
            };
        }

        private static VividSurfaceBindingData CreateUnboundSurfaceBinding()
        {
            return new VividSurfaceBindingData
            {
                BaseColorResource = VividSurfaceBindingData.InvalidResource,
                NormalResource = VividSurfaceBindingData.InvalidResource,
                MaskResource = VividSurfaceBindingData.InvalidResource,
                Flags = VividSurfaceBindingFlags.None,
                UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
            };
        }

        private static uint4[] CreateGenericParameterLanes(
            VividMaterialProgramID programID,
            in VividMaterialData materialData)
        {
            return GPUDrivenMaterialCompiler.CreateGenericParameterLanes(
                GPUDrivenMaterialCompiler.GetCatalogedMaterialProgram(programID),
                materialData,
                default,
                isDualSlab: false);
        }

        private static uint4[] CreateGenericParameterLanes(
            VividMaterialProgramID programID,
            in VividMaterialData materialData,
            in VividDualSlabMaterialData dualSlabMaterialData)
        {
            return GPUDrivenMaterialCompiler.CreateGenericParameterLanes(
                GPUDrivenMaterialCompiler.GetCatalogedMaterialProgram(programID),
                materialData,
                dualSlabMaterialData,
                isDualSlab: true);
        }

        private static VividMaterialResourceData CreateMaterialResource(
            in VividMaterialData materialData,
            in VividSurfaceBindingData surfaceBinding)
        {
            return CreateMaterialResource(
                surfaceBinding,
                materialData.TextureTilingOffset,
                materialData.MetallicSmoothnessRemap,
                materialData.AmbientOcclusionRemap,
                materialData.NormalsStrength,
                0u);
        }

        private static VividMaterialResourceData CreateMaterialResource(
            in VividSurfaceBindingData surfaceBinding,
            float4 textureTilingOffset,
            float4 metallicSmoothnessRemap,
            float4 ambientOcclusionRemap,
            float normalsStrength,
            uint maskMode)
        {
            return new VividMaterialResourceData
            {
                SurfaceBinding = surfaceBinding,
                TextureTilingOffset = textureTilingOffset,
                MetallicSmoothnessRemap = metallicSmoothnessRemap,
                AmbientOcclusionRemap = ambientOcclusionRemap,
                NormalsStrength = normalsStrength,
                MaskMode = maskMode,
            };
        }

        private static VividInstanceData CreatePixelLoopInstance(
            uint materialIndex)
        {
            return new VividInstanceData
            {
                ObjectToWorldMatrix = float4x4.identity,
                WorldToObjectMatrix = float4x4.identity,
                AABBMin = new float4(-1.0f, -1.0f, 0.0f, 0.0f),
                AABBMax = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                MaterialIndex = materialIndex,
                PassMask = VividInstancePassMask.Main,
                Flags = VividInstanceFlags.None,
            };
        }

        private static RenderTexture CreateRenderTexture(
            int width,
            int height,
            GraphicsFormat format,
            string name,
            bool enableRandomWrite = false)
        {
            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                format,
                depthBufferBits: 0)
            {
                dimension = TextureDimension.Tex2D,
                enableRandomWrite = enableRandomWrite,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Assert.That(
                texture.Create(),
                Is.True,
                $"Could not create {name} ({format}).");
            return texture;
        }

        private static Texture2D CreateSolidTexture2D(
            int width,
            int height,
            Color color,
            string name)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBAHalf,
                mipChain: false,
                linear: true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            Array.Fill(pixels, color);
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static Texture2DArray CreateSolidTexture2DArray(
            int width,
            int height,
            int depth,
            Color color,
            string name)
        {
            var texture = new Texture2DArray(
                width,
                height,
                depth,
                TextureFormat.RGBAHalf,
                mipChain: false,
                linear: true)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            Array.Fill(pixels, color);
            for (var slice = 0; slice < depth; slice++)
                texture.SetPixels(pixels, slice);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static Cubemap CreateSolidCubemap(Color color, string name)
        {
            var texture = new Cubemap(
                1,
                TextureFormat.RGBAHalf,
                mipChain: false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            foreach (CubemapFace face in new[]
                     {
                         CubemapFace.PositiveX,
                         CubemapFace.NegativeX,
                         CubemapFace.PositiveY,
                         CubemapFace.NegativeY,
                         CubemapFace.PositiveZ,
                         CubemapFace.NegativeZ,
                     })
            {
                texture.SetPixel(face, 0, 0, color);
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static Texture2D ReadRenderTexture(
            RenderTexture source,
            TextureFormat format)
        {
            var readback = new Texture2D(
                source.width,
                source.height,
                format,
                mipChain: false,
                linear: true)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            RenderTexture.active = source;
            readback.ReadPixels(
                new Rect(0, 0, source.width, source.height),
                0,
                0);
            readback.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            RenderTexture.active = null;
            return readback;
        }

        private static void ClearRenderTexture(
            CommandBuffer commandBuffer,
            RenderTexture target,
            Color color)
        {
            commandBuffer.SetRenderTarget(target);
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: color);
        }

        private static void AssertUsableShader(Shader shader, string label)
        {
            Assert.That(shader, Is.Not.Null, $"Could not load {label} shader.");
            Assert.That(shader.isSupported, Is.True, $"{label} shader is not supported.");
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                $"{label} shader failed to compile.");
        }

        private static void AssertUsableComputeShader(
            ComputeShader shader,
            string label)
        {
            Assert.That(shader, Is.Not.Null, $"Could not load {label} compute shader.");
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
            {
                Assert.That(
                    message.severity.ToString(),
                    Is.Not.EqualTo("Error"),
                    $"{label} compute shader failed to compile: {message.message}");
            }
        }

        private static void AssertDeferredExportHeader(
            Color gbuffer0,
            int expectedHeader,
            string message)
        {
            Assert.That(
                Mathf.RoundToInt(gbuffer0.a * 255.0f),
                Is.EqualTo(expectedHeader),
                message);
        }

        private static string BuildVisibilityDeferredPixelLoopInputShaderSource()
        {
            return @"Shader ""Hidden/VividRP/Tests/VisibilityDeferredPixelLoopInput""
{
    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl""
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl""

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            VividVisibilityBufferFragmentOutput Frag(Varyings input)
            {
                VividVisibilityBufferValue value;
                value.InstanceID = min((uint) input.positionCS.x / 8u, 4u);
                value.MeshletID = 0u;
                value.IndexID = 0u;
                return PackVividVisibilityBufferFragmentOutput(
                    PackVisibilityBufferValue(value),
                    float2(0.25f, 0.75f),
                    float2(0.01f, 0.0f),
                    float2(0.0f, 0.01f),
                    float3(0.0f, 0.0f, 1.0f),
                    float3(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f));
            }
            ENDHLSL
        }
    }
}";
        }

        private static void BindPixelLoopResolveMaterial(
            Material material,
            Texture visibility,
            Texture attributes0,
            Texture attributes1,
            Texture barycentrics,
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer materialBuffer,
            GraphicsBuffer dualSlabMaterialBuffer,
            GraphicsBuffer materialParameterBuffer,
            GraphicsBuffer materialResourceBuffer,
            GraphicsBuffer runtimeHeaderBuffer,
            GraphicsBuffer programBuffer,
            GraphicsBuffer surfaceBindingBuffer,
            GraphicsBuffer terrainMaterialBuffer,
            GraphicsBuffer terrainLayerBuffer,
            GraphicsBuffer meshLodNodeBuffer,
            GraphicsBuffer meshletBuffer,
            GraphicsBuffer vertexBuffer,
            GraphicsBuffer indexBuffer,
            int instanceCount,
            int materialCount,
            int programCount,
            int bindingCount,
            int materialParameterLaneCount,
            int materialResourceCount)
        {
            material.DisableKeyword(
                "VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE");
            material.SetTexture("_VisibilityBuffer", visibility);
            material.SetTexture("_VisibilityBufferAttributes0", attributes0);
            material.SetTexture("_VisibilityBufferAttributes1", attributes1);
            material.SetTexture(
                "_VisibilityBufferBarycentrics",
                barycentrics);
            Vector4 scaleBias = new(1.0f, 1.0f, 0.0f, 0.0f);
            material.SetVector("_VisibilityBufferScaleBias", scaleBias);
            material.SetVector(
                "_VisibilityBufferAttributes0ScaleBias",
                scaleBias);
            material.SetVector(
                "_VisibilityBufferAttributes1ScaleBias",
                scaleBias);
            material.SetVector(
                "_VisibilityBufferBarycentricsScaleBias",
                scaleBias);

            material.SetBuffer("_InstanceData", instanceBuffer);
            material.SetInt("_InstanceDataCount", instanceCount);
            material.SetBuffer("_MaterialData", materialBuffer);
            material.SetInt("_MaterialDataCount", materialCount);
            material.SetBuffer(
                "_DualSlabMaterialData",
                dualSlabMaterialBuffer);
            material.SetInt("_DualSlabMaterialDataCount", 1);
            material.SetBuffer(
                "_MaterialParameterData",
                materialParameterBuffer);
            material.SetInt(
                "_MaterialParameterDataCount",
                materialParameterLaneCount);
            material.SetBuffer(
                "_MaterialResourceData",
                materialResourceBuffer);
            material.SetInt(
                "_MaterialResourceDataCount",
                materialResourceCount);
            material.SetBuffer(
                "_MaterialRuntimeHeaders",
                runtimeHeaderBuffer);
            material.SetInt("_MaterialRuntimeHeaderCount", materialCount);
            material.SetBuffer("_MaterialPrograms", programBuffer);
            material.SetInt("_MaterialProgramCount", programCount);
            material.SetBuffer("_SurfaceBindingData", surfaceBindingBuffer);
            material.SetInt("_SurfaceBindingDataCount", bindingCount);
            material.SetBuffer("_TerrainMaterialData", terrainMaterialBuffer);
            material.SetInt("_TerrainMaterialDataCount", 1);
            material.SetBuffer("_TerrainLayerData", terrainLayerBuffer);
            material.SetInt("_TerrainLayerDataCount", 1);
            material.SetBuffer("_MeshLODNodes", meshLodNodeBuffer);
            material.SetInt("_MeshLODNodeCount", 1);
            material.SetBuffer("_Meshlets", meshletBuffer);
            material.SetInt("_MeshletCount", 1);
            material.SetBuffer("_SharedVertexBuffer", vertexBuffer);
            material.SetBuffer("_SharedIndexBuffer", indexBuffer);
        }

        private static void DispatchMaterialClassification(
            CommandBuffer commandBuffer,
            ComputeShader compute,
            int width,
            int height,
            int tileCount,
            RenderTexture gbuffer0,
            RenderTexture gbuffer1,
            RenderTexture depth,
            GraphicsBuffer featureFlags,
            GraphicsBuffer tileList,
            GraphicsBuffer indirectArgs)
        {
            int clearKernel = compute.FindKernel("ClearDeferredVariantArgs");
            string waveSuffix = SystemInfo.computeSubGroupSize switch
            {
                32 => "Wave32",
                64 => "Wave64",
                _ => string.Empty,
            };
            string classifyKernelName = "ClassifyDeferredExports" + waveSuffix;
            string buildKernelName =
                "BuildDeferredVariantIndirectArgs" + waveSuffix;
            if (!compute.HasKernel(classifyKernelName)
                || !compute.HasKernel(buildKernelName))
            {
                classifyKernelName = "ClassifyDeferredExports";
                buildKernelName = "BuildDeferredVariantIndirectArgs";
            }
            int classifyKernel = compute.FindKernel(classifyKernelName);
            int buildKernel = compute.FindKernel(buildKernelName);
            int Property(string name) => Shader.PropertyToID(name);

            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClassificationWidth"),
                width);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClassificationHeight"),
                height);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_MaterialTileCount"),
                tileCount);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_MaterialTileCountX"),
                tileCount);

            commandBuffer.SetComputeBufferParam(
                compute,
                clearKernel,
                Property("_MaterialFeatureIndirectArgs"),
                indirectArgs);
            commandBuffer.DispatchCompute(compute, clearKernel, 1, 1, 1);

            commandBuffer.SetComputeTextureParam(
                compute,
                classifyKernel,
                Property("_GBuffer0"),
                gbuffer0);
            commandBuffer.SetComputeTextureParam(
                compute,
                classifyKernel,
                Property("_GBuffer1"),
                gbuffer1);
            commandBuffer.SetComputeTextureParam(
                compute,
                classifyKernel,
                Property("_DepthTexture"),
                depth);
            commandBuffer.SetComputeBufferParam(
                compute,
                classifyKernel,
                Property("_MaterialTileFeatureFlags"),
                featureFlags);
            commandBuffer.DispatchCompute(
                compute,
                classifyKernel,
                tileCount,
                1,
                1);

            commandBuffer.SetComputeBufferParam(
                compute,
                buildKernel,
                Property("_MaterialTileFeatureFlags"),
                featureFlags);
            commandBuffer.SetComputeBufferParam(
                compute,
                buildKernel,
                Property("_MaterialFeatureTileList"),
                tileList);
            commandBuffer.SetComputeBufferParam(
                compute,
                buildKernel,
                Property("_MaterialFeatureIndirectArgs"),
                indirectArgs);
            commandBuffer.DispatchCompute(compute, buildKernel, 1, 1, 1);
        }

        private static void DispatchTileAdaptiveDualSlabSidecar(
            CommandBuffer commandBuffer,
            ComputeShader compute,
            Material sidecarMaterial,
            int width,
            int height,
            RenderTexture gbuffer0,
            RenderTexture gbuffer1,
            RenderTexture layerAux0,
            RenderTexture layerAux1,
            GraphicsBuffer tileList,
            GraphicsBuffer indirectArgs)
        {
            int clearKernel = compute.FindKernel(
                "ClearDualSlabSidecarDrawArgs");
            int classifyKernel = compute.FindKernel(
                "ClassifyDualSlabSidecarTiles");
            int Property(string name) => Shader.PropertyToID(name);

            commandBuffer.SetRenderTarget(
                new RenderTargetIdentifier[] { layerAux0, layerAux1 },
                BuiltinRenderTextureType.None);
            commandBuffer.ClearRenderTarget(
                clearDepth: false,
                clearColor: true,
                backgroundColor: Color.clear);

            commandBuffer.SetComputeBufferParam(
                compute,
                clearKernel,
                Property("_MaterialFeatureIndirectArgs"),
                indirectArgs);
            commandBuffer.DispatchCompute(compute, clearKernel, 1, 1, 1);

            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClassificationWidth"),
                width);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClassificationHeight"),
                height);
            commandBuffer.SetComputeTextureParam(
                compute,
                classifyKernel,
                Property("_GBuffer0"),
                gbuffer0);
            commandBuffer.SetComputeTextureParam(
                compute,
                classifyKernel,
                Property("_GBuffer1"),
                gbuffer1);
            commandBuffer.SetComputeBufferParam(
                compute,
                classifyKernel,
                Property("_MaterialFeatureTileList"),
                tileList);
            commandBuffer.SetComputeBufferParam(
                compute,
                classifyKernel,
                Property("_MaterialFeatureIndirectArgs"),
                indirectArgs);
            commandBuffer.DispatchCompute(
                compute,
                classifyKernel,
                (width + 7) / 8,
                (height + 7) / 8,
                1);

            sidecarMaterial.SetBuffer("_DualSlabSidecarTileList", tileList);
            sidecarMaterial.SetVector(
                "_DualSlabSidecarScreenSize",
                new Vector4(
                    width,
                    height,
                    1.0f / width,
                    1.0f / height));
            commandBuffer.DrawProceduralIndirect(
                Matrix4x4.identity,
                sidecarMaterial,
                0,
                MeshTopology.Triangles,
                indirectArgs,
                0);
        }

        private static void DispatchDeferredLighting(
            CommandBuffer commandBuffer,
            ComputeShader compute,
            int width,
            int height,
            int tileCount,
            RenderTexture gbuffer0,
            RenderTexture gbuffer1,
            RenderTexture gbuffer2,
            RenderTexture gbuffer3,
            RenderTexture diffuseIrradiance,
            RenderTexture layerAux0,
            RenderTexture layerAux1,
            RenderTexture depth,
            RenderTexture directionalShadow,
            RenderTexture gtao,
            RenderTexture screenSpaceReflection,
            RenderTexture lighting,
            RenderTexture lightingDebug,
            GraphicsBuffer featureFlags,
            GraphicsBuffer tileList,
            GraphicsBuffer indirectArgs,
            GraphicsBuffer preExposure,
            GraphicsBuffer ambientProbe,
            GraphicsBuffer directionalLights,
            GraphicsBuffer punctualLights,
            GraphicsBuffer areaLights,
            GraphicsBuffer reflectionProbes,
            GraphicsBuffer layeredOffset,
            GraphicsBuffer layeredLightList,
            GraphicsBuffer logBaseBuffer,
            Texture ggxFgd,
            Texture charlieFgd,
            Texture ltcData,
            Texture reflectionAtlas,
            Texture skyTexture)
        {
            int Property(string name) => Shader.PropertyToID(name);
            int clearKernel = compute.FindKernel("ClearDeferredLit");
            int[] variantKernels =
            {
                compute.FindKernel("DeferredLit_Variant0"),
                compute.FindKernel("DeferredLit_Variant1"),
                compute.FindKernel("DeferredLit_Variant2"),
                compute.FindKernel("DeferredLit_Variant3"),
            };

            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ScreenSpaceReflectionEnabled"),
                1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_MaterialTileCountX"),
                tileCount);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_DirectionalLightCount"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_PunctualLightCount"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_AreaLightCount"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ReflectionProbeCount"),
                0);
            commandBuffer.SetComputeIntParam(compute, Property("_DecalCount"), 0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_MainDirectionalLightIndex"),
                -1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusteredPunctualLightGridEnabled"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusteredAreaLightGridEnabled"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusteredReflectionProbeGridEnabled"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusteredDecalGridEnabled"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusterTileSize"),
                8);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusterSliceCount"),
                1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusterTileCountX"),
                tileCount);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusterTileCountY"),
                1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ClusterIsOrthographic"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_NumTileClusteredX"),
                tileCount);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_NumTileClusteredY"),
                1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("g_iLog2NumClusters"),
                0);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("g_isLogBaseBufferEnabled"),
                0);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("_ClusterNearClip"),
                0.1f);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("_ClusterFarClip"),
                100.0f);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("g_fNearPlane"),
                0.1f);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("g_fFarPlane"),
                100.0f);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("g_fClustScale"),
                0.0f);
            commandBuffer.SetComputeFloatParam(
                compute,
                Property("g_fClustBase"),
                1.02f);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ReflectionAtlasMipCount"),
                1);
            commandBuffer.SetComputeIntParam(
                compute,
                Property("_ReflectionAtlasSliceCount"),
                1);
            commandBuffer.SetComputeVectorParam(
                compute,
                Property("_ReflectionAtlasCubeData"),
                Vector4.zero);
            commandBuffer.SetComputeVectorParam(
                compute,
                Property("_SkyTextureTint"),
                Color.black);
            commandBuffer.SetComputeVectorParam(
                compute,
                Property("_SkyTextureParams"),
                Vector4.zero);
            commandBuffer.SetComputeMatrixParam(
                compute,
                Property("_PixelCoordToViewDirWS"),
                Matrix4x4.identity);

            BindDeferredSharedTextures(
                commandBuffer,
                compute,
                clearKernel,
                gbuffer0,
                gbuffer1,
                gbuffer2,
                gbuffer3,
                diffuseIrradiance,
                layerAux0,
                layerAux1,
                depth,
                directionalShadow,
                gtao,
                screenSpaceReflection,
                lighting,
                lightingDebug);
            commandBuffer.SetComputeTextureParam(
                compute,
                clearKernel,
                Property("_SkyTexture"),
                skyTexture);
            commandBuffer.SetComputeBufferParam(
                compute,
                clearKernel,
                Property("_VividAutoExposurePreExposureBuffer"),
                preExposure);
            commandBuffer.DispatchCompute(
                compute,
                clearKernel,
                tileCount,
                1,
                1);

            for (var variant = 0; variant < variantKernels.Length; variant++)
            {
                int kernel = variantKernels[variant];
                BindDeferredSharedTextures(
                    commandBuffer,
                    compute,
                    kernel,
                    gbuffer0,
                    gbuffer1,
                    gbuffer2,
                    gbuffer3,
                    diffuseIrradiance,
                    layerAux0,
                    layerAux1,
                    depth,
                    directionalShadow,
                    gtao,
                    screenSpaceReflection,
                    lighting,
                    lightingDebug);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    kernel,
                    Property("_PreIntegratedFGD_GGXDisneyDiffuse"),
                    ggxFgd);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    kernel,
                    Property("_PreIntegratedFGD_CharlieAndFabric"),
                    charlieFgd);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    kernel,
                    Property("_LtcData"),
                    ltcData);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    kernel,
                    Property("_ReflectionAtlas"),
                    reflectionAtlas);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    kernel,
                    Property("_SkyTexture"),
                    skyTexture);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_VividAutoExposurePreExposureBuffer"),
                    preExposure);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_VividAmbientProbeData"),
                    ambientProbe);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_DirectionalLights"),
                    directionalLights);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_PunctualLights"),
                    punctualLights);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_AreaLights"),
                    areaLights);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_ReflectionProbes"),
                    reflectionProbes);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("g_LayeredOffset"),
                    layeredOffset);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("g_vLayeredLightList"),
                    layeredLightList);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("g_logBaseBuffer"),
                    logBaseBuffer);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_MaterialTileFeatureFlags"),
                    featureFlags);
                commandBuffer.SetComputeBufferParam(
                    compute,
                    kernel,
                    Property("_MaterialFeatureTileList"),
                    tileList);
                commandBuffer.SetComputeIntParam(
                    compute,
                    Property("_MaterialFeatureTileListOffset"),
                    variant * tileCount);
                commandBuffer.DispatchCompute(
                    compute,
                    kernel,
                    indirectArgs,
                    (uint) (variant * 4 * sizeof(uint)));
            }
        }

        private static void BindDeferredSharedTextures(
            CommandBuffer commandBuffer,
            ComputeShader compute,
            int kernel,
            RenderTexture gbuffer0,
            RenderTexture gbuffer1,
            RenderTexture gbuffer2,
            RenderTexture gbuffer3,
            RenderTexture diffuseIrradiance,
            RenderTexture layerAux0,
            RenderTexture layerAux1,
            RenderTexture depth,
            RenderTexture directionalShadow,
            RenderTexture gtao,
            RenderTexture screenSpaceReflection,
            RenderTexture lighting,
            RenderTexture lightingDebug)
        {
            int Property(string name) => Shader.PropertyToID(name);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_GBuffer0"), gbuffer0);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_GBuffer1"), gbuffer1);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_GBuffer2"), gbuffer2);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_GBuffer3"), gbuffer3);
            commandBuffer.SetComputeTextureParam(
                compute,
                kernel,
                Property("_DiffuseIrradiance"),
                diffuseIrradiance);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_LayerAux0"), layerAux0);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_LayerAux1"), layerAux1);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_DepthTexture"), depth);
            commandBuffer.SetComputeTextureParam(
                compute,
                kernel,
                Property("_DirectionalShadowTexture"),
                directionalShadow);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_GTAOTexture"), gtao);
            commandBuffer.SetComputeTextureParam(
                compute,
                kernel,
                Property("_ScreenSpaceReflectionTexture"),
                screenSpaceReflection);
            commandBuffer.SetComputeTextureParam(
                compute, kernel, Property("_LightingTexture"), lighting);
            commandBuffer.SetComputeTextureParam(
                compute,
                kernel,
                Property("_LightingDebugTexture"),
                lightingDebug);
        }

        private static string BuildTestShaderSource()
        {
            return $@"Shader ""Hidden/VividRP/Tests/MaterialProgramAotGpu""
{{
    SubShader
    {{
        Pass
        {{
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl""
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl""
            #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #include_with_pragmas ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl""

            // Pull the shared Slab detail helpers without importing the builtin dispatcher.
            #define VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl""
            #undef VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED

            struct VividMaterialCoverageEvaluation
            {{
                float Coverage;
                float AlphaClipThreshold;
            }};

            #include ""{CoverageIncludePath}""
            #include ""{SurfaceIncludePath}""

            struct Varyings
            {{
                float4 positionCS : SV_POSITION;
            }};

            Varyings Vert(uint vertexID : SV_VertexID)
            {{
                const float2 corner = float2(
                    (vertexID << 1u) & 2u,
                    vertexID & 2u);
                Varyings output;
                output.positionCS = float4(corner * 2.0f - 1.0f, 0.0f, 1.0f);
                return output;
            }}

            float4 Frag(Varyings input) : SV_Target
            {{
                const uint materialIndex = 0u;
                const VividMaterialRuntimeHeader runtimeHeader =
                    PullMaterialRuntimeHeader(materialIndex);
                const VividMaterialProgramData programData =
                    PullMaterialProgramData(runtimeHeader.ProgramID);

                VividAOTCoverageContext coverageContext;
                coverageContext.UV0 = float2(0.25f, 0.75f);
                coverageContext.UV0Ddx = 0.0f.xx;
                coverageContext.UV0Ddy = 0.0f.xx;
                VividMaterialCoverageEvaluation coverage =
                    (VividMaterialCoverageEvaluation) 0;
                const bool coverageSucceeded =
                    VividTryEvaluateAOTCoverageProgram(
                        runtimeHeader,
                        programData,
                        coverageContext,
                        coverage);

                const bool payloadLoaded =
                    runtimeHeader.ProgramID < _MaterialProgramCount
                    && programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
                    && programData.ParameterLayoutID
                        == VIVIDMATERIALPARAMETERLAYOUTID_GENERIC_PARAMETER_LANES
                    && programData.ResourceLayoutID
                        == VIVIDMATERIALRESOURCELAYOUTID_GENERIC_RESOURCE_RECORDS;

                VividAOTSurfaceContext surfaceContext;
                surfaceContext.UV0 = coverageContext.UV0;
                surfaceContext.UV0Ddx = coverageContext.UV0Ddx;
                surfaceContext.UV0Ddy = coverageContext.UV0Ddy;
                surfaceContext.GeometryNormalWS = float3(0.0f, 0.0f, 1.0f);
                surfaceContext.GeometryTangentWS = float4(1.0f, 0.0f, 0.0f, 1.0f);
                surfaceContext.PositionCS = input.positionCS;
                VividAOTSurfaceProgramOutput surfaceOutput =
                    (VividAOTSurfaceProgramOutput) 0;
                VividAOTDeferredExportContract deferredExportContract =
                    (VividAOTDeferredExportContract) 0;
                const bool surfaceSucceeded = payloadLoaded
                    && VividTryEvaluateAOTSurfaceProgram(
                        runtimeHeader,
                        programData,
                        surfaceContext,
                        deferredExportContract,
                        surfaceOutput);

                const uint outputIndex = (uint) input.positionCS.x;
                if (outputIndex == 0u)
                {{
                    return float4(
                        coverage.Coverage,
                        coverage.AlphaClipThreshold,
                        coverageSucceeded ? 1.0f : 0.0f,
                        surfaceSucceeded ? 1.0f : 0.0f);
                }}
                if (outputIndex == 1u)
                {{
                    return float4(
                        surfaceOutput.BaseSlab.BaseColor.rgb,
                        surfaceOutput.BaseSlab.PerceptualRoughness);
                }}
                return float4(
                    surfaceOutput.BaseSlab.Metallic,
                    surfaceOutput.Emission);
            }}
            ENDHLSL
        }}
    }}
}}";
        }

        private static void RequireShaderModel66()
        {
            File.WriteAllText(
                CapabilityShaderPath,
                @"Shader ""Hidden/VividRP/Tests/MaterialProgramAotGpuCapability""
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma editor_sync_compilation
            #pragma require Int64BufferAtomics
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                const float2 corner = float2(
                    (vertexID << 1u) & 2u,
                    vertexID & 2u);
                return float4(corner * 2.0f - 1.0f, 0.0f, 1.0f);
            }

            float4 Frag() : SV_Target
            {
                return 0.0f.xxxx;
            }
            ENDHLSL
        }
    }
}");
            AssetDatabase.ImportAsset(
                CapabilityShaderPath,
                ImportAssetOptions.ForceSynchronousImport);
            Shader capabilityShader =
                AssetDatabase.LoadAssetAtPath<Shader>(CapabilityShaderPath);
            if (capabilityShader == null
                || !capabilityShader.isSupported
                || ShaderUtil.ShaderHasError(capabilityShader))
            {
                Assert.Ignore(
                    "The current D3D12 device/editor cannot compile the required DXC Shader Model 6.6 feature set.");
            }
        }

        private static void EnsureTemporaryFolder()
        {
            AssetDatabase.DeleteAsset(TemporaryFolder);
            if (!AssetDatabase.IsValidFolder(TemporaryFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "VividMaterialProgramAotGpuTests");
            }
        }

        private static void AssertColor(
            Color actual,
            Color expected,
            string message)
        {
            const float tolerance = 0.015f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), message);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), message);
        }
    }
}
