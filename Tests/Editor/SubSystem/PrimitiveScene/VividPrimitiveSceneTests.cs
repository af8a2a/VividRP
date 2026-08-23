using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Editor.Tests
{
    public class VividPrimitiveSceneTests
    {
        private readonly List<GameObject> m_OwnedObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int index = m_OwnedObjects.Count - 1; index >= 0; index--)
            {
                if (m_OwnedObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(m_OwnedObjects[index]);
            }
            m_OwnedObjects.Clear();
        }

        [Test]
        public void GPUDataLayouts_HaveExpectedStrides()
        {
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveData>(), Is.EqualTo(64));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveTransformData>(), Is.EqualTo(128));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitivePreviousTransformData>(), Is.EqualTo(64));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveDrawSectionData>(), Is.EqualTo(32));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveGeometryData>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveMaterialData>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<VividLegacyInstanceMappingData>(), Is.EqualTo(16));
        }

        [Test]
        public void GeneratedHLSL_ContainsAllPrimitiveSceneTables()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VividGPUDrivenSceneData).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Runtime",
                "SubSystem",
                "PrimitiveScene",
                "VividPrimitiveSceneTypes.cs.hlsl");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            StringAssert.Contains("struct VividPrimitiveData", source);
            StringAssert.Contains("struct VividPrimitiveTransformData", source);
            StringAssert.Contains("struct VividPrimitivePreviousTransformData", source);
            StringAssert.Contains("struct VividPrimitiveDrawSectionData", source);
            StringAssert.Contains("struct VividPrimitiveGeometryData", source);
            StringAssert.Contains("struct VividPrimitiveMaterialData", source);
            StringAssert.Contains("struct VividLegacyInstanceMappingData", source);
            AssertHLSLFieldsInOrder(source, "VividPrimitiveData",
                "WorldBoundsMin", "WorldBoundsMax", "TransformIndex", "DrawSectionOffset",
                "DrawSectionCount", "RenderingLayerMask", "PassMask", "Flags", "Generation",
                "CustomDataAddress");
            AssertHLSLFieldsInOrder(source, "VividPrimitiveTransformData",
                "ObjectToWorldMatrix", "WorldToObjectMatrix");
            AssertHLSLFieldsInOrder(source, "VividPrimitivePreviousTransformData",
                "PreviousObjectToWorldMatrix");
            AssertHLSLFieldsInOrder(source, "VividPrimitiveDrawSectionData",
                "GeometryIndex", "GeometryGeneration", "MaterialIndex", "MaterialGeneration",
                "SourceSectionIndex", "Flags", "Padding0", "Padding1");
            AssertHLSLFieldsInOrder(source, "VividPrimitiveGeometryData",
                "Generation", "LegacyTopMeshLODStartIndex", "LegacyTotalMeshLODCount",
                "LegacyMeshLODLevelCount");
            AssertHLSLFieldsInOrder(source, "VividPrimitiveMaterialData",
                "Generation", "LegacyMaterialIndex", "RendererListID", "MaterialFlags");
            AssertHLSLFieldsInOrder(source, "VividLegacyInstanceMappingData",
                "PrimitiveIndex", "PrimitiveGeneration", "DrawSectionIndex", "Flags");
        }

        [Test]
        public void GenerationWrap_SkipsZero()
        {
            Assert.That(VividVersionedSlotAllocator.NextGeneration(uint.MaxValue), Is.EqualTo(1u));
            Assert.That(VividVersionedSlotAllocator.NextGeneration(1u), Is.EqualTo(2u));
        }

        [Test]
        public void SceneToken_RejectsHandleFromAnotherScene()
        {
            using var firstScene = new VividPrimitiveScene();
            using var secondScene = new VividPrimitiveScene();
            EntityId source = CreateEntity("Scene Token Primitive");

            VividPrimitiveHandle firstHandle = firstScene.RegisterOrUpdate(CreateDescriptor(source));
            VividPrimitiveHandle secondHandle = secondScene.RegisterOrUpdate(
                firstHandle,
                CreateDescriptor(source));

            Assert.That(firstHandle.Index, Is.EqualTo(secondHandle.Index));
            Assert.That(firstHandle.Generation, Is.EqualTo(secondHandle.Generation));
            Assert.That(firstHandle.SceneToken, Is.Not.EqualTo(secondHandle.SceneToken));
            Assert.That(firstScene.IsValid(firstHandle), Is.True);
            Assert.That(firstScene.IsValid(secondHandle), Is.False);
            Assert.That(secondScene.IsValid(firstHandle), Is.False);
            Assert.That(secondScene.IsValid(secondHandle), Is.True);
        }

        [Test]
        public void RemoveAndRegister_ReusesPrimitiveSlotWithNewGeneration()
        {
            using var scene = new VividPrimitiveScene();
            EntityId firstEntity = CreateEntity("First Primitive");
            EntityId secondEntity = CreateEntity("Second Primitive");

            VividPrimitiveHandle firstHandle = scene.RegisterOrUpdate(CreateDescriptor(firstEntity));

            Assert.That(firstHandle.IsValid, Is.True);
            Assert.That(scene.IsValid(firstHandle), Is.True);
            Assert.That(scene.Remove(firstEntity), Is.True);
            Assert.That(scene.IsValid(firstHandle), Is.False);
            Assert.That(scene.TryGetHandle(firstEntity, out _), Is.False);

            VividPrimitiveHandle secondHandle = scene.RegisterOrUpdate(CreateDescriptor(secondEntity));

            Assert.That(secondHandle.Index, Is.EqualTo(firstHandle.Index));
            Assert.That(secondHandle.Generation, Is.Not.EqualTo(firstHandle.Generation));
            Assert.That(secondHandle.Generation, Is.Not.Zero);
            Assert.That(scene.IsValid(secondHandle), Is.True);
            Assert.That(scene.IsValid(firstHandle), Is.False);
        }

        [Test]
        public void ActiveCullRecords_AreDenseAndRemainAddressableAfterSwapBack()
        {
            using var scene = new VividPrimitiveScene();
            EntityId first = CreateEntity("Cull Record A");
            EntityId second = CreateEntity("Cull Record B");
            EntityId third = CreateEntity("Cull Record C");
            VividPrimitiveHandle firstHandle = scene.RegisterOrUpdate(CreateDescriptor(
                first,
                cameraLayerMask: 1u << 2));
            VividPrimitiveHandle secondHandle = scene.RegisterOrUpdate(CreateDescriptor(
                second,
                cameraLayerMask: 1u << 3));
            VividPrimitiveHandle thirdHandle = scene.RegisterOrUpdate(CreateDescriptor(
                third,
                cameraLayerMask: 1u << 4));

            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveHandle>(), Is.EqualTo(12));
            Assert.That(UnsafeUtility.SizeOf<VividPrimitiveCullRecord>(), Is.EqualTo(56));
            Assert.That(scene.ActiveCullRecords.Length, Is.EqualTo(3));
            Assert.That(scene.ActiveCullRecords[0].Handle, Is.EqualTo(firstHandle));
            Assert.That(scene.ActiveCullRecords[1].Handle, Is.EqualTo(secondHandle));
            Assert.That(scene.ActiveCullRecords[2].Handle, Is.EqualTo(thirdHandle));
            Assert.That(scene.ActiveCullRecords[2].CameraLayerMask, Is.EqualTo(1u << 4));

            Assert.That(scene.Remove(second), Is.True);

            Assert.That(scene.ActiveCullRecords.Length, Is.EqualTo(2));
            Assert.That(scene.ActiveCullRecords[0].Handle, Is.EqualTo(firstHandle));
            Assert.That(scene.ActiveCullRecords[1].Handle, Is.EqualTo(thirdHandle));
            Assert.That(scene.ActiveCullRecords[0].Handle, Is.Not.EqualTo(secondHandle));
            Assert.That(scene.ActiveCullRecords[1].Handle, Is.Not.EqualTo(secondHandle));

            Matrix4x4 moved = Matrix4x4.Translate(new Vector3(7.0f, 0.0f, 0.0f));
            VividPrimitiveHandle updatedHandle = scene.RegisterOrUpdate(
                thirdHandle,
                CreateDescriptor(
                    third,
                    objectToWorld: moved,
                    cameraLayerMask: 1u << 7));

            Assert.That(updatedHandle, Is.EqualTo(thirdHandle));
            Assert.That(scene.ActiveCullRecords[1].Handle, Is.EqualTo(thirdHandle));
            Assert.That(scene.ActiveCullRecords[1].BoundsMin.x, Is.EqualTo(6.0f));
            Assert.That(scene.ActiveCullRecords[1].BoundsMax.x, Is.EqualTo(8.0f));
            Assert.That(scene.ActiveCullRecords[1].CameraLayerMask, Is.EqualTo(1u << 7));
        }

        [Test]
        public void Register_ThreeSectionsCreatesOnePrimitiveAndOneTransform()
        {
            using var scene = new VividPrimitiveScene();
            EntityId source = CreateEntity("Three Section Primitive");
            VividPrimitiveResourceKey geometryKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Shared Geometry"));
            VividPrimitiveResourceKey materialKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("Shared Material"));
            VividPrimitiveDrawSectionDescriptor[] sections =
            {
                CreateSection(0, geometryKey, materialKey),
                CreateSection(1, geometryKey, materialKey),
                CreateSection(2, geometryKey, materialKey),
            };

            VividPrimitiveHandle handle = scene.RegisterOrUpdate(CreateDescriptor(source, sections));
            VividPrimitiveData primitive = scene.PrimitiveTable[handle.Index];

            Assert.That(scene.PrimitiveTable.Count, Is.EqualTo(1));
            Assert.That(scene.TransformTable.Count, Is.EqualTo(1));
            Assert.That(scene.PreviousTransformTable.Count, Is.EqualTo(1));
            Assert.That(scene.GetStats().ActivePrimitiveCount, Is.EqualTo(1));
            Assert.That(scene.GetStats().ActiveDrawSectionCount, Is.EqualTo(3));
            Assert.That(primitive.TransformIndex, Is.EqualTo((uint) handle.Index));
            Assert.That(primitive.DrawSectionCount, Is.EqualTo(3u));

            for (int sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                VividPrimitiveDrawSectionData section =
                    scene.DrawSectionTable[(int) primitive.DrawSectionOffset + sectionIndex];
                Assert.That(section.SourceSectionIndex, Is.EqualTo((uint) sectionIndex));
                Assert.That((section.Flags & VividPrimitiveDrawSectionFlags.Valid) != 0, Is.True);
            }
        }

        [Test]
        public void SharedResources_AreReleasedOnlyAfterLastPrimitiveIsRemoved()
        {
            using var scene = new VividPrimitiveScene();
            EntityId firstEntity = CreateEntity("First Shared Primitive");
            EntityId secondEntity = CreateEntity("Second Shared Primitive");
            VividPrimitiveResourceKey geometryKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Shared Geometry"));
            VividPrimitiveResourceKey materialKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("Shared Material"));
            VividPrimitiveDrawSectionDescriptor[] sections =
            {
                CreateSection(0, geometryKey, materialKey),
            };

            VividPrimitiveHandle firstHandle = scene.RegisterOrUpdate(CreateDescriptor(firstEntity, sections));
            VividPrimitiveHandle secondHandle = scene.RegisterOrUpdate(CreateDescriptor(secondEntity, sections));
            VividPrimitiveData firstPrimitive = scene.PrimitiveTable[firstHandle.Index];
            VividPrimitiveData secondPrimitive = scene.PrimitiveTable[secondHandle.Index];
            VividPrimitiveDrawSectionData firstSection =
                scene.DrawSectionTable[(int) firstPrimitive.DrawSectionOffset];
            VividPrimitiveDrawSectionData secondSection =
                scene.DrawSectionTable[(int) secondPrimitive.DrawSectionOffset];

            Assert.That(firstSection.GeometryIndex, Is.EqualTo(secondSection.GeometryIndex));
            Assert.That(firstSection.GeometryGeneration, Is.EqualTo(secondSection.GeometryGeneration));
            Assert.That(firstSection.MaterialIndex, Is.EqualTo(secondSection.MaterialIndex));
            Assert.That(firstSection.MaterialGeneration, Is.EqualTo(secondSection.MaterialGeneration));
            Assert.That(scene.GetStats().ActiveGeometryCount, Is.EqualTo(1));
            Assert.That(scene.GetStats().ActiveMaterialCount, Is.EqualTo(1));

            Assert.That(scene.Remove(firstEntity), Is.True);
            Assert.That(scene.GetStats().ActiveGeometryCount, Is.EqualTo(1));
            Assert.That(scene.GetStats().ActiveMaterialCount, Is.EqualTo(1));

            Assert.That(scene.Remove(secondEntity), Is.True);
            Assert.That(scene.GetStats().ActiveGeometryCount, Is.Zero);
            Assert.That(scene.GetStats().ActiveMaterialCount, Is.Zero);
            Assert.That(
                scene.GeometryTable[(int) firstSection.GeometryIndex].Generation,
                Is.Not.EqualTo(firstSection.GeometryGeneration));
            Assert.That(
                scene.MaterialTable[(int) firstSection.MaterialIndex].Generation,
                Is.Not.EqualTo(firstSection.MaterialGeneration));
        }

        [Test]
        public void SameSectionCountUpdate_KeepsRangeAndStableSharedHandles()
        {
            using var scene = new VividPrimitiveScene();
            EntityId source = CreateEntity("In Place Primitive");
            VividPrimitiveResourceKey firstGeometry = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Geometry A"));
            VividPrimitiveResourceKey secondGeometry = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Geometry B"));
            VividPrimitiveResourceKey material = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("In Place Material"));
            VividPrimitiveDrawSectionDescriptor[] initialSections =
            {
                CreateSection(0, firstGeometry, material),
                CreateSection(1, secondGeometry, material),
            };

            VividPrimitiveHandle handle = scene.RegisterOrUpdate(CreateDescriptor(source, initialSections));
            VividPrimitiveData initialPrimitive = scene.PrimitiveTable[handle.Index];
            VividPrimitiveDrawSectionData initialFirst =
                scene.DrawSectionTable[(int) initialPrimitive.DrawSectionOffset];
            VividPrimitiveDrawSectionData initialSecond =
                scene.DrawSectionTable[(int) initialPrimitive.DrawSectionOffset + 1];
            uint revision = scene.GetStats().SceneRevision;

            VividPrimitiveDrawSectionDescriptor[] swappedSections =
            {
                CreateSection(0, secondGeometry, material),
                CreateSection(1, firstGeometry, material),
            };
            scene.RegisterOrUpdate(CreateDescriptor(source, swappedSections));

            VividPrimitiveData updatedPrimitive = scene.PrimitiveTable[handle.Index];
            VividPrimitiveDrawSectionData updatedFirst =
                scene.DrawSectionTable[(int) updatedPrimitive.DrawSectionOffset];
            VividPrimitiveDrawSectionData updatedSecond =
                scene.DrawSectionTable[(int) updatedPrimitive.DrawSectionOffset + 1];
            Assert.That(updatedPrimitive.DrawSectionOffset, Is.EqualTo(initialPrimitive.DrawSectionOffset));
            Assert.That(updatedFirst.GeometryIndex, Is.EqualTo(initialSecond.GeometryIndex));
            Assert.That(updatedFirst.GeometryGeneration, Is.EqualTo(initialSecond.GeometryGeneration));
            Assert.That(updatedSecond.GeometryIndex, Is.EqualTo(initialFirst.GeometryIndex));
            Assert.That(updatedSecond.GeometryGeneration, Is.EqualTo(initialFirst.GeometryGeneration));
            Assert.That(updatedFirst.MaterialIndex, Is.EqualTo(initialFirst.MaterialIndex));
            Assert.That(updatedFirst.MaterialGeneration, Is.EqualTo(initialFirst.MaterialGeneration));
            Assert.That(scene.GetStats().SceneRevision, Is.GreaterThan(revision));
            Assert.That(scene.GetStats().ActiveGeometryCount, Is.EqualTo(2));
            Assert.That(scene.GetStats().ActiveMaterialCount, Is.EqualTo(1));
        }

        [Test]
        public void LegacyPayloadChanges_DoNotChangeLogicalHandles()
        {
            using var scene = new VividPrimitiveScene();
            EntityId source = CreateEntity("Legacy Payload Primitive");
            VividPrimitiveResourceKey geometry = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Legacy Geometry"));
            VividPrimitiveResourceKey material = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("Legacy Material"));
            VividPrimitiveHandle handle = scene.RegisterOrUpdate(CreateDescriptor(
                source,
                new[] { CreateSection(0, geometry, material) }));
            VividPrimitiveData primitive = scene.PrimitiveTable[handle.Index];
            VividPrimitiveDrawSectionData section =
                scene.DrawSectionTable[(int) primitive.DrawSectionOffset];

            var firstInstance = new VividInstanceData
            {
                TopMeshLODStartIndex = 3u,
                TotalMeshLODCount = 4u,
                MeshLODLevelCount = 2u,
            };
            var secondInstance = new VividInstanceData
            {
                TopMeshLODStartIndex = 19u,
                TotalMeshLODCount = 7u,
                MeshLODLevelCount = 5u,
            };
            var materialData = new VividMaterialData
            {
                RendererListID = VividRendererListID.AlphaTest,
                MaterialFlags = VividMaterialFlags.Unlit,
            };
            Assert.That(scene.UpdateGeometryPayload(geometry, firstInstance), Is.True);
            Assert.That(scene.UpdateMaterialPayload(material, 2u, materialData), Is.True);
            Assert.That(scene.UpdateGeometryPayload(geometry, secondInstance), Is.True);
            Assert.That(scene.UpdateMaterialPayload(material, 11u, materialData), Is.True);

            VividPrimitiveDrawSectionData updatedSection =
                scene.DrawSectionTable[(int) primitive.DrawSectionOffset];
            Assert.That(updatedSection.GeometryIndex, Is.EqualTo(section.GeometryIndex));
            Assert.That(updatedSection.GeometryGeneration, Is.EqualTo(section.GeometryGeneration));
            Assert.That(updatedSection.MaterialIndex, Is.EqualTo(section.MaterialIndex));
            Assert.That(updatedSection.MaterialGeneration, Is.EqualTo(section.MaterialGeneration));
            Assert.That(scene.GeometryTable[(int) section.GeometryIndex].LegacyTopMeshLODStartIndex, Is.EqualTo(19u));
            Assert.That(scene.MaterialTable[(int) section.MaterialIndex].LegacyMaterialIndex, Is.EqualTo(11u));
        }

        [Test]
        public void RebuildLegacyBridge_MapsAllResourceDomainsAndPreservesLogicalHandlesOnReorder()
        {
            using var scene = new VividPrimitiveScene();
            var legacyScene = new VividGPUDrivenSceneData();
            EntityId[] primitiveIds =
            {
                CreateEntity("Proxy Primitive"),
                CreateEntity("Unity Material Primitive"),
                CreateEntity("Missing Material Primitive"),
                CreateEntity("Terrain Primitive"),
            };
            VividPrimitiveResourceKey[] geometryKeys = new VividPrimitiveResourceKey[primitiveIds.Length];
            for (int index = 0; index < geometryKeys.Length; index++)
            {
                geometryKeys[index] = CreateResourceKey(
                    index == 3
                        ? VividPrimitiveResourceDomain.TerrainGeometry
                        : VividPrimitiveResourceDomain.MeshletGeometry,
                    CreateEntity($"Bridge Geometry {index}"));
            }
            VividPrimitiveResourceKey[] materialKeys =
            {
                CreateResourceKey(
                    VividPrimitiveResourceDomain.MaterialProxy,
                    CreateEntity("Bridge Proxy")),
                CreateResourceKey(
                    VividPrimitiveResourceDomain.UnityMaterial,
                    CreateEntity("Bridge Unity Material")),
                CreateMissingMaterialKey(primitiveIds[2], 0),
                CreateResourceKey(VividPrimitiveResourceDomain.TerrainMaterial, primitiveIds[3]),
            };
            VividGPUDrivenInstanceSourceFlags[] sourceFlags =
            {
                VividGPUDrivenInstanceSourceFlags.MaterialProxy,
                VividGPUDrivenInstanceSourceFlags.None,
                VividGPUDrivenInstanceSourceFlags.MissingMaterial,
                VividGPUDrivenInstanceSourceFlags.TerrainGeometry
                    | VividGPUDrivenInstanceSourceFlags.TerrainMaterial,
            };
            EntityId[] materialEntityIds =
            {
                materialKeys[0].ObjectId,
                materialKeys[1].ObjectId,
                EntityId.None,
                primitiveIds[3],
            };
            var handles = new VividPrimitiveHandle[primitiveIds.Length];
            var sections = new VividPrimitiveDrawSectionData[primitiveIds.Length];
            for (int index = 0; index < primitiveIds.Length; index++)
            {
                VividPrimitiveDrawSectionFlags sectionFlags = VividPrimitiveDrawSectionFlags.Valid;
                if (index == 3)
                    sectionFlags |= VividPrimitiveDrawSectionFlags.Terrain;
                handles[index] = scene.RegisterOrUpdate(CreateDescriptor(
                    primitiveIds[index],
                    new[]
                    {
                        new VividPrimitiveDrawSectionDescriptor(
                            0,
                            geometryKeys[index],
                            materialKeys[index],
                            sectionFlags),
                    }));
                VividPrimitiveData primitive = scene.PrimitiveTable[handles[index].Index];
                sections[index] = scene.DrawSectionTable[(int) primitive.DrawSectionOffset];
                legacyScene.AddLegacyMaterial(new VividMaterialData
                {
                    RendererListID = (VividRendererListID) index,
                });
                legacyScene.AddInstance(
                    CreateLegacyInstance((uint) index, (uint) (10 + index)),
                    new VividGPUDrivenInstanceSourceData(
                        primitiveIds[index],
                        geometryKeys[index].ObjectId,
                        materialEntityIds[index],
                        0,
                        sourceFlags[index]),
                    0);
            }

            VividPrimitiveSceneAdapter.RebuildLegacyBridge(scene, legacyScene);
            AssertBridgeRows(scene, primitiveIds, handles, sections, new[] { 0u, 1u, 2u, 3u });
            Assert.That(scene.DrawSetSources.Length, Is.EqualTo(scene.DrawSectionTable.Count));
            for (int row = 0; row < primitiveIds.Length; row++)
            {
                uint absoluteSectionIndex = scene.LegacyInstanceMappingTable[row].DrawSectionIndex;
                VividPrimitiveDrawSourceData drawSource = scene.DrawSetSources[(int) absoluteSectionIndex];
                Assert.That(drawSource.PrimitiveHandle, Is.EqualTo(handles[row]));
                Assert.That(drawSource.AbsoluteDrawSectionIndex, Is.EqualTo(absoluteSectionIndex));
                Assert.That(drawSource.LegacyInstanceIndex, Is.EqualTo((uint) row));
                Assert.That(drawSource.RendererListID, Is.EqualTo((VividRendererListID) row));
                Assert.That(drawSource.Flags, Is.EqualTo(VividPrimitiveDrawSourceFlags.Valid));
            }

            legacyScene.ClearInstances();
            uint[] reorderedMaterialIndices = { 2u, 0u, 3u, 1u };
            for (int sourceIndex = primitiveIds.Length - 1; sourceIndex >= 0; sourceIndex--)
            {
                legacyScene.AddInstance(
                    CreateLegacyInstance(
                        reorderedMaterialIndices[sourceIndex],
                        (uint) (100 + sourceIndex)),
                    new VividGPUDrivenInstanceSourceData(
                        primitiveIds[sourceIndex],
                        geometryKeys[sourceIndex].ObjectId,
                        materialEntityIds[sourceIndex],
                        0,
                        sourceFlags[sourceIndex]),
                    0);
            }

            VividPrimitiveSceneAdapter.RebuildLegacyBridge(scene, legacyScene);
            for (int row = 0; row < primitiveIds.Length; row++)
            {
                int sourceIndex = primitiveIds.Length - 1 - row;
                VividLegacyInstanceMappingData mapping = scene.LegacyInstanceMappingTable[row];
                Assert.That(mapping.PrimitiveIndex, Is.EqualTo((uint) handles[sourceIndex].Index));
                Assert.That(mapping.PrimitiveGeneration, Is.EqualTo(handles[sourceIndex].Generation));
                Assert.That(mapping.DrawSectionIndex, Is.EqualTo(
                    (uint) scene.PrimitiveTable[handles[sourceIndex].Index].DrawSectionOffset));
                Assert.That(scene.GeometryTable[(int) sections[sourceIndex].GeometryIndex]
                    .LegacyTopMeshLODStartIndex, Is.EqualTo((uint) (100 + sourceIndex)));
                Assert.That(scene.MaterialTable[(int) sections[sourceIndex].MaterialIndex]
                    .LegacyMaterialIndex, Is.EqualTo(reorderedMaterialIndices[sourceIndex]));
                VividPrimitiveDrawSectionData stableSection = scene.DrawSectionTable[
                    (int) scene.PrimitiveTable[handles[sourceIndex].Index].DrawSectionOffset];
                Assert.That(stableSection.GeometryIndex, Is.EqualTo(sections[sourceIndex].GeometryIndex));
                Assert.That(stableSection.GeometryGeneration, Is.EqualTo(sections[sourceIndex].GeometryGeneration));
                Assert.That(stableSection.MaterialIndex, Is.EqualTo(sections[sourceIndex].MaterialIndex));
                Assert.That(stableSection.MaterialGeneration, Is.EqualTo(sections[sourceIndex].MaterialGeneration));

                VividPrimitiveDrawSourceData drawSource =
                    scene.DrawSetSources[(int) mapping.DrawSectionIndex];
                Assert.That(drawSource.PrimitiveHandle, Is.EqualTo(handles[sourceIndex]));
                Assert.That(drawSource.AbsoluteDrawSectionIndex, Is.EqualTo(mapping.DrawSectionIndex));
                Assert.That(drawSource.LegacyInstanceIndex, Is.EqualTo((uint) row));
                Assert.That(drawSource.RendererListID, Is.EqualTo(
                    legacyScene.Materials[(int) reorderedMaterialIndices[sourceIndex]].RendererListID));
                Assert.That(drawSource.Flags, Is.EqualTo(VividPrimitiveDrawSourceFlags.Valid));
            }
        }

        [Test]
        public void MissingMaterialKeys_DoNotMergeAcrossOwnersOrSections()
        {
            using var scene = new VividPrimitiveScene();
            EntityId firstEntity = CreateEntity("First Missing Material Primitive");
            EntityId secondEntity = CreateEntity("Second Missing Material Primitive");
            VividPrimitiveDrawSectionDescriptor[] firstSections =
            {
                CreateSection(0, VividPrimitiveResourceKey.Invalid, CreateMissingMaterialKey(firstEntity, 0)),
                CreateSection(1, VividPrimitiveResourceKey.Invalid, CreateMissingMaterialKey(firstEntity, 1)),
            };
            VividPrimitiveDrawSectionDescriptor[] secondSections =
            {
                CreateSection(0, VividPrimitiveResourceKey.Invalid, CreateMissingMaterialKey(secondEntity, 0)),
            };

            VividPrimitiveHandle firstHandle = scene.RegisterOrUpdate(CreateDescriptor(firstEntity, firstSections));
            VividPrimitiveHandle secondHandle = scene.RegisterOrUpdate(CreateDescriptor(secondEntity, secondSections));
            VividPrimitiveData firstPrimitive = scene.PrimitiveTable[firstHandle.Index];
            VividPrimitiveData secondPrimitive = scene.PrimitiveTable[secondHandle.Index];
            uint firstMaterial = scene.DrawSectionTable[(int) firstPrimitive.DrawSectionOffset].MaterialIndex;
            uint secondMaterial = scene.DrawSectionTable[(int) firstPrimitive.DrawSectionOffset + 1].MaterialIndex;
            uint thirdMaterial = scene.DrawSectionTable[(int) secondPrimitive.DrawSectionOffset].MaterialIndex;

            Assert.That(scene.GetStats().ActiveMaterialCount, Is.EqualTo(3));
            Assert.That(firstMaterial, Is.Not.EqualTo(secondMaterial));
            Assert.That(firstMaterial, Is.Not.EqualTo(thirdMaterial));
            Assert.That(secondMaterial, Is.Not.EqualTo(thirdMaterial));
        }

        [Test]
        public void MalformedResourceKeys_AreInvalid()
        {
            Assert.That(new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.UnityMaterial,
                EntityId.None,
                EntityId.None,
                -1).IsValid, Is.False);
            Assert.That(new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.MissingMaterial,
                EntityId.None,
                EntityId.None,
                0).IsValid, Is.False);
            Assert.That(new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.MissingMaterial,
                EntityId.None,
                CreateEntity("Missing Material Owner"),
                -1).IsValid, Is.False);
        }

        [Test]
        public void DirtyPages_ScaleWithTouchedRecordPages()
        {
            var table = new VividPrimitiveGpuTable<VividPrimitiveData>();
            table.Resize(100_000);
            table.ClearDirtyPages();

            for (int index = 45_000; index < 55_000; index++)
            {
                table.Set(index, new VividPrimitiveData
                {
                    Generation = 1u,
                });
            }

            var ranges = new List<VividPrimitiveDirtyRange>();
            table.CollectDirtyRanges(ranges);
            Assert.That(ranges, Has.Count.EqualTo(1));
            Assert.That(table.DirtyPageCount, Is.EqualTo(157));
            Assert.That(ranges[0].Count, Is.EqualTo(157 * 64));
            Assert.That(ranges[0].Count, Is.LessThan(table.Count));
        }

        [Test]
        public void TransformUpdate_DirtiesOnlyTransformTablesAndPreviousCatchesUpOnce()
        {
            using var scene = new VividPrimitiveScene();
            EntityId source = CreateEntity("Moving Primitive");
            VividPrimitiveResourceKey geometryKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Moving Geometry"));
            VividPrimitiveResourceKey materialKey = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("Moving Material"));
            VividPrimitiveDrawSectionDescriptor[] sections =
            {
                CreateSection(0, geometryKey, materialKey),
            };

            scene.BeginFrame(0);
            VividPrimitiveHandle handle = scene.RegisterOrUpdate(
                CreateDescriptor(source, sections, Matrix4x4.identity));
            using var buffers = new VividPrimitiveSceneBufferSet();
            buffers.Upload(scene);

            var movedMatrix = Matrix4x4.Translate(new Vector3(5.0f, 2.0f, -3.0f));
            scene.RegisterOrUpdate(CreateDescriptor(source, sections, movedMatrix));

            Assert.That(scene.PrimitiveTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.TransformTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.PreviousTransformTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.DrawSectionTable.DirtyPageCount, Is.Zero);
            Assert.That(scene.GeometryTable.DirtyPageCount, Is.Zero);
            Assert.That(scene.MaterialTable.DirtyPageCount, Is.Zero);
            Assert.That(
                scene.PreviousTransformTable[handle.Index].PreviousObjectToWorldMatrix.c3.x,
                Is.EqualTo(0.0f));

            var beforePrimitive = new VividPrimitiveData[1];
            var beforeSection = new VividPrimitiveDrawSectionData[1];
            buffers.PrimitiveDataBuffer.GetData(beforePrimitive);
            buffers.DrawSectionDataBuffer.GetData(beforeSection);

            buffers.Upload(scene);

            var afterPrimitive = new VividPrimitiveData[1];
            var afterTransform = new VividPrimitiveTransformData[1];
            var afterSection = new VividPrimitiveDrawSectionData[1];
            buffers.PrimitiveDataBuffer.GetData(afterPrimitive);
            buffers.TransformDataBuffer.GetData(afterTransform);
            buffers.DrawSectionDataBuffer.GetData(afterSection);
            VividPrimitiveSceneStats movedStats = scene.GetStats();
            Assert.That(afterPrimitive[0].WorldBoundsMin.x, Is.Not.EqualTo(beforePrimitive[0].WorldBoundsMin.x));
            Assert.That(afterTransform[0].ObjectToWorldMatrix.c3.x, Is.EqualTo(5.0f));
            Assert.That(afterSection[0].GeometryIndex, Is.EqualTo(beforeSection[0].GeometryIndex));
            Assert.That(afterSection[0].MaterialIndex, Is.EqualTo(beforeSection[0].MaterialIndex));
            Assert.That(movedStats.LastUploadRangeCount, Is.EqualTo(3));
            Assert.That(movedStats.LastUploadBytes, Is.EqualTo(256L));

            scene.BeginFrame(1);
            Assert.That(
                scene.PreviousTransformTable[handle.Index].PreviousObjectToWorldMatrix.c3.x,
                Is.EqualTo(5.0f));
            buffers.Upload(scene);
            Assert.That(scene.GetStats().LastUploadRangeCount, Is.EqualTo(1));
            Assert.That(scene.GetStats().LastUploadBytes, Is.EqualTo(64L));

            scene.BeginFrame(2);
            buffers.Upload(scene);
            Assert.That(scene.GetStats().LastUploadRangeCount, Is.Zero);
            Assert.That(scene.GetStats().LastUploadBytes, Is.Zero);
        }

        [Test]
        public void Upload_EmptySceneCreatesPlaceholdersAndBuffersGrowWithoutShrinking()
        {
            using var scene = new VividPrimitiveScene();
            using var buffers = new VividPrimitiveSceneBufferSet();

            buffers.Upload(scene);

            Assert.That(buffers.PrimitiveDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.TransformDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.PreviousTransformDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.DrawSectionDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.GeometryDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.MaterialDataBuffer.count, Is.EqualTo(1));
            Assert.That(buffers.LegacyInstanceMappingBuffer.count, Is.EqualTo(1));
            var commandBuffer = new UnityEngine.Rendering.CommandBuffer();
            try
            {
                Assert.DoesNotThrow(() => buffers.BindGlobals(commandBuffer, scene));
            }
            finally
            {
                commandBuffer.Release();
            }

            EntityId first = CreateEntity("Capacity Primitive 0");
            EntityId second = CreateEntity("Capacity Primitive 1");
            EntityId third = CreateEntity("Capacity Primitive 2");
            scene.RegisterOrUpdate(CreateDescriptor(first));
            scene.RegisterOrUpdate(CreateDescriptor(second));
            scene.RegisterOrUpdate(CreateDescriptor(third));
            buffers.Upload(scene);

            Assert.That(buffers.PrimitiveDataBuffer.count, Is.EqualTo(4));
            Assert.That(buffers.TransformDataBuffer.count, Is.EqualTo(4));
            Assert.That(buffers.PreviousTransformDataBuffer.count, Is.EqualTo(4));

            scene.Remove(first);
            scene.Remove(second);
            scene.Remove(third);
            buffers.Upload(scene);

            Assert.That(scene.GetStats().ActivePrimitiveCount, Is.Zero);
            Assert.That(buffers.PrimitiveDataBuffer.count, Is.EqualTo(4));
            Assert.That(buffers.TransformDataBuffer.count, Is.EqualTo(4));
            Assert.That(buffers.PreviousTransformDataBuffer.count, Is.EqualTo(4));
        }

        [Test]
        public void FullResync_MarksEveryPopulatedTableForWholeTableUpload()
        {
            using var scene = new VividPrimitiveScene();
            EntityId source = CreateEntity("Full Resync Primitive");
            VividPrimitiveResourceKey geometry = CreateResourceKey(
                VividPrimitiveResourceDomain.MeshletGeometry,
                CreateEntity("Full Resync Geometry"));
            VividPrimitiveResourceKey material = CreateResourceKey(
                VividPrimitiveResourceDomain.MaterialProxy,
                CreateEntity("Full Resync Material"));
            VividPrimitiveHandle handle = scene.RegisterOrUpdate(CreateDescriptor(
                source,
                new[] { CreateSection(0, geometry, material) }));
            scene.ResizeLegacyInstanceMappings(1);
            scene.SetLegacyInstanceMapping(0, new VividLegacyInstanceMappingData
            {
                PrimitiveIndex = (uint) handle.Index,
                PrimitiveGeneration = handle.Generation,
                DrawSectionIndex = scene.PrimitiveTable[handle.Index].DrawSectionOffset,
                Flags = 1u,
            });
            using var buffers = new VividPrimitiveSceneBufferSet();
            buffers.Upload(scene);

            scene.RecordFullResync();

            Assert.That(scene.PrimitiveTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.TransformTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.PreviousTransformTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.DrawSectionTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.GeometryTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.MaterialTable.DirtyPageCount, Is.EqualTo(1));
            Assert.That(scene.LegacyInstanceMappingTable.DirtyPageCount, Is.EqualTo(1));

            buffers.Upload(scene);
            Assert.That(scene.GetStats().DirtyPageCount, Is.EqualTo(7));
            Assert.That(scene.GetStats().LastUploadRangeCount, Is.EqualTo(7));
            Assert.That(scene.GetStats().LastUploadBytes, Is.EqualTo(336L));
        }

        private EntityId CreateEntity(string name)
        {
            var gameObject = new GameObject(name);
            m_OwnedObjects.Add(gameObject);
            return gameObject.GetEntityId();
        }

        private static VividPrimitiveSourceDescriptor CreateDescriptor(
            EntityId source,
            IReadOnlyList<VividPrimitiveDrawSectionDescriptor> sections = null,
            Matrix4x4? objectToWorld = null,
            uint cameraLayerMask = uint.MaxValue)
        {
            Matrix4x4 transform = objectToWorld ?? Matrix4x4.identity;
            var center = new Vector3(transform.m03, transform.m13, transform.m23);
            return new VividPrimitiveSourceDescriptor(
                source,
                transform,
                transform.inverse,
                new Bounds(center, Vector3.one * 2.0f),
                uint.MaxValue,
                cameraLayerMask,
                VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                VividPrimitiveFlags.Valid | VividPrimitiveFlags.ReceiveShadows,
                sections ?? Array.Empty<VividPrimitiveDrawSectionDescriptor>());
        }

        private static VividPrimitiveDrawSectionDescriptor CreateSection(
            int sourceSectionIndex,
            VividPrimitiveResourceKey geometryKey,
            VividPrimitiveResourceKey materialKey)
        {
            return new VividPrimitiveDrawSectionDescriptor(
                sourceSectionIndex,
                geometryKey,
                materialKey,
                VividPrimitiveDrawSectionFlags.Valid);
        }

        private static VividInstanceData CreateLegacyInstance(uint materialIndex, uint topMeshLODStartIndex)
        {
            return new VividInstanceData
            {
                TopMeshLODStartIndex = topMeshLODStartIndex,
                TotalMeshLODCount = 2u,
                MeshLODLevelCount = 1u,
                MaterialIndex = materialIndex,
                PassMask = VividInstancePassMask.Main,
            };
        }

        private static void AssertBridgeRows(
            VividPrimitiveScene scene,
            IReadOnlyList<EntityId> primitiveIds,
            IReadOnlyList<VividPrimitiveHandle> handles,
            IReadOnlyList<VividPrimitiveDrawSectionData> sections,
            IReadOnlyList<uint> expectedMaterialIndices)
        {
            for (int row = 0; row < primitiveIds.Count; row++)
            {
                VividLegacyInstanceMappingData mapping = scene.LegacyInstanceMappingTable[row];
                Assert.That(mapping.Flags, Is.EqualTo(1u));
                Assert.That(mapping.PrimitiveIndex, Is.EqualTo((uint) handles[row].Index));
                Assert.That(mapping.PrimitiveGeneration, Is.EqualTo(handles[row].Generation));
                Assert.That(mapping.DrawSectionIndex, Is.EqualTo(
                    scene.PrimitiveTable[handles[row].Index].DrawSectionOffset));
                Assert.That(scene.GeometryTable[(int) sections[row].GeometryIndex]
                    .LegacyTopMeshLODStartIndex, Is.EqualTo((uint) (10 + row)));
                Assert.That(scene.MaterialTable[(int) sections[row].MaterialIndex]
                    .LegacyMaterialIndex, Is.EqualTo(expectedMaterialIndices[row]));
            }
        }

        private static VividPrimitiveResourceKey CreateResourceKey(
            VividPrimitiveResourceDomain domain,
            EntityId objectId)
        {
            return new VividPrimitiveResourceKey(domain, objectId, EntityId.None, -1);
        }

        private static VividPrimitiveResourceKey CreateMissingMaterialKey(
            EntityId ownerId,
            int sourceSectionIndex)
        {
            return new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.MissingMaterial,
                EntityId.None,
                ownerId,
                sourceSectionIndex);
        }

        private static void AssertHLSLFieldsInOrder(
            string source,
            string structName,
            params string[] fieldNames)
        {
            int structStart = source.IndexOf($"struct {structName}", StringComparison.Ordinal);
            Assert.That(structStart, Is.GreaterThanOrEqualTo(0), structName);
            int bodyStart = source.IndexOf('{', structStart);
            int bodyEnd = source.IndexOf('}', bodyStart);
            Assert.That(bodyStart, Is.GreaterThan(structStart), structName);
            Assert.That(bodyEnd, Is.GreaterThan(bodyStart), structName);

            int searchStart = bodyStart;
            for (int index = 0; index < fieldNames.Length; index++)
            {
                int fieldIndex = source.IndexOf(fieldNames[index], searchStart, StringComparison.Ordinal);
                Assert.That(fieldIndex, Is.GreaterThan(searchStart), $"{structName}.{fieldNames[index]}");
                Assert.That(fieldIndex, Is.LessThan(bodyEnd), $"{structName}.{fieldNames[index]}");
                searchStart = fieldIndex;
            }
        }
    }
}
