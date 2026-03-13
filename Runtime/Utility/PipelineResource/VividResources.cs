using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    //can provide PipelineResourceManager.Get<VividRPCoreResources>().BlitShader  accessor
    [PipelineResource] 
    public class VividRPCoreResources
    {
        [ResourcePath("Shaders/Core/Private/Blit")]
        public Shader BlitShader;

        [ResourcePath("Shaders/Core/Private/CoreBlit")]
        public Shader CoreBlitShader;

        [ResourcePath("Shaders/Core/Private/CoreBlitColorAndDepth")]
        public Shader CoreBlitColorAndDepthShader;

        [ResourcePath("Shaders/FullScreenUV")]
        public Shader FullScreenUVShader;

        [ResourcePath("Shaders/Core/Private/HDRISky")]
        public Shader HDRISkyShader;

        [ResourcePath("Shaders/Core/CopyDepth")]
        public Shader CopyDepthShader;

        [ResourcePath("Shaders/Material/MaterialClassification")]
        public ComputeShader MaterialClassificationCompute;
    }
}
