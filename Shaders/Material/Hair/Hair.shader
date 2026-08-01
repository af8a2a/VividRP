Shader "VividRP/Material/Hair"
{
    Properties
    {
        [MainColor] _HairBaseColor("Base Color", Color) = (0.227, 0.130, 0.035, 1)
        [Enum(Color, 0, Physics, 1, Normalized Physics, 2)] _HairAbsorptionModel("Absorption Model", Float) = 1
        _HairMelanin("Melanin", Range(0.0, 1.0)) = 0.805
        _HairMelaninRedness("Melanin Redness", Range(0.0, 1.0)) = 0.05
        _HairLongitudinalRoughness("Longitudinal Roughness", Range(0.001, 1.0)) = 0.4
        _HairAzimuthalRoughness("Azimuthal Roughness", Range(0.001, 1.0)) = 0.6
        _HairIor("IOR", Range(1.0001, 3.0)) = 1.55
        _HairCuticleAngleDegrees("Cuticle Angle", Range(0.0, 10.0)) = 3.0
        [Toggle] _HairFresnelApproximation("Schlick Fresnel", Float) = 1
        [HDR] _HairEmissionColor("Emission", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "ReferencedPathtracingDXR"
            Tags { "LightMode" = "ReferencedPathtracingDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON

                #include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/HairReferencedPathtracing.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "RaytracingGBufferDXR"
            Tags { "LightMode" = "RaytracingGBufferDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile _ INSTANCING_ON

                #include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/HairRaytracingGBuffer.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
