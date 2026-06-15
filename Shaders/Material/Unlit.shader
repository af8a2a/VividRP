Shader "VividRP/Material/Unlit"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _SurfaceType("Surface Type", Float) = 0.0
        [SubEnum(SurfaceOptions, Alpha, 0, Premultiply, 1, Additive, 2, Multiply, 3)] _BlendMode("Blend Mode", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaCutoffEnable("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _CullMode("Cull", Float) = 2.0
        [SubToggle(SurfaceOptions, _)] _TransparentZWrite("Transparent ZWrite", Float) = 0.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0
        [HideInInspector] _TransparentSortPriority("Transparent Sort Priority", Float) = 0.0
        [HideInInspector] _DoubleSidedEnable("Double Sided Enable", Float) = 0.0
        [HideInInspector] _TransparentCullMode("Transparent Cull Mode", Float) = 2.0
        [HideInInspector] _OpaqueCullMode("Opaque Cull Mode", Float) = 2.0
        [HideInInspector] _ZTestTransparent("Transparent ZTest", Float) = 4.0
        [HideInInspector] _ZTestDepthEqualForOpaque("Opaque ZTest", Float) = 4.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [MainTexture] [Tex(SurfaceInputs, _UnlitColor)] _UnlitColorMap("Color Map", 2D) = "white" {}
        [HideInInspector] [MainColor] _UnlitColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaRemapMin("Alpha Remap Min", Float) = 0.0
        [HideInInspector] _AlphaRemapMax("Alpha Remap Max", Float) = 1.0

        [Main(Emission, _, off, off)] _EmissionGroup("Emission", Float) = 0
        [Sub(Emission)] [HDR] _EmissiveColor("Emissive Color", Color) = (0, 0, 0, 0)
        [Sub(Emission)] _EmissiveColorMap("Emissive Color Map", 2D) = "white" {}
        [SubToggle(Emission, _)] _AlbedoAffectEmissive("Albedo Affect Emissive", Float) = 0.0
        [Sub(Emission)] _EmissiveExposureWeight("Emissive Pre Exposure", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _EmissiveColorLDR("Emissive Color LDR", Color) = (0, 0, 0, 0)
        [HideInInspector] _UseEmissiveIntensity("Use Emissive Intensity", Float) = 0.0
        [HideInInspector] _EmissiveIntensity("Emissive Intensity", Float) = 1.0

        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _AlphaSrcBlend("__alphaSrc", Float) = 1.0
        [HideInInspector] _AlphaDstBlend("__alphaDst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0

        [HideInInspector] _MainTex("Color Map", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
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
            Cull [_CullMode]
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragPreDepth

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull [_CullMode]
            ColorMask 0

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragShadow

                #define VIVIDRP_UNLIT_SHADOW_CASTER_PASS 1
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _EMISSIVE_COLOR_MAP
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma vertex Vert
                #pragma fragment FragForward

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _EMISSIVE_COLOR_MAP
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma vertex Vert
                #pragma fragment FragForward

                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature EDITOR_VISUALIZATION
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma shader_feature_local_fragment _EMISSIVE_COLOR_MAP
                #pragma vertex Vert
                #pragma fragment FragMeta

                #define VIVIDRP_UNLIT_META_PASS 1
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ColorMask RG
            ZWrite On
            ZTest LEqual
            Cull [_CullMode]
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma instancing_options renderinglayer
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragMotionVectors

                #define VIVIDRP_UNLIT_MOTION_VECTOR_PASS 1
                #include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/UnlitPass.hlsl"
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.UnlitShaderGUI"

    FallBack Off
}
