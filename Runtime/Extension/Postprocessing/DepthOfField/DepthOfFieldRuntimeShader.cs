using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class DepthOfFieldRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DepthOfFieldMip.compute")]
        private ComputeShader m_DepthOfFieldMipCS;

        public ComputeShader depthOfFieldMipCS
        {
            get => m_DepthOfFieldMipCS;
            set => this.SetValueAndNotify(ref m_DepthOfFieldMipCS, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DepthOfFieldMipSafe.compute")]
        private ComputeShader m_DepthOfFieldMipSafeCS;

        public ComputeShader depthOfFieldMipSafeCS
        {
            get => m_DepthOfFieldMipSafeCS;
            set => this.SetValueAndNotify(ref m_DepthOfFieldMipSafeCS, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DepthOfFieldCoCReproject.compute")]
        private ComputeShader m_DepthOfFieldCoCReprojectCS;

        public ComputeShader depthOfFieldCoCReprojectCS
        {
            get => m_DepthOfFieldCoCReprojectCS;
            set => this.SetValueAndNotify(ref m_DepthOfFieldCoCReprojectCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFApertureShape.compute")]
        private ComputeShader m_DoFApertureShapeCS;

        public ComputeShader doFApertureShapeCS
        {
            get => m_DoFApertureShapeCS;
            set => this.SetValueAndNotify(ref m_DoFApertureShapeCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFCircleOfConfusion.compute")]
        private ComputeShader m_DoFCircleOfConfusionCS;

        public ComputeShader doFCircleOfConfusionCS
        {
            get => m_DoFCircleOfConfusionCS;
            set => this.SetValueAndNotify(ref m_DoFCircleOfConfusionCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFCoCMinMax.compute")]
        private ComputeShader m_DoFCoCMinMaxCS;

        public ComputeShader doFCoCMinMaxCS
        {
            get => m_DoFCoCMinMaxCS;
            set => this.SetValueAndNotify(ref m_DoFCoCMinMaxCS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFCombine.compute")]
        private ComputeShader m_DoFCombineCS;

        public ComputeShader doFCombineCS
        {
            get => m_DoFCombineCS;
            set => this.SetValueAndNotify(ref m_DoFCombineCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFComputeSlowTiles.compute")]
        private ComputeShader m_DoFComputeSlowTilesCS;

        public ComputeShader doFComputeSlowTilesCS
        {
            get => m_DoFComputeSlowTilesCS;
            set => this.SetValueAndNotify(ref m_DoFComputeSlowTilesCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFGather.compute")]
        private ComputeShader m_DoFGatherCS;

        public ComputeShader doFGatherCS
        {
            get => m_DoFGatherCS;
            set => this.SetValueAndNotify(ref m_DoFGatherCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/DepthOfField/DiaphragmDOF/Shader/DoFMinMaxDilate.compute")]
        private ComputeShader m_DoFMinMaxDilateCS;

        public ComputeShader doFMinMaxDilateCS
        {
            get => m_DoFMinMaxDilateCS;
            set => this.SetValueAndNotify(ref m_DoFMinMaxDilateCS, value);
        }
    }
}