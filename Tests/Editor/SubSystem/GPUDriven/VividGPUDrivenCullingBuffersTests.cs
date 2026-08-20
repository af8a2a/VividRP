using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenCullingBuffersTests
    {
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
