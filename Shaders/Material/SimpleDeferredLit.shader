Shader "Hidden/VividRP/SimpleDeferredLit"
{
    Properties
    {
        [NoScaleOffset] _GBuffer0("GBuffer0", 2D) = "black" {}
        [NoScaleOffset] _GBuffer1("GBuffer1", 2D) = "black" {}
        [NoScaleOffset] _GBuffer2("GBuffer2", 2D) = "black" {}
        [NoScaleOffset] _GBuffer3("GBuffer3", 2D) = "black" {}
        [NoScaleOffset] _DepthTexture("Depth", 2D) = "white" {}
        _MainLightDirection("Main Light Direction (To Light)", Vector) = (0.57735, 0.57735, 0.57735, 0.0)
        [HDR] _MainLightColor("Main Light Color", Color) = (1, 1, 1, 1)
        [HDR] _AmbientColor("Ambient Color", Color) = (0.03, 0.03, 0.03, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "SimpleDeferredLit"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
                #pragma target 4.5
                #pragma vertex Vert
                #pragma fragment Frag

                #include "Packages/com.af8a2a.vividrp/Shaders/Core/SimpleDeferredLitPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
