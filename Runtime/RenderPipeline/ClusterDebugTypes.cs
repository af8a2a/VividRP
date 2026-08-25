using System;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum TileClusterDebug
    {
        None,
        Tile,
        Cluster,
        [InspectorName("Deferred Export Classes")]
        MaterialFeatureVariants
    }

    public enum ClusterDebugMode
    {
        VisualizeOpaque,
        VisualizeSlice
    }

    public enum MaterialFeatureVariantDebug
    {
        All = 0,
        [InspectorName("Fast Slab")]
        FastSlab = 1 << 0,
        [InspectorName("General Slab")]
        GeneralSlab = 1 << 1,
        [InspectorName("Dual Slab")]
        DualSlab = 1 << 2,
        [InspectorName("Catch All")]
        CatchAll = 1 << 3,
    }

    [Flags]
    public enum TileClusterCategoryDebug
    {
        Punctual = 1 << 0,
        Area = 1 << 1,
        Environment = 1 << 2,
        Decal = 1 << 3,
    }
}
