Shader "Hidden/VividRP/PhysicallyBasedSky"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "PhysicallyBasedSky"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragRender

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl"

            float4 FragRender(Varyings input) : SV_Target
            {
                return float4(VividApplyPreExposure(EvaluateSkyColor(input.positionCS.xy)), 1.0f);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PhysicallyBasedSkyBaking"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragBaking

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl"

            float4 FragBaking(Varyings input) : SV_Target
            {
                return float4(EvaluateSkyColor(input.positionCS.xy), 1.0f);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
