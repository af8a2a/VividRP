using UnityEngine;

namespace VividRP.Runtime
{
    [PipelineResource]
    public class BlueNoiseResources
    {
        [VividResourcePath("Texture/BlueNoise/ScramblingTile256SPP.png")]
        public Texture2D ScramblingTile;

        [VividResourcePath("Texture/BlueNoise/RankingTile256SPP.png")]
        public Texture2D RankingTile;

        [VividResourcePath("Texture/BlueNoise/SobolOwenScrambled256.png")]
        public Texture2D OwenScrambledSequence;
        
    }
}
