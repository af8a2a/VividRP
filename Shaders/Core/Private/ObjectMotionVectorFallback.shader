Shader "Hidden/VividRP/ObjectMotionVectorFallback"
{
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

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.af8a2a.vividrp/Shaders/Core/Public/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
