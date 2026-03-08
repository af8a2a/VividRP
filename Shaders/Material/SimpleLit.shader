Shader "VividRP/Material/SimpleLit"
{
    Properties
    {
        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [MainTexture] [Tex(SurfaceInputs, _BaseColor)] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Main(MaterialInputs, _, on, off)] _MaterialInputs("Material Inputs", Float) = 1
        [Sub(MaterialInputs)] _LinearRoughness("Linear Roughness", Range(0, 1)) = 0.5
        [Sub(MaterialInputs)] _Metallic("Metallic", Range(0, 1)) = 0.0
        [Sub(MaterialInputs)] _Occlusion("Occlusion", Range(0, 1)) = 1.0
        [SubEnum(MaterialInputs, Standard, 0, Fabric, 1, ClearCoat, 2)] _MaterialId("Material ID", Float) = 0
        [Sub(MaterialInputs)] _CustomData("Custom Data", Range(0, 1)) = 0.0

        [Main(Emission, _, off, off)] _EmissionGroup("Emission", Float) = 0
        [Sub(Emission)] [HDR] _EmissiveColor("Emissive", Color) = (0, 0, 0, 0)
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
            Name "VividPreDepth"
            Tags { "LightMode" = "VividPreDepth" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/SimpleLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividGBuffer"
            Tags { "LightMode" = "VividGBuffer" }

            Blend One Zero
            ZWrite On
            ZTest Equal
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

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
