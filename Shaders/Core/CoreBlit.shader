Shader "Hidden/VividRP/CoreBlit"
{
    HLSLINCLUDE
        #pragma target 2.0
        #pragma editor_sync_compilation
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        // 0: Nearest
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "Nearest"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragNearest
            ENDHLSL
        }

        // 1: Bilinear
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "Bilinear"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBilinear
            ENDHLSL
        }

        // 2: Nearest quad
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "NearestQuad"
            HLSLPROGRAM
                #pragma vertex VertQuad
                #pragma fragment FragNearest
            ENDHLSL
        }

        // 3: Bilinear quad
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuad"
            HLSLPROGRAM
                #pragma vertex VertQuad
                #pragma fragment FragBilinear
            ENDHLSL
        }

        // 4: Nearest quad with padding
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "NearestQuadPadding"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragNearest
            ENDHLSL
        }

        // 5: Bilinear quad with padding
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadPadding"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragBilinear
            ENDHLSL
        }

        // 6: Nearest quad with padding and repeat
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "NearestQuadPaddingRepeat"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragNearestRepeat
            ENDHLSL
        }

        // 7: Bilinear quad with padding and repeat
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadPaddingRepeat"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragBilinearRepeat
            ENDHLSL
        }

        // 8: Bilinear quad with padding (octahedral)
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadPaddingOctahedral"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralBilinearRepeat
            ENDHLSL
        }

        // 9: Nearest quad with padding alpha blend
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "NearestQuadPaddingAlphaBlend"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragNearest
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }

        // 10: Bilinear quad with padding alpha blend
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "BilinearQuadPaddingAlphaBlend"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragBilinear
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }

        // 11: Nearest quad with padding alpha blend repeat
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "NearestQuadPaddingAlphaBlendRepeat"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragNearestRepeat
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }

        // 12: Bilinear quad with padding alpha blend repeat
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "BilinearQuadPaddingAlphaBlendRepeat"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragBilinearRepeat
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }

        // 13: Bilinear quad with padding alpha blend (octahedral)
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "BilinearQuadPaddingAlphaBlendOctahedral"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralBilinearRepeat
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }

        // 14: Cube to octahedral projection
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "CubeToOctahedral"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuad
                #pragma fragment FragOctahedralProject
            ENDHLSL
        }

        // 15: Cube to octahedral luminance
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "CubeToOctahedralLuminance"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuad
                #pragma fragment FragOctahedralProjectLuminance
            ENDHLSL
        }

        // 16: Cube to octahedral alpha
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "CubeToOctahedralAlpha"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuad
                #pragma fragment FragOctahedralProjectAlphaToRGBA
            ENDHLSL
        }

        // 17: Cube to octahedral red
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "CubeToOctahedralRed"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuad
                #pragma fragment FragOctahedralProjectRedToRGBA
            ENDHLSL
        }

        // 18: Bilinear quad luminance
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadLuminance"
            HLSLPROGRAM
                #pragma vertex VertQuad
                #pragma fragment FragBilinearLuminance
            ENDHLSL
        }

        // 19: Bilinear quad alpha
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadAlpha"
            HLSLPROGRAM
                #pragma vertex VertQuad
                #pragma fragment FragBilinearAlphaToRGBA
            ENDHLSL
        }

        // 20: Bilinear quad red
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearQuadRed"
            HLSLPROGRAM
                #pragma vertex VertQuad
                #pragma fragment FragBilinearRedToRGBA
            ENDHLSL
        }

        // 21: Nearest cube to octahedral with padding
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "NearestCubeToOctahedralPadding"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralProjectNearestRepeat
            ENDHLSL
        }

        // 22: Bilinear cube to octahedral with padding
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "BilinearCubeToOctahedralPadding"
            HLSLPROGRAM
                #pragma multi_compile_local _ BLIT_DECODE_HDR
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralProjectBilinearRepeat
            ENDHLSL
        }

        // 23: Nearest quad with padding (octahedral)
        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "NearestQuadPaddingOctahedral"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralNearestRepeat
            ENDHLSL
        }

        // 24: Nearest quad with padding alpha blend (octahedral)
        Pass
        {
            ZWrite Off ZTest Always Blend DstColor Zero Cull Off
            Name "NearestQuadPaddingAlphaBlendOctahedral"
            HLSLPROGRAM
                #pragma vertex VertQuadPadding
                #pragma fragment FragOctahedralNearestRepeat
                #define WITH_ALPHA_BLEND
            ENDHLSL
        }
    }

    Fallback Off
}
