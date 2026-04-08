using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class DDGIRTASBuildPass : ComputePass, IAsyncComputeSupportedPass
    {
        [RenderGraphResource(
            Name = "DDGIRTAS",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphAccelerationStructure m_DDGIAccelerationStructure;

        private bool m_ShouldBuild;

        public DDGIRTASBuildPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DDGIRTASBuildPass));
            m_DDGIAccelerationStructure = new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("DDGIRTAS")
            };
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            DDGIRuntimeData runtimeData = frameData.GetOrCreate<DDGIRuntimeData>();
            RayTracingAccelerationStructure accelerationStructure = runtimeData.isRuntimeReady
                ? DDGISystem.instance.AccelerationStructure
                : null;

            m_DDGIAccelerationStructure.SetAccelerationStructure(accelerationStructure, transferOwnership: false);
            m_ShouldBuild = runtimeData.isRuntimeReady && accelerationStructure != null;
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_ShouldBuild || m_DDGIAccelerationStructure == null)
            {
                return;
            }

            context.cmd.BuildRayTracingAccelerationStructure(m_DDGIAccelerationStructure);
        }

        public override void Dispose()
        {
            m_DDGIAccelerationStructure?.SetAccelerationStructure(null, transferOwnership: false);
            m_DDGIAccelerationStructure?.Dispose();
            m_DDGIAccelerationStructure = null;
            m_ShouldBuild = false;
        }
    }
}
