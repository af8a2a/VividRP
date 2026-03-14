Shader "Hidden/VividRP/DeferredDirectionalLightingIndirect"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "DeferredDirectionalLightingIndirect"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
                #pragma target 4.5
                #pragma vertex Vert
                #pragma fragment Frag

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/DeferredDirectionalLightingIndirectPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
