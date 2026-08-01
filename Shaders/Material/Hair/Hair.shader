Shader "VividRP/Material/Hair"
{
    Properties
    {
        [Enum(Color, 0, Physical Melanin, 1, Normalized Melanin, 2)]
        _HairAbsorptionModel("Absorption Model", Float) = 1
        [MainColor] _HairBaseColor("Absorption Color", Color) = (0.227, 0.130, 0.035, 1)
        _HairMelanin("Melanin Concentration", Range(0.0, 1.0)) = 0.805
        _HairMelaninRedness("Melanin Redness", Range(0.0, 1.0)) = 0.05

        _HairLongitudinalRoughness("Longitudinal Roughness (Beta M)", Range(0.001, 1.0)) = 0.4
        _HairAzimuthalRoughness("Azimuthal Roughness (Beta N)", Range(0.001, 1.0)) = 0.6
        _HairCuticleAngleDegrees("Cuticle Angle (Degrees)", Range(0.0, 10.0)) = 3.0

        _HairIor("Index of Refraction", Range(1.0001, 3.0)) = 1.55
        [Toggle] _HairFresnelApproximation("Use Schlick Fresnel Approximation", Float) = 1

        [HDR] _HairEmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        [HideInInspector] _HairMaterialVersion("Hair Material Version", Float) = 1
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
    CustomEditor "VividRP.Editor.HairShaderGUI"
}
