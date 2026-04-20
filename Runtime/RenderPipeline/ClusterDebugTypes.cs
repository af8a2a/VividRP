using System;

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

    [Flags]
    public enum TileClusterCategoryDebug
    {
        Punctual = 1 << 0,
        Area = 1 << 1,
        Environment = 1 << 2,
        Decal = 1 << 3,
    }
}
