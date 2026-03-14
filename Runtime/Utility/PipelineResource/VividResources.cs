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

        [ResourcePath("Texture/Default/DefaultHDRISky.exr")]
        public Cubemap DefaultHDRISkyCubemap;

        
        [ResourcePath("Shaders/Core/Private/CopyDepth")]
        public Shader CopyDepthShader;

        [ResourcePath("Shaders/Core/Private/CameraMotionVectors")]
        public Shader CameraMotionVectorsShader;

        [ResourcePath("Shaders/Core/Private/ObjectMotionVectorFallback")]
        public Shader ObjectMotionVectorFallbackShader;

        [ResourcePath("Shaders/Material/MaterialClassification")]
        public ComputeShader MaterialClassificationCompute;

        [ResourcePath("Shaders/Material/DeferredDirectionalLighting")]
        public ComputeShader DeferredDirectionalLightingCompute;

        [ResourcePath("Shaders/Material/DeferredDirectionalLightingIndirect")]
        public Shader DeferredDirectionalLightingIndirectShader;
    }
}
