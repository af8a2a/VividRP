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
        AreaAndPunctual = Area | Punctual,
        Environment = 1 << 2,
        EnvironmentAndPunctual = Environment | Punctual,
        EnvironmentAndArea = Environment | Area,
        EnvironmentAndAreaAndPunctual = Environment | Area | Punctual,
        Decal = 1 << 3
    }
}
