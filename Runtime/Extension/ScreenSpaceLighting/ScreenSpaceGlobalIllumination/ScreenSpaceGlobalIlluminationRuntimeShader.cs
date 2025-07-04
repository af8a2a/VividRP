using System;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ScreenSpaceGlobalIlluminationRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


        [SerializeField, ResourcePath("Runtime/Extension/ScreenSpaceLighting/ScreenSpaceGlobalIllumination/Shader/ScreenSpaceGlobalIllumination.compute")]
        private ComputeShader m_ScreenSpaceGlobalIlluminationCS;

        public ComputeShader screenSpaceGlobalIlluminationCS
        {
            get => m_ScreenSpaceGlobalIlluminationCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceGlobalIlluminationCS, value);
        }

        // Denoising
        [SerializeField, ResourcePath("Runtime/Extension/ScreenSpaceLighting/ScreenSpaceGlobalIllumination/Shader/BilateralUpsample.compute")]
        private ComputeShader m_BilateralUpsampleCS;

        public ComputeShader bilateralUpsampleCS
        {
            get => m_BilateralUpsampleCS;
            set => this.SetValueAndNotify(ref m_BilateralUpsampleCS, value);
        }


        // Combine
        [SerializeField, ResourcePath("Runtime/Extension/ScreenSpaceLighting/ScreenSpaceGlobalIllumination/Shader/CombineGI.compute")]
        private ComputeShader m_CombineGICS;

        public ComputeShader combineGICS
        {
            get => m_CombineGICS;
            set => this.SetValueAndNotify(ref m_CombineGICS, value);
        }
    }
}