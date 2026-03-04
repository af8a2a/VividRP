using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Example compute pass that builds a ray tracing acceleration structure.
    /// Demonstrates [RenderGraphResource] attribute usage for automatic resource setup.
    /// </summary>
    public class BuildAccelerationStructurePass : IComputePass
    {
        [RenderGraphResource(Access = AccessFlags.Write)]
        public RenderGraphAccelerationStructureDesc AccelerationStructure =
            RenderGraphAccelerationStructureDesc.Create("SceneAccelerationStructure");

        public void Prepare(ContextContainer frameData)
        {
        }

        public void Record(PassRecordContext context)
        {
            // Access the resolved handle via field name
            var rtas = context.GetAccelerationStructure(nameof(AccelerationStructure));
        }
    }

    /// <summary>
    /// Example compute pass that performs ray tracing using an acceleration structure.
    /// Demonstrates multiple resource types with different access flags.
    /// </summary>
    public class RayTracingPass : IComputePass
    {
        [RenderGraphResource(Access = AccessFlags.Write)]
        public RenderGraphTextureDesc OutputTexture;

        [RenderGraphResource(Access = AccessFlags.Read)]
        public RenderGraphAccelerationStructureDesc SceneAccelStruct =
            RenderGraphAccelerationStructureDesc.Create("SceneAccelerationStructure");

        private ComputeShader m_RayTracingShader;

        public RayTracingPass(ComputeShader rayTracingShader)
        {
            m_RayTracingShader = rayTracingShader;
            OutputTexture = RenderGraphTextureDesc.CreateColorTarget(
                1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
            OutputTexture.EnableRandomWrite = true;
        }

        public void Prepare(ContextContainer frameData)
        {
        }

        public void Record(PassRecordContext context)
        {
            var output = context.GetTexture(nameof(OutputTexture));
            var rtas = context.GetAccelerationStructure(nameof(SceneAccelStruct));
        }
    }

    /// <summary>
    /// Example raster pass that renders geometry into a color + depth target.
    /// Demonstrates raster-specific attachment binding via AttachmentIndex and IsDepthAttachment.
    /// </summary>
    public class ExampleOpaquePass : IRasterPass
    {
        [RenderGraphResource(Access = AccessFlags.Write, AttachmentIndex = 0)]
        public RenderGraphTextureDesc ColorTarget =
            RenderGraphTextureDesc.CreateColorTarget(1920, 1080);

        [RenderGraphResource(Access = AccessFlags.Write, IsDepthAttachment = true)]
        public RenderGraphTextureDesc DepthTarget =
            RenderGraphTextureDesc.CreateDepthTarget(1920, 1080);

        [RenderGraphResource(Access = AccessFlags.Read)]
        public RenderGraphBufferDesc PerObjectData =
            RenderGraphBufferDesc.CreateStructured(1024, 64);

        public void Prepare(ContextContainer frameData)
        {
        }

        public void Record(PassRecordContext context)
        {
            var color = context.GetTexture(nameof(ColorTarget));
            var depth = context.GetTexture(nameof(DepthTarget));
            var buffer = context.GetBuffer(nameof(PerObjectData));
        }
    }
}

