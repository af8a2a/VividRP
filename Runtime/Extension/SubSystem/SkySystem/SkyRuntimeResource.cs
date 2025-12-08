using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]

    public class SkyRuntimeResources: IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        
        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/SkySystem/Shader/AmbientProbeConvolution.compute")]
        private ComputeShader m_AmbientProbeConvolutionCS;

        public ComputeShader ambientProbeConvolutionCS
        {
            get => m_AmbientProbeConvolutionCS;
            set => this.SetValueAndNotify(ref m_AmbientProbeConvolutionCS, value);
        }

        
        // SkyBox

        /// <summary>
        /// Sky.
        /// </summary>
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/SkySystem/Shader/HDRISky.shader")]
        private Shader m_HdriSkyPS;

        public Shader hdriSkyPS
        {
            get => m_HdriSkyPS;
            set => this.SetValueAndNotify(ref m_HdriSkyPS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/SkySystem/Shader/GradientSky.shader")]
        private Shader m_GradientSkyPS;

        public Shader gradientSkyPS
        {
            get => m_GradientSkyPS;
            set => this.SetValueAndNotify(ref m_GradientSkyPS, value);
        }
        
        
        //not support yet
        // [SerializeField, ResourcePath("Runtime/Features/Sky/Shader/ProceduralToonSky.shader")]
        // private Shader m_ProceduralToonSkyBoxPS;
        //
        // public Shader proceduralToonSkyBoxPS
        // {
        //     get => m_ProceduralToonSkyBoxPS;
        //     set => this.SetValueAndNotify(ref m_GradientSkyPS, value);
        // }

        
        
        /// <summary>
        /// Default HDRI Sky
        /// </summary>
        [SerializeField]
        [ResourcePath("Textures/Sky/DefaultHDRISky.exr")]
        private Cubemap m_DefaultHDRISky;

        public Cubemap defaultHDRISky
        {
            get => m_DefaultHDRISky;
            set => this.SetValueAndNotify(ref m_DefaultHDRISky, value, nameof(m_DefaultHDRISky));
        }
        
        

    }
}