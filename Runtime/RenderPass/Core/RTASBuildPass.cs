using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class RTASBuildPass : ComputePass, IAsyncComputeSupportedPass
    {
        [RenderGraphResource(
            Name = "SceneRTAS",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        private bool m_SupportsRayTracing;

        public RTASBuildPass()
        {
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            if (m_SupportsRayTracing)
                m_SceneAccelerationStructure?.EnsureCreated();
        }

        public override void Prepare(ContextContainer frameData)
        {
            if (!m_SupportsRayTracing)
                return;

            m_SceneAccelerationStructure?.EnsureCreated();
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_SupportsRayTracing || m_SceneAccelerationStructure == null)
                return;

            context.cmd.BuildRayTracingAccelerationStructure(m_SceneAccelerationStructure);
        }

        public override void Dispose()
        {
            m_SceneAccelerationStructure?.Dispose();
        }

        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = new RenderGraphAccelerationStructureDesc
                {
                    Name = "SceneRTAS",
                    ManagementMode = RayTracingAccelerationStructure.ManagementMode.Automatic,
                    RayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    LayerMask = ~0,
                }
            };
        }
    }
}
