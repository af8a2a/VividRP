using NUnit.Framework;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.ECS;
using VividRP.Runtime.Particle;
using VividRP.Runtime.Particle.ECS;

namespace VividRP.Editor.Tests
{
    public sealed class VividParticleEcsTests
    {
        [Test]
        public void TypeManager_RegistersParticleTypes_WithStableSoaOffsets()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex noiseStateIndex = VividEcsTypeManager.GetTypeIndex<VividParticleNoiseState>();
            VividEcsTypeIndex systemIdIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>();
            VividEcsTypeIndex moduleKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleModuleSharedKey>();
            VividEcsTypeIndex simulationKernelKeyIndex =
                VividEcsTypeManager.GetTypeIndex<VividParticleSimulationKernelSharedKey>();
            VividEcsTypeIndex renderKernelKeyIndex =
                VividEcsTypeManager.GetTypeIndex<VividParticleRenderKernelSharedKey>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            VividEcsTypeIndex rendererHandleIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererHandle>();
            VividEcsTypeIndex simulationActiveIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSimulationActive>();
            VividEcsTypeIndex rendererActiveIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererActive>();
            VividEcsTypeInfo commonType = VividEcsTypeManager.GetTypeInfo(commonIndex);

            Assert.That(commonIndex.IsValid, Is.True);
            Assert.That(noiseStateIndex.IsValid, Is.True);
            Assert.That(noiseStateIndex.IsSoaComponentType, Is.True);
            Assert.That(systemIdIndex.IsValid, Is.True);
            Assert.That(moduleKeyIndex.IsValid, Is.True);
            Assert.That(simulationKernelKeyIndex.IsValid, Is.True);
            Assert.That(renderKernelKeyIndex.IsValid, Is.True);
            Assert.That(rendererKeyIndex.IsValid, Is.True);
            Assert.That(rendererHandleIndex.IsValid, Is.True);
            Assert.That(simulationActiveIndex.IsValid, Is.True);
            Assert.That(rendererActiveIndex.IsValid, Is.True);
            Assert.That(systemIdIndex.Value, Is.Not.EqualTo(commonIndex.Value));
            Assert.That(systemIdIndex.IsSharedComponentType, Is.True);
            Assert.That(moduleKeyIndex.IsSharedComponentType, Is.True);
            Assert.That(simulationKernelKeyIndex.IsSharedComponentType, Is.True);
            Assert.That(renderKernelKeyIndex.IsSharedComponentType, Is.True);
            Assert.That(rendererKeyIndex.IsSharedComponentType, Is.True);
            Assert.That(rendererHandleIndex.IsSharedComponentType, Is.True);
            Assert.That(simulationActiveIndex.IsTagComponentType, Is.True);
            Assert.That(rendererActiveIndex.IsTagComponentType, Is.True);
            Assert.That(VividEcsTypeManager.RegisteredTypeCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(commonType.IsSoa, Is.True);
            Assert.That(commonType.SoaFieldCount, Is.EqualTo(VividParticleCommon.FieldCountValue));
            Assert.That(commonType.SizeInPage, Is.EqualTo(VividParticleCommon.TypeSizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.PositionFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.PositionOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.PositionFieldIndex).ElementSize, Is.EqualTo(VividParticleCommon.Float3SizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.VelocityFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.VelocityOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.StartColorFieldIndex).ElementSize, Is.EqualTo(VividParticleCommon.Float4SizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.SizeFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.SizeOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.MeshIndexFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.MeshIndexOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.MeshIndexFieldIndex).ElementSize, Is.EqualTo(VividParticleCommon.IntSizeInBytes));
            Assert.That(
                commonType.GetSoaFieldInfo(VividParticleCommon.AccumulatedRotationFieldIndex).OffsetInPage,
                Is.EqualTo(VividParticleCommon.AccumulatedRotationOffsetInPage));
            Assert.That(
                commonType.GetSoaFieldInfo(VividParticleCommon.AccumulatedRotationFieldIndex).ElementSize,
                Is.EqualTo(VividParticleCommon.Float3SizeInBytes));
        }

        [Test]
        public void Storage_UsesPageCapacity_AndClampsActiveCountWhenShrunk()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            storage.EnsureCapacity(300);

            Assert.That(VividEcsConstants.PageEntryCount, Is.EqualTo(VividParticleStorage.PageSize));
            Assert.That(storage.capacity, Is.EqualTo(512));
            Assert.That(storage.pageCount, Is.EqualTo(2));
            Assert.That(storage.tileStart, Is.EqualTo(0));
            Assert.That(storage.tileCount, Is.EqualTo(2));
            Assert.That(storage.allocatorLiveTileCount, Is.EqualTo(2));

            for (int index = 0; index < 300; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            Assert.That(storage.activeCount, Is.EqualTo(300));

            storage.EnsureCapacity(3);

            Assert.That(storage.capacity, Is.EqualTo(256));
            Assert.That(storage.pageCount, Is.EqualTo(1));
            Assert.That(storage.activeCount, Is.EqualTo(3));
            Assert.That(storage.tileCount, Is.EqualTo(1));
            Assert.That(storage.allocatorLiveTileCount, Is.EqualTo(1));
            Assert.That(storage.allocatorHighWatermarkTileCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Constants_AlignToPage_UsesFixedPageEntryCount()
        {
            Assert.That(VividEcsConstants.PageEntryCount, Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(0), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(1), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(256), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(257), Is.EqualTo(512));
        }

        [Test]
        public void Storage_AddWritesAndReadsAcrossPages()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            storage.EnsureCapacity(300);

            for (int index = 0; index < 260; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            Assert.That(storage.activeCount, Is.EqualTo(260));
            Assert.That(storage.capacity, Is.EqualTo(512));
            AssertVector3(new Vector3(0.0f, 1.0f, 2.0f), storage.GetPosition(0));
            AssertVector3(new Vector3(259.0f, 260.0f, 261.0f), storage.GetPosition(259));
            AssertVector3(new Vector3(259.5f, 260.5f, 261.5f), storage.GetVelocity(259));
            Assert.That(storage.GetStartLifetime(259), Is.EqualTo(10.0f));
            Assert.That(storage.GetRemainingLifetime(259), Is.EqualTo(10.0f));
            Assert.That(storage.GetSize(259), Is.EqualTo(260.0f));
            AssertVector3(Vector3.zero, storage.GetAccumulatedRotation(259));
            AssertColor(new Color(0.25f, 0.5f, 0.75f, 1.0f), storage.GetColor(259));
            Assert.That(storage.GetMeshIndex(259), Is.EqualTo(259 % 3));

            using VividEcsPageGroup pageGroup = storage.CreatePageGroup(Allocator.TempJob);
            Assert.That(pageGroup.pageCount, Is.EqualTo(2));
            Assert.That(pageGroup[0].EntryCount, Is.EqualTo(256));
            Assert.That(pageGroup[1].EntryCount, Is.EqualTo(4));
            Assert.That(pageGroup[1].StartIndex, Is.EqualTo(256));
            Assert.That(storage.systemId, Is.EqualTo(new VividParticleSystemId(17)));
        }

        [Test]
        public void Storage_NoiseStateColumn_IsLazyAndPreservesCommonParticleData()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(32);
            Assert.That(AddParticle(storage, 3), Is.True);

            Vector3 position = storage.GetPosition(0);
            Vector3 velocity = storage.GetVelocity(0);
            Color color = storage.GetColor(0);
            float size = storage.GetSize(0);
            int lineId = storage.archetypeLineId;

            Assert.That(storage.hasNoiseStateColumn, Is.False);

            storage.EnsureNoiseStateColumn();

            Assert.That(storage.hasNoiseStateColumn, Is.True);
            Assert.That(storage.archetypeLineId, Is.EqualTo(lineId));
            Assert.That(storage.activeCount, Is.EqualTo(1));
            AssertVector3(position, storage.GetPosition(0));
            AssertVector3(velocity, storage.GetVelocity(0));
            AssertColor(color, storage.GetColor(0));
            Assert.That(storage.GetSize(0), Is.EqualTo(size));
        }

        [Test]
        public void Storage_QueryLineGroups_UseSharedRendererKey()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            var moduleKey = new VividParticleModuleSharedKey(
                VividParticleModuleFlags.VelocityOverLifetime
                | VividParticleModuleFlags.LimitVelocityOverLifetime
                | VividParticleModuleFlags.ColorOverLifetime
                | VividParticleModuleFlags.ColorBySpeed
                | VividParticleModuleFlags.SizeBySpeed
                | VividParticleModuleFlags.RotationBySpeed
                | VividParticleModuleFlags.Noise
                | VividParticleModuleFlags.TextureSheetAnimation);
            storage.moduleSharedKey = moduleKey;
            storage.simulationKernelSharedKey = new VividParticleSimulationKernelSharedKey(
                moduleKey.EnabledFlags);
            storage.renderKernelSharedKey = new VividParticleRenderKernelSharedKey(
                moduleKey.EnabledFlags);
            var rendererKey = new VividParticleRendererSharedKey(
                materialId: 1,
                meshId: 2,
                renderMode: (int)VividParticleRenderMode.Billboard,
                layer: 3,
                gpuDataLayoutHash: 4,
                dataPerSharpBits: 5u,
                shadowCastingMode: 0,
                sortMode: 0,
                renderingLayerMask: 0xffu,
                receiveShadows: false);
            storage.rendererSharedKey = rendererKey;
            var rendererHandle = new VividParticleRendererHandle(recordSlot: 7, recordVersion: 3);
            Assert.That(storage.rendererHandle, Is.EqualTo(VividParticleRendererHandle.Invalid));
            Assert.That(storage.rendererActive, Is.False);
            storage.rendererActive = true;
            storage.rendererHandle = rendererHandle;
            storage.EnsureCapacity(4);
            Assert.That(AddParticle(storage, 0), Is.True);

            using VividEcsPageGroup pageGroup = storage.CreateSimulationPageGroup(Allocator.TempJob);
            var groups = storage.CreateLineGroups();

            Assert.That(pageGroup.pageCount, Is.EqualTo(1));
            Assert.That(storage.queryLineGroupCount, Is.EqualTo(1));
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].activeCount, Is.EqualTo(1));
            Assert.That(storage.rendererSharedKey, Is.EqualTo(rendererKey));
            Assert.That(storage.moduleSharedKey, Is.EqualTo(moduleKey));
            Assert.That(storage.moduleSharedKey.Has(VividParticleModuleFlags.VelocityOverLifetime), Is.True);
            Assert.That(
                storage.simulationKernelSharedKey.EnabledFlags,
                Is.EqualTo(VividParticleModuleFlags.VelocityOverLifetime
                    | VividParticleModuleFlags.LimitVelocityOverLifetime
                    | VividParticleModuleFlags.RotationBySpeed
                    | VividParticleModuleFlags.Noise));
            Assert.That(
                storage.renderKernelSharedKey.EnabledFlags,
                Is.EqualTo(VividParticleModuleFlags.VelocityOverLifetime
                    | VividParticleModuleFlags.ColorOverLifetime
                    | VividParticleModuleFlags.ColorBySpeed
                    | VividParticleModuleFlags.SizeBySpeed
                    | VividParticleModuleFlags.RotationBySpeed
                    | VividParticleModuleFlags.TextureSheetAnimation));
            Assert.That(storage.rendererHandle, Is.EqualTo(rendererHandle));
            Assert.That(storage.rendererActive, Is.True);
            storage.rendererHandle = VividParticleRendererHandle.Invalid;
            Assert.That(storage.rendererHandle, Is.EqualTo(VividParticleRendererHandle.Invalid));
            Assert.That(storage.rendererActive, Is.True);
            storage.rendererActive = false;
            Assert.That(storage.rendererActive, Is.False);
        }

        [Test]
        public void GlobalStorage_RendererQuery_OnlyIncludesActiveRendererHandles()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            VividEcsTypeIndex rendererActiveIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererActive>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                var rendererKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false);
                first.rendererSharedKey = rendererKey;
                second.rendererSharedKey = rendererKey;
                first.rendererActive = true;
                first.rendererHandle = new VividParticleRendererHandle(recordSlot: 2, recordVersion: 7);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex, rendererActiveIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(groups, Has.Count.EqualTo(1));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].lines[0].ArchetypeLineId, Is.EqualTo(first.archetypeLineId));
                Assert.That(first.rendererActive, Is.True);
                Assert.That(second.rendererActive, Is.False);
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_SimulationQuery_OnlyIncludesActiveSimulationLines()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex simulationActiveIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSimulationActive>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.simulationActive = true;

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex, simulationActiveIndex);

                Assert.That(query.PrepareMatchingLines(), Is.EqualTo(1));
                Assert.That(query.GetMatchingLine(0).ArchetypeLineId, Is.EqualTo(first.archetypeLineId));
                Assert.That(first.simulationActive, Is.True);
                Assert.That(second.simulationActive, Is.False);

                first.simulationActive = false;
                second.simulationActive = true;

                Assert.That(query.PrepareMatchingLines(), Is.EqualTo(1));
                Assert.That(query.GetMatchingLine(0).ArchetypeLineId, Is.EqualTo(second.archetypeLineId));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_QueryLineGroups_GroupDifferentSystemsByRendererKey()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                var rendererKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false);
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.rendererSharedKey = rendererKey;
                second.rendererSharedKey = rendererKey;
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(2));
                Assert.That(groups, Has.Count.EqualTo(1));
                Assert.That(groups[0].lineCount, Is.EqualTo(2));
                Assert.That(groups[0].activeCount, Is.EqualTo(2));

                first.Dispose();
                groups = world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(1));
                Assert.That(groups, Has.Count.EqualTo(1));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].activeCount, Is.EqualTo(1));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_QueryLineGroups_SplitByRendererSortingPriority()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    sortingPriority: 10);
                second.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    sortingPriority: 20);
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(2));
                Assert.That(groups, Has.Count.EqualTo(2));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[1].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].SharedKey, Is.Not.EqualTo(groups[1].SharedKey));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_QueryLineGroups_SplitByRendererBatchLayer()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    batchLayer: 1);
                second.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    batchLayer: 2);
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(2));
                Assert.That(groups, Has.Count.EqualTo(2));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[1].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].SharedKey, Is.Not.EqualTo(groups[1].SharedKey));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_QueryLineGroups_SplitByRendererMotionMode()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    motionMode: (int)MotionVectorGenerationMode.ForceNoMotion);
                second.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    motionMode: (int)MotionVectorGenerationMode.Camera);
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(2));
                Assert.That(groups, Has.Count.EqualTo(2));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[1].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].SharedKey, Is.Not.EqualTo(groups[1].SharedKey));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void GlobalStorage_QueryLineGroups_SplitByRendererStaticShadowCaster()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.systemId = new VividParticleSystemId(17);
                second.systemId = new VividParticleSystemId(23);
                first.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    staticShadowCaster: false);
                second.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false,
                    staticShadowCaster: true);
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                VividEcsQuery query = world.CreateQuery().WithAll(commonIndex);
                List<VividEcsArchetypeLineGroup> groups =
                    world.CreateArchetypeLineGroups(query, rendererKeyIndex);

                Assert.That(world.archetypeLineCount, Is.EqualTo(2));
                Assert.That(groups, Has.Count.EqualTo(2));
                Assert.That(groups[0].lineCount, Is.EqualTo(1));
                Assert.That(groups[1].lineCount, Is.EqualTo(1));
                Assert.That(groups[0].SharedKey, Is.Not.EqualTo(groups[1].SharedKey));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void Storage_QueryLineGroupCount_UsesRendererSharedKeyGroupingWithoutCreatingGroups()
        {
            using var world = new VividEcsWorld();
            var first = new VividParticleEcsStorage(world);
            var second = new VividParticleEcsStorage(world);
            try
            {
                first.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 1,
                    meshId: 2,
                    renderMode: (int)VividParticleRenderMode.Billboard,
                    layer: 3,
                    gpuDataLayoutHash: 4,
                    dataPerSharpBits: 5u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false);
                second.rendererSharedKey = new VividParticleRendererSharedKey(
                    materialId: 6,
                    meshId: 7,
                    renderMode: (int)VividParticleRenderMode.Mesh,
                    layer: 8,
                    gpuDataLayoutHash: 9,
                    dataPerSharpBits: 10u,
                    shadowCastingMode: 0,
                    sortMode: 0,
                    renderingLayerMask: 0xffu,
                    receiveShadows: false);
                first.EnsureCapacity(4);
                second.EnsureCapacity(4);

                Assert.That(AddParticle(first, 0), Is.True);
                Assert.That(AddParticle(second, 1), Is.True);

                Assert.That(first.queryLineGroupCount, Is.EqualTo(2));
                Assert.That(second.queryLineGroupCount, Is.EqualTo(2));
                Assert.That(first.CreateLineGroups(), Has.Count.EqualTo(2));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void PageJob_SchedulesForEachLivePage()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(300);
            for (int index = 0; index < 300; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            using VividEcsPageGroup pageGroup = storage.CreatePageGroup(Allocator.TempJob);
            var counts = new NativeArray<int>(pageGroup.pageCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var job = new CapturePageCountsJob
                {
                    Counts = counts,
                };

                JobHandle handle = job.Schedule(pageGroup.pages);
                handle.Complete();

                Assert.That(counts[0], Is.EqualTo(256));
                Assert.That(counts[1], Is.EqualTo(44));
            }
            finally
            {
                counts.Dispose();
            }
        }

        [Test]
        public void Storage_ScheduleIntegrate_MatchesExistingStorage()
        {
            using var legacyStorage = new VividParticleStorage();
            using var ecsStorage = new VividParticleEcsStorage();
            legacyStorage.EnsureCapacity(8);
            ecsStorage.EnsureCapacity(8);

            for (int index = 0; index < 3; index++)
            {
                Vector3 position = new(index, index + 2.0f, index + 4.0f);
                Vector3 velocity = new(index + 0.25f, index + 0.5f, index + 0.75f);
                Color color = new(0.1f * index, 0.2f, 0.3f, 1.0f);
                Assert.That(legacyStorage.Add(position, velocity, 5.0f, 5.0f, index + 1.0f, color), Is.True);
                Assert.That(ecsStorage.Add(position, velocity, 5.0f, 5.0f, index + 1.0f, color), Is.True);
            }

            Vector3 gravity = new(0.0f, -9.81f, 0.0f);
            Assert.That(legacyStorage.ScheduleIntegrate(0.125f, gravity, out JobHandle legacyHandle), Is.True);
            Assert.That(ecsStorage.ScheduleIntegrate(0.125f, gravity, out JobHandle ecsHandle), Is.True);

            legacyHandle.Complete();
            ecsHandle.Complete();
            legacyStorage.ApplyScheduledIntegrateResult();
            ecsStorage.ApplyScheduledIntegrateResult();

            Assert.That(ecsStorage.activeCount, Is.EqualTo(legacyStorage.activeCount));
            for (int index = 0; index < legacyStorage.activeCount; index++)
            {
                AssertVector3(legacyStorage.GetPosition(index), ecsStorage.GetPosition(index));
                AssertVector3(legacyStorage.GetVelocity(index), ecsStorage.GetVelocity(index));
                Assert.That(ecsStorage.GetStartLifetime(index), Is.EqualTo(legacyStorage.GetStartLifetime(index)));
                Assert.That(ecsStorage.GetRemainingLifetime(index), Is.EqualTo(legacyStorage.GetRemainingLifetime(index)));
                Assert.That(ecsStorage.GetSize(index), Is.EqualTo(legacyStorage.GetSize(index)));
                AssertColor(legacyStorage.GetColor(index), ecsStorage.GetColor(index));
            }
        }

        [Test]
        public void Storage_ScheduleIntegrate_CompactsExpiredParticlesWithSwapBack()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(3);
            Assert.That(storage.Add(Vector3.zero, Vector3.zero, 0.05f, 0.05f, 1.0f, Color.red, meshIndex: 0), Is.True);
            Assert.That(storage.Add(Vector3.one, Vector3.zero, 5.0f, 5.0f, 2.0f, Color.green, meshIndex: 1), Is.True);
            Assert.That(storage.Add(Vector3.right, Vector3.zero, 6.0f, 6.0f, 3.0f, Color.blue, meshIndex: 2), Is.True);

            Assert.That(storage.ScheduleIntegrate(0.1f, Vector3.zero, out JobHandle handle), Is.True);
            handle.Complete();
            storage.ApplyScheduledIntegrateResult();

            Assert.That(storage.activeCount, Is.EqualTo(2));
            AssertColor(Color.blue, storage.GetColor(0));
            Assert.That(storage.GetMeshIndex(0), Is.EqualTo(2));
            AssertColor(Color.green, storage.GetColor(1));
            Assert.That(storage.GetMeshIndex(1), Is.EqualTo(1));
            Assert.That(storage.GetRemainingLifetime(0), Is.EqualTo(5.9f).Within(0.0001f));
        }

        [Test]
        public void Storage_ReserveInitializeParticles_WritesPointParticlesInBurst()
        {
            using var storage = new VividParticleEcsStorage();
            using var works = new NativeList<VividParticleEcsInitializeParticlesWork>(1, Allocator.TempJob);
            storage.EnsureCapacity(4);
            VividParticleSystemFrameSnapshot snapshot = CreatePointSnapshot(meshCount: 3);

            Assert.That(storage.ReserveInitializeParticles(3, snapshot, 123u, works, out int firstIndex, out int count), Is.True);
            Assert.That(firstIndex, Is.EqualTo(0));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(storage.activeCount, Is.EqualTo(3));

            var job = new VividParticleEcsInitializeParticlesJob
            {
                Works = works.AsArray(),
            };
            job.Schedule(works.Length, innerloopBatchCount: 1).Complete();

            for (int index = 0; index < 3; index++)
            {
                AssertVector3(Vector3.zero, storage.GetPosition(index));
                AssertVector3(new Vector3(0.0f, 0.0f, 2.5f), storage.GetVelocity(index));
                Assert.That(storage.GetStartLifetime(index), Is.EqualTo(3.0f));
                Assert.That(storage.GetRemainingLifetime(index), Is.EqualTo(3.0f));
                Assert.That(storage.GetSize(index), Is.EqualTo(0.75f));
                AssertColor(new Color(0.2f, 0.4f, 0.6f, 0.8f), storage.GetColor(index));
                Assert.That(storage.GetMeshIndex(index), Is.EqualTo(index % 3));
            }
        }

        [Test]
        public void Storage_ColumnView_RefreshesOnlyWhenBackingMemoryChanges()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(32);

            Assert.That(storage.EnsureColumnView(), Is.True);
            int initialVersion = storage.columnViewVersion;
            Assert.That(storage.capacity, Is.EqualTo(256));
            Assert.That(storage.columnViewRefreshCount, Is.EqualTo(1));

            Assert.That(AddParticle(storage, 0), Is.True);
            storage.rendererHandle = new VividParticleRendererHandle(recordSlot: 4, recordVersion: 2);
            storage.EnsureCapacity(128);

            Assert.That(storage.EnsureColumnView(), Is.True);
            Assert.That(storage.columnViewVersion, Is.EqualTo(initialVersion));
            Assert.That(storage.columnViewRefreshCount, Is.EqualTo(1));

            storage.EnsureCapacity(300);

            Assert.That(storage.EnsureColumnView(), Is.True);
            int resizedVersion = storage.columnViewVersion;
            Assert.That(storage.capacity, Is.EqualTo(512));
            Assert.That(resizedVersion, Is.Not.EqualTo(initialVersion));
            Assert.That(storage.columnViewRefreshCount, Is.EqualTo(2));
            AssertVector3(new Vector3(0.0f, 1.0f, 2.0f), storage.GetPosition(0));

            storage.EnsureCapacity(300);
            Assert.That(storage.EnsureColumnView(), Is.True);
            Assert.That(storage.columnViewVersion, Is.EqualTo(resizedVersion));
            Assert.That(storage.columnViewRefreshCount, Is.EqualTo(2));
        }

        [Test]
        public void Storage_AnimatedMotionColumn_IsLazyAndPreservesCommonData()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(32);
            Assert.That(AddParticle(storage, 0), Is.True);
            Assert.That(storage.hasAnimatedMotionColumn, Is.False);

            storage.EnsureAnimatedMotionColumn();

            Assert.That(storage.hasAnimatedMotionColumn, Is.True);
            Assert.That(storage.EnsureColumnView(), Is.True);
            AssertVector3(Vector3.zero, storage.GetAnimatedVelocity(0));
            AssertVector3(new Vector3(0.0f, 1.0f, 2.0f), storage.GetPosition(0));

            storage.EnsureCapacity(300);
            Assert.That(storage.EnsureColumnView(), Is.True);
            AssertVector3(new Vector3(0.0f, 1.0f, 2.0f), storage.GetPosition(0));
        }

        [Test]
        public void Storage_DisposeCanRepeat_WithoutThrowing()
        {
            var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(1);

            Assert.DoesNotThrow(() => storage.Dispose());
            Assert.DoesNotThrow(() => storage.Dispose());
        }

        private static bool AddParticle(VividParticleEcsStorage storage, int index)
        {
            return storage.Add(
                new Vector3(index, index + 1.0f, index + 2.0f),
                new Vector3(index + 0.5f, index + 1.5f, index + 2.5f),
                10.0f,
                10.0f,
                index + 1.0f,
                new Color(0.25f, 0.5f, 0.75f, 1.0f),
                index % 3);
        }

        private static VividParticleSystemFrameSnapshot CreatePointSnapshot(int meshCount = 0)
        {
            return new VividParticleSystemFrameSnapshot(
                0.0f,
                true,
                true,
                false,
                false,
                5.0f,
                true,
                3.0f,
                2.5f,
                0.75f,
                new Color(0.2f, 0.4f, 0.6f, 0.8f),
                0.0f,
                VividParticleSystemSimulationSpace.Local,
                4,
                1u,
                false,
                true,
                0.0f,
                null,
                true,
                VividParticleShapeType.Point,
                1.0f,
                Vector3.one,
                25.0f,
                false,
                Vector3.zero,
                VividParticleForceSpace.Local,
                true,
                VividParticleRenderMode.Billboard,
                null,
                null,
                meshCount,
                Color.white,
                1.0f,
                2.0f,
                0.0f,
                0,
                0,
                Vector3.zero,
                Matrix4x4.identity,
                Quaternion.identity,
                1);
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        [BurstCompile]
        private struct CapturePageCountsJob : IVividEcsPageJob
        {
            public NativeArray<int> Counts;

            public void Execute(VividEcsPageInfo page, int pageIndex)
            {
                Counts[pageIndex] = page.EntryCount;
            }
        }
    }
}
