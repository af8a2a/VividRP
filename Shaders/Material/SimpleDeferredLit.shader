Shader "Hidden/VividRP/SimpleDeferredLit"
{
    Properties
    {
        [Main(GBufferInputs, _, on, off)] _GBufferInputs("GBuffer Inputs", Float) = 1
        [Sub(GBufferInputs)] [NoScaleOffset] _GBuffer0("GBuffer0", 2D) = "black" {}
        [Sub(GBufferInputs)] [NoScaleOffset] _GBuffer1("GBuffer1", 2D) = "black" {}
        [Sub(GBufferInputs)] [NoScaleOffset] _GBuffer2("GBuffer2", 2D) = "black" {}
        [Sub(GBufferInputs)] [NoScaleOffset] _GBuffer3("GBuffer3", 2D) = "black" {}
        [Sub(GBufferInputs)] [NoScaleOffset] _DepthTexture("Depth", 2D) = "white" {}

        [Main(LightingInputs, _, on, off)] _LightingInputs("Lighting Inputs", Float) = 1
        [Sub(LightingInputs)] _MainLightDirection("Main Light Direction (To Light)", Vector) = (0.57735, 0.57735, 0.57735, 0.0)
        [Sub(LightingInputs)] [HDR] _MainLightColor("Main Light Color", Color) = (1, 1, 1, 1)
        [Sub(LightingInputs)] [HDR] _AmbientColor("Ambient Color", Color) = (0.03, 0.03, 0.03, 1)
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

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/SimpleDeferredLitPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
