using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkyCubemapGGXConvolution : System.IDisposable
    {
        internal const string GGXConvolutionShaderName = "Hidden/VividRP/Sky/GGXConvolve";
        internal const int ConvolutionMipCount = 7;

        private const int MaxConvolutionMipLevel = ConvolutionMipCount - 1;
        private const float GoldenRatio = 1.618033988749895f;
        private const int GgxConvolutionPassIndex = 0;
        private const int CopyMipZeroPassIndex = 1;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int GgxIblSamplesId = Shader.PropertyToID("_GgxIblSamples");
        private static readonly int LevelId = Shader.PropertyToID("_Level");
        private static readonly int InvOmegaPId = Shader.PropertyToID("_InvOmegaP");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly ProfilingSampler s_CopyMipZeroSampler = new("SkyCubemapGGXConvolution.CopyMipZero");
        private static readonly ProfilingSampler s_RenderCubemapGGXConvolutionSampler = new("SkyCubemapGGXConvolution.RenderCubemapGGXConvolution");

        private readonly MaterialPropertyBlock m_PropertyBlock = new();
        private Material m_ConvolutionMaterial;
        private Texture2D m_GgxIblSampleData;

        internal bool IsSupported =>
            m_GgxIblSampleData != null
            && SkyShaderCompilationUtility.EnsureMaterialPassReady(
                m_ConvolutionMaterial,
                GgxConvolutionPassIndex)
            && SkyShaderCompilationUtility.EnsureMaterialPassReady(
                m_ConvolutionMaterial,
                CopyMipZeroPassIndex);

        internal void Build(VividRPCoreResources resources)
        {
            Dispose();

            var shader = resources?.SkyGGXConvolutionShader;
            #if UNITY_EDITOR
            shader ??= Shader.Find(GGXConvolutionShaderName);
            #endif
            if (shader != null)
                m_ConvolutionMaterial = CoreUtils.CreateEngineMaterial(shader);

            m_GgxIblSampleData = BuildGgxIblSampleDataTexture();
        }

        internal int GetConvolutionMipLevel(Texture source)
        {
            return source != null
                ? Mathf.Min(MaxConvolutionMipLevel, Mathf.Max(0, source.mipmapCount - 1))
                : 0;
        }

        internal bool Convolve(CommandBuffer cmd, Texture source, RenderTexture target)
        {
            if (!IsSupported
                || cmd == null
                || source == null
                || target == null
                || !IsConvolvableCubemap(source))
            {
                return false;
            }

            var maxMipLevel = Mathf.Min(
                GetConvolutionMipLevel(source),
                GetConvolutionMipLevel(target));
            m_PropertyBlock.Clear();
            m_PropertyBlock.SetTexture(MainTexId, source);
            m_PropertyBlock.SetTexture(GgxIblSamplesId, m_GgxIblSampleData);
            m_PropertyBlock.SetFloat(InvOmegaPId, GetInverseTexelSolidAngle(source.width));

            using (new ProfilingScope(cmd, s_CopyMipZeroSampler))
            {
                RenderCubemapLevel(cmd, target, 0, CopyMipZeroPassIndex);
            }

            if (maxMipLevel <= 0)
                return true;

            using (new ProfilingScope(cmd, s_RenderCubemapGGXConvolutionSampler))
            {
                for (var mipLevel = 1; mipLevel <= maxMipLevel; mipLevel++)
                    RenderCubemapLevel(cmd, target, mipLevel, GgxConvolutionPassIndex);
            }

            return true;
        }

        public void Dispose()
        {
            if (m_ConvolutionMaterial != null)
            {
                CoreUtils.Destroy(m_ConvolutionMaterial);
                m_ConvolutionMaterial = null;
            }

            if (m_GgxIblSampleData != null)
            {
                CoreUtils.Destroy(m_GgxIblSampleData);
                m_GgxIblSampleData = null;
            }
        }

        private static Texture2D BuildGgxIblSampleDataTexture()
        {
            var maxSampleCount = GetMaxIblSampleCount();
            var texture = new Texture2D(maxSampleCount, MaxConvolutionMipLevel, TextureFormat.RGBAHalf, false, true)
            {
                name = "VividSkyGGXIblSampleData",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[maxSampleCount * MaxConvolutionMipLevel];

            for (var mipLevel = 1; mipLevel <= MaxConvolutionMipLevel; mipLevel++)
            {
                var validSampleCount = GetIBLRuntimeFilterSampleCount(mipLevel);
                var roughness = PerceptualRoughnessToRoughness(MipmapLevelToPerceptualRoughness(mipLevel));
                var rowOffset = (mipLevel - 1) * maxSampleCount;

                for (var sampleIndex = 0; sampleIndex < maxSampleCount; sampleIndex++)
                    pixels[rowOffset + sampleIndex] = BuildGgxIblSample(validSampleCount, roughness, sampleIndex);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color BuildGgxIblSample(uint validSampleCount, float roughness, int sampleIndex)
        {
            if (sampleIndex >= validSampleCount)
                return Color.clear;

            var shiftedSampleIndex = (uint)sampleIndex;
            var sampleCount = validSampleCount;

            while (true)
            {
                uint acceptedSampleCount = 0;
                uint currentShiftedSampleIndex = 0;

                for (uint i = 0; i < sampleCount; i++)
                {
                    var u = Golden2dSeq(i, sampleCount);
                    SampleGGXDir(u, roughness, out _, out var nDotL, out _);
                    if (nDotL <= 0.0f)
                        continue;

                    if (acceptedSampleCount == sampleIndex)
                        currentShiftedSampleIndex = i;

                    acceptedSampleCount++;
                }

                if (acceptedSampleCount == validSampleCount)
                {
                    shiftedSampleIndex = currentShiftedSampleIndex;
                    break;
                }

                sampleCount++;
            }

            var sample = Golden2dSeq(shiftedSampleIndex, sampleCount);
            SampleGGXDir(sample, roughness, out var localL, out _, out var nDotH);

            var pdf = 0.25f * Dggx(nDotH, roughness);
            var omegaS = pdf > 0.0f ? 1.0f / (sampleCount * pdf) : 0.0f;

            return new Color(localL.x, localL.y, localL.z, omegaS);
        }

        private static uint GetIBLRuntimeFilterSampleCount(int mipLevel)
        {
            return mipLevel switch
            {
                1 => 21u,
                2 => 34u,
                3 => UseReducedSampleCount() ? 34u : 55u,
                _ => UseReducedSampleCount() ? 34u : 89u,
            };
        }

        private static int GetMaxIblSampleCount()
        {
            return UseReducedSampleCount() ? 34 : 89;
        }

        private static bool UseReducedSampleCount()
        {
#if UNITY_SWITCH || UNITY_SWITCH2
            return true;
#else
            return Application.isMobilePlatform;
#endif
        }

        private static Vector2 Golden2dSeq(uint sampleIndex, uint sampleCount)
        {
            return new Vector2(
                sampleIndex / (float)sampleCount + 0.5f / sampleCount,
                Frac(sampleIndex / GoldenRatio));
        }

        private static void SampleGGXDir(Vector2 u, float roughness, out Vector3 localL, out float nDotL, out float nDotH)
        {
            var roughnessSquared = roughness * roughness;
            var cosTheta = Mathf.Sqrt(Mathf.Max(0.0f, (1.0f - u.x) / (1.0f + (roughnessSquared - 1.0f) * u.x)));
            var phi = 2.0f * Mathf.PI * u.y;
            var localH = SphericalToCartesian(phi, cosTheta);

            nDotH = cosTheta;
            localL = new Vector3(
                2.0f * nDotH * localH.x,
                2.0f * nDotH * localH.y,
                2.0f * nDotH * localH.z - 1.0f);
            nDotL = localL.z;
        }

        private static Vector3 SphericalToCartesian(float phi, float cosTheta)
        {
            var sinTheta = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - cosTheta * cosTheta));
            return new Vector3(
                sinTheta * Mathf.Cos(phi),
                sinTheta * Mathf.Sin(phi),
                cosTheta);
        }

        private static float Dggx(float nDotH, float roughness)
        {
            var alphaSquared = roughness * roughness;
            var s = (nDotH * alphaSquared - nDotH) * nDotH + 1.0f;
            return s > 0.0f ? alphaSquared / (Mathf.PI * s * s) : 0.0f;
        }

        private static float MipmapLevelToPerceptualRoughness(float mipmapLevel)
        {
            var normalizedMipLevel = Mathf.Clamp01(mipmapLevel / MaxConvolutionMipLevel);
            var term = 2.89f / 1.96f - (2.8f / 1.96f) * normalizedMipLevel;
            return Mathf.Clamp01(1.7f / 1.4f - Mathf.Sqrt(Mathf.Max(0.0f, term)));
        }

        private static float PerceptualRoughnessToRoughness(float perceptualRoughness)
        {
            return perceptualRoughness * perceptualRoughness;
        }

        private static float GetInverseTexelSolidAngle(int cubemapResolution)
        {
            return cubemapResolution > 0
                ? (6.0f * cubemapResolution * cubemapResolution) / (4.0f * Mathf.PI)
                : 0.0f;
        }

        private static float Frac(float value)
        {
            return value - Mathf.Floor(value);
        }

        private static bool IsConvolvableCubemap(Texture source)
        {
            if (source == null || source.dimension != TextureDimension.Cube || source.width <= 0 || source.height <= 0)
                return false;

            return source is not RenderTexture renderTexture || renderTexture.IsCreated();
        }

        private void RenderCubemapLevel(CommandBuffer cmd, RenderTexture target, int mipLevel, int passIndex)
        {
            var faceSize = Mathf.Max(1, target.width >> mipLevel);
            m_PropertyBlock.SetFloat(LevelId, mipLevel);

            for (var faceIndex = 0; faceIndex < SkyDiffuseSHUtility.ValidCubemapFaces.Length; faceIndex++)
            {
                var face = SkyDiffuseSHUtility.ValidCubemapFaces[faceIndex];
                m_PropertyBlock.SetMatrix(
                    PixelCoordToViewDirWSId,
                    SkyCubemapBakingUtility.GetCubemapFacePixelCoordToViewDirWSMatrix(face, faceSize));
                CoreUtils.SetRenderTarget(cmd, target, ClearFlag.None, mipLevel, face);
                CoreUtils.DrawFullScreen(cmd, m_ConvolutionMaterial, m_PropertyBlock, passIndex);
            }
        }
    }
}
