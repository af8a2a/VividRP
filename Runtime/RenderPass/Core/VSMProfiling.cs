using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    // Cached samplers: no frame-dependent names or recurring allocations.
    internal static class VSMProfiling
    {
        internal static readonly ProfilingSampler MarkReceivers = new("VSM.MarkReceiverPages");
        internal static readonly ProfilingSampler Allocate = new("VSM.Allocate");
        internal static readonly ProfilingSampler Clear = new("VSM.ClearPhysicalPages");
        internal static readonly ProfilingSampler Finalize = new("VSM.FinalizePages");
        internal static readonly ProfilingSampler DynamicRaster = new("VSM.DynamicRaster");
        internal static readonly ProfilingSampler Layout = new("VSM.LayoutRemap");
        internal static readonly ProfilingSampler Invalidate = new("VSM.InvalidateStatic");
        internal static readonly ProfilingSampler StaticCull = new("VSM.StaticCasterCull");
        internal static readonly ProfilingSampler DynamicCull = new("VSM.DynamicCasterCull");
        internal static readonly ProfilingSampler PageCull = new("VSM.PageCull");
        internal static readonly ProfilingSampler StaticRaster = new("VSM.StaticRaster");
        internal static readonly ProfilingSampler UnityRaster = new("VSM.UnityCompatibilityRaster");
        internal static readonly ProfilingSampler Resolve = new("VSM.Resolve");
        internal static readonly ProfilingSampler ResetFeedback = new("VSM.ResetFeedback");
    }
}
