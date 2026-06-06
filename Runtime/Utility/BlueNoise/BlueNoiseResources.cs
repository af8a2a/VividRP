using UnityEngine;

namespace VividRP.Runtime
{
    [PipelineResource]
    public class BlueNoiseResources
    {
        [VividResourcePath("Texture/BlueNoise/ScramblingTile1SPP.png")]
        public Texture2D ScramblingTile1SPP;

        [VividResourcePath("Texture/BlueNoise/RankingTile1SPP.png")]
        public Texture2D RankingTile1SPP;

        [VividResourcePath("Texture/BlueNoise/ScramblingTile8SPP.png")]
        public Texture2D ScramblingTile8SPP;

        [VividResourcePath("Texture/BlueNoise/RankingTile8SPP.png")]
        public Texture2D RankingTile8SPP;

        [VividResourcePath("Texture/BlueNoise/ScramblingTile256SPP.png")]
        public Texture2D ScramblingTile;

        [VividResourcePath("Texture/BlueNoise/RankingTile256SPP.png")]
        public Texture2D RankingTile;

        [VividResourcePath("Texture/BlueNoise/SobolOwenScrambled256.png")]
        public Texture2D OwenScrambledSequence;
    }
}
