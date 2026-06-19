#ifndef VIVIDRP_COLOR_CHECKER_PASS_INCLUDED
#define VIVIDRP_COLOR_CHECKER_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BakedGI.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#define VIVID_COLOR_CHECKER_TEXTURE_SIZE 8.0
#define VIVID_COLOR_CHECKER_SMOOTHNESS_COLUMNS 6.0

CBUFFER_START(UnityPerMaterial)
    float4 _Gradient_Color_A;
    float4 _Gradient_Color_B;
    float _Compare_to_Unlit;
    float _NumberOfFields;
    float _FieldsPerRow;
    float _gridThickness;
    float _SquareSize;
    float _Add_Gradient;
    float _gradient_power;
    float _sphereMode;
    float _material_mode;
    float _texture_mode;
    float _reflection_mode;
    float _rawTextureAvailable;
    float _rawTexturePreExposure;
    float _textureSlice;
CBUFFER_END

TEXTURE2D(_CheckerTexture);
SAMPLER(sampler_CheckerTexture);
TEXTURE2D(_rawTexture);
SAMPLER(sampler_rawTexture);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionOS : TEXCOORD0;
    float3 normalOS : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    float3 normalWS : TEXCOORD3;
    float2 lightmapUV : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct ColorCheckerSurface
{
    float3 baseColor;
    float3 emission;
    float smoothness;
    float metallic;
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionOS = input.positionOS.xyz;
    output.normalOS = normalize(input.normalOS);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.lightmapUV = TransformVividLightmapUV(input.uv1);
    return output;
}

float PositiveFieldCount()
{
    return max(_NumberOfFields, 1.0);
}

float PositiveFieldsPerRow()
{
    return max(_FieldsPerRow, 1.0);
}

float PositiveSquareSize()
{
    return max(_SquareSize, 1e-4);
}

float2 GetColorCheckerBaseUV(float3 positionOS, float3 normalOS, float numberOfRows)
{
    float2 chartSize = float2(
        PositiveFieldsPerRow() * PositiveSquareSize(),
        max(numberOfRows, 1.0) * PositiveSquareSize());
    float2 baseUV = positionOS.xy / chartSize;

    if (_sphereMode < 0.5)
        baseUV.x = lerp(baseUV.x, 1.0 - baseUV.x, step(0.5, normalOS.z));

    return baseUV;
}

float4 SampleColorField(float fieldIndex)
{
    float clampedIndex = clamp(fieldIndex, 0.0, VIVID_COLOR_CHECKER_TEXTURE_SIZE * VIVID_COLOR_CHECKER_TEXTURE_SIZE - 1.0);
    float fieldIndexV = floor(clampedIndex / VIVID_COLOR_CHECKER_TEXTURE_SIZE);
    float fieldIndexU = clampedIndex - fieldIndexV * VIVID_COLOR_CHECKER_TEXTURE_SIZE;
    float2 uv = (float2(fieldIndexU, fieldIndexV) + 0.5) / VIVID_COLOR_CHECKER_TEXTURE_SIZE;
    return SAMPLE_TEXTURE2D_LOD(_CheckerTexture, sampler_CheckerTexture, uv, 0.0);
}

float4 SampleCheckerTexture(float2 uv)
{
    return SAMPLE_TEXTURE2D(_CheckerTexture, sampler_CheckerTexture, saturate(uv));
}

float4 SampleRawTexture(float2 uv)
{
    return SAMPLE_TEXTURE2D(_rawTexture, sampler_rawTexture, saturate(uv));
}

ColorCheckerSurface BuildTextureModeSurface(float2 baseUV)
{
    float2 textureUV = saturate(baseUV);
    float4 litColor = SampleCheckerTexture(textureUV);
    float4 rawColor = _rawTextureAvailable > 0.5 ? SampleRawTexture(textureUV) : litColor;
    float rawMask = (_Compare_to_Unlit > 0.5) ? step(saturate(_textureSlice), textureUV.x) : 0.0;
    float exposureCompensation = _rawTexturePreExposure > 0.5 ? 1.0 : VividGetOneOverPreExposure();

    ColorCheckerSurface surface;
    surface.baseColor = lerp(litColor.rgb, 0.0, rawMask);
    surface.emission = rawColor.rgb * rawMask * exposureCompensation;
    surface.smoothness = 0.0;
    surface.metallic = 0.0;
    return surface;
}

ColorCheckerSurface BuildProceduralSurface(Varyings input)
{
    float numberOfFields = PositiveFieldCount();
    float fieldsPerRow = PositiveFieldsPerRow();
    float numberOfRows = ceil(numberOfFields / fieldsPerRow);
    float2 baseUV = GetColorCheckerBaseUV(input.positionOS, input.normalOS, numberOfRows);

    if (_texture_mode > 0.5)
        return BuildTextureModeSurface(baseUV);

    float2 contourThickness = _gridThickness / (numberOfRows + 1.0) * 0.25;
    float2 contour = baseUV - contourThickness;
    contour = saturate(contour / max(1.0 - contourThickness * 2.0, 1e-4));

    float isLastRow = step(numberOfRows - 1.0, numberOfRows * contour.y);
    float fieldsInLastRow = fmod(numberOfFields, fieldsPerRow);
    fieldsInLastRow = fieldsInLastRow == 0.0 ? fieldsPerRow : fieldsInLastRow;
    float trueFieldsPerRow = lerp(fieldsPerRow, fieldsInLastRow, isLastRow);

    float2 unclampedUV = float2(trueFieldsPerRow * contour.x, numberOfRows * contour.y);
    float2 checkerUV = frac(unclampedUV);
    float fieldsRatio = trueFieldsPerRow / fieldsPerRow;
    float gridEdge = 0.01 * fieldsRatio;
    float sideMask = step(0.1, abs(input.normalOS.z));
    float grid = 1.0;

    if (_sphereMode > 0.5)
    {
        checkerUV.x = checkerUV.x / max(fieldsRatio, 1e-4) - lerp(0.5 / max(fieldsRatio, 1e-4), 0.0, fieldsRatio);
    }
    else
    {
        float gridThicknessColumns = fieldsRatio * _gridThickness;
        float2 gridMargins = float2(gridThicknessColumns, _gridThickness);
        checkerUV = (checkerUV - gridMargins) / max(1.0 - gridMargins * 2.0, 1e-4);
        float2 gridMask = smoothstep(-gridEdge, 0.0, 0.5 - abs(checkerUV - 0.5));
        grid = gridMask.x * gridMask.y * sideMask;
    }

    float fieldIndex = floor(unclampedUV.x) + fieldsPerRow * floor(unclampedUV.y);
    if (_material_mode > 0.5)
        fieldIndex = floor(baseUV.y * (numberOfFields / VIVID_COLOR_CHECKER_SMOOTHNESS_COLUMNS));

    float4 colorField = SampleColorField(fieldIndex);
    float4 backgroundColor = float4(0.04, 0.04, 0.04, 1.0);

    ColorCheckerSurface surface;
    surface.baseColor = lerp(backgroundColor.rgb, colorField.rgb, grid);
    surface.emission = 0.0;
    surface.smoothness = 0.0;
    surface.metallic = 0.0;

    if (_Compare_to_Unlit > 0.5)
    {
        float unlitMask = smoothstep(-gridEdge * 0.5, gridEdge * 0.5, dot(checkerUV, normalize(float2(1.0, -1.0)))) * grid;
        surface.emission = colorField.rgb * unlitMask;
        surface.baseColor = lerp(surface.baseColor, 0.0, unlitMask);
    }

    if (_reflection_mode > 0.5)
    {
        surface.baseColor = 1.0;
        surface.emission = 0.0;
        surface.smoothness = 1.0;
        surface.metallic = 1.0;
    }

    if (_material_mode > 0.5)
    {
        surface.emission = 0.0;
        surface.metallic = colorField.a;
        surface.smoothness = floor(saturate(baseUV.x) * VIVID_COLOR_CHECKER_SMOOTHNESS_COLUMNS)
            / (VIVID_COLOR_CHECKER_SMOOTHNESS_COLUMNS - 1.0);
    }

    if (_Add_Gradient > 0.5)
    {
        float gradientMask = step(baseUV.y, 0.001);
        float3 gradientColor = lerp(
            _Gradient_Color_A.rgb,
            _Gradient_Color_B.rgb,
            saturate(pow(saturate(baseUV.x), max(_gradient_power, 0.01))));
        gradientColor = lerp(backgroundColor.rgb, gradientColor, sideMask);
        surface.baseColor = lerp(surface.baseColor, gradientColor, gradientMask);

        if (_Compare_to_Unlit > 0.5)
        {
            float unlitGradientMask = step(baseUV.y, -0.5 / max(numberOfRows, 1.0)) * sideMask;
            surface.emission = lerp(surface.emission, surface.baseColor * unlitGradientMask, gradientMask);
            surface.baseColor *= 1.0 - unlitGradientMask;
        }
    }

    return surface;
}

VividGBufferSurfaceData BuildColorCheckerGBufferSurfaceData(Varyings input)
{
    ColorCheckerSurface checkerSurface = BuildProceduralSurface(input);
    float3 normalWS = normalize(input.normalWS);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = checkerSurface.baseColor;
    surfaceData.normalWS = normalWS;
    surfaceData.linearRoughness = (1.0 - checkerSurface.smoothness) * (1.0 - checkerSurface.smoothness);
    surfaceData.metallic = checkerSurface.metallic;
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.customData = 0.0;
    surfaceData.customData1 = 0.0;
    surfaceData.materialFeatures = VIVID_MATERIALFEATURE_DEFAULT;
    surfaceData.emissive = checkerSurface.emission;
    surfaceData.builtinData = BuildVividBuiltinData(
        SampleVividBakedGI(input.lightmapUV, normalWS),
        HasVividBakedGI(),
        input.lightmapUV,
        input.positionWS);
    return surfaceData;
}

VividGBufferFragmentOutput FragGBuffer(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    return PackVividGBufferSurfaceData(BuildColorCheckerGBufferSurfaceData(input));
}

half4 FragPreDepth(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    return 0.0;
}

half4 FragDebug(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    ColorCheckerSurface checkerSurface = BuildProceduralSurface(input);
    return half4(checkerSurface.baseColor + checkerSurface.emission, 1.0);
}

#endif
