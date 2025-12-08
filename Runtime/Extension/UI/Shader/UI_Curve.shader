Shader "UI_Shader/UI_Curve"
{
    Properties
    {

        [Space(20)]
        _SinCurveFactor ("频率(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (10, 0, 0, 0)
        _SinCurveFactor2 ("振幅(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (1, 0, 0, 0)
        _SinCurveFactor3 ("相位(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)
        _SinCurveFactor4 ("上下(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)
        _Speed ("速度(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)
        _SinCurveFactor5 ("振幅阻尼(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)
        _SinCurveFactor6 ("振幅阻尼附加项(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)
        _SinCurveFactor7 ("振幅衰减(x:波形1,y:波形2,z:波形3,w:波形4)", Vector) = (0, 0, 0, 0)

        _NoiseTex ("Noise Tex", 2D) = "white" { }


        [Space(20)]
        [Toggle] _EndpointFixed ("端点固定", Float) = 0
        _CurveThickness ("曲线厚度", Range(0.001, 50)) = 1
        _Color ("曲线颜色", Color) = (1, 1, 1, 1)
        _BackGroundColor ("背景色", Color) = (0, 0, 0, 0)

        [Space(20)]
        //No use but required by UI image component
        [HideInInspector]_MainTex ("Main Tex", 2D) = "white" { }
        [HideInInspector]_StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector]_Stencil ("Stencil ID", Float) = 0
        [HideInInspector]_StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector]_StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector]_StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector]_ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("透明度裁剪", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Transparent"
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


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UICurve.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            half4 _Color, _BackGroundColor;
            float _CurveThickness;



            v2f vert(appdata v)
            {
                v2f o;

                o.uv = v.uv;
                o.vertex = TransformObjectToHClip(v.vertex);


                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // 曲线sdf
                float sdf = sdfCurve(i.uv.x, i.uv.y);
                half4 col = lerp(0, _Color, 1.0 - smoothstep(0.0, _CurveThickness / _ScreenParams.y, sdf));

                return col;
            }
            ENDHLSL
        }
    }

}