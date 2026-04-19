using System;
using UnityEngine.Rendering;

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

    [Serializable]
    public sealed class TileClusterDebugParameter : VolumeParameter<TileClusterDebug>
    {
        public TileClusterDebugParameter(TileClusterDebug value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ClusterDebugModeParameter : VolumeParameter<ClusterDebugMode>
    {
        public ClusterDebugModeParameter(ClusterDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class TileClusterCategoryDebugParameter : VolumeParameter<TileClusterCategoryDebug>
    {
        public TileClusterCategoryDebugParameter(TileClusterCategoryDebug value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Obsolete("ClusterDebugVolume is deprecated. Use the Rendering Debugger instead.")]
    [Serializable]
    public sealed class ClusterDebugVolume : VolumeComponent
    {
        public TileClusterDebugParameter tileClusterDebug = new(TileClusterDebug.None);
        public TileClusterCategoryDebugParameter tileClusterDebugByCategory = new(TileClusterCategoryDebug.Punctual);
        public ClusterDebugModeParameter clusterDebugMode = new(ClusterDebugMode.VisualizeOpaque);
        public MinFloatParameter clusterDebugDistance = new(1f, 0f);

        public bool IsActive()
        {
            return active
                && tileClusterDebug != null
                && tileClusterDebug.overrideState
                && tileClusterDebug.value != TileClusterDebug.None;
        }
    }
}
