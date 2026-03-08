using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    //can provide PipelineResourceManager.Get<VividRPCoreResources>().BlitShader  accessor
    [PipelineResource] 
    public class VividRPCoreResources
    {
        [ResourcePath("Shaders/Core/Blit")]
        public Shader BlitShader;

        [ResourcePath("Shaders/Core/CoreBlit")]
        public Shader CoreBlitShader;

        [ResourcePath("Shaders/Core/CoreBlitColorAndDepth")]
        public Shader CoreBlitColorAndDepthShader;

        [ResourcePath("Shaders/FullScreenUV")]
        public Shader FullScreenUVShader;

        [ResourcePath("Shaders/Material/MaterialClassification")]
        public ComputeShader MaterialClassificationCompute;
    }
}
