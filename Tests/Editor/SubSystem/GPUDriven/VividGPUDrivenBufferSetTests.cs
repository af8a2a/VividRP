using System.IO;
using System.Linq;
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
            Assert.That(UnsafeUtility.SizeOf<VividMeshletVertex>(), Is.EqualTo(32));
        }

        [Test]
        public void MeshletVertexPacking_PreservesLosslessFieldsAndDirectionPrecision()
        {
            var random = new System.Random(918273);
            var position = new float3(-1234.125f, 0.0009765625f, 87654.5f);
            var uv = new float2(-17.25f, 8192.125f);

            for (int index = 0; index < 4096; index++)
            {
                float3 normal = NextUnitVector(random);
                float3 tangentDirection = NextUnitVector(random);
                float handedness = (index & 1) == 0 ? 1.0f : -1.0f;
                VividMeshletVertex packed = VividMeshletVertexPacking.Pack(
                    position,
                    normal,
                    new float4(tangentDirection, handedness),
                    uv
                );

                Assert.That(math.all(math.asuint(packed.Position) == math.asuint(position)), Is.True);
                Assert.That(math.all(math.asuint(packed.UV) == math.asuint(uv)), Is.True);
                Assert.That(packed.Reserved, Is.Zero);

                float3 decodedNormal = VividMeshletVertexPacking.UnpackNormal(packed.PackedNormal);
                float4 decodedTangent = VividMeshletVertexPacking.UnpackTangent(packed.PackedTangent);
                Assert.That(DirectionErrorDegrees(normal, decodedNormal), Is.LessThanOrEqualTo(0.02f));
                Assert.That(
                    DirectionErrorDegrees(tangentDirection, decodedTangent.xyz),
                    Is.LessThanOrEqualTo(0.02f)
                );
                Assert.That(decodedTangent.w, Is.EqualTo(handedness));
            }
        }

        [Test]
        public void MeshletVertexPacking_RejectsInvalidDirectionsWithoutProducingNaN()
        {
            VividMeshletVertex packed = VividMeshletVertexPacking.Pack(
                new float3(1.0f, 2.0f, 3.0f),
                new float3(float.NaN, 0.0f, 1.0f),
                new float4(float.PositiveInfinity, 0.0f, 0.0f, -1.0f),
                new float2(4.0f, 5.0f)
            );

            Assert.That(packed.PackedNormal, Is.Zero);
            Assert.That(packed.PackedTangent, Is.Zero);
            Assert.That(VividMeshletVertexPacking.UnpackNormal(packed.PackedNormal), Is.EqualTo(float3.zero));
            Assert.That(VividMeshletVertexPacking.UnpackTangent(packed.PackedTangent), Is.EqualTo(float4.zero));
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
            Assert.That(source, Does.Contain("struct VividMeshletVertex"));
            Assert.That(source, Does.Contain("float PositionX;"));
            Assert.That(source, Does.Contain("float PositionY;"));
            Assert.That(source, Does.Contain("float PositionZ;"));
            Assert.That(source, Does.Contain("uint PackedNormal;"));
            Assert.That(source, Does.Contain("uint PackedTangent;"));
            Assert.That(source, Does.Contain("float2 UV;"));
            Assert.That(source, Does.Contain("uint Reserved;"));
        }

        [Test]
        public void GPUDrivenShaders_DecodePackedMeshletVerticesThroughCommonHelper()
        {
            string commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividGPUDrivenCommon.hlsl"));
            Assert.That(commonSource, Does.Contain("struct VividDecodedMeshletVertex"));
            Assert.That(commonSource, Does.Contain("DecodeVividMeshletOctahedral15"));
            Assert.That(commonSource, Does.Contain("DecodeVividMeshletVertex"));
            Assert.That(commonSource, Does.Contain("packedVertex.PositionX"));
            Assert.That(commonSource, Does.Contain("packedVertex.PackedNormal"));
            Assert.That(commonSource, Does.Contain("packedVertex.PackedTangent"));

            string[][] shaderPaths =
            {
                new[] { "Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferPass.shader" },
                new[] { "Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferShadowCasterPass.shader" },
                new[] { "Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferResolve.shader" },
                new[] { "Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferGBufferResolve.shader" },
                new[] { "Shaders", "Core", "Private", "Debug", "VisibilityBufferDebug.shader" },
            };
            foreach (string[] shaderPath in shaderPaths)
            {
                string shaderSource = File.ReadAllText(GetPackageFilePath(shaderPath));
                Assert.That(shaderSource, Does.Contain("DecodeVividMeshletVertex("));
            }
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
                Position = new float3(0.0f, 0.0f, 0.0f),
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
            Assert.That(bufferSet.SharedVertexBuffer.stride, Is.EqualTo(32));
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
                Position = new float3(1.0f, 2.0f, 3.0f),
            });
            sceneData.MutableIndices.Add(0);
            sceneData.MutableIndices.Add(0);
            sceneData.MutableIndices.Add(0);

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            sceneData.MutableInstances.Add(default);
            sceneData.MutableVertices[0] = new VividMeshletVertex
            {
                Position = new float3(9.0f, 9.0f, 9.0f),
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
            return GetPackageFilePath(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenStructs.cs.hlsl");
        }

        private static string GetPackageFilePath(params string[] relativePathSegments)
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
                string path = Path.Combine(new[] { packageRoot }.Concat(relativePathSegments).ToArray());
                if (File.Exists(path))
                {
                    return path;
                }
            }

            Assert.Fail($"Could not locate package file '{Path.Combine(relativePathSegments)}'.");
            return string.Empty;
        }

        private static float3 NextUnitVector(System.Random random)
        {
            float3 value;
            do
            {
                value = new float3(
                    (float) random.NextDouble() * 2.0f - 1.0f,
                    (float) random.NextDouble() * 2.0f - 1.0f,
                    (float) random.NextDouble() * 2.0f - 1.0f
                );
            }
            while (math.lengthsq(value) <= 1e-6f);

            return math.normalize(value);
        }

        private static float DirectionErrorDegrees(float3 expected, float3 actual)
        {
            float chordLength = math.length(math.normalize(expected) - math.normalize(actual));
            return math.degrees(2.0f * math.asin(math.saturate(chordLength * 0.5f)));
        }
    }
}
