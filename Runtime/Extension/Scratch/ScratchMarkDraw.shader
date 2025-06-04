Shader "ScratchMask"
{
    Properties
    {
        _TraceTex ("Trace Tex", 2D) = "white" { }
        _TracePos ("Trace Pos", Vector) = (0.5, 0.5, 0, 0)
    }

    SubShader
    {
        Pass
        {
            Tags
            {
                "LightMode" = "ForwardBase"
            }

            Blend One One
            BlendOp Max
            ZTest Always
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct a2v
            {
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            TEXTURE2D(_TraceTex);
            half2 _TracePos;
            half _TraceSize;

            #ifdef UNITY_UI_CLIP_RECT
            float4 _ClipRect;
            #endif

            v2f vert(a2v v)
            {
                v2f o;

                o.uv = v.uv;

                o.vertex.xy = (o.uv * 2 - 1) * _TraceSize + _TracePos * 2 - 1;

                o.vertex.y *= _ProjectionParams.x;
                o.vertex.z = 0;
                o.vertex.w = 1;

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half col = SAMPLE_TEXTURE2D(_TraceTex, sampler_LinearClamp, i.uv).r;
                return col;
            }
            ENDHLSL
        }
    }
}