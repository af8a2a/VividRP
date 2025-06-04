Shader "UI_Scratch"
{
    Properties
    {

        _MainTex ("Main Tex", 2D) = "white" { }
        _UnderTex("Under Tex", 2D)= "white" { }
        [HideInInspector]_TraceTexture("TraceTexture", 2D)= "black" { }

        [Space(20)]
        //No use but required by UI image component
        [HideInInspector]_StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector]_Stencil ("Stencil ID", Float) = 0
        [HideInInspector]_StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector]_StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector]_StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector]_ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "Queue" = "Transparent"
        }
        LOD 100

        Pass
        {
            Stencil
            {
                Ref[_Stencil]
                Comp[_StencilComp]
                Pass[_StencilOp]
                ReadMask[_StencilReadMask]
                WriteMask[_StencilWriteMask]
            }

            Blend One OneMinusSrcAlpha
            //Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                #if IS_MESH_GRAPHIC
                    float3 uv : TEXCOORD0;
                #else
                float2 uv : TEXCOORD0;
                #endif

                float4 vertex : SV_POSITION;

                #ifdef UNITY_UI_CLIP_RECT
                    float2 worldPosition : TEXCOORD1;
                #endif
            };


            #ifdef UNITY_UI_CLIP_RECT
                float4 _ClipRect;
            #endif

            v2f vert(appdata v)
            {
                v2f o;

                o.uv = v.uv;
                o.vertex = UnityObjectToClipPos(v.vertex);

                #ifdef UNITY_UI_CLIP_RECT
                    o.worldPosition = v.vertex.xy ;
                #endif

                return o;
            }

            sampler2D _MainTex;
            sampler2D _UnderTex;
            sampler2D _TraceTexture;
            half _FinishRate;

            half4 frag(v2f i) : SV_Target
            {


                half mask = tex2Dlod(_TraceTexture, float4(i.uv,0,0)).r;
                half rate = tex2Dlod(_TraceTexture, float4(i.uv,0,10)).r;
                mask = rate > _FinishRate ? 1 : mask;
                half4 col = tex2D(_MainTex, i.uv);
                half4 maskCol = tex2D(_UnderTex, i.uv);
                half4 finalCol = lerp(col, maskCol, mask);

                #ifdef UNITY_UI_CLIP_RECT
                finalCol.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip (finalCol.a - 0.001);
                #endif

                return finalCol;
            }
            ENDHLSL
        }
    }

}