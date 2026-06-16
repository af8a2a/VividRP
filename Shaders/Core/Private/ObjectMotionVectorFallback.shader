Shader "Hidden/VividRP/ObjectMotionVectorFallback"
{
    Properties
    {
        [HideInInspector] _BaseMap("BaseMap", 2D) = "white" {}
        [HideInInspector] _BaseColor("BaseColor", Color) = (1, 1, 1, 1)
        [HideInInspector] _OpacityMap("Opacity Map", 2D) = "white" {}
        [HideInInspector] _Cutoff("Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _AlphaClip("Alpha Clip", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "MotionVectors"
            ColorMask RG
            Cull Back
            ZWrite On
            ZTest LEqual
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
