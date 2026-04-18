Shader "Hidden/VividRP/OpaqueAtmosphericScattering"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "Default"
            Cull Off    ZWrite Off
            Blend 0 One SrcAlpha, Zero One // Premultiplied alpha for RGB, preserve alpha for the alpha channel
            Blend 1 Off
            ZTest Less  // Required for XR occlusion mesh optimization
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragOpaqueAtmosphericScattering
            #define OPAQUE_FOG_PASS
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "HDRISky"
            Cull Off    ZWrite Off
            Blend 0 One SrcAlpha, Zero One // Premultiplied alpha for RGB, preserve alpha for the alpha channel
            Blend 1 Off
            ZTest Less  // Required for XR occlusion mesh optimization

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragOpaqueAtmosphericScatteringForHDRISky
            #define OPAQUE_FOG_PASS
            #define ATMOSPHERE_NO_AERIAL_PERSPECTIVE
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
