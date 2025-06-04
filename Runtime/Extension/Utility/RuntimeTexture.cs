using System;

namespace UnityEngine.Rendering.Universal
{
    
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class RuntimeTexture : IRenderPipelineResources
    {
        [SerializeField][HideInInspector] private int m_Version = 1;
        public int version => m_Version;


        [SerializeField] [ResourcePath("Textures/CoherentNoise/OwenScrambledNoise4.png")]
        private Texture2D m_OwenScrambledRGBATex;

        public Texture2D owenScrambledRGBATex
        {
            get => m_OwenScrambledRGBATex;
            set => this.SetValueAndNotify(ref m_OwenScrambledRGBATex, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/OwenScrambledNoise256.png")]
        private Texture2D m_OwenScrambled256Tex;
        
        public Texture2D owenScrambled256Tex
        {
            get => m_OwenScrambled256Tex;
            set => this.SetValueAndNotify(ref m_OwenScrambled256Tex, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/ScrambleNoise.png")]
        private Texture2D m_ScramblingTex;

        public Texture2D scramblingTex
        {
            get => m_ScramblingTex;
            set => this.SetValueAndNotify(ref m_ScramblingTex, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/RankingTile1SPP.png")]
        private Texture2D m_RankingTile1SPP;

        public Texture2D rankingTile1SPP
        {
            get => m_RankingTile1SPP;
            set => this.SetValueAndNotify(ref m_RankingTile1SPP, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/ScramblingTile1SPP.png")]
        private Texture2D m_ScramblingTile1SPP;

        public Texture2D scramblingTile1SPP
        {
            get => m_ScramblingTile1SPP;
            set => this.SetValueAndNotify(ref m_ScramblingTile1SPP, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/RankingTile8SPP.png")]
        private Texture2D m_RankingTile8SPP;

        public Texture2D rankingTile8SPP
        {
            get => m_RankingTile8SPP;
            set => this.SetValueAndNotify(ref m_RankingTile8SPP, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/ScramblingTile8SPP.png")]
        private Texture2D m_ScramblingTile8SPP;

        public Texture2D scramblingTile8SPP
        {
            get => m_ScramblingTile8SPP;
            set => this.SetValueAndNotify(ref m_ScramblingTile8SPP, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/RankingTile256SPP.png")]
        private Texture2D m_RankingTile256SPP;

        public Texture2D rankingTile256SPP
        {
            get => m_RankingTile256SPP;
            set => this.SetValueAndNotify(ref m_RankingTile256SPP, value);
        }

        [SerializeField] [ResourcePath("Textures/CoherentNoise/ScramblingTile256SPP.png")]
        private Texture2D m_ScramblingTile256SPP;

        public Texture2D scramblingTile256SPP
        {
            get => m_ScramblingTile256SPP;
            set => this.SetValueAndNotify(ref m_ScramblingTile256SPP, value);
        }
        
        
        /// <summary>
        /// STBN, Spatial-Temporal Blue Noise, vec1
        /// </summary>
        [SerializeField]
        [ResourceFormattedPaths("Textures/STBN/vec1/stbn_vec1_2Dx1D_128x128x64_{0}.png", 0, 64)]
        private Texture2D[] m_BlueNoise128RTex = new Texture2D[64];
        public Texture2D[] blueNoise128RTex
        {
            get => m_BlueNoise128RTex;
            set => this.SetValueAndNotify(ref m_BlueNoise128RTex, value);
        }

        /// <summary>
        /// STBN, Spatial-Temporal Blue Noise, vec2
        /// </summary>
        [SerializeField]
        [ResourceFormattedPaths("Textures/STBN/vec2/stbn_vec2_2Dx1D_128x128x64_{0}.png", 0, 64)]
        private Texture2D[] m_BlueNoise128RGTex = new Texture2D[64];
        public Texture2D[] blueNoise128RGTex
        {
            get => m_BlueNoise128RGTex;
            set => this.SetValueAndNotify(ref m_BlueNoise128RGTex, value);
        }

    }
}