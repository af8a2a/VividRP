using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    internal sealed class VividPrimitiveScene
    {
        internal const uint InvalidIndex = uint.MaxValue;

        private readonly VividVersionedSlotAllocator m_PrimitiveSlots = new();
        private readonly VividVersionedSlotAllocator m_GeometrySlots = new();
        private readonly VividVersionedSlotAllocator m_MaterialSlots = new();
        private readonly VividPrimitiveSectionRangeAllocator m_SectionRanges = new();
        private readonly Dictionary<EntityId, VividPrimitiveHandle> m_PrimitivesByEntityId = new(new EntityIdComparer());
        private readonly Dictionary<VividPrimitiveResourceKey, VividPrimitiveGeometryHandle> m_GeometriesByKey = new();
        private readonly Dictionary<VividPrimitiveResourceKey, VividPrimitiveMaterialHandle> m_MaterialsByKey = new();
        private readonly List<PrimitiveRecord> m_PrimitiveRecords = new();
        private readonly List<GeometryRecord> m_GeometryRecords = new();
        private readonly List<MaterialRecord> m_MaterialRecords = new();
        private readonly List<VividPrimitiveDrawSectionData> m_SectionUpdateScratch = new();
        private readonly HashSet<int> m_MovedPrimitiveSlots = new();

        private int m_PreparedFrameIndex = int.MinValue;
        private int m_ActiveDrawSectionCount;
        private int m_ChangedPrimitiveCount;
        private int m_FullResyncCount;
        private int m_LastUploadDirtyPageCount;
        private int m_LastUploadRangeCount;
        private long m_LastUploadBytes;
        private uint m_SceneRevision;

        internal VividPrimitiveGpuTable<VividPrimitiveData> PrimitiveTable { get; } = new();
        internal VividPrimitiveGpuTable<VividPrimitiveTransformData> TransformTable { get; } = new();
        internal VividPrimitiveGpuTable<VividPrimitivePreviousTransformData> PreviousTransformTable { get; } = new();
        internal VividPrimitiveGpuTable<VividPrimitiveDrawSectionData> DrawSectionTable { get; } = new();
        internal VividPrimitiveGpuTable<VividPrimitiveGeometryData> GeometryTable { get; } = new();
        internal VividPrimitiveGpuTable<VividPrimitiveMaterialData> MaterialTable { get; } = new();
        internal VividPrimitiveGpuTable<VividLegacyInstanceMappingData> LegacyInstanceMappingTable { get; } = new();

        internal VividPrimitiveHandle RegisterOrUpdate(in VividPrimitiveSourceDescriptor descriptor)
        {
            if (descriptor.SourceEntityId.Equals(EntityId.None))
                throw new ArgumentException("PrimitiveScene requires a valid source EntityId.", nameof(descriptor));

            if (!m_PrimitivesByEntityId.TryGetValue(descriptor.SourceEntityId, out VividPrimitiveHandle handle)
                || !IsValid(handle))
            {
                handle = Register(descriptor);
                m_ChangedPrimitiveCount++;
                IncrementSceneRevision();
                return handle;
            }

            if (Update(handle, descriptor))
                m_ChangedPrimitiveCount++;
            return handle;
        }

        internal bool Remove(EntityId sourceEntityId)
        {
            if (sourceEntityId.Equals(EntityId.None)
                || !m_PrimitivesByEntityId.TryGetValue(sourceEntityId, out VividPrimitiveHandle handle)
                || !IsValid(handle))
            {
                return false;
            }

            PrimitiveRecord record = m_PrimitiveRecords[handle.Index];
            ReleaseSections(record.DrawSectionOffset, record.DrawSectionCount);
            m_ActiveDrawSectionCount -= record.DrawSectionCount;
            m_PrimitivesByEntityId.Remove(sourceEntityId);
            m_MovedPrimitiveSlots.Remove(handle.Index);

            if (!m_PrimitiveSlots.Free(handle.Index, handle.Generation, out uint nextGeneration))
                return false;

            m_PrimitiveRecords[handle.Index] = default;
            PrimitiveTable.Set(handle.Index, new VividPrimitiveData
            {
                TransformIndex = (uint) handle.Index,
                DrawSectionOffset = InvalidIndex,
                Generation = nextGeneration,
                CustomDataAddress = InvalidIndex,
            });
            TransformTable.Set(handle.Index, default);
            PreviousTransformTable.Set(handle.Index, default);
            m_ChangedPrimitiveCount++;
            IncrementSceneRevision();
            return true;
        }

        internal bool TryGetHandle(EntityId sourceEntityId, out VividPrimitiveHandle handle)
        {
            handle = VividPrimitiveHandle.Invalid;
            return !sourceEntityId.Equals(EntityId.None)
                && m_PrimitivesByEntityId.TryGetValue(sourceEntityId, out handle)
                && IsValid(handle);
        }

        internal bool IsValid(VividPrimitiveHandle handle)
        {
            return handle.IsValid && m_PrimitiveSlots.IsValid(handle.Index, handle.Generation);
        }

        internal void CollectSourceEntityIds(List<EntityId> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            foreach (KeyValuePair<EntityId, VividPrimitiveHandle> primitive in m_PrimitivesByEntityId)
            {
                if (IsValid(primitive.Value))
                    destination.Add(primitive.Key);
            }
        }

        internal void BeginFrame(int frameIndex)
        {
            if (m_PreparedFrameIndex == frameIndex)
                return;

            m_PreparedFrameIndex = frameIndex;
            m_ChangedPrimitiveCount = 0;
            m_LastUploadDirtyPageCount = 0;
            m_LastUploadRangeCount = 0;
            m_LastUploadBytes = 0L;

            foreach (int primitiveSlot in m_MovedPrimitiveSlots)
            {
                if (!m_PrimitiveSlots.IsAllocated(primitiveSlot))
                    continue;

                VividPrimitiveTransformData transformData = TransformTable[primitiveSlot];
                PreviousTransformTable.SetIfChanged(primitiveSlot, new VividPrimitivePreviousTransformData
                {
                    PreviousObjectToWorldMatrix = transformData.ObjectToWorldMatrix,
                });
            }
            m_MovedPrimitiveSlots.Clear();
        }

        internal void RecordFullResync()
        {
            m_FullResyncCount++;
            PrimitiveTable.MarkAllDirty();
            TransformTable.MarkAllDirty();
            PreviousTransformTable.MarkAllDirty();
            DrawSectionTable.MarkAllDirty();
            GeometryTable.MarkAllDirty();
            MaterialTable.MarkAllDirty();
            LegacyInstanceMappingTable.MarkAllDirty();
        }

        internal void InvalidateLegacyResourcePayloads()
        {
            for (int index = 0; index < m_GeometryRecords.Count; index++)
            {
                GeometryRecord record = m_GeometryRecords[index];
                if (!record.Allocated)
                    continue;

                GeometryTable.SetIfChanged(index, CreateInvalidGeometryData(record.Handle.Generation));
            }

            for (int index = 0; index < m_MaterialRecords.Count; index++)
            {
                MaterialRecord record = m_MaterialRecords[index];
                if (!record.Allocated)
                    continue;

                MaterialTable.SetIfChanged(index, CreateInvalidMaterialData(record.Handle.Generation));
            }
        }

        internal bool UpdateGeometryPayload(
            VividPrimitiveResourceKey key,
            in VividInstanceData legacyInstanceData)
        {
            if (!TryGetGeometryHandle(key, out VividPrimitiveGeometryHandle handle))
                return false;

            GeometryTable.SetIfChanged(handle.Index, new VividPrimitiveGeometryData
            {
                Generation = handle.Generation,
                LegacyTopMeshLODStartIndex = legacyInstanceData.TopMeshLODStartIndex,
                LegacyTotalMeshLODCount = legacyInstanceData.TotalMeshLODCount,
                LegacyMeshLODLevelCount = legacyInstanceData.MeshLODLevelCount,
            });
            return true;
        }

        internal bool UpdateMaterialPayload(
            VividPrimitiveResourceKey key,
            uint legacyMaterialIndex,
            in VividMaterialData legacyMaterialData)
        {
            if (!TryGetMaterialHandle(key, out VividPrimitiveMaterialHandle handle))
                return false;

            MaterialTable.SetIfChanged(handle.Index, new VividPrimitiveMaterialData
            {
                Generation = handle.Generation,
                LegacyMaterialIndex = legacyMaterialIndex,
                RendererListID = legacyMaterialData.RendererListID,
                MaterialFlags = legacyMaterialData.MaterialFlags,
            });
            return true;
        }

        internal bool TryGetAbsoluteDrawSectionIndex(
            EntityId primitiveEntityId,
            int sourceSectionIndex,
            out VividPrimitiveHandle primitiveHandle,
            out int absoluteDrawSectionIndex)
        {
            primitiveHandle = VividPrimitiveHandle.Invalid;
            absoluteDrawSectionIndex = -1;
            if (!TryGetHandle(primitiveEntityId, out primitiveHandle))
                return false;

            PrimitiveRecord record = m_PrimitiveRecords[primitiveHandle.Index];
            for (int sectionOffset = 0; sectionOffset < record.DrawSectionCount; sectionOffset++)
            {
                int sectionIndex = record.DrawSectionOffset + sectionOffset;
                VividPrimitiveDrawSectionData sectionData = DrawSectionTable[sectionIndex];
                if ((sectionData.Flags & VividPrimitiveDrawSectionFlags.Valid) != 0
                    && sectionData.SourceSectionIndex == (uint) sourceSectionIndex)
                {
                    absoluteDrawSectionIndex = sectionIndex;
                    return true;
                }
            }

            return false;
        }

        internal void ResizeLegacyInstanceMappings(int count)
        {
            LegacyInstanceMappingTable.Resize(count);
        }

        internal void SetLegacyInstanceMapping(int index, in VividLegacyInstanceMappingData mapping)
        {
            LegacyInstanceMappingTable.Set(index, mapping);
        }

        internal VividPrimitiveSceneStats GetStats()
        {
            return new VividPrimitiveSceneStats(
                m_PrimitiveSlots.ActiveCount,
                m_PrimitiveSlots.SlotCount,
                m_PrimitiveSlots.FreeCount,
                m_ActiveDrawSectionCount,
                m_SectionRanges.HighWaterMark,
                m_GeometrySlots.ActiveCount,
                m_GeometrySlots.SlotCount,
                m_GeometrySlots.FreeCount,
                m_MaterialSlots.ActiveCount,
                m_MaterialSlots.SlotCount,
                m_MaterialSlots.FreeCount,
                m_SceneRevision,
                m_ChangedPrimitiveCount,
                m_FullResyncCount,
                m_LastUploadDirtyPageCount,
                m_LastUploadRangeCount,
                m_LastUploadBytes);
        }

        internal void SetLastUploadStats(int dirtyPageCount, int rangeCount, long byteCount)
        {
            m_LastUploadDirtyPageCount = Mathf.Max(0, dirtyPageCount);
            m_LastUploadRangeCount = Mathf.Max(0, rangeCount);
            m_LastUploadBytes = Math.Max(0L, byteCount);
        }

        private VividPrimitiveHandle Register(in VividPrimitiveSourceDescriptor descriptor)
        {
            int primitiveSlot = m_PrimitiveSlots.Allocate(out uint generation);
            var handle = new VividPrimitiveHandle(primitiveSlot, generation);
            EnsureRecordCount(m_PrimitiveRecords, primitiveSlot + 1);

            int sectionCount = descriptor.DrawSections.Count;
            int sectionOffset = AllocateSections(descriptor.DrawSections);
            m_ActiveDrawSectionCount += sectionCount;
            m_PrimitiveRecords[primitiveSlot] = new PrimitiveRecord(
                descriptor.SourceEntityId,
                handle,
                sectionOffset,
                sectionCount);
            m_PrimitivesByEntityId.Add(descriptor.SourceEntityId, handle);

            float4x4 objectToWorld = ToFloat4x4(descriptor.ObjectToWorldMatrix);
            PrimitiveTable.Set(primitiveSlot, CreatePrimitiveData(descriptor, handle, sectionOffset, sectionCount));
            TransformTable.Set(primitiveSlot, new VividPrimitiveTransformData
            {
                ObjectToWorldMatrix = objectToWorld,
                WorldToObjectMatrix = ToFloat4x4(descriptor.WorldToObjectMatrix),
            });
            PreviousTransformTable.Set(primitiveSlot, new VividPrimitivePreviousTransformData
            {
                PreviousObjectToWorldMatrix = objectToWorld,
            });
            return handle;
        }

        private bool Update(VividPrimitiveHandle handle, in VividPrimitiveSourceDescriptor descriptor)
        {
            PrimitiveRecord record = m_PrimitiveRecords[handle.Index];
            bool sectionsChanged = !DoSectionsMatch(record, descriptor.DrawSections);
            if (sectionsChanged)
            {
                int newSectionCount = descriptor.DrawSections.Count;
                int newSectionOffset;
                if (newSectionCount == record.DrawSectionCount)
                {
                    newSectionOffset = record.DrawSectionOffset;
                    UpdateSectionsInPlace(newSectionOffset, descriptor.DrawSections);
                }
                else
                {
                    newSectionOffset = AllocateSections(descriptor.DrawSections);
                    ReleaseSections(record.DrawSectionOffset, record.DrawSectionCount);
                }
                m_ActiveDrawSectionCount += newSectionCount - record.DrawSectionCount;
                record = new PrimitiveRecord(
                    descriptor.SourceEntityId,
                    handle,
                    newSectionOffset,
                    newSectionCount);
                m_PrimitiveRecords[handle.Index] = record;
                IncrementSceneRevision();
            }

            VividPrimitiveTransformData previousTransform = TransformTable[handle.Index];
            float4x4 objectToWorld = ToFloat4x4(descriptor.ObjectToWorldMatrix);
            float4x4 worldToObject = ToFloat4x4(descriptor.WorldToObjectMatrix);
            bool transformChanged = !AreEqual(previousTransform.ObjectToWorldMatrix, objectToWorld)
                || !AreEqual(previousTransform.WorldToObjectMatrix, worldToObject);
            if (transformChanged)
            {
                if (m_MovedPrimitiveSlots.Add(handle.Index))
                {
                    PreviousTransformTable.Set(handle.Index, new VividPrimitivePreviousTransformData
                    {
                        PreviousObjectToWorldMatrix = previousTransform.ObjectToWorldMatrix,
                    });
                }

                TransformTable.Set(handle.Index, new VividPrimitiveTransformData
                {
                    ObjectToWorldMatrix = objectToWorld,
                    WorldToObjectMatrix = worldToObject,
                });
            }

            bool primitiveDataChanged = PrimitiveTable.SetIfChanged(
                handle.Index,
                CreatePrimitiveData(descriptor, handle, record.DrawSectionOffset, record.DrawSectionCount));
            return sectionsChanged || transformChanged || primitiveDataChanged;
        }

        private int AllocateSections(IReadOnlyList<VividPrimitiveDrawSectionDescriptor> descriptors)
        {
            int count = descriptors.Count;
            if (count == 0)
                return 0;

            int sectionOffset = m_SectionRanges.Allocate(count);
            DrawSectionTable.Resize(m_SectionRanges.HighWaterMark);
            for (int sectionIndex = 0; sectionIndex < count; sectionIndex++)
            {
                DrawSectionTable.Set(
                    sectionOffset + sectionIndex,
                    CreateSectionData(descriptors[sectionIndex]));
            }
            return sectionOffset;
        }

        private void UpdateSectionsInPlace(
            int sectionOffset,
            IReadOnlyList<VividPrimitiveDrawSectionDescriptor> descriptors)
        {
            m_SectionUpdateScratch.Clear();
            for (int index = 0; index < descriptors.Count; index++)
                m_SectionUpdateScratch.Add(CreateSectionData(descriptors[index]));

            for (int index = 0; index < descriptors.Count; index++)
            {
                int absoluteIndex = sectionOffset + index;
                ReleaseSectionResources(DrawSectionTable[absoluteIndex]);
                DrawSectionTable.Set(absoluteIndex, m_SectionUpdateScratch[index]);
            }
            m_SectionUpdateScratch.Clear();
        }

        private VividPrimitiveDrawSectionData CreateSectionData(
            in VividPrimitiveDrawSectionDescriptor descriptor)
        {
            VividPrimitiveGeometryHandle geometryHandle = AcquireGeometry(descriptor.GeometryKey);
            VividPrimitiveMaterialHandle materialHandle = AcquireMaterial(descriptor.MaterialKey);
            return new VividPrimitiveDrawSectionData
            {
                GeometryIndex = geometryHandle.IsValid ? (uint) geometryHandle.Index : InvalidIndex,
                GeometryGeneration = geometryHandle.Generation,
                MaterialIndex = materialHandle.IsValid ? (uint) materialHandle.Index : InvalidIndex,
                MaterialGeneration = materialHandle.Generation,
                SourceSectionIndex = (uint) descriptor.SourceSectionIndex,
                Flags = descriptor.Flags,
            };
        }

        private void ReleaseSections(int sectionOffset, int sectionCount)
        {
            if (sectionCount <= 0)
                return;

            for (int index = 0; index < sectionCount; index++)
            {
                int absoluteIndex = sectionOffset + index;
                VividPrimitiveDrawSectionData section = DrawSectionTable[absoluteIndex];
                ReleaseSectionResources(section);
                DrawSectionTable.Set(absoluteIndex, default);
            }
            m_SectionRanges.Free(sectionOffset, sectionCount);
        }

        private void ReleaseSectionResources(in VividPrimitiveDrawSectionData section)
        {
            ReleaseGeometry(new VividPrimitiveGeometryHandle((int) section.GeometryIndex, section.GeometryGeneration));
            ReleaseMaterial(new VividPrimitiveMaterialHandle((int) section.MaterialIndex, section.MaterialGeneration));
        }

        private bool DoSectionsMatch(
            in PrimitiveRecord record,
            IReadOnlyList<VividPrimitiveDrawSectionDescriptor> descriptors)
        {
            if (record.DrawSectionCount != descriptors.Count)
                return false;

            for (int index = 0; index < record.DrawSectionCount; index++)
            {
                VividPrimitiveDrawSectionData section = DrawSectionTable[record.DrawSectionOffset + index];
                VividPrimitiveDrawSectionDescriptor descriptor = descriptors[index];
                if (section.SourceSectionIndex != (uint) descriptor.SourceSectionIndex
                    || section.Flags != descriptor.Flags
                    || !GeometryKeyMatches(section, descriptor.GeometryKey)
                    || !MaterialKeyMatches(section, descriptor.MaterialKey))
                {
                    return false;
                }
            }
            return true;
        }

        private VividPrimitiveGeometryHandle AcquireGeometry(VividPrimitiveResourceKey key)
        {
            if (!key.IsValid)
                return VividPrimitiveGeometryHandle.Invalid;

            if (m_GeometriesByKey.TryGetValue(key, out VividPrimitiveGeometryHandle handle)
                && m_GeometrySlots.IsValid(handle.Index, handle.Generation))
            {
                GeometryRecord record = m_GeometryRecords[handle.Index];
                record.ReferenceCount++;
                m_GeometryRecords[handle.Index] = record;
                return handle;
            }

            int slot = m_GeometrySlots.Allocate(out uint generation);
            handle = new VividPrimitiveGeometryHandle(slot, generation);
            EnsureRecordCount(m_GeometryRecords, slot + 1);
            m_GeometryRecords[slot] = new GeometryRecord(key, handle, 1, true);
            m_GeometriesByKey.Add(key, handle);
            GeometryTable.Set(slot, CreateInvalidGeometryData(generation));
            return handle;
        }

        private VividPrimitiveMaterialHandle AcquireMaterial(VividPrimitiveResourceKey key)
        {
            if (!key.IsValid)
                return VividPrimitiveMaterialHandle.Invalid;

            if (m_MaterialsByKey.TryGetValue(key, out VividPrimitiveMaterialHandle handle)
                && m_MaterialSlots.IsValid(handle.Index, handle.Generation))
            {
                MaterialRecord record = m_MaterialRecords[handle.Index];
                record.ReferenceCount++;
                m_MaterialRecords[handle.Index] = record;
                return handle;
            }

            int slot = m_MaterialSlots.Allocate(out uint generation);
            handle = new VividPrimitiveMaterialHandle(slot, generation);
            EnsureRecordCount(m_MaterialRecords, slot + 1);
            m_MaterialRecords[slot] = new MaterialRecord(key, handle, 1, true);
            m_MaterialsByKey.Add(key, handle);
            MaterialTable.Set(slot, CreateInvalidMaterialData(generation));
            return handle;
        }

        private void ReleaseGeometry(VividPrimitiveGeometryHandle handle)
        {
            if (!handle.IsValid || !m_GeometrySlots.IsValid(handle.Index, handle.Generation))
                return;

            GeometryRecord record = m_GeometryRecords[handle.Index];
            record.ReferenceCount--;
            if (record.ReferenceCount > 0)
            {
                m_GeometryRecords[handle.Index] = record;
                return;
            }

            m_GeometriesByKey.Remove(record.Key);
            m_GeometrySlots.Free(handle.Index, handle.Generation, out uint nextGeneration);
            m_GeometryRecords[handle.Index] = default;
            GeometryTable.Set(handle.Index, CreateInvalidGeometryData(nextGeneration));
        }

        private void ReleaseMaterial(VividPrimitiveMaterialHandle handle)
        {
            if (!handle.IsValid || !m_MaterialSlots.IsValid(handle.Index, handle.Generation))
                return;

            MaterialRecord record = m_MaterialRecords[handle.Index];
            record.ReferenceCount--;
            if (record.ReferenceCount > 0)
            {
                m_MaterialRecords[handle.Index] = record;
                return;
            }

            m_MaterialsByKey.Remove(record.Key);
            m_MaterialSlots.Free(handle.Index, handle.Generation, out uint nextGeneration);
            m_MaterialRecords[handle.Index] = default;
            MaterialTable.Set(handle.Index, CreateInvalidMaterialData(nextGeneration));
        }

        private bool TryGetGeometryHandle(
            VividPrimitiveResourceKey key,
            out VividPrimitiveGeometryHandle handle)
        {
            handle = VividPrimitiveGeometryHandle.Invalid;
            return key.IsValid
                && m_GeometriesByKey.TryGetValue(key, out handle)
                && m_GeometrySlots.IsValid(handle.Index, handle.Generation);
        }

        private bool TryGetMaterialHandle(
            VividPrimitiveResourceKey key,
            out VividPrimitiveMaterialHandle handle)
        {
            handle = VividPrimitiveMaterialHandle.Invalid;
            return key.IsValid
                && m_MaterialsByKey.TryGetValue(key, out handle)
                && m_MaterialSlots.IsValid(handle.Index, handle.Generation);
        }

        private bool GeometryKeyMatches(
            in VividPrimitiveDrawSectionData section,
            VividPrimitiveResourceKey key)
        {
            if (!key.IsValid)
                return section.GeometryIndex == InvalidIndex && section.GeometryGeneration == 0u;
            if (section.GeometryIndex > int.MaxValue)
                return false;

            var handle = new VividPrimitiveGeometryHandle((int) section.GeometryIndex, section.GeometryGeneration);
            return m_GeometrySlots.IsValid(handle.Index, handle.Generation)
                && m_GeometryRecords[handle.Index].Key.Equals(key);
        }

        private bool MaterialKeyMatches(
            in VividPrimitiveDrawSectionData section,
            VividPrimitiveResourceKey key)
        {
            if (!key.IsValid)
                return section.MaterialIndex == InvalidIndex && section.MaterialGeneration == 0u;
            if (section.MaterialIndex > int.MaxValue)
                return false;

            var handle = new VividPrimitiveMaterialHandle((int) section.MaterialIndex, section.MaterialGeneration);
            return m_MaterialSlots.IsValid(handle.Index, handle.Generation)
                && m_MaterialRecords[handle.Index].Key.Equals(key);
        }

        private static VividPrimitiveData CreatePrimitiveData(
            in VividPrimitiveSourceDescriptor descriptor,
            VividPrimitiveHandle handle,
            int sectionOffset,
            int sectionCount)
        {
            Vector3 minimum = descriptor.WorldBounds.min;
            Vector3 maximum = descriptor.WorldBounds.max;
            return new VividPrimitiveData
            {
                WorldBoundsMin = new float4(minimum.x, minimum.y, minimum.z, 0.0f),
                WorldBoundsMax = new float4(maximum.x, maximum.y, maximum.z, 0.0f),
                TransformIndex = (uint) handle.Index,
                DrawSectionOffset = sectionCount > 0 ? (uint) sectionOffset : InvalidIndex,
                DrawSectionCount = (uint) sectionCount,
                RenderingLayerMask = descriptor.RenderingLayerMask,
                PassMask = (uint) descriptor.PassMask,
                Flags = descriptor.Flags,
                Generation = handle.Generation,
                CustomDataAddress = InvalidIndex,
            };
        }

        private static VividPrimitiveGeometryData CreateInvalidGeometryData(uint generation)
        {
            return new VividPrimitiveGeometryData
            {
                Generation = generation,
                LegacyTopMeshLODStartIndex = InvalidIndex,
                LegacyTotalMeshLODCount = 0u,
                LegacyMeshLODLevelCount = 0u,
            };
        }

        private static VividPrimitiveMaterialData CreateInvalidMaterialData(uint generation)
        {
            return new VividPrimitiveMaterialData
            {
                Generation = generation,
                LegacyMaterialIndex = InvalidIndex,
                RendererListID = VividRendererListID.Default,
                MaterialFlags = VividMaterialFlags.None,
            };
        }

        private void IncrementSceneRevision()
        {
            m_SceneRevision = m_SceneRevision == uint.MaxValue ? 1u : m_SceneRevision + 1u;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }

        private static bool AreEqual(float4x4 left, float4x4 right)
        {
            return math.all(left.c0 == right.c0)
                && math.all(left.c1 == right.c1)
                && math.all(left.c2 == right.c2)
                && math.all(left.c3 == right.c3);
        }

        private static void EnsureRecordCount<T>(List<T> records, int count)
        {
            while (records.Count < count)
                records.Add(default);
        }

        private readonly struct PrimitiveRecord
        {
            internal PrimitiveRecord(
                EntityId sourceEntityId,
                VividPrimitiveHandle handle,
                int drawSectionOffset,
                int drawSectionCount)
            {
                SourceEntityId = sourceEntityId;
                Handle = handle;
                DrawSectionOffset = drawSectionOffset;
                DrawSectionCount = drawSectionCount;
            }

            internal EntityId SourceEntityId { get; }
            internal VividPrimitiveHandle Handle { get; }
            internal int DrawSectionOffset { get; }
            internal int DrawSectionCount { get; }
        }

        private struct GeometryRecord
        {
            internal GeometryRecord(
                VividPrimitiveResourceKey key,
                VividPrimitiveGeometryHandle handle,
                int referenceCount,
                bool allocated)
            {
                Key = key;
                Handle = handle;
                ReferenceCount = referenceCount;
                Allocated = allocated;
            }

            internal VividPrimitiveResourceKey Key;
            internal VividPrimitiveGeometryHandle Handle;
            internal int ReferenceCount;
            internal bool Allocated;
        }

        private struct MaterialRecord
        {
            internal MaterialRecord(
                VividPrimitiveResourceKey key,
                VividPrimitiveMaterialHandle handle,
                int referenceCount,
                bool allocated)
            {
                Key = key;
                Handle = handle;
                ReferenceCount = referenceCount;
                Allocated = allocated;
            }

            internal VividPrimitiveResourceKey Key;
            internal VividPrimitiveMaterialHandle Handle;
            internal int ReferenceCount;
            internal bool Allocated;
        }

        private sealed class EntityIdComparer : IEqualityComparer<EntityId>
        {
            public bool Equals(EntityId x, EntityId y)
            {
                return EntityId.ToULong(x) == EntityId.ToULong(y);
            }

            public int GetHashCode(EntityId obj)
            {
                return EntityId.ToULong(obj).GetHashCode();
            }
        }
    }
}
