Shader "Hidden/VividRP/CopyDepth"
{
    HLSLINCLUDE
        #define USE_FULL_PRECISION_BLIT_TEXTURE
        #pragma target 2.0
        #pragma editor_sync_compilation
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            ZWrite Off ZTest Always Blend Off Cull Off
            Name "CopyDepth"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragCopyDepth

                float FragCopyDepth(Varyings input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord.xy, _BlitMipLevel).r;
                }
            ENDHLSL
        }
    }

    Fallback Off
}
