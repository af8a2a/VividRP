using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Cached samplers: no frame-dependent names or recurring allocations.
    internal static class VSMProfiling
    {
        internal static readonly ProfilingSampler MarkReceivers = new("VSM.MarkReceiverPages");
        internal static readonly ProfilingSampler MarkCoarsePages = new("VSM.MarkCoarsePages");
        internal static readonly ProfilingSampler ClearPageHierarchy = new("VSM.ClearPageHierarchy");
        internal static readonly ProfilingSampler LODTraversal = new("VSM.LODTraversal");
        internal static readonly ProfilingSampler CompactViews = new("VSM.CompactViews");
        internal static readonly ProfilingSampler BuildPageHierarchy = new("VSM.BuildPageHierarchy");
        internal static readonly ProfilingSampler AllocationPrepare = new("VSM.AllocationPrepare");
        internal static readonly ProfilingSampler AllocationCommit = new("VSM.AllocationCommit");
        internal static readonly ProfilingSampler Allocate = new("VSM.Allocate");
        internal static readonly ProfilingSampler Clear = new("VSM.ClearPhysicalPages");
        internal static readonly ProfilingSampler BuildPageWorkLists = new("VSM.BuildPageWorkLists");
        internal static readonly ProfilingSampler Occupancy = new("VSM.PageOccupancy");
        internal static readonly ProfilingSampler Finalize = new("VSM.FinalizePages");
        internal static readonly ProfilingSampler DynamicRaster = new("VSM.DynamicRaster");
        internal static readonly ProfilingSampler Layout = new("VSM.LayoutRemap");
        internal static readonly ProfilingSampler Invalidate = new("VSM.InvalidateStatic");
        internal static readonly ProfilingSampler DynamicInvalidate = new("VSM.InvalidateDynamic");
        internal static readonly ProfilingSampler StaticCull = new("VSM.StaticCasterCull");
        internal static readonly ProfilingSampler DynamicCull = new("VSM.DynamicCasterCull");
        internal static readonly ProfilingSampler PageCull = new("VSM.PageCull");
        internal static readonly ProfilingSampler StaticRasterClear = new("VSM.StaticRasterClear");
        internal static readonly ProfilingSampler StaticRasterDraw = new("VSM.StaticRasterDraw");
        internal static readonly ProfilingSampler DynamicRasterClear = new("VSM.DynamicRasterClear");
        internal static readonly ProfilingSampler DynamicRasterDraw = new("VSM.DynamicRasterDraw");
        internal static readonly ProfilingSampler StaticRaster = new("VSM.StaticRaster");
        internal static readonly ProfilingSampler UnityRaster = new("VSM.UnityCompatibilityRaster");
        internal static readonly ProfilingSampler Resolve = new("VSM.Resolve");
        internal static readonly ProfilingSampler ResolveTrace = new("VSM.ResolveTrace");
        internal static readonly ProfilingSampler FilterHorizontal = new("VSM.FilterHorizontal");
        internal static readonly ProfilingSampler FilterVertical = new("VSM.FilterVertical");
        internal static readonly ProfilingSampler FilterTemporalVertical = new("VSM.FilterTemporalVertical");
        internal static readonly ProfilingSampler ResetFeedback = new("VSM.ResetFeedback");
    }
}
