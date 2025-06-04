using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ExposureRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/Exposure/Shader/Exposure.compute")]
        private ComputeShader m_ExposureCS;

        public ComputeShader exposureCS
        {
            get => m_ExposureCS;
            set => this.SetValueAndNotify(ref m_ExposureCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/Exposure/Shader/HistogramExposure.compute")]
        private ComputeShader m_HistogramExposureCS;

        public ComputeShader histogramExposureCS
        {
            get => m_HistogramExposureCS;
            set => this.SetValueAndNotify(ref m_HistogramExposureCS, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/Exposure/Shader/ApplyExposure.compute")]
        private ComputeShader m_ApplyExposureCS;
        public ComputeShader applyExposureCS
        {
            get => m_ApplyExposureCS;
            set => this.SetValueAndNotify(ref m_ApplyExposureCS, value);
        }

    }
}