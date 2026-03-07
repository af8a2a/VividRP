Shader "VividRP/Material/SimpleLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _LinearRoughness("Linear Roughness", Range(0, 1)) = 0.5
        _Metallic("Metallic", Range(0, 1)) = 0.0
        _Occlusion("Occlusion", Range(0, 1)) = 1.0
        [Enum(Standard, 0, Fabric, 1, ClearCoat, 2)] _MaterialId("Material ID", Float) = 0
        _CustomData("Custom Data", Range(0, 1)) = 0.0
        [HDR] _EmissiveColor("Emissive", Color) = (0, 0, 0, 0)
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
            Name "VividGBuffer"
            Tags { "LightMode" = "VividGBuffer" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma vertex Vert
                #pragma fragment FragGBuffer

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/SimpleLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma vertex Vert
                #pragma fragment FragDebug

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/SimpleLitGBufferPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
