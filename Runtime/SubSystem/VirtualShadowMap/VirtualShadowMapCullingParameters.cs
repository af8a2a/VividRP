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
        private static readonly int s_ActiveViewsId = Shader.PropertyToID("_VSMActiveViews");
        private static readonly int s_ActiveViewsRWId = Shader.PropertyToID("_VSMActiveViewsRW");
        private static readonly int s_ActiveViewOffsetId = Shader.PropertyToID("_VSMActiveViewOffset");
        private static readonly int s_InstanceDispatchArgsRWId = Shader.PropertyToID("_VSMInstanceDispatchArgsRW");
        private static readonly int s_SourceInstanceCountId = Shader.PropertyToID("_VSMSourceInstanceCount");
        private readonly GraphicsBuffer m_Projections, m_Hierarchy, m_UncachedBounds;
        private readonly GraphicsBuffer m_ActiveViews;
        internal readonly GraphicsBuffer InstanceDispatchArgs;
        private readonly Vector4 m_Parameters;
        private readonly int m_ReceiverMaskEnabled;

        internal bool IsEnabled => m_Hierarchy != null;
        internal bool UsesCompactedViews => IsEnabled && m_ActiveViews != null && InstanceDispatchArgs != null;
        internal uint InstanceDispatchArgsByteOffset => (uint)m_Parameters.w * 3u * sizeof(uint);
        private int ActiveViewOffset => (int)m_Parameters.w * (m_UncachedBounds.count / 2 + 1);

        internal VirtualShadowMapCullingParameters(GraphicsBuffer projections, GraphicsBuffer hierarchy,
            GraphicsBuffer uncachedBounds, int pagesPerAxis, int pageSize, int resolution,
            int casterLayer, bool receiverMaskEnabled, GraphicsBuffer activeViews = null, GraphicsBuffer instanceDispatchArgs = null)
        {
            m_ActiveViews = activeViews;
            InstanceDispatchArgs = instanceDispatchArgs;
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
            VirtualShadowMapPrototypeRuntime.VirtualResolution, casterLayer, true,
            VirtualShadowMapPrototypeRuntime.ActiveViews, VirtualShadowMapPrototypeRuntime.InstanceDispatchArgs);

        internal void BindViews(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeIntParam(shader, s_ActiveViewOffsetId, ActiveViewOffset);
            cmd.SetComputeBufferParam(shader, kernel, s_ActiveViewsId, m_ActiveViews);
        }

        internal void CompactViews(CommandBuffer cmd, ComputeShader shader, int kernel, int instanceCount,
            int projectionCount, GraphicsBuffer listBuildArgs)
        {
            using var scope = new ProfilingScope(cmd, VSMProfiling.CompactViews);
            cmd.SetComputeVectorParam(shader, s_ParametersId, m_Parameters);
            cmd.SetComputeIntParam(shader, s_ActiveViewOffsetId, ActiveViewOffset);
            cmd.SetComputeIntParam(shader, s_SourceInstanceCountId, instanceCount);
            cmd.SetComputeIntParam(shader, GPUDriven.VividGPUDrivenShaderIDs._CullingContextCount, projectionCount);
            cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedBounds);
            cmd.SetComputeBufferParam(shader, kernel, s_ActiveViewsRWId, m_ActiveViews);
            cmd.SetComputeBufferParam(shader, kernel, s_InstanceDispatchArgsRWId, InstanceDispatchArgs);
            cmd.SetComputeBufferParam(shader, kernel, GPUDriven.VividGPUDrivenShaderIDs._MeshletListBuildIndirectArgs, listBuildArgs);
            cmd.DispatchCompute(shader, kernel, 1, 1, 1);
        }

        internal void Bind(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            if (UsesCompactedViews) BindViews(cmd, shader, kernel);
            cmd.SetComputeVectorParam(shader, s_ParametersId, m_Parameters);
            cmd.SetComputeIntParam(shader, VirtualShadowMapPrototypeRuntime.ReceiverMaskEnabledId, m_ReceiverMaskEnabled);
            cmd.SetComputeBufferParam(shader, kernel, s_ProjectionsId, m_Projections);
            cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.PageCullHierarchyId, m_Hierarchy);
            cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapPrototypeRuntime.UncachedPageRectBoundsId, m_UncachedBounds);
        }
    }
}
