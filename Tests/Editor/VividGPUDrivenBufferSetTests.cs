using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenBufferSetTests
    {
        [Test]
        public void Upload_CreatesExpectedBufferCounts_WhenSceneDataContainsData()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(new VividInstanceData
            {
                ObjectToWorldMatrix = float4x4.identity,
                WorldToObjectMatrix = float4x4.identity,
            });
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                AlbedoColor = new float4(1.0f, 1.0f, 1.0f, 1.0f),
            });
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletStartIndex = 0,
                MeshletCount = 1,
            });
            sceneData.MutableMeshlets.Add(new VividMeshlet
            {
                VertexCount = 3,
                TriangleCount = 1,
            });
            sceneData.MutableVertices.Add(new VividMeshletVertex
            {
                Position = new float4(0.0f, 0.0f, 0.0f, 1.0f),
            });
            sceneData.MutableIndices.Add(0);
            sceneData.MutableIndices.Add(1);
            sceneData.MutableIndices.Add(2);

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceCount, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshLODNodeCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshletCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedVertexCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedIndexCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshLODNodesBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshletsBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedVertexBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedIndexBuffer, Is.Not.Null);
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MeshLODNodesBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MeshletsBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.SharedVertexBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.SharedIndexBuffer.count, Is.EqualTo(1));
        }

        [Test]
        public void Upload_ResizesBuffers_WhenSceneDataCountChanges()
        {
            var sceneData = new VividGPUDrivenSceneData();
            using var bufferSet = new VividGPUDrivenBufferSet();

            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));

            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(default);
            sceneData.MutableMaterials.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(3));
        }

        [Test]
        public void BindGlobals_DoesNotThrow_WhenBuffersWereUploaded()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(default);

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            CommandBuffer cmd = CommandBufferPool.Get("VividGPUDrivenBufferSetTests");

            try
            {
                Assert.DoesNotThrow(() => bufferSet.BindGlobals(cmd));
            }
            finally
            {
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }

        [Test]
        public void Upload_KeepsStaticVertexBufferContents_WhenStaticUploadIsSkipped()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(default);
            sceneData.MutableMeshLODNodes.Add(new VividMeshLODNode
            {
                MeshletStartIndex = 0,
                MeshletCount = 1,
            });
            sceneData.MutableMeshlets.Add(new VividMeshlet
            {
                VertexCount = 1,
                TriangleCount = 1,
            });
            sceneData.MutableVertices.Add(new VividMeshletVertex
            {
                Position = new float4(1.0f, 2.0f, 3.0f, 1.0f),
            });
            sceneData.MutableIndices.Add(0);
            sceneData.MutableIndices.Add(0);
            sceneData.MutableIndices.Add(0);

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(default);
            sceneData.MutableVertices[0] = new VividMeshletVertex
            {
                Position = new float4(9.0f, 9.0f, 9.0f, 1.0f),
            };

            bufferSet.Upload(sceneData, false);

            var vertices = new VividMeshletVertex[1];
            bufferSet.SharedVertexBuffer.GetData(vertices);

            Assert.That(vertices[0].Position.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(vertices[0].Position.y, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(vertices[0].Position.z, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(2));
        }
    }
}
