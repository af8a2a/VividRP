using System;

namespace UnityEngine.Rendering.Universal
{
        
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class FluidRuntimeShader : IRenderPipelineResources
    {
        public int version { get; }
        
        

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidAdvection.compute")]
        private ComputeShader m_FluidAdvection;

        public ComputeShader fluidAdvection
        {
            get => m_FluidAdvection;
            set => this.SetValueAndNotify(ref m_FluidAdvection, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidDiffusion.compute")]
        private ComputeShader m_FluidDiffusion;

        public ComputeShader fluidDiffusion
        {
            get => m_FluidDiffusion;
            set => this.SetValueAndNotify(ref m_FluidDiffusion, value);
        }
        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidForce.compute")]
        private ComputeShader m_FluidForce;

        public ComputeShader fluidForce
        {
            get => m_FluidForce;
            set => this.SetValueAndNotify(ref m_FluidForce, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidDivergence.compute")]
        private ComputeShader m_FluidDivergence;

        public ComputeShader fluidDivergence
        {
            get => m_FluidDivergence;
            set => this.SetValueAndNotify(ref m_FluidDivergence, value);
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidPressureInit.compute")]
        private ComputeShader m_FluidPressureInit;

        public ComputeShader fluidPressureInit
        {
            get => m_FluidPressureInit;
            set => this.SetValueAndNotify(ref m_FluidPressureInit, value);
        }


        
        
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidPressure.compute")]
        private ComputeShader m_FluidPressure;

        public ComputeShader fluidPressure
        {
            get => m_FluidPressure;
            set => this.SetValueAndNotify(ref m_FluidPressure, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/FluidSystem/NavierStokes/Shader/FluidGradient.compute")]
        private ComputeShader m_FluidGradient;

        public ComputeShader fluidGradient
        {
            get => m_FluidGradient;
            set => this.SetValueAndNotify(ref m_FluidGradient, value);
        }

    }
}