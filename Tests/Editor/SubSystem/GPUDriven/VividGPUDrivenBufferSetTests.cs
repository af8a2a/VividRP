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
            Assert.That(UnsafeUtility.SizeOf<VividMaterialData>(), Is.EqualTo(128));
            Assert.That(UnsafeUtility.SizeOf<VividMaterialRuntimeHeader>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividMaterialProgramData>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividSurfaceBindingData>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividTerrainMaterialData>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividTerrainLayerGPUData>(), Is.EqualTo(48));
            Assert.That(UnsafeUtility.SizeOf<VividMeshlet>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividMeshLODNode>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividMeshletVertex>(), Is.EqualTo(32));
        }

        [Test]
        public void MeshletMetadataPacking_PreservesOffsetsCountsAndConservativeCone()
        {
            var random = new System.Random(28471);
            for (int index = 0; index < 4096; index++)
            {
                float3 sourceAxis = NextUnitVector(random);
                float sourceCutoff = (float) (random.NextDouble() * 1.8 - 0.9);
                VividMeshlet meshlet = VividMeshletMetadataPacking.PackMeshlet(
                    123456789u,
                    987654321u,
                    128u,
                    127u,
                    new float4(1000.25f, -2000.5f, 3.75f, 42.0f),
                    sourceAxis,
                    sourceCutoff);

                Assert.That(meshlet.VertexOffset, Is.EqualTo(123456789u));
                Assert.That(meshlet.TriangleOffset, Is.EqualTo(987654321u));
                Assert.That(meshlet.VertexCount, Is.EqualTo(128u));
                Assert.That(meshlet.TriangleCount, Is.EqualTo(127u));
                Assert.That(VividMeshletMetadataPacking.IsConeValid(meshlet.PackedCone), Is.True);
                Assert.That(math.all(math.isfinite(meshlet.ConeAxis)), Is.True);

                float3 decodedAxis = meshlet.ConeAxis.xyz;
                float decodedCutoff = meshlet.ConeApexCutoff.w;
                for (int sampleIndex = 0; sampleIndex < 16; sampleIndex++)
                {
                    float3 viewDirection = NextUnitVector(random);
                    if (math.dot(viewDirection, decodedAxis) >= decodedCutoff)
                    {
                        Assert.That(
                            math.dot(viewDirection, sourceAxis),
                            Is.GreaterThanOrEqualTo(sourceCutoff - 1e-5f));
                    }
                }
            }
        }

        [Test]
        public void MeshletMetadataPacking_RejectsInvalidConeAndBuildsConservativeParentSphere()
        {
            VividMeshlet meshlet = VividMeshletMetadataPacking.PackMeshlet(
                1u,
                2u,
                3u,
                4u,
                new float4(0.0f, 0.0f, 0.0f, 1.0f),
                new float3(float.NaN, 0.0f, 1.0f),
                0.25f);
            Assert.That(VividMeshletMetadataPacking.IsConeValid(meshlet.PackedCone), Is.False);
            Assert.That(meshlet.ConeAxis, Is.EqualTo(float4.zero));

            float3 sourceAxis = math.normalize(new float3(1.0f, 2.0f, 3.0f));
            var propertyPackedMeshlet = new VividMeshlet
            {
                ConeAxis = new float4(sourceAxis, 0.0f),
                ConeApexCutoff = new float4(0.0f, 0.0f, 0.0f, 0.4f),
            };
            Assert.That(VividMeshletMetadataPacking.IsConeValid(propertyPackedMeshlet.PackedCone), Is.True);
            Assert.That(propertyPackedMeshlet.ConeApexCutoff.w, Is.GreaterThan(0.4f));

            var bounds = new float4(10.0f, -4.0f, 3.0f, 2.0f);
            var parentBounds = new float4(14.0f, -1.0f, 3.0f, 8.0f);
            VividMeshLODNode node = VividMeshletMetadataPacking.PackMeshLODNode(
                bounds,
                parentBounds,
                0.03125f,
                0.0078125f,
                uint.MaxValue - 7u,
                17u,
                9u);

            float requiredParentRadius = parentBounds.w + math.distance(bounds.xyz, parentBounds.xyz);
            Assert.That(node.Bounds, Is.EqualTo(bounds));
            Assert.That(node.ParentBounds.xyz, Is.EqualTo(bounds.xyz));
            Assert.That(node.ParentBounds.w, Is.GreaterThanOrEqualTo(requiredParentRadius));
            Assert.That(node.ParentError, Is.GreaterThanOrEqualTo(0.03125f));
            Assert.That(node.Error, Is.EqualTo(0.0078125f));
            Assert.That(node.MeshletStartIndex, Is.EqualTo(uint.MaxValue - 7u));
            Assert.That(node.MeshletCount, Is.EqualTo(17u));
            Assert.That(node.LevelIndex, Is.EqualTo(9u));

            var largeBounds = new float4(1.0e10f, -2.0e10f, 3.0e10f, 1.0e8f);
            var largeParentBounds = new float4(1.1e10f, -1.8e10f, 3.3e10f, 4.0e8f);
            VividMeshLODNode largeNode = VividMeshletMetadataPacking.PackMeshLODNode(
                largeBounds,
                largeParentBounds,
                0.0012345f,
                0.0005f,
                0u,
                1u,
                0u);
            Assert.That(math.isfinite(largeNode.ParentBounds.w), Is.True);
            Assert.That(
                largeNode.ParentBounds.w,
                Is.GreaterThanOrEqualTo(
                    largeParentBounds.w + math.distance(largeBounds.xyz, largeParentBounds.xyz)));
            Assert.That(largeNode.ParentError, Is.GreaterThanOrEqualTo(0.0012345f));
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
        public void Upload_CreatesExpectedBufferCounts_WhenSceneDataContainsData()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableInstances.Add(new VividInstanceData
            {
                ObjectToWorldMatrix = float4x4.identity,
                WorldToObjectMatrix = float4x4.identity,
            });
            sceneData.AddLegacyMaterial(new VividMaterialData
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
            Assert.That(bufferSet.MaterialRuntimeHeaderCount, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialProgramCount, Is.EqualTo(3));
            Assert.That(bufferSet.DualSlabMaterialCount, Is.Zero);
            Assert.That(bufferSet.SurfaceBindingCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshLODNodeCount, Is.EqualTo(1));
            Assert.That(bufferSet.MeshletCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedVertexCount, Is.EqualTo(1));
            Assert.That(bufferSet.SharedIndexCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.DualSlabMaterialDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialProgramBuffer, Is.Not.Null);
            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshLODNodesBuffer, Is.Not.Null);
            Assert.That(bufferSet.MeshletsBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedVertexBuffer, Is.Not.Null);
            Assert.That(bufferSet.SharedIndexBuffer, Is.Not.Null);
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialProgramBuffer.count, Is.EqualTo(2));
            Assert.That(bufferSet.DualSlabMaterialDataBuffer.count, Is.EqualTo(1));
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
            sceneData.AddLegacyMaterial(default);
            sceneData.MutableSurfaceBindings.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer.count, Is.EqualTo(1));
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(1));

            sceneData.MutableInstances.Add(default);
            sceneData.AddLegacyMaterial(default);
            sceneData.AddLegacyMaterial(default);
            sceneData.MutableSurfaceBindings.Add(default);
            sceneData.MutableSurfaceBindings.Add(default);
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.InstanceCount, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialCount, Is.EqualTo(3));
            Assert.That(bufferSet.SurfaceBindingCount, Is.EqualTo(3));
            Assert.That(bufferSet.InstanceDataBuffer.count, Is.EqualTo(2));
            Assert.That(bufferSet.MaterialDataBuffer.count, Is.EqualTo(3));
            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer.count, Is.EqualTo(3));
            Assert.That(bufferSet.SurfaceBindingDataBuffer.count, Is.EqualTo(3));
        }

        [Test]
        public void Upload_PreservesMaterialRuntimeAndProgramTableContents()
        {
            var sceneData = new VividGPUDrivenSceneData();
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = VividMaterialProgramID.StandardSingleSlab,
                ParameterAddress = 0u,
                ResourceBindingAddress = 5u,
                Flags = VividMaterialRuntimeFlags.AlphaClip,
            };
            for (int bindingIndex = 0; bindingIndex < 6; bindingIndex++)
                sceneData.MutableSurfaceBindings.Add(default);
            sceneData.AddMaterial(
                new VividMaterialData { SurfaceBindingIndex = 5u },
                runtimeHeader);
            sceneData.MutableDualSlabMaterials.Add(new VividDualSlabMaterialData
            {
                LayerOperator = VividDualSlabOperator.VerticalLayer,
                LayerWeight = 0.75f,
            });

            using var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            var uploadedHeaders = new VividMaterialRuntimeHeader[1];
            bufferSet.MaterialRuntimeHeaderBuffer.GetData(uploadedHeaders);
            var uploadedPrograms = new VividMaterialProgramData[3];
            bufferSet.MaterialProgramBuffer.GetData(uploadedPrograms);
            var uploadedDualSlabs = new VividDualSlabMaterialData[1];
            bufferSet.DualSlabMaterialDataBuffer.GetData(uploadedDualSlabs);

            Assert.That(uploadedHeaders[0].ProgramID, Is.EqualTo(runtimeHeader.ProgramID));
            Assert.That(uploadedHeaders[0].ParameterAddress, Is.EqualTo(runtimeHeader.ParameterAddress));
            Assert.That(
                uploadedHeaders[0].ResourceBindingAddress,
                Is.EqualTo(runtimeHeader.ResourceBindingAddress));
            Assert.That(uploadedHeaders[0].Flags, Is.EqualTo(runtimeHeader.Flags));
            Assert.That(uploadedPrograms[0].Version, Is.EqualTo(GPUDrivenMaterialCompiler.ProgramVersion));
            Assert.That(
                uploadedPrograms[0].SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(
                uploadedPrograms[0].ParameterLayoutID,
                Is.EqualTo(VividMaterialParameterLayoutID.LegacyMaterialData));
            Assert.That(
                uploadedPrograms[0].ResourceLayoutID,
                Is.EqualTo(VividMaterialResourceLayoutID.LegacySurfaceBinding));
            Assert.That(
                uploadedPrograms[1].SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(
                uploadedPrograms[2].SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(
                uploadedDualSlabs[0].LayerOperator,
                Is.EqualTo(VividDualSlabOperator.VerticalLayer));
            Assert.That(uploadedDualSlabs[0].LayerWeight, Is.EqualTo(0.75f));
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
            sceneData.AddLegacyMaterial(default);
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
            sceneData.AddLegacyMaterial(new VividMaterialData
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
        public void Dispose_ReleasesMaterialSidecarAndSurfaceBindingBuffers()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.AddLegacyMaterial(default);
            sceneData.MutableSurfaceBindings.Add(default);
            var bufferSet = new VividGPUDrivenBufferSet();
            bufferSet.Upload(sceneData);

            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer, Is.Not.Null);
            Assert.That(bufferSet.MaterialProgramBuffer, Is.Not.Null);
            Assert.That(bufferSet.SurfaceBindingDataBuffer, Is.Not.Null);

            bufferSet.Dispose();

            Assert.That(bufferSet.MaterialRuntimeHeaderBuffer, Is.Null);
            Assert.That(bufferSet.MaterialProgramBuffer, Is.Null);
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
