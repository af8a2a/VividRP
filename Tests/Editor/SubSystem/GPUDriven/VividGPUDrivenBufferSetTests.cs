using System.IO;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenBufferSetTests
    {
        [Test]
        public void GPUDataLayouts_HaveExpectedStrides()
        {
            Assert.That(UnsafeUtility.SizeOf<VividMaterialData>(), Is.EqualTo(96));
            Assert.That(UnsafeUtility.SizeOf<VividSurfaceBindingData>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividTerrainMaterialData>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividTerrainLayerGPUData>(), Is.EqualTo(48));
        }

        [Test]
        public void GeneratedShaderInclude_ContainsSurfaceBindingLayout()
        {
            string source = File.ReadAllText(GetGeneratedStructIncludePath());

            Assert.That(source, Does.Contain("uint SurfaceBindingIndex;"));
            Assert.That(source, Does.Contain("struct VividSurfaceBindingData"));
            Assert.That(source, Does.Contain("uint BaseColorResource;"));
            Assert.That(source, Does.Contain("uint NormalResource;"));
            Assert.That(source, Does.Contain("uint MaskResource;"));
            Assert.That(source, Does.Contain("uint Flags;"));
            Assert.That(source, Does.Contain("float4 UVScaleBias;"));
            Assert.That(source, Does.Not.Contain("uint AlbedoIndex;"));
        }

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
            sceneData.MutableSurfaceBindings.Add(new VividSurfaceBindingData
            {
                BaseColorResource = 4u,
                UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
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
            Assert.That(bufferSet.SurfaceBindingCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshLODNodeCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshletCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedVertexCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedIndexCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshLODNodesBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshletsBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedVertexBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedIndexBuffer, Is.Not.Null);
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(1));
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
            sceneData.MutableSurfaceBindings.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(1));

            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(default);
            sceneData.MutableMaterials.Add(default);
            sceneData.MutableSurfaceBindings.Add(default);
            sceneData.MutableSurfaceBindings.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(3));
            Assert.That(bufferSet.SurfaceBindingCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(3));
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(3));
        }

        [Test]
        public void BindGlobals_DoesNotThrow_WhenBuffersWereUploaded()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(default);

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.SurfaceBindingCount, Is.Zero);
            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(1));

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
            sceneData.MutableVertices[0] = new VividMeshletVertex
            {
                Position = new float4(9.0f, 9.0f, 9.0f, 1.0f),
            };

            bufferSet.Upload(sceneData, uploadMaterialData: false, uploadStaticData: false);

            var vertices = new VividMeshletVertex[1];
            bufferSet.SharedVertexBuffer.GetData(vertices);

            Assert.That(vertices[0].Position.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(vertices[0].Position.y, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(vertices[0].Position.z, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(1));
        }

        [Test]
        public void Upload_KeepsMaterialBufferContents_WhenMaterialUploadIsSkipped()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                AlbedoColor = new float4(1.0f, 0.0f, 0.0f, 1.0f),
            });
            sceneData.MutableSurfaceBindings.Add(new VividSurfaceBindingData
            {
                BaseColorResource = 7u,
            });

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            sceneData.MutableInstances.Add(default);
            sceneData.MutableMaterials[0] = new VividMaterialData
            {
                AlbedoColor = new float4(0.0f, 1.0f, 0.0f, 1.0f),
            };
            sceneData.MutableSurfaceBindings[0] = new VividSurfaceBindingData
            {
                BaseColorResource = 11u,
            };

            bufferSet.Upload(sceneData, uploadMaterialData: false, uploadStaticData: false);

            var materials = new VividMaterialData[1];
            bufferSet.MaterialDataBuffer.GetData(materials);
            var surfaceBindings = new VividSurfaceBindingData[1];
            bufferSet.SurfaceBindingDataBuffer.GetData(surfaceBindings);

            Assert.That(materials[0].AlbedoColor.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(materials[0].AlbedoColor.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(surfaceBindings[0].BaseColorResource, Is.EqualTo(7u));
            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_ReleasesSurfaceBindingBuffer()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableSurfaceBindings.Add(default);
            var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Not.Null);

            bufferSet.Dispose();

            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Null);
        }

        [Test]
        public void Upload_CreatesAndDisposesTerrainMaterialBuffers()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableTerrainMaterials.Add(new VividTerrainMaterialData
            {
                LayerStartIndex = 0u,
                LayerCount = 2u,
            });
            sceneData.MutableTerrainLayers.Add(new VividTerrainLayerGPUData
            {
                TextureTilingOffset = new float4(2.0f, 2.0f, 0.0f, 0.0f),
                SurfaceBindingIndex = 0u,
            });
            sceneData.MutableTerrainLayers.Add(new VividTerrainLayerGPUData
            {
                TextureTilingOffset = new float4(4.0f, 4.0f, 0.0f, 0.0f),
                SurfaceBindingIndex = 1u,
            });

            var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.TerrainMaterialCount, Is.EqualTo(1));
            Assert.That(bufferSet.TerrainLayerCount, Is.EqualTo(2));
            Assert.That(bufferSet.TerrainMaterialDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.TerrainLayerDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.TerrainMaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.TerrainLayerDataBuffer.count, Is.EqualTo(2));

            var terrainMaterials = new VividTerrainMaterialData[1];
            bufferSet.TerrainMaterialDataBuffer.GetData(terrainMaterials);
            Assert.That(terrainMaterials[0].LayerCount, Is.EqualTo(2u));

            bufferSet.Dispose();

            Assert.That(bufferSet.TerrainMaterialDataBuffer, Is.Null);
            Assert.That(bufferSet.TerrainLayerDataBuffer, Is.Null);
        }

        [Test]
        public void Upload_KeepsInstanceBufferContents_WhenInstanceUploadIsSkipped()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(new VividInstanceData
            {
                ObjectToWorldMatrix = float4x4.identity,
                WorldToObjectMatrix = float4x4.identity,
            });

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            sceneData.MutableInstances[0] = new VividInstanceData
            {
                ObjectToWorldMatrix = new float4x4(
                    new float4(2.0f, 0.0f, 0.0f, 0.0f),
                    new float4(0.0f, 2.0f, 0.0f, 0.0f),
                    new float4(0.0f, 0.0f, 2.0f, 0.0f),
                    new float4(5.0f, 6.0f, 7.0f, 1.0f)),
                WorldToObjectMatrix = float4x4.identity,
            };

            bufferSet.Upload(
                sceneData,
                uploadInstanceData: false,
                uploadMaterialData: false,
                uploadStaticData: false);

            var instances = new VividInstanceData[1];
            bufferSet.InstanceDataBuffer.GetData(instances);

            Assert.That(instances[0].ObjectToWorldMatrix.c0.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(instances[0].ObjectToWorldMatrix.c3.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(instances[0].ObjectToWorldMatrix.c3.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(instances[0].ObjectToWorldMatrix.c3.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(bufferSet.InstanceCount, Is.EqualTo(1));
        }

        private static string GetGeneratedStructIncludePath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
            };

            foreach (string packageRoot in packageRoots)
            {
                string path = Path.Combine(
                    packageRoot,
                    "Runtime",
                    "SubSystem",
                    "GPUDriven",
                    "VividGPUDrivenStructs.cs.hlsl");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            Assert.Fail("Could not locate VividGPUDrivenStructs.cs.hlsl.");
            return string.Empty;
        }
    }
}
