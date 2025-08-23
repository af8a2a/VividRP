using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [VolumeComponentMenu("Post-processing Custom/CMAA2")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class CMAA2 : VolumeComponent
    {
        public CMAA2()
        {
            displayName = "CMAA2";
        }

        public BoolParameter enabled = new BoolParameter(false);
    }
}