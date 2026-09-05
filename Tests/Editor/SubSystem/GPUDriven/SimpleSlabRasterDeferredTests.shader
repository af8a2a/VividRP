Shader "Hidden/VividRP/Tests/SimpleSlabRasterDeferred"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Fullscreen"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/SimpleDeferredLitPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "PixelIndices"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #include "Packages/com.vivid.render-pipelines/Shaders/Material/DeferredDirectionalLightingIndirectPass.hlsl"
            ENDHLSL
        }
    }
}
