using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    static partial class PassRecorder
    {
        private static List<IRenderPass> _renderPasses = new List<IRenderPass>();
        private static ContextContainer m_frameData = new();
        private static Dictionary<IRenderPass,PassResource>  m_passResources = new();

        static bool complied = false;

        static void Compile()
        {
            //todo:
            //collect from asset
            BuildAccelerationStructurePass accelerationStructurePass = new BuildAccelerationStructurePass();
            _renderPasses.Add(accelerationStructurePass);

            foreach (var pass in _renderPasses)
            {
                m_passResources[pass] = pass.Initialize();
            }
            complied = true;
        }

        public static void RecordRenderGraph(RenderGraph renderGraph, ScriptableRenderContext context)
        {
            
            if (!complied)
            {
                Compile();
            }

            foreach (var pass in _renderPasses)
            {
                pass.Prepare(m_frameData);
            }

            foreach (var pass in _renderPasses)
            {
                if (pass is ComputePass computePass)
                {
                    RecordComputePass(renderGraph, computePass, m_passResources[pass]);
                }else if (pass is RasterPass rasterPass)
                {
                    RecordRasterPass(renderGraph, rasterPass, m_passResources[pass]);
                }
                else if(pass is UnsafePass unsafePass)
                {
                    RecordUnsafePass(renderGraph, unsafePass, m_passResources[pass]);
                }
            }
        }
        
    }
}