using System.ComponentModel;

namespace UnityEngine.Rendering.Universal
{
    
    [VolumeComponentMenu("Post-processing Custom/AgX Tonemapping")]
    [VolumeRequiresRendererFeatures(typeof(AgXFeature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]

    public class AgXSetting : VolumeComponent
    {
        public BoolParameter approx = new BoolParameter(false);
        public BoolParameter enable = new BoolParameter(false);


        public AgXSetting()
        {
            displayName = "AgX Tonemapping";
        }
    }
}