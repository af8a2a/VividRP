using System;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum TileClusterDebug
    {
        None,
        Tile,
        Cluster,
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
        Lit = 1 << 0,
        Fabric = 1 << 1,
        [InspectorName("Clear Coat")]
        ClearCoat = 1 << 2,
        [InspectorName("SSR Receive")]
        SSRReceive = 1 << 3,
        [InspectorName("Decal Receive")]
        DecalReceive = 1 << 4,
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
