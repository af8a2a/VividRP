using System.IO;
using Unity.Mathematics;
using VividRP.Runtime.VirtualShadowMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenCullingBuffersTests
    {
        [TestCase(0)] // Ordinary CS path used by main view / CSM.
        [TestCase(1)] // VSM without view compaction.
        [TestCase(2)] // Production VSM hierarchy + compacted views.
        public void Dispatcher_LODSentinel_SelectsAutomaticErrorsAndPreservesForcedDepth(int mode)
        {
            Assume.That(SystemInfo.supportsComputeShaders, Is.True);
            const int nodesPerLevel = 32, nodeCount = 3 * nodesPerLevel;
            var scene = new VividGPUDrivenSceneData();
            scene.MutableMaterials.Add(default);
            for (int n = 0; n < nodeCount; n++)
            {
                int level = n / nodesPerLevel;
                scene.MutableMeshLODNodes.Add(new VividMeshLODNode
                {
                    Bounds = new float4(.5f, .5f, 0, .01f),
                    Error = level == 0 ? 1f : level == 1 ? .1f : 0f,
                    ParentError = level == 0 ? -1f : level == 1 ? 1f : .1f,
                    ParentBounds = new float4(.5f, .5f, 0, .01f),
                    LevelIndex = (uint)level, MeshletStartIndex = (uint)n, MeshletCount = 1,
                });
                scene.MutableMeshlets.Add(new VividMeshlet { BoundingSphere = new float4(.5f, .5f, 0, .01f) });
            }
            scene.AddInstance(new VividInstanceData
            {
                ObjectToWorldMatrix = float4x4.identity, WorldToObjectMatrix = float4x4.identity,
                AABBMin = new float4(.48f, .48f, -.02f, 0), AABBMax = new float4(.52f, .52f, .02f, 0),
                TotalMeshLODCount = nodeCount, MeshLODLevelCount = 3, LODErrorScale = 1,
                PassMask = VividInstancePassMask.Shadows, Flags = VividInstanceFlags.TwoSidedShadows,
            }, nodeCount);
            using var sceneBuffers = new VividGPUDrivenBufferSet();
            sceneBuffers.Upload(scene);
            using var dispatcher = new VividGPUDrivenCullingDispatcher(supportsOcclusion: false);
            using var cmd = new CommandBuffer();
            using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 160);
            using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 12);
            using var bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var views = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            projections.SetData(new[] { new VirtualShadowMapProjection { WorldToShadow = Matrix4x4.identity } });
            hierarchy.SetData(new[] { new uint3(2u | 4u, uint.MaxValue, uint.MaxValue) });
            bounds.SetData(new[] { new uint4(0), new uint4(0) });
            var parameters = mode == 0 ? default : new VirtualShadowMapCullingParameters(
                projections, hierarchy, bounds, 1, 128, 128, 0, false, mode == 2 ? views : null, mode == 2 ? args : null);
            var contexts = new[] { new VividGPUCullingContext
                { ViewProjectionMatrix = float4x4.identity, PassMask = (int)VividInstancePassMask.Shadows } };
            var lod = new VividGPULODSelectionContext { ScreenSizePixels = new float2(128) };
            var shaders = new ComputeShader[4];
            string[] names = { "GPUInstanceCulling", "MeshletListBuild", "GPUMeshletCulling", "FixupVisibleMeshletIndirectDrawArgs" };
            try
            {
                for (int i = 0; i < shaders.Length; i++) shaders[i] = Object.Instantiate(
                    UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/" + names[i] + ".compute"));
                // 32 records per level put the levels in separate hierarchy leaves.
                // A wrong tree sentinel must not hide nodes from a correct leaf selector.
                int[] depths = { -1, -1, -1, -2, int.MinValue, 0, 1, 2, 8, int.MaxValue, -1 };
                float[] thresholds = { .02f, .2f, 2f, .2f, .2f, .02f, 2f, 2f, 2f, 2f, .2f };
                uint[] levels = { 2, 1, 0, 1, 1, 0, 1, 2, 2, 2, 1 };
                var count = new uint[1]; var requests = new VividMeshletRenderRequestPacked[nodeCount];
                for (int i = 0; i < depths.Length; i++)
                {
                    cmd.Clear();
                    dispatcher.DispatchBatch(cmd, contexts, 1, lod, scene, sceneBuffers,
                        shaders[0], shaders[1], shaders[2], shaders[3], depths[i], thresholds[i], vsmCulling: parameters);
                    Graphics.ExecuteCommandBuffer(cmd);
                    dispatcher.BufferSet.VisibleMeshletRenderRequestCounterBuffer.GetData(count);
                    dispatcher.BufferSet.CandidateMeshletRenderRequestsBuffer.GetData(requests);
                    Assert.That(count[0], Is.EqualTo((uint)nodesPerLevel), $"mode={mode}, depth={depths[i]}, threshold={thresholds[i]}");
                    uint seen = 0;
                    for (int n = 0; n < count[0]; n++)
                    {
                        Assert.That(requests[n].MeshletID / nodesPerLevel, Is.EqualTo(levels[i]));
                        seen |= 1u << (int)(requests[n].MeshletID % nodesPerLevel);
                    }
                    Assert.That(seen, Is.EqualTo(uint.MaxValue));
                }
            }
            finally { foreach (var shader in shaders) if (shader != null) Object.DestroyImmediate(shader); }
        }

        [Test]
        public void EnsureCapacity_CreatesIndirectDrawArgsBufferForAllRendererLists()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletCount = 4,
            });
            sceneData.AddInstance(
                new VividInstanceData
                {
                    TopMeshLODStartIndex = 0,
                    TotalMeshLODCount = 1,
                },
                maxVisibleMeshletRenderRequestCount: 4);

            using var bufferSet = new VividGPUDrivenCullingBuffers();
            bufferSet.EnsureCapacity(sceneData);

            Assert.That(bufferSet.MeshletListBuildJobsBuffer, Is.Not.Null);
            Assert.That(bufferSet.CandidateMeshletRenderRequestsBuffer, Is.Not.Null);
            Assert.That(bufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer, Is.Not.Null);
            Assert.That(bufferSet.VisibleMeshletRenderRequestsBuffer, Is.Not.Null);
            Assert.That(bufferSet.VisibleMeshletIndirectDrawArgsBuffer, Is.Not.Null);
            Assert.That(bufferSet.OccludedMeshletRenderRequestsBuffer, Is.Not.Null);
            Assert.That(bufferSet.OccludedMeshletRenderRequestCounterBuffer, Is.Not.Null);
            Assert.That(bufferSet.OccludedMeshletIndirectDispatchArgsBuffer, Is.Not.Null);
            Assert.That(bufferSet.RecoveredMeshletRenderRequestsBuffer, Is.Not.Null);
            Assert.That(bufferSet.RecoveredRendererListMeshletCountsBuffer, Is.Not.Null);
            Assert.That(bufferSet.RecoveredMeshletIndirectDrawArgsBuffer, Is.Not.Null);
            Assert.That(bufferSet.CandidateMeshletRenderRequestsBuffer.count, Is.EqualTo(4));
            Assert.That(bufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer.count, Is.EqualTo(3));
            Assert.That(bufferSet.VisibleMeshletIndirectDrawArgsBuffer.count, Is.EqualTo((int) VividRendererListID.Count * 4));
            Assert.That(bufferSet.VisibleMeshletIndirectDrawArgsBuffer.stride, Is.EqualTo(sizeof(uint)));
            Assert.That(
                bufferSet.VisibleMeshletIndirectDrawArgsBuffer.target,
                Is.EqualTo(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments)
            );
            Assert.That(bufferSet.MaxVisibleMeshletRenderRequestCount, Is.EqualTo(4));
        }

        [Test]
        public void EnsureCapacity_DoesNotAllocateOcclusionBuffers_WhenOcclusionIsUnsupported()
        {
            var sceneData = new VividGPUDrivenSceneData();
            using var bufferSet = new VividGPUDrivenCullingBuffers(supportsOcclusion: false);

            bufferSet.EnsureCapacity(sceneData);

            Assert.That(bufferSet.SupportsOcclusion, Is.False);
            Assert.That(bufferSet.VisibleMeshletRenderRequestsBuffer, Is.Not.Null);
            Assert.That(bufferSet.OccludedMeshletRenderRequestsBuffer, Is.Null);
            Assert.That(bufferSet.OccludedMeshletRenderRequestCounterBuffer, Is.Null);
            Assert.That(bufferSet.OccludedMeshletIndirectDispatchArgsBuffer, Is.Null);
            Assert.That(bufferSet.RecoveredMeshletRenderRequestsBuffer, Is.Null);
            Assert.That(bufferSet.RecoveredRendererListMeshletCountsBuffer, Is.Null);
            Assert.That(bufferSet.RecoveredMeshletIndirectDrawArgsBuffer, Is.Null);
        }

        [Test]
        public void EnsureCapacity_CreatesDisjointBuffersForBatchedCullingContexts()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletCount = 4,
            });
            sceneData.AddInstance(
                new VividInstanceData
                {
                    TopMeshLODStartIndex = 0,
                    TotalMeshLODCount = 1,
                },
                maxVisibleMeshletRenderRequestCount: 4);

            using var bufferSet = new VividGPUDrivenCullingBuffers(supportsOcclusion: false);
            bufferSet.EnsureCapacity(sceneData, 4);

            Assert.That(bufferSet.CullingContextCount, Is.EqualTo(4));
            Assert.That(bufferSet.CullingContextBuffer.count, Is.EqualTo(4));
            Assert.That(bufferSet.MeshletListBuildJobsBuffer.count,
                Is.EqualTo(bufferSet.MaxMeshletListBuildJobCount * 4));
            Assert.That(bufferSet.MeshletListBuildJobCounterBuffer.count, Is.EqualTo(4));
            Assert.That(bufferSet.CandidateMeshletRenderRequestsBuffer.count, Is.EqualTo(16));
            Assert.That(bufferSet.VisibleMeshletRenderRequestsBuffer.count, Is.EqualTo(16));
            Assert.That(bufferSet.VisibleMeshletRenderRequestCounterBuffer.count, Is.EqualTo(4));
            Assert.That(bufferSet.VisibleRendererListMeshletCountsBuffer.count,
                Is.EqualTo((int)VividRendererListID.Count * 4));
            Assert.That(bufferSet.VisibleMeshletIndirectDrawArgsBuffer.count,
                Is.EqualTo((int)VividRendererListID.Count * 4 * 4));

            bufferSet.EnsureCapacity(sceneData, 1);

            Assert.That(bufferSet.CullingContextCount, Is.EqualTo(1));
            Assert.That(bufferSet.CullingContextBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.VisibleMeshletRenderRequestsBuffer.count, Is.EqualTo(4));
            Assert.That(bufferSet.VisibleMeshletRenderRequestCounterBuffer.count, Is.EqualTo(1));
        }

        [Test]
        public void BatchedOffsets_AreCascadeMajorAndDisjoint()
        {
            int rendererListCount = (int)VividRendererListID.Count;

            Assert.That(VividGPUDrivenCullingBuffers.GetContextOffset(3, 17), Is.EqualTo(51));
            Assert.That(
                VividGPUDrivenCullingBuffers.GetIndirectDrawArgsCommandIndex(1, 0),
                Is.EqualTo(rendererListCount));
            Assert.That(
                VividGPUDrivenCullingBuffers.GetIndirectDrawArgsCommandIndex(3, rendererListCount - 1),
                Is.EqualTo(rendererListCount * 4 - 1));
            Assert.That(
                VividGPUDrivenCullingBuffers.GetIndirectDrawArgsByteOffset(1, 0),
                Is.EqualTo(rendererListCount * VividGPUDrivenCullingBuffers.IndirectDrawArgsByteStride));
        }

        [Test]
        public void UploadContexts_DoesNotThrow_WhenUsingCommandBufferSetBufferData()
        {
            var sceneData = new VividGPUDrivenSceneData();
            using var bufferSet = new VividGPUDrivenCullingBuffers();
            bufferSet.EnsureCapacity(sceneData);

            GameObject cameraObject = null;
            CommandBuffer cmd = null;

            try
            {
                cameraObject = new GameObject("GPUDrivenCullingBuffersCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                cmd = CommandBufferPool.Get("GPUDrivenCullingBuffers");

                camera.Build(
                    VividInstancePassMask.Main,
                    out VividGPUCullingContext cullingContext,
                    out VividGPULODSelectionContext lodSelectionContext
                );

                Assert.DoesNotThrow(() => bufferSet.UploadContexts(cmd, cullingContext, lodSelectionContext));
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                }

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }

        [Test]
        public void GPUInstanceCullingShader_MapsDrawSetEntriesToLegacyInstanceIds()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VividGPUDrivenSceneData).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "GPUInstanceCulling.compute");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            StringAssert.Contains("StructuredBuffer<uint> _VividPrimitiveDrawSetInstanceIndices", source);
            StringAssert.Contains("dispatchInstanceIndex >= _VividPrimitiveDrawSetInstanceCount", source);
            StringAssert.Contains("_VividPrimitiveDrawSetInstanceIndices[dispatchInstanceIndex]", source);
            StringAssert.Contains("instanceID >= _InstanceDataCount", source);
            StringAssert.Contains("job.InstanceID = instanceID", source);

            Assert.That(
                VividGPUDrivenShaderIDs._VividPrimitiveDrawSetInstanceIndices,
                Is.EqualTo(Shader.PropertyToID("_VividPrimitiveDrawSetInstanceIndices")));
            Assert.That(
                VividGPUDrivenShaderIDs._VividPrimitiveDrawSetInstanceCount,
                Is.EqualTo(Shader.PropertyToID("_VividPrimitiveDrawSetInstanceCount")));
            Assert.That(
                VividGPUDrivenShaderIDs._VividPrimitiveDrawSetEnabled,
                Is.EqualTo(Shader.PropertyToID("_VividPrimitiveDrawSetEnabled")));
        }

        [Test]
        public void Dispatcher_DrawSetCountContractAcceptsZeroAndValidPositiveInputs()
        {
            var sceneData = new VividGPUDrivenSceneData();
            using var sceneBuffers = new VividGPUDrivenBufferSet();
            using var dispatcher = new VividGPUDrivenCullingDispatcher(supportsOcclusion: false);
            using var drawSetIndices = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint));
            drawSetIndices.SetData(new[] { 0u });

            GameObject cameraObject = null;
            CommandBuffer cmd = null;
            try
            {
                cameraObject = new GameObject("GPUDrivenDrawSetContractCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.Build(
                    VividInstancePassMask.Main,
                    out VividGPUCullingContext cullingContext,
                    out VividGPULODSelectionContext lodSelectionContext);
                cmd = CommandBufferPool.Get("GPUDrivenDrawSetContract");

                Assert.DoesNotThrow(() => dispatcher.Dispatch(
                    cmd,
                    cullingContext,
                    lodSelectionContext,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    null,
                    0));
                Assert.DoesNotThrow(() => dispatcher.Dispatch(
                    cmd,
                    cullingContext,
                    lodSelectionContext,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    drawSetIndices,
                    1));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => dispatcher.Dispatch(
                    cmd,
                    cullingContext,
                    lodSelectionContext,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    drawSetIndices,
                    2));
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                }
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Dispatcher_BatchDrawSetCountContractAcceptsZeroAndValidPositiveInputs()
        {
            var sceneData = new VividGPUDrivenSceneData();
            using var sceneBuffers = new VividGPUDrivenBufferSet();
            using var dispatcher = new VividGPUDrivenCullingDispatcher(supportsOcclusion: false);
            using var drawSetIndices = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint));
            drawSetIndices.SetData(new[] { 0u });
            var cullingContexts = new VividGPUCullingContext[1];
            CommandBuffer cmd = null;
            try
            {
                cmd = CommandBufferPool.Get("GPUDrivenBatchDrawSetContract");

                Assert.DoesNotThrow(() => dispatcher.DispatchBatch(
                    cmd,
                    cullingContexts,
                    1,
                    default,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    null,
                    0));
                Assert.DoesNotThrow(() => dispatcher.DispatchBatch(
                    cmd,
                    cullingContexts,
                    1,
                    default,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    drawSetIndices,
                    1));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => dispatcher.DispatchBatch(
                    cmd,
                    cullingContexts,
                    1,
                    default,
                    sceneData,
                    sceneBuffers,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0.0f,
                    default,
                    drawSetIndices,
                    2));
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                }
            }
        }
    }
}
