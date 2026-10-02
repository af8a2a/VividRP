using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Independent 48-byte ABI; the generic primitive/instance layouts stay unchanged.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VirtualShadowMapInvalidationSource
    {
        internal const uint Active = 1, Static = 2, Deformable = 4, Unbounded = 8;
        internal float4 BoundsMin, BoundsMax;
        // Change revision, slot generation, cache flags, camera layer mask.
        internal uint4 State;
    }

    // Persistent GPU instance history, owned by the single directional cache/view.
    // Sources upload only dirty ranges. Removed slots remain tombstones until the
    // GPU consumes them; reuse is distinguished by generation as well as revision.
    internal static class VirtualShadowMapCacheInvalidation
    {
        internal static GraphicsBuffer Sources { get; private set; }
        internal static GraphicsBuffer States { get; private set; }
        internal static GraphicsBuffer Queue { get; private set; }
        internal static GraphicsBuffer Args { get; private set; }
        internal static GraphicsBuffer DispatchArgs { get; private set; }
        internal static bool NeedsFullRefresh { get; private set; }
        private static readonly List<VividPrimitiveDirtyRange> s_Ranges = new();
        private static VividPrimitiveScene s_Scene;
        private static uint s_Epoch, s_PreparedEpoch, s_CameraMask;
        private static bool s_Reset = true, s_UseHZB, s_Deformable, s_RecordPending;
        private static int s_Count;
        private static ComputeShader s_Shader;
        private static int s_ResetKernel, s_UpdateKernel, s_PrepareKernel, s_ProcessKernel;
        private static readonly int SourcesId = Shader.PropertyToID("_VSMInvalidationSources");
        private static readonly int StatesId = Shader.PropertyToID("_VSMInvalidationStates");
        private static readonly int QueueId = Shader.PropertyToID("_VSMInvalidationQueue");
        private static readonly int ArgsId = Shader.PropertyToID("_VSMInvalidationArgs");
        private static readonly int DispatchArgsId = Shader.PropertyToID("_VSMInvalidationDispatchArgs");
        private static readonly int CountId = Shader.PropertyToID("_VSMInvalidationSourceCount");
        private static readonly int ResetId = Shader.PropertyToID("_VSMInvalidationReset");
        private static readonly int CameraMaskId = Shader.PropertyToID("_VSMInvalidationCameraMask");
        private static readonly int DeformableId = Shader.PropertyToID("_VSMDeformableMeshesInvalidate");
        private static readonly int HZBId = Shader.PropertyToID("_VSMInvalidateUseHZB");
        private static readonly int MetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly ProfilingSampler StateSampler = new("VSM.Cache.UpdateInstanceState");
        private static readonly ProfilingSampler QueueSampler = new("VSM.Cache.ProcessInvalidationQueue");

        internal static void Prepare(VividPrimitiveScene scene, ComputeShader shader, uint cameraMask,
            bool useHZB, bool deformable)
        {
            // A thrown/aborted render graph may never reach CompleteFrame.
            if (s_RecordPending) AbortFrame();
            if (s_Shader != shader)
            {
                s_Shader = shader;
                s_ResetKernel = shader.FindKernel("VSMResetInvalidationQueue");
                s_UpdateKernel = shader.FindKernel("VSMUpdateInvalidationInstances");
                s_PrepareKernel = shader.FindKernel("VSMPrepareInvalidationQueue");
                s_ProcessKernel = shader.FindKernel("VSMProcessInvalidationQueue");
                s_Reset = true;
            }
            var table = scene.ShadowInvalidationTable;
            bool newScene = !ReferenceEquals(s_Scene, scene);
            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, table.Count));
            bool recreate = Sources == null || Sources.count < capacity;
            if (recreate)
            {
                ReleaseBuffers();
                Sources = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 48) { name = "VSMInvalidationSources" };
                States = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 48) { name = "VSMInvalidationStates" };
                // At most old + new footprint per slot, even during slot reuse.
                Queue = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2, 48) { name = "VSMInvalidationQueue" };
                // Dispatch xyz, item count, work cursor, tested/culled/written page visits.
                Args = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 8, 4)
                    { name = "VSMInvalidationArgs" };
                DispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 3, 4)
                    { name = "VSMInvalidationDispatchArgs" };
            }
            s_Reset |= recreate || newScene || s_CameraMask != cameraMask;
            NeedsFullRefresh = s_Reset || s_Epoch != scene.ShadowInvalidationEpoch;
            if (recreate || newScene)
            {
                if (table.Count > 0) Sources.SetData(table.Data, 0, 0, table.Count);
            }
            else
            {
                table.CollectDirtyRanges(s_Ranges);
                for (int i = 0; i < s_Ranges.Count; i++)
                {
                    var range = s_Ranges[i];
                    Sources.SetData(table.Data, range.Start, range.Start, range.Count);
                }
            }
            table.ClearDirtyPages();
            s_Scene = scene;
            s_PreparedEpoch = scene.ShadowInvalidationEpoch;
            s_CameraMask = cameraMask;
            s_Count = table.Count;
            s_UseHZB = useHZB;
            s_Deformable = deformable;
        }

        // Before projections.Upload/remap/allocation: pages, projection matrices
        // and HZB all refer to the previous layout. New mappings are already dirty.
        internal static void Record(CommandBuffer cmd, bool fullRefresh)
        {
            var shader = s_Shader;
            cmd.SetComputeIntParam(shader, CountId, s_Count);
            cmd.SetComputeIntParam(shader, ResetId, s_Reset ? 1 : 0);
            cmd.SetComputeIntParam(shader, CameraMaskId, unchecked((int)s_CameraMask));
            cmd.SetComputeIntParam(shader, DeformableId, s_Deformable ? 1 : 0);
            using (new ProfilingScope(cmd, StateSampler))
            {
                cmd.SetComputeBufferParam(shader, s_ResetKernel, ArgsId, Args);
                cmd.DispatchCompute(shader, s_ResetKernel, 1, 1, 1);
                if (s_Count > 0)
                {
                    BindQueue(cmd, s_UpdateKernel);
                    cmd.SetComputeBufferParam(shader, s_UpdateKernel, SourcesId, Sources);
                    cmd.SetComputeBufferParam(shader, s_UpdateKernel, StatesId, States);
                    cmd.DispatchCompute(shader, s_UpdateKernel, CoreUtils.DivRoundUp(s_Count, 64), 1, 1);
                }
            }
            if (!s_Reset && !fullRefresh)
            {
                using var scope = new ProfilingScope(cmd, QueueSampler);
                cmd.SetComputeBufferParam(shader, s_PrepareKernel, ArgsId, Args);
                cmd.SetComputeBufferParam(shader, s_PrepareKernel, DispatchArgsId, DispatchArgs);
                cmd.DispatchCompute(shader, s_PrepareKernel, 1, 1, 1);
                BindQueue(cmd, s_ProcessKernel);
                cmd.SetComputeBufferParam(shader, s_ProcessKernel, MetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                cmd.SetComputeBufferParam(shader, s_ProcessKernel, VirtualShadowMapProjectionSet.BufferId,
                    VirtualShadowMapPrototypeRuntime.Projections.Buffer);
                cmd.SetComputeIntParam(shader, HZBId, s_UseHZB ? 1 : 0);
                VirtualShadowMapHZB.BindInvalidation(cmd, shader, s_ProcessKernel);
                cmd.DispatchCompute(shader, s_ProcessKernel, DispatchArgs, 0);
            }
            s_Reset = false;
            s_RecordPending = true;
        }

        private static void BindQueue(CommandBuffer cmd, int kernel)
        {
            cmd.SetComputeBufferParam(s_Shader, kernel, QueueId, Queue);
            cmd.SetComputeBufferParam(s_Shader, kernel, ArgsId, Args);
        }

        internal static void CompleteFrame()
        {
            s_Epoch = s_PreparedEpoch;
            s_RecordPending = false;
        }

        internal static void AbortFrame()
        {
            // Commands may have consumed state without completing depth production.
            // Rebuild both pools next time instead of losing an old footprint.
            s_Reset = true;
            s_RecordPending = false;
            VirtualShadowMapHZB.InvalidateHistory();
        }

        private static void ReleaseBuffers()
        {
            Sources?.Dispose(); Sources = null;
            States?.Dispose(); States = null;
            Queue?.Dispose(); Queue = null;
            Args?.Dispose(); Args = null;
            DispatchArgs?.Dispose(); DispatchArgs = null;
        }

        internal static void Dispose()
        {
            ReleaseBuffers();
            s_Scene = null;
            s_Shader = null;
            s_Reset = true;
            NeedsFullRefresh = true;
            s_RecordPending = false;
            s_Ranges.Clear();
        }
    }
}
