// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/StandardLit.slab.template"
//
// Full-feature Substrate Slab migration of VividRP/Material/StandardLit (coexistence companion).
// - Preserves 100% of StandardLit properties, StandardLitShaderGUI, and all 9 Raster/Meta/Motion/DXR passes.
// - Automatically compiles a standalone, zero-switch GPUDriven ComputeShader SubAsset consuming
//   VividMaterialData + VividSurfaceBindingData (VT primary + Bindless experimental) into Substrate OpenPBR V2.
// - Supports optional WYSIWYG @vivid-surface-begin..end / @vivid-vertex-begin..end / @vivid-coverage-begin..end hooks.
Shader "VividRP/Material/StandardLitSlab"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _Surface("Surface Type", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaClip("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _Cull("Cull", Float) = 2.0
        [SubToggle(SurfaceOptions, _)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveSSR("Receive SSR", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveDecals("Receive Decals", Float) = 1.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [SubEnum(SurfaceInputs, Specular, 0, Metallic, 1)] _WorkflowMode("Workflow Mode", Float) = 1.0
        [AdvancedHeaderProperty] [MainTexture] [Tex(SurfaceInputs, _BaseColor)] _BaseMap("Albedo", 2D) = "white" {}
        [Advanced] [HideInInspector] [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Advanced] [TilingOffset(SurfaceInputs)] _BaseMap_ST("UV Tiling and Offset", Vector) = (1, 1, 0, 0)
        [Tex(SurfaceInputs)][Tex] _OpacityMap("Opacity Map", 2D) = "white" {}
        [Tex(SurfaceInputs)] _RMOMap("RMO Map (R: Roughness, G: Metallic, B: AO)", 2D) = "white" {}
        [Sub(SurfaceInputs)] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceInputs, Metallic Alpha, 0, Albedo Alpha, 1)] _SmoothnessTextureChannel("Smoothness Source", Float) = 0.0
        [Sub(SurfaceInputs)] [MinMaxSlider(SurfaceInputs, _MetallicRemapMin, _MetallicRemapMax)] _MetallicRemap("Metallic Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _MetallicRemapMin("Metallic Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _MetallicRemapMax("Metallic Remap Max", Range(0.0, 1.0)) = 1.0
        [Sub(SurfaceInputs)] [MinMaxSlider(SurfaceInputs, _SmoothnessRemapMin, _SmoothnessRemapMax)] _SmoothnessRemap("Smoothness Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _SmoothnessRemapMin("Smoothness Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _SmoothnessRemapMax("Smoothness Remap Max", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _BumpScale)] [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1.0
        [HideInInspector] _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [Sub(SurfaceInputs)] [MinMaxSlider(SurfaceInputs, _AORemapMin, _AORemapMax)] _AORemap("AO Remap", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _AORemapMin("AO Remap Min", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _AORemapMax("AO Remap Max", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _EmissionColor)] _EmissionMap("Emission Map", 2D) = "black" {}
        [HideInInspector] [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        [Sub(SurfaceInputs)] _ClearCoatMask("Clear Coat Mask", Range(0.0, 1.0)) = 0.0
        [Sub(SurfaceInputs)] _ClearCoatSmoothness("Clear Coat Smoothness", Range(0.0, 1.0)) = 1.0

        [Main(ReferencePathTracing, _, on, off)] _ReferencePathTracing("Reference Path Tracing", Float) = 1
        [SubToggle(ReferencePathTracing, _)] _ThinWalledTransmission("Thin-Walled Transmission", Float) = 0.0
        [Sub(ReferencePathTracing)] _TransmissionWeight("Transmission Weight", Range(0.0, 1.0)) = 0.0
        [NoScaleOffset] [Sub(ReferencePathTracing)] _TransmissionMap("Transmission Map (R)", 2D) = "white" {}
        [Sub(ReferencePathTracing)] _TransmissionColor("Transmission Color", Color) = (1, 1, 1, 1)
        [Sub(ReferencePathTracing)] _TransmissionDepth("Transmission Depth", Float) = 0.0
        [HDR] [Sub(ReferencePathTracing)] _TransmissionScatter("Transmission Scatter", Color) = (0, 0, 0, 0)
        [Sub(ReferencePathTracing)] _TransmissionScatterAnisotropy("Transmission Scatter Anisotropy", Range(-0.95, 0.95)) = 0.0
        [Sub(ReferencePathTracing)] _SpecularIOR("Specular IOR", Range(1.0, 3.0)) = 1.5
        [Sub(ReferencePathTracing)] _SubsurfaceWeight("Face Subsurface Weight", Range(0.0, 1.0)) = 0.0
        [Sub(ReferencePathTracing)] _SubsurfaceColor("Face Subsurface Color", Color) = (1, 1, 1, 1)
        [Sub(ReferencePathTracing)] _SubsurfaceRadius("Face Subsurface Radius (World Units)", Float) = 0.01
        [Sub(ReferencePathTracing)] _SubsurfaceRadiusScale("Face Subsurface Radius Scale", Color) = (1, 0.5, 0.25, 1)
        [Sub(ReferencePathTracing)] _SubsurfaceScatterAnisotropy("Face Subsurface Scatter Anisotropy", Range(-0.95, 0.95)) = 0.0
        [Sub(ReferencePathTracing)] _SubsurfaceTransmissionWeight("Ear Transmission Weight", Range(0.0, 1.0)) = 0.0

        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _MetallicGlossMap("Legacy Metallic Map", 2D) = "white" {}
        [HideInInspector] _RoughnessMap("Legacy Roughness Map", 2D) = "white" {}
        [HideInInspector] _OcclusionMap("Legacy Occlusion Map", 2D) = "white" {}
    }

    VIVID_SLAB_USE_BASE_SUBSHADER

    CustomEditor "VividRP.Editor.StandardLitShaderGUI"

    FallBack Off
}
