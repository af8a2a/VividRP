using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static unsafe class VividPerObjectBufferSystem
    {
        internal const uint ShaderUserValueMagic = 0xa0000000u;
        internal const uint ShaderUserValueMagicMask = 0xf0000000u;
        internal const uint ShaderUserValueAddressMask = 0x0fffffffu;

        private const int StagingBufferCount = 3;
        private const int RawBufferStride = sizeof(uint);
        private const int MaxThreadGroupsPerDispatch = 65535;
        private const string UploadKernelName = "CopyPerObjectBufferRanges";

        private static readonly int s_PerObjectBufferId = Shader.PropertyToID("_VividPerObjectBuffer");
        private static readonly int s_UploadSourceId = Shader.PropertyToID("_VividPerObjectUploadSource");
        private static readonly int s_UploadDestinationId = Shader.PropertyToID("_VividPerObjectUploadDestination");
        private static readonly int s_UploadOperationsId = Shader.PropertyToID("_VividPerObjectUploadOperations");
        private static readonly int s_UploadOperationCountId = Shader.PropertyToID("_VividPerObjectUploadOperationCount");
        private static readonly int s_UploadOperationBaseId = Shader.PropertyToID("_VividPerObjectUploadOperationBase");

        private static readonly ProfilerMarker s_PrepareMarker = new("VividRP.PerObjectBuffer.PrepareAndBind");
        private static readonly ProfilerMarker s_UploadMarker = new("VividRP.PerObjectBuffer.Upload");
        private static readonly Dictionary<EntityId, Binding> s_Bindings = new();
        private static readonly List<EntityId> s_DestroyedRendererIds = new();
        private static readonly List<DirtyRange> s_DirtyRanges = new();
        private static readonly List<DirtyRange> s_CoalescedRanges = new();
        private static readonly GraphicsBuffer[] s_StagingBuffers = new GraphicsBuffer[StagingBufferCount];

        private static VividPerObjectRecordAllocator s_Allocator;
        private static GraphicsBuffer s_RenderBuffer;
        private static GraphicsBuffer s_UploadOperationBuffer;
        private static UploadOperation[] s_UploadOperations = Array.Empty<UploadOperation>();
        private static int[] s_FallbackUploadData = Array.Empty<int>();
        private static ComputeShader s_UploadShader;
        private static int s_UploadKernel = -1;
        private static int s_StagingBufferIndex = -1;
        private static int s_MainThreadId;
        private static uint s_NextGeneration = 1u;
        private static bool s_FullUploadRequired = true;
        private static int s_LastPreparedFrame = -1;
        private static int s_LastUploadBytes;
        private static int s_LastUploadRangeCount;
#if UNITY_INCLUDE_TESTS
        private static bool s_ForceFallbackForTests;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            DisposeAllCore(restoreRendererValues: true);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorReloadCleanup()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DisposeBeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeBeforeAssemblyReload;
        }

        private static void DisposeBeforeAssemblyReload()
        {
            DisposeAllCore(restoreRendererValues: true);
        }
#endif

        internal static VividPerObjectBlock Bind(Renderer renderer, VividPerObjectLayout layout)
        {
            EnsureMainThread();
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            {
                throw new NotSupportedException(
                    $"{renderer.GetType().Name} does not expose Renderer ShaderUserValue. " +
                    "Only MeshRenderer and SkinnedMeshRenderer are supported.");
            }

            layout.Validate();
            EnsureAllocator();
            EntityId rendererId = renderer.GetEntityId();
            if (s_Bindings.TryGetValue(rendererId, out Binding existing))
            {
                if (!ReferenceEquals(existing.Renderer, renderer))
                {
                    ReleaseBinding(existing, restoreRendererValue: false);
                    s_Bindings.Remove(rendererId);
                }
                else if (existing.Layout.IsEquivalentTo(layout)
                    && existing.LayoutSignature == layout.Signature)
                {
                    return new VividPerObjectBlock(rendererId, existing.Generation);
                }
                else
                {
                    return Rebind(existing, layout);
                }
            }

            if ((Application.isEditor || Debug.isDebugBuild) && renderer.HasPropertyBlock())
            {
                Debug.LogWarning(
                    $"[VividRP] Renderer '{renderer.name}' already has a MaterialPropertyBlock. " +
                    "Per-object buffer binding will not clear it, so SRP Batcher compatibility is not guaranteed.",
                    renderer);
            }

            int address = AllocateAndInitialize(layout);
            uint originalValue = GetRendererShaderUserValue(renderer);
            uint assignedValue = EncodeAddress(address);
            SetRendererShaderUserValue(renderer, assignedValue);
            var binding = new Binding(
                renderer,
                layout,
                layout.Signature,
                address,
                layout.RecordStride,
                originalValue,
                assignedValue,
                NextGeneration());
            s_Bindings.Add(rendererId, binding);
            return new VividPerObjectBlock(rendererId, binding.Generation);
        }

        internal static void Unbind(Renderer renderer)
        {
            EnsureMainThread();
            if (renderer == null)
                return;

            EntityId rendererId = renderer.GetEntityId();
            if (!s_Bindings.TryGetValue(rendererId, out Binding binding)
                || !ReferenceEquals(binding.Renderer, renderer))
            {
                return;
            }

            ReleaseBinding(binding, restoreRendererValue: true);
            s_Bindings.Remove(rendererId);
        }

        internal static bool IsBound(Renderer renderer)
        {
            EnsureMainThread();
            if (renderer == null)
                return false;

            return s_Bindings.TryGetValue(renderer.GetEntityId(), out Binding binding)
                && ReferenceEquals(binding.Renderer, renderer);
        }

        internal static bool IsBlockValid(VividPerObjectBlock block)
        {
            EnsureMainThread();
            return TryGetBinding(block, out _);
        }

        internal static VividPerObjectBufferStats GetStats()
        {
            EnsureMainThread();
            int capacity = s_Allocator?.Capacity ?? VividPerObjectRecordAllocator.ReservedBytes;
            int used = s_Allocator?.UsedBytes ?? VividPerObjectRecordAllocator.ReservedBytes;
            int largestFreeBlock = s_Allocator?.LargestFreeBlock ?? 0;
            return new VividPerObjectBufferStats(
                s_Bindings.Count,
                used,
                capacity,
                largestFreeBlock,
                s_DirtyRanges.Count,
                s_LastUploadBytes,
                s_LastUploadRangeCount);
        }

        internal static void SetValue<T>(
            VividPerObjectBlock block,
            int propertyNameId,
            VividPerObjectPropertyType expectedType,
            T value)
            where T : unmanaged
        {
            Binding binding = GetBinding(block);
            VividPerObjectPropertyHandle property = binding.Layout.GetProperty(propertyNameId);
            SetValue(block, property, expectedType, value);
        }

        internal static void SetValue<T>(
            VividPerObjectBlock block,
            VividPerObjectPropertyHandle property,
            VividPerObjectPropertyType expectedType,
            T value)
            where T : unmanaged
        {
            Binding binding = GetBinding(block);
            ValidateProperty(binding, property, expectedType, sizeof(T));
            int destinationOffset = binding.Address + property.Offset;
            byte[] data = s_Allocator.Data;
            fixed (byte* destination = &data[destinationOffset])
            {
                T* source = &value;
                if (UnsafeUtility.MemCmp(destination, source, sizeof(T)) == 0)
                    return;
                UnsafeUtility.MemCpy(destination, source, sizeof(T));
            }

            MarkDirty(destinationOffset, sizeof(T));
        }

        internal static void PrepareAndBind(CommandBuffer cmd)
        {
            EnsureMainThread();
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));

            using var prepareScope = s_PrepareMarker.Auto();
            ResetFrameUploadStatsIfNeeded();
            SweepDestroyedRenderers();
            EnsureRenderBuffer();

            if (s_FullUploadRequired)
            {
                s_DirtyRanges.Clear();
                int byteCount = s_Allocator?.Capacity ?? VividPerObjectRecordAllocator.ReservedBytes;
                MarkDirty(0, byteCount);
                s_FullUploadRequired = false;
            }

            if (s_DirtyRanges.Count > 0)
            {
                using var uploadScope = s_UploadMarker.Auto();
                CoalesceDirtyRanges();
                if (!TryUploadWithCompute(cmd))
                    UploadWithCommandBuffer(cmd);

                for (int i = 0; i < s_CoalescedRanges.Count; i++)
                {
                    s_LastUploadBytes += s_CoalescedRanges[i].Length;
                    s_LastUploadRangeCount++;
                }

                s_DirtyRanges.Clear();
                s_CoalescedRanges.Clear();
            }

            cmd.SetGlobalBuffer(s_PerObjectBufferId, s_RenderBuffer);
        }

        internal static void DisposeAll()
        {
            EnsureMainThread();
            DisposeAllCore(restoreRendererValues: true);
        }

#if UNITY_INCLUDE_TESTS
        internal static int GetRecordAddressForTests(VividPerObjectBlock block)
        {
            return GetBinding(block).Address;
        }

        internal static byte[] GetDataForTests()
        {
            EnsureMainThread();
            return s_Allocator?.Data;
        }

        internal static void ClearDirtyRangesForTests()
        {
            EnsureMainThread();
            s_DirtyRanges.Clear();
            s_CoalescedRanges.Clear();
        }

        internal static void SweepDestroyedRenderersForTests()
        {
            EnsureMainThread();
            SweepDestroyedRenderers();
        }

        internal static void SetForceFallbackForTests(bool forceFallback)
        {
            EnsureMainThread();
            s_ForceFallbackForTests = forceFallback;
        }
#endif

        private static VividPerObjectBlock Rebind(Binding binding, VividPerObjectLayout layout)
        {
            int address = AllocateAndInitialize(layout);
            uint assignedValue = EncodeAddress(address);
            SetRendererShaderUserValue(binding.Renderer, assignedValue);

            int previousAddress = binding.Address;
            int previousSize = binding.RecordSize;
            binding.Layout = layout;
            binding.LayoutSignature = layout.Signature;
            binding.Address = address;
            binding.RecordSize = layout.RecordStride;
            binding.AssignedValue = assignedValue;
            binding.Generation = NextGeneration();
            s_Allocator.Free(previousAddress, previousSize);
            MarkDirty(previousAddress, previousSize);
            return new VividPerObjectBlock(binding.Renderer.GetEntityId(), binding.Generation);
        }

        private static int AllocateAndInitialize(VividPerObjectLayout layout)
        {
            int address = s_Allocator.Allocate(layout.RecordStride, out bool capacityChanged);
            if (capacityChanged)
                s_FullUploadRequired = true;
            layout.InitializeRecord(s_Allocator.Data, address);
            MarkDirty(address, layout.RecordStride);
            return address;
        }

        private static void ReleaseBinding(Binding binding, bool restoreRendererValue)
        {
            if (restoreRendererValue && binding.Renderer != null)
                SetRendererShaderUserValue(binding.Renderer, binding.OriginalValue);

            if (s_Allocator != null)
            {
                s_Allocator.Free(binding.Address, binding.RecordSize);
                MarkDirty(binding.Address, binding.RecordSize);
            }
        }

        private static Binding GetBinding(VividPerObjectBlock block)
        {
            EnsureMainThread();
            if (!TryGetBinding(block, out Binding binding))
                throw new InvalidOperationException("The VividPerObjectBlock is stale or no longer bound.");
            if (binding.LayoutSignature != binding.Layout.Signature)
            {
                throw new InvalidOperationException(
                    $"Per-object layout '{binding.Layout.ShaderIdentifier}' changed after binding. Bind the Renderer again.");
            }
            return binding;
        }

        private static bool TryGetBinding(VividPerObjectBlock block, out Binding binding)
        {
            return s_Bindings.TryGetValue(block.RendererEntityId, out binding)
                && binding.Generation == block.Generation
                && binding.Renderer != null;
        }

        private static void ValidateProperty(
            Binding binding,
            VividPerObjectPropertyHandle property,
            VividPerObjectPropertyType expectedType,
            int valueSize)
        {
            if (!property.IsValid
                || !property.Layout.IsEquivalentTo(binding.Layout)
                || property.LayoutSignature != binding.LayoutSignature)
            {
                throw new ArgumentException("The property handle does not belong to the bound layout.", nameof(property));
            }
            if (property.Type != expectedType)
            {
                throw new ArgumentException(
                    $"Property type is {property.Type}, but {expectedType} was requested.",
                    nameof(property));
            }
            if (VividPerObjectLayout.GetPropertySize(expectedType) != valueSize)
                throw new InvalidOperationException("Per-object property storage size does not match the supplied value type.");
        }

        private static void EnsureAllocator()
        {
            if (s_Allocator != null)
                return;

            long graphicsBufferLimit = SystemInfo.maxGraphicsBufferSize;
            long addressLimit = (long)ShaderUserValueAddressMask * VividPerObjectLayout.RecordAlignment;
            long managedArrayLimit = int.MaxValue & ~15;
            long resolvedLimit = Math.Min(addressLimit, managedArrayLimit);
            if (graphicsBufferLimit > 0)
                resolvedLimit = Math.Min(resolvedLimit, graphicsBufferLimit);
            resolvedLimit &= ~15L;
            if (resolvedLimit < VividPerObjectRecordAllocator.DefaultInitialCapacity)
            {
                throw new NotSupportedException(
                    $"The active graphics device exposes only {resolvedLimit} bytes of graphics-buffer storage.");
            }

            s_Allocator = new VividPerObjectRecordAllocator(
                VividPerObjectRecordAllocator.DefaultInitialCapacity,
                (int)resolvedLimit);
            s_FullUploadRequired = true;
        }

        private static void EnsureRenderBuffer()
        {
            int requiredBytes = s_Allocator?.Capacity ?? VividPerObjectRecordAllocator.ReservedBytes;
            int requiredCount = requiredBytes / RawBufferStride;
            if (s_RenderBuffer != null
                && s_RenderBuffer.IsValid()
                && s_RenderBuffer.count == requiredCount
                && s_RenderBuffer.stride == RawBufferStride)
            {
                return;
            }

            s_RenderBuffer?.Dispose();
            s_RenderBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination,
                requiredCount,
                RawBufferStride)
            {
                name = "Vivid Per-Object Buffer",
            };
            s_FullUploadRequired = true;
        }

        private static bool TryUploadWithCompute(CommandBuffer cmd)
        {
#if UNITY_INCLUDE_TESTS
            if (s_ForceFallbackForTests)
                return false;
#endif
            ResolveUploadShader();
            if (s_UploadShader == null || s_UploadKernel < 0 || !SystemInfo.supportsComputeShaders)
                return false;

            int totalBytes = 0;
            for (int i = 0; i < s_CoalescedRanges.Count; i++)
                totalBytes += s_CoalescedRanges[i].Length;
            int totalDwords = totalBytes / sizeof(int);

            s_StagingBufferIndex = (s_StagingBufferIndex + 1) % StagingBufferCount;
            GraphicsBuffer stagingBuffer = EnsureStagingBuffer(s_StagingBufferIndex, totalDwords);
            EnsureUploadOperationCapacity(s_CoalescedRanges.Count);
            NativeArray<int> mappedData = stagingBuffer.LockBufferForWrite<int>(0, totalDwords);
            try
            {
                byte* destination = (byte*)mappedData.GetUnsafePtr();
                int sourceOffset = 0;
                byte[] data = s_Allocator?.Data ?? new byte[VividPerObjectRecordAllocator.ReservedBytes];
                fixed (byte* sourceBase = data)
                {
                    for (int i = 0; i < s_CoalescedRanges.Count; i++)
                    {
                        DirtyRange range = s_CoalescedRanges[i];
                        UnsafeUtility.MemCpy(destination + sourceOffset, sourceBase + range.Start, range.Length);
                        s_UploadOperations[i] = new UploadOperation(
                            (uint)sourceOffset,
                            (uint)range.Start,
                            (uint)range.Length);
                        sourceOffset += range.Length;
                    }
                }
            }
            finally
            {
                stagingBuffer.UnlockBufferAfterWrite<int>(totalDwords);
            }

            EnsureUploadOperationBuffer(s_CoalescedRanges.Count);
            cmd.SetBufferData(s_UploadOperationBuffer, s_UploadOperations, 0, 0, s_CoalescedRanges.Count);
            cmd.SetComputeBufferParam(s_UploadShader, s_UploadKernel, s_UploadSourceId, stagingBuffer);
            cmd.SetComputeBufferParam(s_UploadShader, s_UploadKernel, s_UploadDestinationId, s_RenderBuffer);
            cmd.SetComputeBufferParam(s_UploadShader, s_UploadKernel, s_UploadOperationsId, s_UploadOperationBuffer);
            cmd.SetComputeIntParam(s_UploadShader, s_UploadOperationCountId, s_CoalescedRanges.Count);

            int operationBase = 0;
            while (operationBase < s_CoalescedRanges.Count)
            {
                int dispatchCount = Math.Min(MaxThreadGroupsPerDispatch, s_CoalescedRanges.Count - operationBase);
                cmd.SetComputeIntParam(s_UploadShader, s_UploadOperationBaseId, operationBase);
                cmd.DispatchCompute(s_UploadShader, s_UploadKernel, dispatchCount, 1, 1);
                operationBase += dispatchCount;
            }

            return true;
        }

        private static void UploadWithCommandBuffer(CommandBuffer cmd)
        {
            byte[] data = s_Allocator?.Data ?? new byte[VividPerObjectRecordAllocator.ReservedBytes];
            for (int i = 0; i < s_CoalescedRanges.Count; i++)
            {
                DirtyRange range = s_CoalescedRanges[i];
                int dwordCount = range.Length / sizeof(int);
                if (s_FallbackUploadData.Length < dwordCount)
                    s_FallbackUploadData = new int[dwordCount];
                Buffer.BlockCopy(data, range.Start, s_FallbackUploadData, 0, range.Length);
                cmd.SetBufferData(s_RenderBuffer, s_FallbackUploadData, 0, range.Start / sizeof(int), dwordCount);
            }
        }

        private static void ResolveUploadShader()
        {
            ComputeShader resolvedShader = PipelineResourceManager.Get<VividRPCoreResources>()?.PerObjectBufferUploadCompute;
            if (ReferenceEquals(resolvedShader, s_UploadShader))
                return;

            s_UploadShader = resolvedShader;
            s_UploadKernel = -1;
            if (s_UploadShader == null)
                return;

            try
            {
                s_UploadKernel = s_UploadShader.FindKernel(UploadKernelName);
            }
            catch (ArgumentException)
            {
                s_UploadShader = null;
                s_UploadKernel = -1;
            }
        }

        private static GraphicsBuffer EnsureStagingBuffer(int index, int requiredDwords)
        {
            int requiredCount = NextPowerOfTwo(Math.Max(1, requiredDwords));
            GraphicsBuffer buffer = s_StagingBuffers[index];
            if (buffer != null && buffer.IsValid() && buffer.count >= requiredCount)
                return buffer;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource,
                GraphicsBuffer.UsageFlags.LockBufferForWrite,
                requiredCount,
                RawBufferStride)
            {
                name = $"Vivid Per-Object Upload {index}",
            };
            s_StagingBuffers[index] = buffer;
            return buffer;
        }

        private static void EnsureUploadOperationBuffer(int requiredCount)
        {
            int capacity = NextPowerOfTwo(Math.Max(1, requiredCount));
            if (s_UploadOperationBuffer != null
                && s_UploadOperationBuffer.IsValid()
                && s_UploadOperationBuffer.count >= capacity)
            {
                return;
            }

            s_UploadOperationBuffer?.Dispose();
            s_UploadOperationBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                Marshal.SizeOf<UploadOperation>())
            {
                name = "Vivid Per-Object Upload Operations",
            };
        }

        private static void EnsureUploadOperationCapacity(int requiredCount)
        {
            if (s_UploadOperations.Length >= requiredCount)
                return;
            s_UploadOperations = new UploadOperation[NextPowerOfTwo(requiredCount)];
        }

        private static void CoalesceDirtyRanges()
        {
            s_DirtyRanges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            s_CoalescedRanges.Clear();
            DirtyRange current = s_DirtyRanges[0];
            for (int i = 1; i < s_DirtyRanges.Count; i++)
            {
                DirtyRange next = s_DirtyRanges[i];
                if (next.Start <= current.End)
                {
                    current = new DirtyRange(current.Start, Math.Max(current.End, next.End) - current.Start);
                    continue;
                }

                s_CoalescedRanges.Add(current);
                current = next;
            }
            s_CoalescedRanges.Add(current);
        }

        private static void MarkDirty(int start, int length)
        {
            if (length <= 0)
                return;
            if ((start & 3) != 0 || (length & 3) != 0)
                throw new InvalidOperationException("Per-object dirty ranges must be aligned to four bytes.");
            s_DirtyRanges.Add(new DirtyRange(start, length));
        }

        private static void SweepDestroyedRenderers()
        {
            s_DestroyedRendererIds.Clear();
            foreach (KeyValuePair<EntityId, Binding> pair in s_Bindings)
            {
                if (pair.Value.Renderer == null)
                    s_DestroyedRendererIds.Add(pair.Key);
            }

            for (int i = 0; i < s_DestroyedRendererIds.Count; i++)
            {
                EntityId rendererId = s_DestroyedRendererIds[i];
                Binding binding = s_Bindings[rendererId];
                ReleaseBinding(binding, restoreRendererValue: false);
                s_Bindings.Remove(rendererId);
            }
        }

        private static void ResetFrameUploadStatsIfNeeded()
        {
            int frame = Time.frameCount;
            if (s_LastPreparedFrame == frame)
                return;
            s_LastPreparedFrame = frame;
            s_LastUploadBytes = 0;
            s_LastUploadRangeCount = 0;
        }

        private static void DisposeAllCore(bool restoreRendererValues)
        {
            if (restoreRendererValues)
            {
                foreach (Binding binding in s_Bindings.Values)
                {
                    if (binding.Renderer != null)
                        SetRendererShaderUserValue(binding.Renderer, binding.OriginalValue);
                }
            }

            s_Bindings.Clear();
            s_DestroyedRendererIds.Clear();
            s_DirtyRanges.Clear();
            s_CoalescedRanges.Clear();
            s_Allocator = null;
            s_RenderBuffer?.Dispose();
            s_RenderBuffer = null;
            for (int i = 0; i < s_StagingBuffers.Length; i++)
            {
                s_StagingBuffers[i]?.Dispose();
                s_StagingBuffers[i] = null;
            }
            s_UploadOperationBuffer?.Dispose();
            s_UploadOperationBuffer = null;
            s_UploadOperations = Array.Empty<UploadOperation>();
            s_FallbackUploadData = Array.Empty<int>();
            s_UploadShader = null;
            s_UploadKernel = -1;
            s_StagingBufferIndex = -1;
            s_FullUploadRequired = true;
            s_LastPreparedFrame = -1;
            s_LastUploadBytes = 0;
            s_LastUploadRangeCount = 0;
#if UNITY_INCLUDE_TESTS
            s_ForceFallbackForTests = false;
#endif
        }

        private static uint EncodeAddress(int byteAddress)
        {
            if (byteAddress < VividPerObjectRecordAllocator.ReservedBytes
                || (byteAddress & (VividPerObjectLayout.RecordAlignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteAddress));
            }

            uint encodedAddress = (uint)(byteAddress / VividPerObjectLayout.RecordAlignment);
            if (encodedAddress > ShaderUserValueAddressMask)
                throw new InvalidOperationException("Per-object buffer address exceeds the ShaderUserValue encoding range.");
            return ShaderUserValueMagic | encodedAddress;
        }

        private static uint GetRendererShaderUserValue(Renderer renderer)
        {
            return renderer switch
            {
                MeshRenderer meshRenderer => meshRenderer.GetShaderUserValue(),
                SkinnedMeshRenderer skinnedMeshRenderer => skinnedMeshRenderer.GetShaderUserValue(),
                _ => throw new NotSupportedException(renderer.GetType().Name),
            };
        }

        private static void SetRendererShaderUserValue(Renderer renderer, uint value)
        {
            switch (renderer)
            {
                case MeshRenderer meshRenderer:
                    meshRenderer.SetShaderUserValue(value);
                    break;
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    skinnedMeshRenderer.SetShaderUserValue(value);
                    break;
                default:
                    throw new NotSupportedException(renderer.GetType().Name);
            }
        }

        private static uint NextGeneration()
        {
            uint generation = s_NextGeneration++;
            if (generation == 0u)
                generation = s_NextGeneration++;
            return generation;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;
            if (value >= 1 << 30)
                return int.MaxValue;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        private static void EnsureMainThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (s_MainThreadId == 0)
                s_MainThreadId = currentThreadId;
            if (currentThreadId != s_MainThreadId)
                throw new InvalidOperationException("VividPerObjectBuffer APIs may only be called from the Unity main thread.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct UploadOperation
        {
            internal UploadOperation(uint sourceOffset, uint destinationOffset, uint size)
            {
                SourceOffset = sourceOffset;
                DestinationOffset = destinationOffset;
                Size = size;
            }

            internal readonly uint SourceOffset;
            internal readonly uint DestinationOffset;
            internal readonly uint Size;
        }

        private readonly struct DirtyRange
        {
            internal DirtyRange(int start, int length)
            {
                Start = start;
                Length = length;
            }

            internal int Start { get; }

            internal int Length { get; }

            internal int End => Start + Length;
        }

        private sealed class Binding
        {
            internal Binding(
                Renderer renderer,
                VividPerObjectLayout layout,
                uint layoutSignature,
                int address,
                int recordSize,
                uint originalValue,
                uint assignedValue,
                uint generation)
            {
                Renderer = renderer;
                Layout = layout;
                LayoutSignature = layoutSignature;
                Address = address;
                RecordSize = recordSize;
                OriginalValue = originalValue;
                AssignedValue = assignedValue;
                Generation = generation;
            }

            internal Renderer Renderer;
            internal VividPerObjectLayout Layout;
            internal uint LayoutSignature;
            internal int Address;
            internal int RecordSize;
            internal uint OriginalValue;
            internal uint AssignedValue;
            internal uint Generation;
        }
    }
}
