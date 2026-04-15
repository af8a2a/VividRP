Shader "Hidden/VividRP/AerialPerspective"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "OpaqueAtmosphericScattering"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragOpaqueAtmosphericScattering
            #define OPAQUE_FOG_PASS
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
