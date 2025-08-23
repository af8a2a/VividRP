using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [VolumeComponentMenu("Post-processing Custom/CMAA2")]
    [VolumeRequiresRendererFeatures(typeof(CMAA2Feature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class CMAA2Volume : VolumeComponent
    {
        public CMAA2Volume()
        {
            displayName = "CMAA2";
        }

        public BoolParameter enabled = new BoolParameter(false);
    }
}