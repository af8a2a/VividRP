using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    // An explicit, frame-local view of the post-budget VSM hierarchy. Default
    // selects ordinary culling; it never consults the last camera's VSM globals.
    internal readonly struct VirtualShadowMapCullingParameters
    {
        private static readonly int s_ParametersId = Shader.PropertyToID("_VSMCasterCullingParameters");
        private static readonly int s_ProjectionsId = Shader.PropertyToID("_VSMProjections");
        private readonly GraphicsBuffer m_Projections, m_Hierarchy, m_UncachedBounds;
        private readonly Vector4 m_Parameters;
        private readonly int m_ReceiverMaskEnabled;

        internal bool IsEnabled => m_Hierarchy != null;

        internal VirtualShadowMapCullingParameters(GraphicsBuffer projections, GraphicsBuffer hierarchy,
            GraphicsBuffer uncachedBounds, int pagesPerAxis, int pageSize, int resolution,
            int casterLayer, bool receiverMaskEnabled)
        {
            m_Projections = projections;
            m_Hierarchy = hierarchy;
            m_UncachedBounds = uncachedBounds;
            m_Parameters = new Vector4(pagesPerAxis, pageSize, resolution, casterLayer);
            m_ReceiverMaskEnabled = receiverMaskEnabled ? 1 : 0;
        }

        internal static VirtualShadowMapCullingParameters ForCurrentFrame(int casterLayer) => new(
            VirtualShadowMapPrototypeRuntime.Projections.Buffer,
            VirtualShadowMapPrototypeRuntime.PageCullHierarchy,
            VirtualShadowMapPrototypeRuntime.UncachedPageRectBounds,
            VirtualShadowMapPrototypeRuntime.PagesPerAxis,
            VirtualShadowMapPrototypeRuntime.PageSize,
            VirtualShadowMapPrototypeRuntime.VirtualResolution, casterLayer, true);

        internal void Bind(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeVectorParam(shader, s_ParametersId, m_Parameters);
            cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, m_ReceiverMaskEnabled);
            cmd.SetComputeBufferParam(shader, kernel, s_ProjectionsId, m_Projections);
            cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.PageCullHierarchyId, m_Hierarchy);
            cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedBounds);
        }
    }
}
