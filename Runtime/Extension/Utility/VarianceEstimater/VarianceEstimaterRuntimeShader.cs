using System;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class VarianceEstimaterRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int m_Version;
        public int version => m_Version;


         [SerializeField] [ResourcePath("Runtime/Extension/Utility/VarianceEstimater/Shader/VarianceEstimater.compute")]
        private ComputeShader mVarianceEstimaterCs;

        public ComputeShader varianceEstimaterCS
        {
            get => mVarianceEstimaterCs;
            set => this.SetValueAndNotify(ref mVarianceEstimaterCs, value);
        }
    }
}