using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class PreIntegratedRuntimeShader : IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        /// <summary>
        /// PreIntegratedFGD
        /// </summary>
        [SerializeField, ResourcePath("Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD_GGXDisneyDiffuse.shader")]
        private Shader m_PreIntegratedFGD_GGXDisneyDiffusePS;

        public Shader preIntegratedFGD_GGXDisneyDiffusePS
        {
            get => m_PreIntegratedFGD_GGXDisneyDiffusePS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_GGXDisneyDiffusePS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD_CharlieFabricLambert.shader")]
        private Shader m_PreIntegratedFGD_CharlieFabricLambertPS;

        public Shader preIntegratedFGD_CharlieFabricLambertPS
        {
            get => m_PreIntegratedFGD_CharlieFabricLambertPS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_CharlieFabricLambertPS, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD_Marschner.shader")]
        private Shader m_PreIntegratedFGD_MarschnerPS;

        public Shader preIntegratedFGD_MarschnerPS
        {
            get => m_PreIntegratedFGD_MarschnerPS;
            set => this.SetValueAndNotify(ref m_PreIntegratedFGD_MarschnerPS, value);
        }

    }
}