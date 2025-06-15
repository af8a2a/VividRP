using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]


    public class IBLFilterRuntimeShader:IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        
        
        [SerializeField, ResourcePath("Runtime/Extension/Filter/IBLFilter/Shader/GGXConvolve.shader")]
        private Shader m_GGXConvolvePS;

        public Shader GGXConvolvePS
        {
            get => m_GGXConvolvePS;
            set => this.SetValueAndNotify(ref m_GGXConvolvePS, value);
        }

        /// <summary>
        /// GGX Convolution
        /// </summary>
        [SerializeField, ResourcePath("Runtime/Extension/Filter/IBLFilter/Shader/BuildProbabilityTables.compute")]
        private ComputeShader m_BuildProbabilityTablesCS;

        public ComputeShader buildProbabilityTablesCS
        {
            get => m_BuildProbabilityTablesCS;
            set => this.SetValueAndNotify(ref m_BuildProbabilityTablesCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Filter/IBLFilter/Shader/ComputeGgxIblSampleData.compute")]
        private ComputeShader m_ComputeGgxIblSampleDataCS;

        public ComputeShader computeGgxIblSampleDataCS
        {
            get => m_ComputeGgxIblSampleDataCS;
            set => this.SetValueAndNotify(ref m_ComputeGgxIblSampleDataCS, value);
        }

    }
}