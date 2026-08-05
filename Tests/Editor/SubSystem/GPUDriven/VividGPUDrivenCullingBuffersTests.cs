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

                VividGPUDrivenCullingContextUtility.Build(
                    camera,
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
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }

        [Test]
        public void Reset_UsesNativeUploadArrays_ForNoGcCommandBufferSetBufferData()
        {
            string source = File.ReadAllText(
                GetPackageFilePath("Runtime", "SubSystem", "GPUDriven", "VividGPUDrivenCullingBuffers.cs"));

            Assert.That(source, Does.Contain("NativeArray<IndirectDispatchArgs> m_InitialIndirectDispatchArgsUpload"));
            Assert.That(source, Does.Contain("NativeArray<uint> m_ZeroUintUpload"));
            Assert.That(source, Does.Contain("NativeArray<uint> m_ZeroRendererListCountsUpload"));
            Assert.That(source, Does.Contain("NativeArray<uint> m_ZeroIndirectDrawArgsWordsUpload"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(MeshletListBuildJobCounterBuffer, m_ZeroUintUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(MeshletListBuildIndirectArgsBuffer, m_InitialIndirectDispatchArgsUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(VisibleRendererListMeshletCountsBuffer, m_ZeroRendererListCountsUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(VisibleMeshletIndirectDrawArgsBuffer, m_ZeroIndirectDrawArgsWordsUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(OccludedMeshletRenderRequestCounterBuffer, m_ZeroUintUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(OccludedMeshletIndirectDispatchArgsBuffer, m_InitialIndirectDispatchArgsUpload);"));
            Assert.That(source, Does.Contain("cmd.SetBufferData(RecoveredMeshletIndirectDrawArgsBuffer, m_ZeroIndirectDrawArgsWordsUpload);"));
            Assert.That(source, Does.Not.Contain("static readonly uint[] s_Zero"));
            Assert.That(source, Does.Not.Contain("IndirectDispatchArgs[] s_InitialIndirectDispatchArgs"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var path = Path.Combine("Packages", "VividRP");
            foreach (var part in relativeParts)
                path = Path.Combine(path, part);

            return path;
        }
    }
}
