using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]

    public class TonemappingRuntimeShader: IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
    
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/ToneMapping/GranTurismo/Shader/GranTurismoTonemap.shader")]
        private Shader m_GranTurismo;

        public Shader granTurismo
        {
            get => m_GranTurismo;
            set => this.SetValueAndNotify(ref m_GranTurismo, value);
        }
 
        
        [SerializeField, ResourcePath("Runtime/Extension/Postprocessing/ToneMapping/AgX/Shader/AgX.shader")]
        private Shader m_Agx;

        public Shader AgX
        {
            get => m_Agx;
            set => this.SetValueAndNotify(ref m_Agx, value);
        }

    }
}