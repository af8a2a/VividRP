using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [PipelineResource] 
    public class PostProcessingShader
    {
        [ResourcePath("Shaders/Core/Private/LutBuilder3D.compute")]
        public ComputeShader colorGradingShader;
    }
}