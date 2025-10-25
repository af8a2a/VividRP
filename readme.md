# Vivid Render Pipeline (VividRP) – Custom SRP Package

This package provides a custom implementation of Unity's **Universal Render Pipeline (URP)**, a prebuilt Scriptable Render Pipeline (SRP) designed for performance, flexibility, and cross-platform graphics in Unity 6 and above.

## Overview

**VividRP** is Unity's High-End-platform rendering solution, built on the Scriptable Render Pipeline (SRP) architecture. It is designed to deliver optimized graphics for a wide range of hardware, from mobile devices to high-end PCs and consoles. This package contains a custom version of URP, allowing for further extension and experimentation with Unity's rendering pipeline.  

**VividRP** is currently undergoing radical evolution, and some interfaces and implementations may be completely rewritten. Maintain maximum compatibility with existing resources and be ready to upgrade to the latest version of Unity at any time


## Key Features
- SuperResolution
  - [x] DLSS4
  - [x] TAAU
  - [x] STP  
  - [x] FSR1
  - [ ] FSR2 
  - [ ] FSR3
  - [ ] Unreal TSR
- Material  
  - [x] Forward ToonMaterial  
    - [x] PBR Lighting
    - [ ] Rimlight
    - [x] Outline
    - [ ] Shadow
    - [ ] Fur
    - [ ] Stocking
  - [x] Physically Based Material
    - [x] Standard 
- PostProcessing
  - Bloom
    - [x] MobileBloom
    - [ ] ConvolutionBloom
  - AntiAlaiasing
    - [X] CMAA2
  - [x] Physically Based Depth Of Field
  - [x] AutoExposure
  - [x] BackgroundLightScatter
  - [x] Diffusion
  - ToneMapping
    - [x] Neutral
    - [x] ACES
    - [x] GranTurismo
    - [x] AgX
    - [x] LumaPreservingMapper
- GlobalIllumination
  - [x] Hybrid Reflection
  - [x] ScreenSpace Global Illumination
  - [X] ScreenSpace PathTracing
  - [x] ScreenSpace PlanarReflection
  - [ ] RTGI
  - [ ] SDFGI
- AmbientOcclusion
  - [x] HBAO
  - [x] XeGTAO
  - [x] RTAO
- Shadow
  - [X] Hybrid Shadow
  - [X] Cascade Shadow
  - [X] Shadow Scatter
  - [x] PerObject Shadow
- Denoiser
  - [x] FidelityFX Reflection Denoiser
  - [x] Bilatal Filter 
  - [x] Temporal Filter
  - [x] NVIDIA NRD
- Lighting Culling
  - [x] Cluster based deferred Lighting(CBDL)
  - [ ] Fine Pruned Tiled Light Lists(FPTL)
- Sky
  - [x] HDRI
  - [x] Gradient
  - [ ] Physically Based Sky
  - [ ] Procedural Sky
- Fog & Cloud
  - [x] Volumetric Fog 
  - [ ] Height Fog
  - [ ] Atomosphere Scatter
  - [ ] Volumetric Cloud
- Advanced Technique
  - [x] Shader Execution Reordering(NVSER)
  - [x] Realtime AreaLight
  - [x] FidelityFX SinglePassDownsample
  - [x] FidelityFX SinglePassGaussianBlur
  - [x] NVIDIA Real-time Denoising (NRD)  



## Reference
- [DanbaidongRP(ToonRenderPipeline Unity6 RayTracing)](https://github.com/danbaidong1111/DanbaidongRP)
- [Unity Graphics](https://github.com/Unity-Technologies/Graphics)
- [Unreal Engine](https://github.com/EpicGames/UnrealEngine)
- [aaaa-rp](https://github.com/Delt06/aaaa-rp)
- [AMD Fidelityfx](https://gpuopen.com/amd-fidelityfx-sdk/)
- [Intel GameTechDev](https://github.com/GameTechDev)
- [SnapdragonStudios](https://github.com/SnapdragonStudios/snapdragon-gsr)
- [NVIDIA DesignWorks Samples](https://github.com/nvpro-samples)
