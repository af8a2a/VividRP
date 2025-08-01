using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class VolumetricLightRuntimeResource : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        [SerializeField] [ResourcePath("Runtime/Extension/Volumetric/Shader/GenerateMaxZ.compute")]
        private ComputeShader m_MaxZCS;

        public ComputeShader maxZCS
        {
            get => m_MaxZCS;
            set => this.SetValueAndNotify(ref m_MaxZCS, value);
        }
        
        
        // Default Fog Volume Shader
        [SerializeField, ResourcePath("Runtime/Extension/Volumetric/Shader/TextureFog.shader")]
        private Shader m_DefaultFogVolumeShader;
        public Shader defaultFogVolumeShader
        {
            get => m_DefaultFogVolumeShader;
            set => this.SetValueAndNotify(ref m_DefaultFogVolumeShader, value);
        }
        
        
        // Default Fog Volume Shader
        [SerializeField, ResourcePath("Runtime/Extension/Volumetric/Shader/VolumetricFinal.shader")]
        private Shader m_FinalShader;
        public Shader finalShader
        {
            get => m_FinalShader;
            set => this.SetValueAndNotify(ref m_FinalShader, value);
        }


        
        [SerializeField] [ResourcePath("Runtime/Extension/Volumetric/Shader/VolumetricFogInitialize.compute")]
        private ComputeShader m_VolumeInitializeCS;
        
        public ComputeShader volumeInitializeCS
        {
            get => m_VolumeInitializeCS;
            set => this.SetValueAndNotify(ref m_VolumeInitializeCS, value);
        }

        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Volumetric/Shader/VolumetricFogIndirect.compute")]
        private ComputeShader m_VolumetricFogIndirectCS;
        
        public ComputeShader volumetricFogIndirectCS
        {
            get => m_VolumetricFogIndirectCS;
            set => this.SetValueAndNotify(ref m_VolumetricFogIndirectCS, value);
        }
        
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Volumetric/Shader/VolumetricLighting.compute")]
        private ComputeShader m_VolumetricLightingCS;

        public ComputeShader volumetricLightingCS
        {
            get => m_VolumetricLightingCS;
            set => this.SetValueAndNotify(ref m_VolumetricLightingCS, value);
        }

        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Volumetric/Shader/VolumetricLightingFiltering.compute")]
        private ComputeShader m_VolumetricLightingFilteringCS;
        
        public ComputeShader volumetricLightingFilteringCS
        {
            get => m_VolumetricLightingFilteringCS;
            set => this.SetValueAndNotify(ref m_VolumetricLightingFilteringCS, value, nameof(m_VolumetricLightingFilteringCS));
        }
        
        
        
        
        // [SerializeField] [ResourcePath("Textures/Volumetric/3d_fractal_noise_sample01.tga")]
        // private Texture3D m_FractalNoiseTexture;
        
        
        // public Texture3D factalNoiseTexture
        // {
        //     get => m_FractalNoiseTexture;
        //     set => this.SetValueAndNotify(ref m_FractalNoiseTexture, value, nameof(m_FractalNoiseTexture));
        // }
        //
        // [SerializeField] [ResourcePath("Runtime/Features/Volumetric/Shader/VolumetricFinal.mat")]
        //
        // private Material m_VolumetricTextureFogMat;
        //
        //
        // public Material volumetricTextureFogMat
        // {
        //     get => m_VolumetricTextureFogMat;
        //     set => this.SetValueAndNotify(ref m_VolumetricTextureFogMat, value, nameof(m_VolumetricTextureFogMat));
        // }
        //
        
    }
}