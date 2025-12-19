using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public enum BlueNoiseTexFormat
    {
        _128R,
        _128RG
    }

    /// <summary>
    /// A bank of nvidia pre-generated spatiotemporal blue noise textures.
    /// ref: https://github.com/NVIDIAGameWorks/SpatiotemporalBlueNoiseSDK/tree/main
    /// </summary>
    [Serializable]
    public sealed class RuntimeTextureSystem : IDisposable
    {
        private static Lazy<RuntimeTextureSystem> m_Instance = new Lazy<RuntimeTextureSystem>();

        public static RuntimeTextureSystem instance => m_Instance.Value;

        public static int blueNoiseArraySize = 64;


        [ResourceFormattedPaths("", 0, 64)] Texture2DArray m_TextureArray128R;
        Texture2DArray m_TextureArray128RG;

        RTHandle m_TextureHandle128R;
        RTHandle m_TextureHandle128RG;


        public Texture2DArray textureArray128R
        {
            get { return m_TextureArray128R; }
        }

        public Texture2DArray textureArray128RG
        {
            get { return m_TextureArray128RG; }
        }

        public RTHandle textureHandle128R
        {
            get { return m_TextureHandle128R; }
        }

        public RTHandle textureHandle128RG
        {
            get { return m_TextureHandle128RG; }
        }


        public RTHandle owenScrambledRGBATex;
        public RTHandle owenScrambled256Tex;
        public RTHandle scramblingTile1SPP;
        public RTHandle scramblingTile8SPP;
        public RTHandle scramblingTile256SPP;
        public RTHandle rankingTile1SPP;
        public RTHandle rankingTile8SPP;
        public RTHandle rankingTile256SPP;
        public RTHandle scramblingTex;
        
        
        public RTHandle scramblingRanking1SPP;
        public RTHandle scramblingRanking2SPP;
        public RTHandle scramblingRanking4SPP;
        public RTHandle scramblingRanking8SPP;
        public RTHandle scramblingRanking16SPP;
        public RTHandle scramblingRanking32SPP;
        public RTHandle scramblingRanking64SPP;
        public RTHandle scramblingRanking128SPP;
        public RTHandle scramblingRanking256SPP;
        public RTHandle sobel;



        DitheredTextureSet m_DitheredTextureSet1SPP;
        DitheredTextureSet m_DitheredTextureSet8SPP;
        DitheredTextureSet m_DitheredTextureSet256SPP;

        public void Init()
        {
            var textures = GraphicsSettings.GetRenderPipelineSettings<RuntimeTexture>();
            InitTextures(128, TextureFormat.R16, textures.blueNoise128RTex, out m_TextureArray128R, out m_TextureHandle128R);
            InitTextures(128, TextureFormat.RG32, textures.blueNoise128RGTex, out m_TextureArray128RG, out m_TextureHandle128RG);


            scramblingRanking1SPP = RTHandles.Alloc(textures.scramblingRanking1SPP);
            scramblingRanking2SPP = RTHandles.Alloc(textures.scramblingRanking2SPP);
            scramblingRanking4SPP = RTHandles.Alloc(textures.scramblingRanking4SPP);
            scramblingRanking8SPP = RTHandles.Alloc(textures.scramblingRanking8SPP);
            scramblingRanking16SPP = RTHandles.Alloc(textures.scramblingRanking16SPP);
            scramblingRanking32SPP = RTHandles.Alloc(textures.scramblingRanking32SPP);
            scramblingRanking64SPP = RTHandles.Alloc(textures.scramblingRanking64SPP);
            scramblingRanking128SPP = RTHandles.Alloc(textures.scramblingRanking128SPP);
            scramblingRanking256SPP = RTHandles.Alloc(textures.scramblingRanking256SPP);
            sobel = RTHandles.Alloc(textures.sobol256_4DTex);

            scramblingTex = RTHandles.Alloc(textures.scramblingTex);
            owenScrambled256Tex = RTHandles.Alloc(textures.owenScrambled256Tex);
            owenScrambledRGBATex = RTHandles.Alloc(textures.owenScrambledRGBATex);
            scramblingTile1SPP = RTHandles.Alloc(textures.scramblingTile1SPP);
            scramblingTile8SPP = RTHandles.Alloc(textures.scramblingTile8SPP);
            scramblingTile256SPP = RTHandles.Alloc(textures.scramblingTile256SPP);

            rankingTile1SPP = RTHandles.Alloc(textures.rankingTile1SPP);
            rankingTile8SPP = RTHandles.Alloc(textures.rankingTile8SPP);
            rankingTile256SPP = RTHandles.Alloc(textures.rankingTile256SPP);

            m_DitheredTextureSet1SPP = new DitheredTextureSet
            {
                owenScrambled256Tex = owenScrambled256Tex,
                scramblingTile = scramblingTile1SPP,
                rankingTile = rankingTile1SPP,
                scramblingTex = scramblingTex
            };

            m_DitheredTextureSet8SPP = new DitheredTextureSet
            {
                owenScrambled256Tex = owenScrambled256Tex,
                scramblingTile = scramblingTile8SPP,
                rankingTile = rankingTile8SPP,
                scramblingTex = scramblingTex
            };

            m_DitheredTextureSet256SPP = new DitheredTextureSet
            {
                owenScrambled256Tex = owenScrambled256Tex,
                scramblingTile = scramblingTile256SPP,
                rankingTile = rankingTile256SPP,
                scramblingTex = scramblingTex
            };
        }

        public RuntimeTextureSystem()
        {
        }

        public static readonly int s_STBNVec1Texture = Shader.PropertyToID("_STBNVec1Texture");
        public static readonly int s_STBNVec2Texture = Shader.PropertyToID("_STBNVec2Texture");
        public static readonly int s_STBNIndex = Shader.PropertyToID("_STBNIndex");
        public static readonly int _OwenScrambledRGTexture = Shader.PropertyToID("_OwenScrambledRGTexture");
        public static readonly int _OwenScrambledTexture = Shader.PropertyToID("_OwenScrambledTexture");
        public static readonly int _ScramblingTileXSPP = Shader.PropertyToID("_ScramblingTileXSPP");
        public static readonly int _RankingTileXSPP = Shader.PropertyToID("_RankingTileXSPP");
        public static readonly int _ScramblingTexture = Shader.PropertyToID("_ScramblingTexture");


        // Structure that holds all the dithered sampling texture that shall be binded at dispatch time.
        internal struct DitheredTextureSet
        {
            public RTHandle owenScrambled256Tex;
            public RTHandle scramblingTile;
            public RTHandle rankingTile;
            public RTHandle scramblingTex;


            public DitheredTextureHandleSet RenderGraphImport(RenderGraph renderGraph)
            {
                return new DitheredTextureHandleSet
                {
                    owenScrambled256Tex = renderGraph.ImportTexture(owenScrambled256Tex),
                    scramblingTile = renderGraph.ImportTexture(scramblingTile),
                    rankingTile = renderGraph.ImportTexture(rankingTile),
                    scramblingTex = renderGraph.ImportTexture(scramblingTex),
                };
            }
        }

        internal DitheredTextureSet DitheredTextureSet1SPP() => m_DitheredTextureSet1SPP;

        internal DitheredTextureSet DitheredTextureSet8SPP() => m_DitheredTextureSet8SPP;

        internal DitheredTextureSet DitheredTextureSet256SPP() => m_DitheredTextureSet256SPP;

        /// <summary>
        /// Cleanups up internal textures.
        /// </summary>
        public void Dispose()
        {
            CoreUtils.Destroy(m_TextureArray128R);
            CoreUtils.Destroy(m_TextureArray128RG);

            RTHandles.Release(m_TextureHandle128R);
            RTHandles.Release(m_TextureHandle128RG);

            RTHandles.Release(m_TextureHandle128R);
            RTHandles.Release(m_TextureHandle128RG);


            owenScrambled256Tex?.Release();
            scramblingTile1SPP?.Release();
            scramblingTile8SPP?.Release();
            scramblingTile256SPP?.Release();
            rankingTile1SPP?.Release();
            rankingTile8SPP?.Release();
            rankingTile256SPP?.Release();
            scramblingTex?.Release();

            owenScrambled256Tex = null;
            scramblingTile1SPP = null;
            scramblingTile8SPP = null;
            scramblingTile256SPP = null;
            rankingTile1SPP = null;
            rankingTile8SPP = null;
            rankingTile256SPP = null;
            scramblingTex = null;
            m_TextureArray128R = null;
            m_TextureArray128RG = null;
        }

        static void InitTextures(int size, TextureFormat format, Texture2D[] sourceTextures,
            out Texture2DArray destinationArray, out RTHandle destinationHandle)
        {
            Assert.IsNotNull(sourceTextures);

            int len = sourceTextures.Length;

            Assert.IsTrue(len > 0);

            destinationArray = new Texture2DArray(size, size, len, format, false, true);
            destinationArray.hideFlags = HideFlags.HideAndDontSave;

            for (int i = 0; i < len; i++)
            {
                var noiseTex = sourceTextures[i];
                // Fail safe; should never happen unless the resources asset is broken
                if (noiseTex == null)
                {
                    continue;
                }

                Graphics.CopyTexture(noiseTex, 0, 0, destinationArray, i, 0);
            }

            destinationHandle = RTHandles.Alloc(destinationArray);
        }

        /// <summary>
        /// Bind spatiotemporal blue noise texture with given index (loop in blueNoiseArraySize).
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="textureIndex"></param>
        public static void BindSTBNParams(BlueNoiseTexFormat format, ComputeCommandBuffer cmd,
            ComputeShader computeShader, int kernel, TextureHandle texture, int frameCount)
        {
            var texID = (format == BlueNoiseTexFormat._128R) ? s_STBNVec1Texture : s_STBNVec2Texture;
            cmd.SetComputeTextureParam(computeShader, kernel, texID, texture);
            cmd.SetComputeIntParam(computeShader, s_STBNIndex, frameCount % blueNoiseArraySize);
        }


        public class DitheredTextureHandleSet
        {
            public TextureHandle owenScrambled256Tex;
            public TextureHandle scramblingTile;
            public TextureHandle rankingTile;
            public TextureHandle scramblingTex;

            public void Use(IBaseRenderGraphBuilder builder)
            {
                builder.UseTexture(owenScrambled256Tex);
                builder.UseTexture(scramblingTile);
                builder.UseTexture(rankingTile);
                builder.UseTexture(scramblingTex);
            }
        }


        internal static void BindDitheredTextureSet(ComputeCommandBuffer cmd,ComputeShader computeShader, int kernel, 
            DitheredTextureHandleSet ditheredTextureSet)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, _OwenScrambledTexture, ditheredTextureSet.owenScrambled256Tex);
            cmd.SetComputeTextureParam(computeShader, kernel, _ScramblingTileXSPP, ditheredTextureSet.scramblingTile);
            cmd.SetComputeTextureParam(computeShader, kernel, _RankingTileXSPP, ditheredTextureSet.rankingTile);
            cmd.SetComputeTextureParam(computeShader, kernel, _ScramblingTexture, ditheredTextureSet.scramblingTex);
        }

        internal static void BindDitheredTextureSet(ComputeCommandBuffer cmd, DitheredTextureHandleSet ditheredTextureSet)
        {
            cmd.SetGlobalTexture(_OwenScrambledTexture, (ditheredTextureSet.owenScrambled256Tex));
            cmd.SetGlobalTexture(_ScramblingTileXSPP, (ditheredTextureSet.scramblingTile));
            cmd.SetGlobalTexture(_RankingTileXSPP, (ditheredTextureSet.rankingTile));
            cmd.SetGlobalTexture(_ScramblingTexture, (ditheredTextureSet.scramblingTex));
        }

        internal static void BindRaytraceDitheredTextureSet(ComputeCommandBuffer cmd,RayTracingShader rayTracingShader, DitheredTextureHandleSet ditheredTextureSet)
        {
            cmd.SetRayTracingTextureParam(rayTracingShader,_OwenScrambledTexture, (ditheredTextureSet.owenScrambled256Tex));
            cmd.SetRayTracingTextureParam(rayTracingShader,_ScramblingTileXSPP, (ditheredTextureSet.scramblingTile));
            cmd.SetRayTracingTextureParam(rayTracingShader,_RankingTileXSPP, (ditheredTextureSet.rankingTile));
            cmd.SetRayTracingTextureParam(rayTracingShader,_ScramblingTexture, (ditheredTextureSet.scramblingTex));
        }

        public static void ClearAll()
        {
            instance?.Dispose();
        }
    }
}