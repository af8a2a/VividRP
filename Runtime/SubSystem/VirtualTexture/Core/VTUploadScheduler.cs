using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal interface IVTUploadFenceHandle
    {
        bool IsPassed { get; }
    }

    internal interface IVTUploadFenceFactory
    {
        IVTUploadFenceHandle Create(CommandBuffer cmd);
    }

    internal interface IVTUploadRequestCommitter
    {
        bool TryCommitUpload(in VTRequest request, int frameIndex);
    }

    internal interface IVTUploadRequestCommitterResolver
    {
        IVTUploadRequestCommitter ResolveCommitter(int spaceId);
    }

    internal static class VTPageUploadUtility
    {
        internal static Texture2DArray CreateStagingTexture(string spaceName, int physicalPageSize, int depth, string suffix)
        {
            var stagingTexture = new Texture2DArray(
                physicalPageSize,
                physicalPageSize,
                Mathf.Max(1, depth),
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = $"VividVT_{spaceName}_{suffix}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            stagingTexture.Apply(false, false);
            return stagingTexture;
        }

        internal static Texture2DArray CreateEncodedStagingTexture(
            string spaceName,
            int physicalPageSize,
            int depth,
            GraphicsFormat graphicsFormat,
            string suffix)
        {
            var stagingTexture = new Texture2DArray(
                physicalPageSize,
                physicalPageSize,
                Mathf.Max(1, depth),
                graphicsFormat,
                TextureCreationFlags.None)
            {
                name = $"VividVT_{spaceName}_{suffix}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            return stagingTexture;
        }

        internal static RenderTexture CreateGpuStagingTexture(
            string spaceName,
            int physicalPageSize,
            int depth,
            GraphicsFormat graphicsFormat,
            string suffix,
            bool enableRandomWrite = true)
        {
            var descriptor = new RenderTextureDescriptor(
                physicalPageSize,
                physicalPageSize,
                graphicsFormat,
                0)
            {
                msaaSamples = 1,
                volumeDepth = Mathf.Max(1, depth),
                dimension = TextureDimension.Tex2DArray,
                enableRandomWrite = enableRandomWrite,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
            };
            var stagingTexture = new RenderTexture(descriptor)
            {
                name = $"VividVT_{spaceName}_{suffix}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            if (!stagingTexture.Create())
            {
                CoreUtils.Destroy(stagingTexture);
                throw new InvalidOperationException(
                    $"[VividRP] Failed to create VT GPU upload staging texture for '{spaceName}'.");
            }

            return stagingTexture;
        }

        internal static void WritePayloadToStagingTexture(
            Texture2DArray stagingTexture,
            int slice,
            Color32[] scratchPixels,
            in VTPageUploadPayload payload,
            CommandBuffer cmd)
        {
            FinalizePayloadRender(payload, cmd);
            WritePayloadLayerToStagingTexture(stagingTexture, slice, scratchPixels, payload, layerIndex: 0);
        }

        internal static void FinalizePayloadRender(in VTPageUploadPayload payload, CommandBuffer cmd)
        {
            if (!payload.IsValid)
                throw new ArgumentException("Upload payload must include a page finalizer.", nameof(payload));

            if (payload.Finalizer is not IVTPageFinalizer cpuFinalizer)
                throw new ArgumentException("Upload payload does not contain a CPU page finalizer.", nameof(payload));

            cpuFinalizer.FinalizeRender(cmd);
        }

        internal static void WritePayloadLayerToStagingTexture(
            Texture2DArray stagingTexture,
            int slice,
            Color32[] scratchPixels,
            in VTPageUploadPayload payload,
            int layerIndex)
        {
            if (stagingTexture == null)
                throw new ArgumentNullException(nameof(stagingTexture));
            if (scratchPixels == null)
                throw new ArgumentNullException(nameof(scratchPixels));
            if (!payload.IsValid)
                throw new ArgumentException("Upload payload must include a page finalizer.", nameof(payload));

            if (payload.Finalizer is IVTMultiLayerPageFinalizer multiLayerFinalizer)
            {
                multiLayerFinalizer.FinalizeUploadLayer(stagingTexture, slice, layerIndex, scratchPixels);
                return;
            }

            if (layerIndex == 0 && payload.Finalizer is IVTPageFinalizer cpuFinalizer)
            {
                cpuFinalizer.FinalizeUpload(stagingTexture, slice, scratchPixels);
                return;
            }

            for (int pixelIndex = 0; pixelIndex < scratchPixels.Length; pixelIndex++)
                scratchPixels[pixelIndex] = new Color32(0, 0, 0, 255);

            stagingTexture.SetPixels32(scratchPixels, slice, 0);
        }
    }

    internal sealed class VTUploadScheduler : IDisposable
    {
        private const int k_DefaultMaxUploadsPerFrame = 64;

        private readonly struct UploadPoolKey : IEquatable<UploadPoolKey>
        {
            internal UploadPoolKey(in VirtualTextureSpaceDesc desc)
            {
                PhysicalPageSize = desc.PhysicalPageSize;
                PhysicalPoolDesc = VTPhysicalPoolDesc.FromSpaceDesc(desc);
                LayoutKey = PhysicalPoolDesc.LayerGroup;
            }

            internal int PhysicalPageSize { get; }

            internal VTPhysicalPoolDesc PhysicalPoolDesc { get; }

            internal string LayoutKey { get; }

            internal GraphicsFormat GraphicsFormat => PhysicalPoolDesc.GraphicsFormat;

            internal int LayerCount => PhysicalPoolDesc.LayerCount;

            internal int PhysicalGroupCount => PhysicalPoolDesc.PhysicalGroupCount;

            internal int GetGroupLayerCount(int physicalGroup)
            {
                return PhysicalPoolDesc.GetGroupLayerCount(physicalGroup);
            }

            internal GraphicsFormat GetGroupStorageFormat(int physicalGroup)
            {
                return PhysicalPoolDesc.GetGroupStorageFormat(physicalGroup);
            }

            public bool Equals(UploadPoolKey other)
            {
                return PhysicalPageSize == other.PhysicalPageSize
                       && string.Equals(LayoutKey, other.LayoutKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is UploadPoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    PhysicalPageSize,
                    StringComparer.Ordinal.GetHashCode(LayoutKey ?? string.Empty));
            }
        }

        private readonly struct QueuedUpload
        {
            internal QueuedUpload(
                UploadPoolKey key,
                VTPhysicalPool physicalPool,
                in VTPageUploadPayload payload)
            {
                Key = key;
                PhysicalPool = physicalPool;
                Payload = payload;
            }

            internal UploadPoolKey Key { get; }

            internal VTPhysicalPool PhysicalPool { get; }

            internal VTPageUploadPayload Payload { get; }
        }

        private sealed class GraphicsFenceHandle : IVTUploadFenceHandle
        {
            private readonly GraphicsFence m_Fence;

            internal GraphicsFenceHandle(GraphicsFence fence)
            {
                m_Fence = fence;
            }

            public bool IsPassed => m_Fence.passed;
        }

        private sealed class GraphicsFenceFactory : IVTUploadFenceFactory
        {
            internal static readonly GraphicsFenceFactory Instance = new();

            private GraphicsFenceFactory()
            {
            }

            public IVTUploadFenceHandle Create(CommandBuffer cmd)
            {
                if (cmd == null)
                    throw new ArgumentNullException(nameof(cmd));

                GraphicsFence fence = cmd.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.AllGPUOperations);
                return new GraphicsFenceHandle(fence);
            }
        }

        private sealed class UploadBatch : IDisposable
        {
            private readonly string m_SpaceName;
            private readonly int m_PhysicalPageSize;
            private readonly UploadPoolKey m_Key;
            private readonly VTRequest[] m_Requests;
            private readonly VTPhysicalPool[] m_PhysicalPools;
            private readonly bool[] m_UsesGpuStaging;
            private readonly bool[] m_UsesEncodedStaging;
            private readonly RenderTexture[] m_ConvertedStagingTextures;
            private readonly Texture2DArray[] m_EncodedStagingTextures;

            private int m_RequestCount;
            private IVTUploadFenceHandle m_Fence;
            private Texture2DArray m_CpuStagingTexture;
            private RenderTexture m_GpuStagingTexture;

            internal UploadBatch(
                string spaceName,
                int physicalPageSize,
                int layerCount,
                int capacity,
                int batchIndex,
                in UploadPoolKey key)
            {
                Capacity = Mathf.Max(1, capacity);
                LayerCount = Mathf.Max(1, layerCount);
                m_SpaceName = spaceName;
                m_PhysicalPageSize = physicalPageSize;
                m_Key = key;
                m_Requests = new VTRequest[Capacity];
                m_PhysicalPools = new VTPhysicalPool[Capacity];
                m_UsesGpuStaging = new bool[Capacity];
                m_UsesEncodedStaging = new bool[Capacity];
                m_ConvertedStagingTextures = new RenderTexture[Mathf.Max(1, key.PhysicalGroupCount)];
                m_EncodedStagingTextures = new Texture2DArray[Mathf.Max(1, key.PhysicalGroupCount)];
                BatchIndex = batchIndex;
            }

            internal int Capacity { get; }

            internal int LayerCount { get; }

            private int BatchIndex { get; }

            internal Texture2DArray CpuStagingTexture
            {
                get
                {
                    m_CpuStagingTexture ??= VTPageUploadUtility.CreateStagingTexture(
                        m_SpaceName,
                        m_PhysicalPageSize,
                        Capacity * LayerCount,
                        $"UploadBatch{BatchIndex}");
                    return m_CpuStagingTexture;
                }
            }

            internal RenderTexture GpuStagingTexture
            {
                get
                {
                    m_GpuStagingTexture ??= VTPageUploadUtility.CreateGpuStagingTexture(
                        m_SpaceName,
                        m_PhysicalPageSize,
                        Capacity * LayerCount,
                        m_Key.GraphicsFormat,
                        $"GPUUploadBatch{BatchIndex}");
                    return m_GpuStagingTexture;
                }
            }

            internal RenderTexture GetConvertedStagingTexture(int physicalGroup)
            {
                if (physicalGroup < 0 || physicalGroup >= m_ConvertedStagingTextures.Length)
                    throw new ArgumentOutOfRangeException(nameof(physicalGroup));

                RenderTexture stagingTexture = m_ConvertedStagingTextures[physicalGroup];
                if (stagingTexture != null)
                    return stagingTexture;

                int groupLayerCount = Mathf.Max(1, m_Key.GetGroupLayerCount(physicalGroup));
                GraphicsFormat storageFormat = m_Key.GetGroupStorageFormat(physicalGroup);
                stagingTexture = VTPageUploadUtility.CreateGpuStagingTexture(
                    m_SpaceName,
                    m_PhysicalPageSize,
                    Capacity * groupLayerCount,
                    storageFormat,
                    $"ConvertedUploadBatch{BatchIndex}_Group{physicalGroup}",
                    enableRandomWrite: false);
                m_ConvertedStagingTextures[physicalGroup] = stagingTexture;
                return stagingTexture;
            }

            internal Texture2DArray GetEncodedStagingTexture(int physicalGroup)
            {
                if (physicalGroup < 0 || physicalGroup >= m_EncodedStagingTextures.Length)
                    throw new ArgumentOutOfRangeException(nameof(physicalGroup));

                Texture2DArray stagingTexture = m_EncodedStagingTextures[physicalGroup];
                if (stagingTexture != null)
                    return stagingTexture;

                int groupLayerCount = Mathf.Max(1, m_Key.GetGroupLayerCount(physicalGroup));
                stagingTexture = VTPageUploadUtility.CreateEncodedStagingTexture(
                    m_SpaceName,
                    m_PhysicalPageSize,
                    Capacity * groupLayerCount,
                    m_Key.GetGroupStorageFormat(physicalGroup),
                    $"EncodedUploadBatch{BatchIndex}_Group{physicalGroup}");
                m_EncodedStagingTextures[physicalGroup] = stagingTexture;
                return stagingTexture;
            }

            internal bool InFlight => m_Fence != null;

            internal int RequestCount => m_RequestCount;

            internal bool HasGpuStagingTexture => m_GpuStagingTexture != null;

            internal bool HasCpuStagingTexture => m_CpuStagingTexture != null;

            internal VTRequest GetRequest(int index)
            {
                if (index < 0 || index >= m_RequestCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return m_Requests[index];
            }

            internal bool HasRequest(in VTRequest request)
            {
                for (int requestIndex = 0; requestIndex < m_RequestCount; requestIndex++)
                {
                    if (IsSameUploadIdentity(m_Requests[requestIndex], request))
                        return true;
                }

                return false;
            }

            internal bool HasRequestForSpace(int spaceId)
            {
                for (int requestIndex = 0; requestIndex < m_RequestCount; requestIndex++)
                {
                    if (m_Requests[requestIndex].SpaceId == spaceId)
                        return true;
                }

                return false;
            }

            internal void SetRequest(int index, in VTRequest request)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_Requests[index] = request;
            }

            internal void SetPhysicalPool(int index, VTPhysicalPool physicalPool)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_PhysicalPools[index] = physicalPool;
            }

            internal void SetUsesGpuStaging(int index, bool usesGpuStaging)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_UsesGpuStaging[index] = usesGpuStaging;
            }

            internal void SetUsesEncodedStaging(int index, bool usesEncodedStaging)
            {
                if (index < 0 || index >= Capacity)
                    throw new ArgumentOutOfRangeException(nameof(index));

                m_UsesEncodedStaging[index] = usesEncodedStaging;
            }

            internal Texture GetStagingTexture(int index, int physicalGroup)
            {
                if (index < 0 || index >= m_RequestCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                if (m_UsesEncodedStaging[index])
                    return GetEncodedStagingTexture(physicalGroup);
                return m_UsesGpuStaging[index] ? m_GpuStagingTexture : m_CpuStagingTexture;
            }

            internal bool UsesEncodedStaging(int index)
            {
                return index >= 0 && index < m_RequestCount && m_UsesEncodedStaging[index];
            }

            internal VTPhysicalPool GetPhysicalPool(int index)
            {
                if (index < 0 || index >= m_RequestCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return m_PhysicalPools[index];
            }

            internal void SealRequests(int requestCount)
            {
                m_RequestCount = Mathf.Clamp(requestCount, 0, Capacity);
            }

            internal void Submit(IVTUploadFenceHandle fence)
            {
                m_Fence = fence ?? throw new ArgumentNullException(nameof(fence));
            }

            internal bool IsPassed()
            {
                return m_Fence != null && m_Fence.IsPassed;
            }

            internal void Reset()
            {
                m_Fence = null;
                Array.Clear(m_PhysicalPools, 0, m_PhysicalPools.Length);
                Array.Clear(m_UsesGpuStaging, 0, m_UsesGpuStaging.Length);
                Array.Clear(m_UsesEncodedStaging, 0, m_UsesEncodedStaging.Length);
                m_RequestCount = 0;
            }

            internal int CancelRequestsForSpace(int spaceId)
            {
                return CancelRequests(request => request.SpaceId == spaceId);
            }

            internal int CancelRequestsForRegion(int spaceId, int mip, RectInt pageRegion)
            {
                return CancelRequests(request =>
                    request.SpaceId == spaceId
                    && request.PageCoord.Mip == mip
                    && pageRegion.Contains(new Vector2Int(request.PageCoord.X, request.PageCoord.Y)));
            }

            private int CancelRequests(Predicate<VTRequest> shouldCancel)
            {
                int removedCount = 0;
                int writeIndex = 0;
                for (int readIndex = 0; readIndex < m_RequestCount; readIndex++)
                {
                    if (shouldCancel(m_Requests[readIndex]))
                    {
                        m_PhysicalPools[readIndex] = null;
                        m_UsesGpuStaging[readIndex] = false;
                        m_UsesEncodedStaging[readIndex] = false;
                        removedCount += 1;
                        continue;
                    }

                    if (writeIndex != readIndex)
                    {
                        m_Requests[writeIndex] = m_Requests[readIndex];
                        m_PhysicalPools[writeIndex] = m_PhysicalPools[readIndex];
                        m_UsesGpuStaging[writeIndex] = m_UsesGpuStaging[readIndex];
                        m_UsesEncodedStaging[writeIndex] = m_UsesEncodedStaging[readIndex];
                    }

                    writeIndex += 1;
                }

                if (writeIndex < m_RequestCount)
                {
                    Array.Clear(m_PhysicalPools, writeIndex, m_RequestCount - writeIndex);
                    Array.Clear(m_UsesGpuStaging, writeIndex, m_RequestCount - writeIndex);
                    Array.Clear(m_UsesEncodedStaging, writeIndex, m_RequestCount - writeIndex);
                }

                m_RequestCount = writeIndex;
                return removedCount;
            }

            public void Dispose()
            {
                Reset();
                if (m_CpuStagingTexture != null)
                    CoreUtils.Destroy(m_CpuStagingTexture);
                m_CpuStagingTexture = null;
                if (m_GpuStagingTexture != null)
                    CoreUtils.Destroy(m_GpuStagingTexture);
                m_GpuStagingTexture = null;
                for (int groupIndex = 0; groupIndex < m_ConvertedStagingTextures.Length; groupIndex++)
                {
                    if (m_ConvertedStagingTextures[groupIndex] != null)
                        CoreUtils.Destroy(m_ConvertedStagingTextures[groupIndex]);
                    m_ConvertedStagingTextures[groupIndex] = null;
                    if (m_EncodedStagingTextures[groupIndex] != null)
                        CoreUtils.Destroy(m_EncodedStagingTextures[groupIndex]);
                    m_EncodedStagingTextures[groupIndex] = null;
                }
            }

            private static bool IsSameUploadIdentity(in VTRequest left, in VTRequest right)
            {
                return left.SpaceId == right.SpaceId
                       && left.PageCoord.Equals(right.PageCoord)
                       && left.PhysicalPageId == right.PhysicalPageId
                       && left.Generation == right.Generation;
            }
        }

        private sealed class UploadPool : IDisposable
        {
            private readonly string m_Name;
            private readonly UploadPoolKey m_Key;
            private readonly List<UploadBatch> m_Batches = new();
            private readonly bool[] m_TouchedEncodedGroups;

            internal UploadPool(string name, in UploadPoolKey key, int batchCapacity)
            {
                m_Name = string.IsNullOrWhiteSpace(name) ? "Global" : name;
                m_Key = key;
                m_TouchedEncodedGroups = new bool[Mathf.Max(1, m_Key.PhysicalGroupCount)];
                BatchCapacity = Mathf.Max(1, batchCapacity);
                for (int batchIndex = 0; batchIndex < 2; batchIndex++)
                    m_Batches.Add(CreateBatch(batchIndex));
            }

            internal int BatchCapacity { get; private set; }

            internal int InFlightBatchCount
            {
                get
                {
                    int count = 0;
                    for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    {
                        if (m_Batches[batchIndex].InFlight)
                            count += 1;
                    }

                    return count;
                }
            }

            internal int GpuStagingTextureCount
            {
                get
                {
                    int count = 0;
                    for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    {
                        if (m_Batches[batchIndex].HasGpuStagingTexture)
                            count += 1;
                    }

                    return count;
                }
            }

            internal int CpuStagingTextureCount
            {
                get
                {
                    int count = 0;
                    for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    {
                        if (m_Batches[batchIndex].HasCpuStagingTexture)
                            count += 1;
                    }

                    return count;
                }
            }

            internal int AvailableBatchCapacity => FindAvailableBatch()?.Capacity ?? 0;

            internal void EnsureBatchCapacity(int batchCapacity)
            {
                int normalizedCapacity = Mathf.Max(1, batchCapacity);
                if (normalizedCapacity <= BatchCapacity)
                    return;

                BatchCapacity = normalizedCapacity;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (batch.InFlight)
                        continue;

                    batch.Dispose();
                    m_Batches[batchIndex] = CreateBatch(batchIndex);
                }
            }

            internal bool HasInFlightRequest(in VTRequest request)
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequest(request))
                        return true;
                }

                return false;
            }

            internal bool HasInFlightRequestForSpace(int spaceId)
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    if (m_Batches[batchIndex].InFlight && m_Batches[batchIndex].HasRequestForSpace(spaceId))
                        return true;
                }

                return false;
            }

            internal bool CommitCompletedUploads(
                IVTUploadRequestCommitterResolver committerResolver,
                int frameIndex)
            {
                bool committedAny = false;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (!batch.InFlight || !batch.IsPassed())
                        continue;

                    for (int requestIndex = 0; requestIndex < batch.RequestCount; requestIndex++)
                    {
                        VTRequest request = batch.GetRequest(requestIndex);
                        IVTUploadRequestCommitter committer = committerResolver?.ResolveCommitter(request.SpaceId);
                        if (committer != null)
                            committedAny |= committer.TryCommitUpload(request, frameIndex);
                    }

                    batch.Reset();
                    if (batch.Capacity < BatchCapacity)
                    {
                        batch.Dispose();
                        m_Batches[batchIndex] = CreateBatch(batchIndex);
                    }
                }

                return committedAny;
            }

            internal int CancelRequestsForSpace(int spaceId)
            {
                int removedCount = 0;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    removedCount += m_Batches[batchIndex].CancelRequestsForSpace(spaceId);

                return removedCount;
            }

            internal int CancelRequestsForRegion(int spaceId, int mip, RectInt pageRegion)
            {
                int removedCount = 0;
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    removedCount += m_Batches[batchIndex].CancelRequestsForRegion(spaceId, mip, pageRegion);

                return removedCount;
            }

            internal bool ScheduleUploads(
                IReadOnlyList<QueuedUpload> uploads,
                int startIndex,
                int count,
                Color32[] scratchPixels,
                CommandBuffer cmd,
                IVTUploadFenceFactory fenceFactory,
                ref int skippedUploadCount,
                ref int cpuProducedPageCount,
                ref int gpuProducedPageCount,
                ref int gpuDispatchCount)
            {
                if (uploads == null || count <= 0)
                    return false;

                UploadBatch batch = FindAvailableBatch();
                if (batch == null)
                {
                    skippedUploadCount += count;
                    DisposePayloads(uploads, startIndex, count);
                    return false;
                }

                int requestCount = 0;
                bool usedCpuStaging = false;
                Array.Clear(m_TouchedEncodedGroups, 0, m_TouchedEncodedGroups.Length);
                try
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeRenderPayloadsMarker.Auto())
                    {
                        for (int uploadIndex = 0; uploadIndex < count; uploadIndex++)
                        {
                            QueuedUpload upload = uploads[startIndex + uploadIndex];
                            VTPageUploadPayload payload = upload.Payload;
                            if (!payload.IsValid || upload.PhysicalPool == null)
                            {
                                skippedUploadCount += 1;
                                continue;
                            }

                            if (requestCount >= batch.Capacity)
                            {
                                skippedUploadCount += 1;
                                continue;
                            }

                            int baseSlice = requestCount * batch.LayerCount;
                            bool usesGpuStaging;
                            bool usesEncodedStaging = false;
                            if (payload.Finalizer is IVTEncodedPageFinalizer encodedFinalizer)
                            {
                                if (encodedFinalizer.LayerCount != batch.LayerCount)
                                {
                                    skippedUploadCount += 1;
                                    continue;
                                }

                                for (int layerIndex = 0; layerIndex < batch.LayerCount; layerIndex++)
                                {
                                    int physicalGroup = upload.PhysicalPool.GetLayerPhysicalGroup(layerIndex);
                                    int groupLayerCount = Mathf.Max(1, upload.PhysicalPool.GetGroupLayerCount(physicalGroup));
                                    int physicalLayerIndex = upload.PhysicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                                    int encodedSlice = requestCount * groupLayerCount + physicalLayerIndex;
                                    encodedFinalizer.FinalizeEncodedUploadLayer(
                                        batch.GetEncodedStagingTexture(physicalGroup),
                                        encodedSlice,
                                        layerIndex);
                                    m_TouchedEncodedGroups[physicalGroup] = true;
                                }

                                usesGpuStaging = false;
                                usesEncodedStaging = true;
                                cpuProducedPageCount += 1;
                            }
                            else if (payload.Finalizer is IVTGpuPageFinalizer gpuFinalizer)
                            {
                                if (gpuFinalizer.LayerCount != batch.LayerCount)
                                {
                                    skippedUploadCount += 1;
                                    continue;
                                }

                                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeRecordGpuMarker.Auto())
                                    gpuFinalizer.RecordGpuUpload(cmd, batch.GpuStagingTexture, baseSlice);
                                usesGpuStaging = true;
                                gpuProducedPageCount += 1;
                                gpuDispatchCount += 1;
                            }
                            else if (payload.Finalizer is IVTPageFinalizer)
                            {
                                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeWriteCpuStagingMarker.Auto())
                                {
                                    VTPageUploadUtility.FinalizePayloadRender(payload, cmd);
                                    for (int layerIndex = 0; layerIndex < batch.LayerCount; layerIndex++)
                                    {
                                        VTPageUploadUtility.WritePayloadLayerToStagingTexture(
                                            batch.CpuStagingTexture,
                                            baseSlice + layerIndex,
                                            scratchPixels,
                                            payload,
                                            layerIndex);
                                    }
                                }

                                usesGpuStaging = false;
                                usedCpuStaging = true;
                                cpuProducedPageCount += 1;
                            }
                            else
                            {
                                skippedUploadCount += 1;
                                continue;
                            }

                            batch.SetRequest(requestCount, payload.Request);
                            batch.SetPhysicalPool(requestCount, upload.PhysicalPool);
                            batch.SetUsesGpuStaging(requestCount, usesGpuStaging);
                            batch.SetUsesEncodedStaging(requestCount, usesEncodedStaging);
                            requestCount += 1;
                        }
                    }
                }
                finally
                {
                    DisposePayloads(uploads, startIndex, count);
                }

                if (requestCount == 0)
                    return false;

                batch.SealRequests(requestCount);
                if (usedCpuStaging)
                {
                    using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeApplyStagingMarker.Auto())
                        batch.CpuStagingTexture.Apply(false, false);
                }
                for (int physicalGroup = 0; physicalGroup < m_TouchedEncodedGroups.Length; physicalGroup++)
                {
                    if (m_TouchedEncodedGroups[physicalGroup])
                        batch.GetEncodedStagingTexture(physicalGroup).Apply(false, false);
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeCopyToCacheMarker.Auto())
                {
                    for (int uploadIndex = 0; uploadIndex < requestCount; uploadIndex++)
                    {
                        VTRequest request = batch.GetRequest(uploadIndex);
                        VTPhysicalPool physicalPool = batch.GetPhysicalPool(uploadIndex);
                        if (physicalPool == null)
                            continue;

                        int sourceBaseSlice = uploadIndex * batch.LayerCount;
                        for (int layerIndex = 0; layerIndex < batch.LayerCount; layerIndex++)
                        {
                            int physicalGroup = physicalPool.GetLayerPhysicalGroup(layerIndex);
                            Texture stagingTexture = batch.GetStagingTexture(uploadIndex, physicalGroup);
                            if (stagingTexture == null)
                                continue;
                            Texture2D physicalCache = physicalPool.GetTextureForGroup(physicalGroup);
                            if (physicalCache == null)
                                continue;

                            int groupLayerCount = Mathf.Max(1, physicalPool.GetGroupLayerCount(physicalGroup));
                            int physicalLayerIndex = physicalPool.GetLayerPhysicalLayerIndex(layerIndex);
                            int convertedSlice = uploadIndex * groupLayerCount + physicalLayerIndex;
                            RectInt destinationTile = physicalPool.GetPhysicalTileRect(
                                physicalGroup,
                                request.PhysicalPageId,
                                physicalLayerIndex);
                            int sourceSlice = batch.UsesEncodedStaging(uploadIndex)
                                ? convertedSlice
                                : sourceBaseSlice + layerIndex;
                            if (stagingTexture.graphicsFormat == physicalCache.graphicsFormat)
                            {
                                cmd.CopyTexture(
                                    stagingTexture,
                                    sourceSlice,
                                    0,
                                    0,
                                    0,
                                    destinationTile.width,
                                    destinationTile.height,
                                    physicalCache,
                                    0,
                                    0,
                                    destinationTile.x,
                                    destinationTile.y);
                                continue;
                            }

                            if (batch.UsesEncodedStaging(uploadIndex))
                            {
                                throw new InvalidOperationException(
                                    $"[VividRP] Encoded VT staging format {stagingTexture.graphicsFormat} does not "
                                    + $"match physical cache format {physicalCache.graphicsFormat}. Runtime BC "
                                    + "conversion is intentionally disabled.");
                            }

                            RenderTexture convertedStagingTexture =
                                batch.GetConvertedStagingTexture(physicalGroup);
                            cmd.ConvertTexture(
                                stagingTexture,
                                sourceSlice,
                                convertedStagingTexture,
                                convertedSlice);
                            cmd.CopyTexture(
                                convertedStagingTexture,
                                convertedSlice,
                                0,
                                0,
                                0,
                                destinationTile.width,
                                destinationTile.height,
                                physicalCache,
                                0,
                                0,
                                destinationTile.x,
                                destinationTile.y);
                        }
                    }
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeSubmitMarker.Auto())
                    batch.Submit(fenceFactory.Create(cmd));
                return true;
            }

            public void Dispose()
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                    m_Batches[batchIndex].Dispose();

                m_Batches.Clear();
            }

            private UploadBatch FindAvailableBatch()
            {
                for (int batchIndex = 0; batchIndex < m_Batches.Count; batchIndex++)
                {
                    UploadBatch batch = m_Batches[batchIndex];
                    if (!batch.InFlight && batch.Capacity < BatchCapacity)
                    {
                        batch.Dispose();
                        batch = CreateBatch(batchIndex);
                        m_Batches[batchIndex] = batch;
                    }

                    if (!batch.InFlight)
                        return batch;
                }

                return null;
            }

            private UploadBatch CreateBatch(int batchIndex)
            {
                return new UploadBatch(
                    m_Name,
                    m_Key.PhysicalPageSize,
                    m_Key.LayerCount,
                    BatchCapacity,
                    batchIndex,
                    m_Key);
            }
        }

        private static IVTUploadFenceFactory s_FenceFactory = GraphicsFenceFactory.Instance;

        private readonly Dictionary<UploadPoolKey, UploadPool> m_Pools = new();
        private readonly Dictionary<UploadPoolKey, int> m_QueuedCountsByKey = new();
        private readonly List<QueuedUpload> m_QueuedUploads = new();
        private Color32[] m_ScratchPixels = Array.Empty<Color32>();
        private int m_MaxUploadsPerFrame = k_DefaultMaxUploadsPerFrame;
        private int m_MaxUploadBytesPerFrame = int.MaxValue;
        private int m_ReservedUploadCountThisFrame;
        private int m_ReservedUploadBytesThisFrame;
        private int m_LastDuplicateUploadCount;
        private int m_LastSkippedUploadCount;
        private int m_LastCpuProducedPageCount;
        private int m_LastGpuProducedPageCount;
        private int m_LastGpuDispatchCount;

        internal bool IsEnabled => true;

        internal int MaxUploadsPerFrame
        {
            get => m_MaxUploadsPerFrame;
            set => m_MaxUploadsPerFrame = value <= 0 ? int.MaxValue : value;
        }

        internal int MaxUploadBytesPerFrame
        {
            get => m_MaxUploadBytesPerFrame;
            set => m_MaxUploadBytesPerFrame = value <= 0 ? int.MaxValue : value;
        }

        internal int InFlightBatchCount
        {
            get
            {
                int count = 0;
                foreach (UploadPool pool in m_Pools.Values)
                    count += pool.InFlightBatchCount;

                return count;
            }
        }

        internal int LastDuplicateUploadCount => m_LastDuplicateUploadCount;

        internal int LastSkippedUploadCount => m_LastSkippedUploadCount;

        internal int LastCpuProducedPageCount => m_LastCpuProducedPageCount;

        internal int LastGpuProducedPageCount => m_LastGpuProducedPageCount;

        internal int LastGpuDispatchCount => m_LastGpuDispatchCount;

        internal int GpuStagingTextureCount
        {
            get
            {
                int count = 0;
                foreach (UploadPool pool in m_Pools.Values)
                    count += pool.GpuStagingTextureCount;

                return count;
            }
        }

        internal int CpuStagingTextureCount
        {
            get
            {
                int count = 0;
                foreach (UploadPool pool in m_Pools.Values)
                    count += pool.CpuStagingTextureCount;

                return count;
            }
        }

        internal int ScratchPixelCount => m_ScratchPixels.Length;

        internal static void SetFenceFactoryForTesting(IVTUploadFenceFactory fenceFactory)
        {
            s_FenceFactory = fenceFactory ?? GraphicsFenceFactory.Instance;
        }

        internal static void ResetFenceFactory()
        {
            s_FenceFactory = GraphicsFenceFactory.Instance;
        }

        internal void BeginFrame()
        {
            ResetLastScheduleStats();
            m_ReservedUploadCountThisFrame = 0;
            m_ReservedUploadBytesThisFrame = 0;
            if (m_QueuedUploads.Count > 0)
                DisposeQueuedUploads();

            m_QueuedCountsByKey.Clear();
        }

        internal bool CommitCompletedUploads(
            IVTUploadRequestCommitterResolver committerResolver,
            int frameIndex)
        {
            bool committedAny = false;
            foreach (UploadPool pool in m_Pools.Values)
                committedAny |= pool.CommitCompletedUploads(committerResolver, frameIndex);

            return committedAny;
        }

        internal int GetAvailableBatchCapacity(string spaceName, in VirtualTextureSpaceDesc desc)
        {
            UploadPoolKey key = new(desc);
            UploadPool pool = GetOrCreatePool(spaceName, key, desc.MaxUploadsPerFrame);
            m_QueuedCountsByKey.TryGetValue(key, out int queuedCount);
            return Mathf.Max(0, pool.AvailableBatchCapacity - queuedCount);
        }

        internal void ResetLastScheduleStats()
        {
            m_LastDuplicateUploadCount = 0;
            m_LastSkippedUploadCount = 0;
            m_LastCpuProducedPageCount = 0;
            m_LastGpuProducedPageCount = 0;
            m_LastGpuDispatchCount = 0;
        }

        internal void AddSkippedUploadCount(int skippedUploadCount)
        {
            m_LastSkippedUploadCount += Mathf.Max(0, skippedUploadCount);
        }

        internal int FilterInFlightRequests(
            IReadOnlyList<VTRequest> pendingRequests,
            List<VTRequest> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            output.Clear();
            if (pendingRequests == null)
                return 0;

            int duplicateCount = 0;
            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
            {
                VTRequest request = pendingRequests[requestIndex];
                if (IsRequestInFlight(request))
                {
                    duplicateCount += 1;
                    continue;
                }

                output.Add(request);
            }

            m_LastDuplicateUploadCount += duplicateCount;
            return duplicateCount;
        }

        internal int CountInFlightRequests(IReadOnlyList<VTRequest> pendingRequests)
        {
            if (pendingRequests == null)
                return 0;

            int duplicateCount = 0;
            for (int requestIndex = 0; requestIndex < pendingRequests.Count; requestIndex++)
            {
                if (IsRequestInFlight(pendingRequests[requestIndex]))
                    duplicateCount += 1;
            }

            m_LastDuplicateUploadCount += duplicateCount;
            return duplicateCount;
        }

        internal bool HasInFlightUploadForSpace(int spaceId)
        {
            foreach (UploadPool pool in m_Pools.Values)
            {
                if (pool.HasInFlightRequestForSpace(spaceId))
                    return true;
            }

            return false;
        }

        internal bool IsRequestInFlight(in VTRequest request)
        {
            foreach (UploadPool pool in m_Pools.Values)
            {
                if (pool.HasInFlightRequest(request))
                    return true;
            }

            return false;
        }

        internal void CancelUploadsForSpace(int spaceId)
        {
            for (int uploadIndex = m_QueuedUploads.Count - 1; uploadIndex >= 0; uploadIndex--)
            {
                QueuedUpload upload = m_QueuedUploads[uploadIndex];
                if (upload.Payload.Request.SpaceId != spaceId)
                    continue;

                upload.Payload.Finalizer?.Dispose();
                ReleaseUploadReservation(upload.Key);
                m_QueuedUploads.RemoveAt(uploadIndex);
            }

            foreach (UploadPool pool in m_Pools.Values)
                pool.CancelRequestsForSpace(spaceId);
        }

        internal void CancelUploadsForRegion(int spaceId, int mip, RectInt pageRegion)
        {
            for (int uploadIndex = m_QueuedUploads.Count - 1; uploadIndex >= 0; uploadIndex--)
            {
                QueuedUpload upload = m_QueuedUploads[uploadIndex];
                VTRequest request = upload.Payload.Request;
                if (request.SpaceId != spaceId
                    || request.PageCoord.Mip != mip
                    || !pageRegion.Contains(new Vector2Int(request.PageCoord.X, request.PageCoord.Y)))
                {
                    continue;
                }

                upload.Payload.Finalizer?.Dispose();
                ReleaseUploadReservation(upload.Key);
                m_QueuedUploads.RemoveAt(uploadIndex);
            }

            foreach (UploadPool pool in m_Pools.Values)
                pool.CancelRequestsForRegion(spaceId, mip, pageRegion);
        }

        internal bool TryReserveUpload(string spaceName, in VirtualTextureSpaceDesc desc)
        {
            if (m_ReservedUploadCountThisFrame >= m_MaxUploadsPerFrame)
                return false;

            int uploadByteSize = ComputeUploadByteSize(desc);
            if (m_ReservedUploadBytesThisFrame > m_MaxUploadBytesPerFrame - uploadByteSize)
                return false;

            if (GetAvailableBatchCapacity(spaceName, desc) <= 0)
                return false;

            UploadPoolKey key = new(desc);
            m_QueuedCountsByKey.TryGetValue(key, out int queuedCount);
            m_QueuedCountsByKey[key] = queuedCount + 1;
            m_ReservedUploadCountThisFrame += 1;
            m_ReservedUploadBytesThisFrame += uploadByteSize;
            return true;
        }

        internal void ReleaseUploadReservation(in VirtualTextureSpaceDesc desc)
        {
            ReleaseUploadReservation(new UploadPoolKey(desc));
        }

        private void ReleaseUploadReservation(in UploadPoolKey key)
        {
            if (m_QueuedCountsByKey.TryGetValue(key, out int queuedCount))
            {
                if (queuedCount <= 1)
                    m_QueuedCountsByKey.Remove(key);
                else
                    m_QueuedCountsByKey[key] = queuedCount - 1;
            }

            int uploadByteSize = ComputeUploadByteSize(key);
            m_ReservedUploadCountThisFrame = Mathf.Max(0, m_ReservedUploadCountThisFrame - 1);
            m_ReservedUploadBytesThisFrame = Mathf.Max(0, m_ReservedUploadBytesThisFrame - uploadByteSize);
        }

        internal void EnqueueReservedUpload(
            string spaceName,
            in VirtualTextureSpaceDesc desc,
            VTPhysicalPool physicalPool,
            in VTPageUploadPayload payload)
        {
            UploadPoolKey key = new(desc);
            GetOrCreatePool(spaceName, key, desc.MaxUploadsPerFrame);
            m_QueuedUploads.Add(new QueuedUpload(key, physicalPool, payload));
        }

        internal bool FinalizeUploads(CommandBuffer cmd)
        {
            if (m_QueuedUploads.Count == 0)
                return false;

            if (cmd == null)
            {
                AddSkippedUploadCount(m_QueuedUploads.Count);
                DisposeQueuedUploads();
                return false;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizePrepareMarker.Auto())
            {
                if (HasQueuedCpuUpload())
                    EnsureScratchPixels(GetMaxQueuedPhysicalPageSize());
            }
            bool scheduledAny = false;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeSortMarker.Auto())
                m_QueuedUploads.Sort(QueuedUploadComparer.Instance);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemVirtualTextureUploadsFinalizeScheduleMarker.Auto())
            {
                int startIndex = 0;
                while (startIndex < m_QueuedUploads.Count)
                {
                    UploadPoolKey key = m_QueuedUploads[startIndex].Key;
                    int count = 1;
                    while (startIndex + count < m_QueuedUploads.Count
                           && m_QueuedUploads[startIndex + count].Key.Equals(key))
                    {
                        count += 1;
                    }

                    if (m_Pools.TryGetValue(key, out UploadPool pool))
                    {
                        scheduledAny |= pool.ScheduleUploads(
                            m_QueuedUploads,
                            startIndex,
                            count,
                            m_ScratchPixels,
                            cmd,
                            s_FenceFactory,
                            ref m_LastSkippedUploadCount,
                            ref m_LastCpuProducedPageCount,
                            ref m_LastGpuProducedPageCount,
                            ref m_LastGpuDispatchCount);
                    }
                    else
                    {
                        m_LastSkippedUploadCount += count;
                        DisposePayloads(m_QueuedUploads, startIndex, count);
                    }

                    startIndex += count;
                }
            }

            m_QueuedUploads.Clear();
            return scheduledAny;
        }

        public void Dispose()
        {
            DisposeQueuedUploads();
            foreach (UploadPool pool in m_Pools.Values)
                pool.Dispose();

            m_Pools.Clear();
            m_QueuedCountsByKey.Clear();
        }

        private UploadPool GetOrCreatePool(string spaceName, in UploadPoolKey key, int batchCapacity)
        {
            if (m_Pools.TryGetValue(key, out UploadPool pool))
            {
                pool.EnsureBatchCapacity(batchCapacity);
                return pool;
            }

            pool = new UploadPool(spaceName, key, batchCapacity);
            m_Pools.Add(key, pool);
            return pool;
        }

        private void EnsureScratchPixels(int physicalPageSize)
        {
            int pixelCount = Mathf.Max(1, physicalPageSize) * Mathf.Max(1, physicalPageSize);
            if (m_ScratchPixels.Length < pixelCount)
                m_ScratchPixels = new Color32[pixelCount];
        }

        private int GetMaxQueuedPhysicalPageSize()
        {
            int physicalPageSize = 1;
            for (int uploadIndex = 0; uploadIndex < m_QueuedUploads.Count; uploadIndex++)
                physicalPageSize = Mathf.Max(physicalPageSize, m_QueuedUploads[uploadIndex].Key.PhysicalPageSize);

            return physicalPageSize;
        }

        private bool HasQueuedCpuUpload()
        {
            for (int uploadIndex = 0; uploadIndex < m_QueuedUploads.Count; uploadIndex++)
            {
                if (m_QueuedUploads[uploadIndex].Payload.Finalizer is IVTPageFinalizer)
                    return true;
            }

            return false;
        }

        private void DisposeQueuedUploads()
        {
            DisposePayloads(m_QueuedUploads, 0, m_QueuedUploads.Count);
            m_QueuedUploads.Clear();
        }

        private static int ComputeUploadByteSize(in VirtualTextureSpaceDesc desc)
        {
            return ComputeUploadByteSize(new UploadPoolKey(desc));
        }

        private static int ComputeUploadByteSize(in UploadPoolKey key)
        {
            int physicalPageSize = Mathf.Max(1, key.PhysicalPageSize);
            long byteSize = 0;
            for (int physicalGroup = 0; physicalGroup < key.PhysicalGroupCount; physicalGroup++)
            {
                GraphicsFormat storageFormat = key.GetGroupStorageFormat(physicalGroup);
                long blockWidth = Math.Max(1u, GraphicsFormatUtility.GetBlockWidth(storageFormat));
                long blockHeight = Math.Max(1u, GraphicsFormatUtility.GetBlockHeight(storageFormat));
                long blockSize = Math.Max(1u, GraphicsFormatUtility.GetBlockSize(storageFormat));
                long blocksX = (physicalPageSize + blockWidth - 1) / blockWidth;
                long blocksY = (physicalPageSize + blockHeight - 1) / blockHeight;
                byteSize += blocksX
                            * blocksY
                            * blockSize
                            * Mathf.Max(1, key.GetGroupLayerCount(physicalGroup));
            }

            return byteSize >= int.MaxValue ? int.MaxValue : (int)byteSize;
        }

        private static void DisposePayloads(IReadOnlyList<QueuedUpload> uploads, int startIndex, int count)
        {
            if (uploads == null)
                return;

            int endIndex = Mathf.Min(uploads.Count, startIndex + count);
            for (int uploadIndex = startIndex; uploadIndex < endIndex; uploadIndex++)
                uploads[uploadIndex].Payload.Finalizer?.Dispose();
        }

        private sealed class QueuedUploadComparer : IComparer<QueuedUpload>
        {
            internal static readonly QueuedUploadComparer Instance = new();

            private QueuedUploadComparer()
            {
            }

            public int Compare(QueuedUpload left, QueuedUpload right)
            {
                int sizeCompare = left.Key.PhysicalPageSize.CompareTo(right.Key.PhysicalPageSize);
                if (sizeCompare != 0)
                    return sizeCompare;

                int formatCompare = left.Key.GraphicsFormat.CompareTo(right.Key.GraphicsFormat);
                if (formatCompare != 0)
                    return formatCompare;

                int layerCountCompare = left.Key.LayerCount.CompareTo(right.Key.LayerCount);
                if (layerCountCompare != 0)
                    return layerCountCompare;

                int layoutCompare = string.Compare(
                    left.Key.LayoutKey,
                    right.Key.LayoutKey,
                    StringComparison.Ordinal);
                if (layoutCompare != 0)
                    return layoutCompare;

                return left.Payload.Request.SpaceId.CompareTo(right.Payload.Request.SpaceId);
            }
        }
    }
}
