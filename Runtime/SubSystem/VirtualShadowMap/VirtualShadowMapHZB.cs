using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Owned with the physical pool. Slice 1 is static; slice 0 is merged depth.
    // Each slice retains last frame until its main cull has finished, then is
    // updated for post culling. Previous addressing is snapshotted independently.
    internal static class VirtualShadowMapHZB
    {
        internal static bool Enabled { get; set; } = true;
        internal static RTHandle Texture { get; private set; }
        internal static GraphicsBuffer PreviousTable { get; private set; }
        internal static GraphicsBuffer PreviousProjections { get; private set; }
        internal static GraphicsBuffer Deferred { get; private set; }
        internal static GraphicsBuffer DeferredArgs { get; private set; }
        internal static GraphicsBuffer BuildArgs { get; private set; }
        private static RenderTexture s_Pool;
        private static bool s_Valid;
        private static ulong s_Generation;
        private static ComputeShader s_Compute;
        private static int s_Build, s_BuildTop, s_Prepare, s_Snapshot, s_Reset;
        internal static int PostKernel { get; private set; }
        internal static int ResetPostKernel { get; private set; }
        private static readonly int TextureId = Shader.PropertyToID("_VSMHZB");
        private static readonly int TableId = Shader.PropertyToID("_VSMHZBPreviousTable");
        private static readonly int ProjectionsId = Shader.PropertyToID("_VSMHZBPreviousProjections");
        private static readonly int DeferredId = Shader.PropertyToID("_VSMHZBDeferred");
        private static readonly int ArgsId = Shader.PropertyToID("_VSMHZBDeferredArgs");
        private static readonly int EnabledId = Shader.PropertyToID("_VSMHZBEnabled");
        private static readonly int ValidId = Shader.PropertyToID("_VSMHZBHistoryValid");
        private static readonly int LayerId = Shader.PropertyToID("_VSMHZBBuildLayer");
        private static readonly int BuildArgsId = Shader.PropertyToID("_VSMHZBBuildArgs");
        private static readonly int PageWorkArgsId = Shader.PropertyToID("_VSMHZBPageWorkArgs");
        private static readonly int FullBuildId = Shader.PropertyToID("_VSMHZBFullBuild");
        private static readonly int OwnersId = Shader.PropertyToID("_VSMHZBOwnersRead");
        private static readonly int PoolId = Shader.PropertyToID("_VSMPhysicalPagePool");
        private static readonly int WorkListId = Shader.PropertyToID("_VSMPageWorkList");
        private static readonly int PageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int MetadataReadId = Shader.PropertyToID("_VSMHZBMetadataRead");
        private static readonly int TableRWId = Shader.PropertyToID("_VSMHZBPreviousTableRW");
        private static readonly int ProjectionsRWId = Shader.PropertyToID("_VSMHZBPreviousProjectionsRW");
        private static readonly int MetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int[] Outputs = {
            Shader.PropertyToID("_VSMHZBOut0"), Shader.PropertyToID("_VSMHZBOut1"),
            Shader.PropertyToID("_VSMHZBOut2"), Shader.PropertyToID("_VSMHZBOut3"),
            Shader.PropertyToID("_VSMHZBOut4"), Shader.PropertyToID("_VSMHZBOut5"),
            Shader.PropertyToID("_VSMHZBOut6") };
        private static readonly ProfilingSampler BuildSampler = new("VSM.HZB.Build");
        private static readonly ProfilingSampler SnapshotSampler = new("VSM.HZB.Snapshot");
        internal static readonly ProfilingSampler PostCullSampler = new("VSM.HZB.PostCull");
        internal static readonly ProfilingSampler PostRasterSampler = new("VSM.HZB.PostRaster");

        internal static void EnsureResources(ComputeShader compute)
        {
            if (s_Compute != compute)
            {
                s_Compute = compute;
                s_Build = compute.FindKernel("VSMBuildShadowHZB");
                s_BuildTop = compute.FindKernel("VSMBuildShadowHZBTop");
                s_Prepare = compute.FindKernel("VSMPrepareShadowHZB");
                s_Snapshot = compute.FindKernel("VSMSnapshotShadowHZB");
                s_Reset = compute.FindKernel("VSMResetHZBDeferred");
                PostKernel = compute.FindKernel("VSMPostCullMeshletsToPagesHZB");
                ResetPostKernel = compute.FindKernel("VSMResetHZBPostDraws");
            }
            var pool = VirtualShadowMapPrototypeRuntime.PhysicalPagePool.rt;
            int entries = VirtualShadowMapPrototypeRuntime.PageTableEntryCount;
            int projections = VirtualShadowMapPrototypeRuntime.Projections.Buffer.count;
            if (s_Pool == pool && Texture != null && PreviousTable.count == entries
                && PreviousProjections.count == projections) return;
            ReleaseResources();
            s_Pool = pool;
            var descriptor = new RenderTextureDescriptor(pool.width / 2, pool.height / 2)
            {
                graphicsFormat = GraphicsFormat.R32_SFloat, depthBufferBits = 0,
                dimension = TextureDimension.Tex2DArray, volumeDepth = 2,
                msaaSamples = 1, useMipMap = true, autoGenerateMips = false,
                mipCount = 7, enableRandomWrite = true
            };
            var texture = new RenderTexture(descriptor) { name = "VSMShadowHZB", filterMode = FilterMode.Point };
            texture.Create();
            Texture = RTHandles.Alloc(texture, transferOwnership: true);
            PreviousTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entries, 4) { name = "VSMHZBPreviousTable" };
            PreviousProjections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projections, 160) { name = "VSMHZBPreviousProjections" };
            BuildArgs = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 6, 4)
                { name = "VSMHZBBuildArgs" };
            DeferredArgs = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 5, 4)
                { name = "VSMHZBDeferredArgs" };
        }

        internal static void EnsureDeferredCapacity(int sourceCapacity)
        {
            if (Deferred != null && Deferred.count >= sourceCapacity) return;
            Deferred?.Dispose();
            Deferred = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceCapacity, 16) { name = "VSMHZBDeferred" };
        }

        internal static void BindCull(CommandBuffer cmd, ComputeShader compute, int kernel, bool post)
        {
            cmd.SetComputeIntParam(compute, EnabledId, Enabled ? 1 : 0);
            cmd.SetComputeIntParam(compute, ValidId, s_Valid && s_Generation == VirtualShadowMapPrototypeRuntime.Projections.Generation ? 1 : 0);
            cmd.SetComputeTextureParam(compute, kernel, TextureId, Texture);
            cmd.SetComputeBufferParam(compute, kernel, TableId, PreviousTable);
            cmd.SetComputeBufferParam(compute, kernel, ProjectionsId, PreviousProjections);
            cmd.SetComputeBufferParam(compute, kernel, DeferredId, Deferred);
            cmd.SetComputeBufferParam(compute, kernel, ArgsId, DeferredArgs);
            if (post) return;
            cmd.SetComputeBufferParam(compute, s_Reset, ArgsId, DeferredArgs);
            cmd.DispatchCompute(compute, s_Reset, 1, 1, 1);
        }

        internal static void InvalidateHistory() => s_Valid = false;

        internal static void BindInvalidation(CommandBuffer cmd, ComputeShader compute, int kernel)
        {
            cmd.SetComputeIntParam(compute, ValidId, Enabled && s_Valid
                && s_Generation == VirtualShadowMapPrototypeRuntime.Projections.Generation ? 1 : 0);
            cmd.SetComputeTextureParam(compute, kernel, TextureId, Texture);
            cmd.SetComputeBufferParam(compute, kernel, TableId, PreviousTable);
        }

        internal static void Build(CommandBuffer cmd, int slice)
        {
            if (!Enabled) return;
            using var scope = new ProfilingScope(cmd, BuildSampler);
            cmd.SetComputeIntParam(s_Compute, LayerId, slice);
            cmd.SetComputeIntParam(s_Compute, FullBuildId, s_Valid ? 0 : 1);
            cmd.SetComputeBufferParam(s_Compute, s_Prepare, PageWorkArgsId, VirtualShadowMapPrototypeRuntime.PageWorkDispatchArgs);
            cmd.SetComputeBufferParam(s_Compute, s_Prepare, BuildArgsId, BuildArgs);
            cmd.DispatchCompute(s_Compute, s_Prepare, 1, 1, 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int kernel = pass == 0 ? s_Build : s_BuildTop;
                cmd.SetComputeBufferParam(s_Compute, kernel, WorkListId, VirtualShadowMapPrototypeRuntime.PageWorkList);
                cmd.SetComputeBufferParam(s_Compute, kernel, OwnersId, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners);
                cmd.SetComputeBufferParam(s_Compute, kernel, MetadataReadId, VirtualShadowMapPrototypeRuntime.PageMetadata);
                if (pass == 0)
                    cmd.SetComputeTextureParam(s_Compute, kernel, PoolId, VirtualShadowMapPrototypeRuntime.PhysicalPagePool);
                else
                    cmd.SetComputeTextureParam(s_Compute, kernel, TextureId, Texture);
                for (int mip = pass == 0 ? 0 : 5; mip < (pass == 0 ? 5 : 7); mip++)
                    cmd.SetComputeTextureParam(s_Compute, kernel, Outputs[mip], Texture, mip);
                cmd.DispatchCompute(s_Compute, kernel, BuildArgs, pass == 0 ? 0u : 12u);
            }
        }

        // Called only after final merge/finalization. No CPU readback or full atlas copy.
        internal static void Commit(CommandBuffer cmd)
        {
            if (!Enabled) { s_Valid = false; return; }
            Build(cmd, 2);
            using var scope = new ProfilingScope(cmd, SnapshotSampler);
            cmd.SetComputeBufferParam(s_Compute, s_Snapshot, TableRWId, PreviousTable);
            cmd.SetComputeBufferParam(s_Compute, s_Snapshot, ProjectionsRWId, PreviousProjections);
            cmd.SetComputeBufferParam(s_Compute, s_Snapshot, VirtualShadowMapProjectionSet.BufferId,
                VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            cmd.SetComputeBufferParam(s_Compute, s_Snapshot, PageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
            cmd.SetComputeBufferParam(s_Compute, s_Snapshot, MetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
            cmd.DispatchCompute(s_Compute, s_Snapshot, CoreUtils.DivRoundUp(VirtualShadowMapPrototypeRuntime.PageTableEntryCount, 64), 1, 1);
            s_Valid = true;
            s_Generation = VirtualShadowMapPrototypeRuntime.Projections.Generation;
        }

        internal static void ReleaseResources()
        {
            Texture?.Release(); Texture = null;
            PreviousTable?.Dispose(); PreviousTable = null;
            PreviousProjections?.Dispose(); PreviousProjections = null;
            Deferred?.Dispose(); Deferred = null;
            DeferredArgs?.Dispose(); DeferredArgs = null;
            BuildArgs?.Dispose(); BuildArgs = null;
            s_Pool = null;
            s_Valid = false;
        }
    }
}
