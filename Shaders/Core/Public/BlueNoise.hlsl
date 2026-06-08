#ifndef _SAMPLING_SOBOLBLUENOISESAMPLING_HLSL_
#define _SAMPLING_SOBOLBLUENOISESAMPLING_HLSL_
#define FLOAT_ONE_MINUS_EPSILON 0.99999994

Texture2D<float>                _SobolScramblingTile;
Texture2D<float>                _SobolRankingTile;
Texture2D<float>                _SobolScramblingTile1SPP;
Texture2D<float>                _SobolRankingTile1SPP;
Texture2D<float>                _SobolScramblingTile8SPP;
Texture2D<float>                _SobolRankingTile8SPP;
Texture2D<float>                _SobolScramblingTile256SPP;
Texture2D<float>                _SobolRankingTile256SPP;
Texture2D<float2>               _SobolOwenScrambledSequence;

StructuredBuffer<uint>          _SobolMatricesBuffer;

// This is an implementation of the method from the paper
// "A Low-Discrepancy Sampler that Distributes Monte Carlo Errors as a Blue Noise in Screen Space" by Heitz et al.
#define VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(FUNCTION_NAME, SCRAMBLING_TILE, RANKING_TILE, SAMPLE_INDEX_MASK) \
float FUNCTION_NAME(uint2 pixelCoord, uint sampleIndex, uint sampleDimension) \
{ \
    pixelCoord = pixelCoord & 127u; \
    sampleIndex = sampleIndex & (uint)SAMPLE_INDEX_MASK; \
    sampleDimension = sampleDimension & 255u; \
\
    uint rankingIndex = (pixelCoord.x + pixelCoord.y * 128u) * 8u + (sampleDimension & 7u); \
    uint rankedSampleIndex = sampleIndex ^ clamp((uint)(RANKING_TILE[uint2(rankingIndex & 127u, rankingIndex / 128u)] * 256.0), 0u, (uint)SAMPLE_INDEX_MASK); \
\
    uint value = clamp((uint)(_SobolOwenScrambledSequence[uint2(sampleDimension, rankedSampleIndex.x)] * 256.0), 0u, 255u); \
\
    uint scramblingIndex = (pixelCoord.x + pixelCoord.y * 128u) * 8u + (sampleDimension & 7u); \
    float scramblingValue = min(SCRAMBLING_TILE[uint2(scramblingIndex & 127u, scramblingIndex / 128u)], 0.999); \
    value = value ^ uint(scramblingValue * 256.0); \
\
    return min((max(0.001, scramblingValue) + value) / 256.0, FLOAT_ONE_MINUS_EPSILON); \
}

VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(GetBNDSequenceSample, _SobolScramblingTile, _SobolRankingTile, 255)
VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(GetBNDSequenceSample1SPPTemporal, _SobolScramblingTile1SPP, _SobolRankingTile1SPP, 255)
VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(GetBNDSequenceSample1SPP, _SobolScramblingTile1SPP, _SobolRankingTile1SPP, 0)
VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(GetBNDSequenceSample8SPP, _SobolScramblingTile8SPP, _SobolRankingTile8SPP, 7)
VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION(GetBNDSequenceSample256SPP, _SobolScramblingTile256SPP, _SobolRankingTile256SPP, 255)

#undef VIVID_DEFINE_BND_SEQUENCE_SAMPLE_FUNCTION

#endif // _SAMPLING_SOBOLBLUENOISESAMPLING_HLSL_
