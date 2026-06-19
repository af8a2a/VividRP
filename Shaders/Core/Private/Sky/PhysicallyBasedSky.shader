Shader "Hidden/VividRP/PhysicallyBasedSky"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma multi_compile_fragment _ LOCAL_SKY

    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
    #if defined(SHADER_API_D3D12)
    #define VIVIDRP_SKY_BINDLESS_SURFACE_TEXTURES 1
    #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"
    #endif
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Sky/PhysicallyBasedSkyRendering.hlsl"
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Sky/PhysicallyBasedSkyEvaluation.hlsl"
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        return output;
    }

    float4 RenderSky(Varyings input)
    {
        const float R = _PlanetaryRadius;
        const float3 V = GetSkyViewDirWS(input.positionCS.xy);
        const bool renderSunDisk = _RenderSunDisk != 0;
        float3 N;
        float r;

    #ifdef LOCAL_SKY
        const float3 O = _PBRSkyCameraPosPS;

        float tEntry = IntersectAtmosphere(O, V, N, r).x;
        float tExit = IntersectAtmosphere(O, V, N, r).y;

        float cosChi = -dot(N, V);
        float cosHor = ComputeCosineOfHorizonAngle(r);
    #else
        N = float3(0.0f, 1.0f, 0.0f);
        r = _PlanetaryRadius;
        float cosChi = -dot(N, V);
        float cosHor = 0.0f;
        const float3 O = N * r;

        float tEntry = 0.0f;
        float tExit = IntersectSphere(_AtmosphericRadius, -dot(N, V), r).y;
    #endif

        bool rayIntersectsAtmosphere = tEntry >= 0.0f;
        bool lookAboveHorizon = cosChi >= cosHor;

        float tFrag = FLT_INF;
        float3 radiance = 0.0f;

        if (renderSunDisk)
            radiance = RenderSunDisk(tFrag, tExit, V);

        if (rayIntersectsAtmosphere && !lookAboveHorizon)
        {
            float tGround = tEntry + IntersectSphere(R, cosChi, r).x;

            if (tGround < tFrag)
            {
                tFrag = -tGround;
                radiance = 0.0f;

                float3 gP = O + tGround * -V;
                float3 gN = normalize(gP);
                radiance += SampleGroundEmission(gN);

                float3 gBrdf = INV_PI * SampleGroundAlbedo(gN);

                for (uint i = 0; i < _CelestialLightCount; i++)
                {
                    CelestialBodyData light = _CelestialBodyDatas[i];

                    float3 L = -light.forward.xyz;
                    float3 intensity = light.color.rgb;

                #ifdef LOCAL_SKY
                    intensity *= SampleGroundIrradianceTexture(dot(gN, L));
                #else
                    float3 opticalDepth = ComputeAtmosphericOpticalDepth(r, dot(N, L), true);
                    intensity *= TransmittanceFromOpticalDepth(opticalDepth) * saturate(dot(N, L));
                #endif

                    radiance += gBrdf * intensity;
                }
            }
        }
        else if (tFrag == FLT_INF)
        {
            radiance += SampleSpaceEmission(-V);
        }

        float3 skyColor = 0.0f;
        float3 skyOpacity = 0.0f;

    #ifdef LOCAL_SKY
        if (rayIntersectsAtmosphere)
            EvaluatePbrAtmosphere(_PBRSkyCameraPosPS, V, tFrag, renderSunDisk, skyColor, skyOpacity);
    #else
        if (lookAboveHorizon)
            EvaluateDistantAtmosphereWithLut(-V, skyColor, skyOpacity);
    #endif

        skyColor += radiance * (1.0f - skyOpacity);
        skyColor *= _IntensityMultiplier;

        return float4(skyColor, 1.0f);
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float4 value = RenderSky(input);
        value.rgb = ClampToFloat16Max(VividApplyPreExposure(value.rgb));
        return value;
    }

    float4 FragBaking(Varyings input) : SV_Target
    {
        return RenderSky(input);
    }

    ENDHLSL

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
            #pragma fragment FragRender
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
            #pragma fragment FragBaking
            ENDHLSL
        }
    }

    FallBack Off
}
