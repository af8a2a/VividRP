using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Utility.PipelineResource
{
    //can provide PipelineResourceManager.Get<VividRPCoreResources>().BlitShader  accessor
    [PipelineResource] 
    public class VividRPCoreResources
    {
        [ResourcePath("Shaders/Blit")]
        public Shader BlitShader;

        [ResourcePath("Shaders/CoreBlit")]
        public Shader CoreBlitShader;

        [ResourcePath("Shaders/CoreBlitColorAndDepth")]
        public Shader CoreBlitColorAndDepthShader;

        [ResourcePath("Shaders/FullScreenUV")]
        public Shader FullScreenUVShader;
    }
}
