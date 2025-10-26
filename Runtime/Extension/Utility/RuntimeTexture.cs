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

        
        
        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_1spp.png")]
        private Texture2D m_ScramblingRanking1SPP;
        public Texture2D scramblingRanking1SPP
        {
            get => m_ScramblingRanking1SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking1SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_2spp.png")]
        private Texture2D m_ScramblingRanking2SPP;
        public Texture2D scramblingRanking2SPP
        {
            get => m_ScramblingRanking2SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking2SPP, value);
        }

        
        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_4spp.png")]
        private Texture2D m_ScramblingRanking4SPP;
        public Texture2D scramblingRanking4SPP
        {
            get => m_ScramblingRanking4SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking4SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_8spp.png")]
        private Texture2D m_ScramblingRanking8SPP;
        public Texture2D scramblingRanking8SPP
        {
            get => m_ScramblingRanking8SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking8SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_16spp.png")]
        private Texture2D m_ScramblingRanking16SPP;
        public Texture2D scramblingRanking16SPP
        {
            get => m_ScramblingRanking16SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking16SPP, value);
        }

        
        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_32spp.png")]
        private Texture2D m_ScramblingRanking32SPP;
        public Texture2D scramblingRanking32SPP
        {
            get => m_ScramblingRanking32SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking32SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_64spp.png")]
        private Texture2D m_ScramblingRanking64SPP;
        public Texture2D scramblingRanking64SPP
        {
            get => m_ScramblingRanking64SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking64SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_128spp.png")]
        private Texture2D m_ScramblingRanking128SPP;
        public Texture2D scramblingRanking128SPP
        {
            get => m_ScramblingRanking128SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking128SPP, value);
        }

        [SerializeField, ResourcePath("Textures/NVIDIA/scrambling_ranking_128x128_2d_256spp.png")]
        private Texture2D m_ScramblingRanking256SPP;
        public Texture2D scramblingRanking256SPP
        {
            get => m_ScramblingRanking256SPP;
            set => this.SetValueAndNotify(ref m_ScramblingRanking256SPP, value);
        }


        [SerializeField, ResourcePath("Textures/NVIDIA/sobol_256_4d.png")]
        private Texture2D m_Sobol256_4DTex;
        public Texture2D sobol256_4DTex
        {
            get => m_Sobol256_4DTex;
            set => this.SetValueAndNotify(ref m_Sobol256_4DTex, value);
        }

    }
}