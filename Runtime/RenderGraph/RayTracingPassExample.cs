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
    public class BuildAccelerationStructurePass : ComputePass
    {
        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Create()
        {
        }

        public override void Record(ComputeGraphContext context)
        {
        }
    }
}

