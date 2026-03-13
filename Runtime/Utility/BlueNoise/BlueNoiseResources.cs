using UnityEngine;

namespace VividRP.Runtime
{
    [PipelineResource]
    public class BlueNoiseResources
    {
        [ResourcePath("Texture/BlueNoise/ScramblingTile256SPP.png")]
        public Texture2D ScramblingTile;

        [ResourcePath("Texture/BlueNoise/RankingTile256SPP.png")]
        public Texture2D RankingTile;

        [ResourcePath("Texture/BlueNoise/SobolOwenScrambled256.png")]
        public Texture2D OwenScrambledSequence;
        
    }
}
