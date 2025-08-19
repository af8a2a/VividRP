Shader "Unlit/Outline"
{
    Properties
    {
        _Intensity("Intensity", Range(0.0, 1.0)) = 0.5
        _Transparency("Transparency", Range(0.0, 1.0)) = 0.5
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

    float _Intensity;
    float4 _OutlineColor;
    float _OutlineClampScale;
    // float _Radius;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 tangentOS : TANGENT;
        float2 texcoord : TEXCOORD0;
        float2 staticLightmapUV : TEXCOORD1;
        float2 dynamicLightmapUV : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        half4 tangentWS : TEXCOORD3; // xyz: tangent, w: sign
        half3 viewDirTS : TEXCOORD7;
        float4 positionCS : SV_POSITION;
    };

    Varyings LitPassVertex(Attributes input)
    {
        Varyings output = (Varyings)0;

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

        // normalWS and tangentWS already normalize.
        // this is required to avoid skewing the direction during interpolation
        // also required for per-vertex lighting and SH evaluation
        VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
        output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

        // already normalized from normal transform to WS.
        output.normalWS = normalInput.normalWS;
        real sign = input.tangentOS.w * GetOddNegativeScale();
        half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
        output.tangentWS = tangentWS;

        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
        half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
        output.viewDirTS = viewDirTS;


        output.positionWS = vertexInput.positionWS;


        output.positionCS = TransformWorldToHClip(output.positionWS);


        // output.positionCS = vertexInput.positionCS;

        return output;
    }

    Varyings OutlinePassVertex(Attributes input)
    {
        Varyings output = (Varyings)0;

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

        // normalWS and tangentWS already normalize.
        // this is required to avoid skewing the direction during interpolation
        // also required for per-vertex lighting and SH evaluation
        VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
        output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

        // already normalized from normal transform to WS.
        output.normalWS = normalInput.normalWS;
        real sign = input.tangentOS.w * GetOddNegativeScale();
        half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
        output.tangentWS = tangentWS;

        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
        half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
        output.viewDirTS = viewDirTS;


        float4 scaledScreenParams = GetScaledScreenParams();
        float ScaleX = abs(scaledScreenParams.x / scaledScreenParams.y);
        float3 normalCS = TransformWorldToHClipDir(output.normalWS);
        float2 extend = normalize(normalCS) * (_Intensity * 0.01);
        extend.x /= ScaleX;

        output.positionWS = vertexInput.positionWS;
        output.positionCS = TransformWorldToHClip(output.positionWS);

        float ctrl = clamp(1 / (output.positionCS.w + _OutlineClampScale), 0, 1);
        output.positionCS.xy += extend;

        return output;
    }


    half4 DummyFrag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        // float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
        // float sample_depth = SampleSceneDepth(uv);
        // float ndc_z = ComputeNormalizedDeviceCoordinatesWithZ(input.positionCS).z;
        // if (COMPARE_DEVICE_DEPTH_CLOSER(sample_depth, ndc_z))
        // {
        //     discard;
        // }

        return 0;
    }

    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);


        return _OutlineColor;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "LightMode" = "UniversalForward"
        }
        LOD 100


        Pass
        {
            Name "Predepth"
            Tags
            {
                "RenderPipeline" = "UniversalPipeline"
                "LightMode" = "UniversalForward"
            }
            ZTest Always
            //            Offset -1, -1
            BlendOp Add
            Blend Zero One
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                ZFail Keep
            }
            ZWrite Off
            //            Cull Front
            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment DummyFrag
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags
            {
                "RenderPipeline" = "UniversalPipeline"
                "LightMode" = "UniversalForward"
            }
            ZWrite Off
            Cull Front

            ZTest GEqual
            Stencil
            {
                Ref 1
                Comp NotEqual
                Fail Keep
            }
            HLSLPROGRAM
            #pragma vertex OutlinePassVertex
            #pragma fragment Frag
            ENDHLSL
        }


    }

}