using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividRenderingData : ContextItem
    {
        public CullingResults cullingResults;
        public ScriptableRenderContext context;

        public override void Reset()
        {
        }
    }

    public sealed class VividGPUDrivenFrameData : ContextItem
    {
        public GraphicsBuffer visibleMeshletRenderRequestsBuffer;
        public GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer;
        public bool requiresDualSlabSidecar;
        public bool occlusionCullingEnabled;
        public bool occlusionHistoryValid;
        public bool occlusionObservationMode;
        public GraphicsBuffer occludedMeshletRenderRequestsBuffer;
        public GraphicsBuffer occludedMeshletRenderRequestCounterBuffer;
        public GraphicsBuffer occludedMeshletIndirectDispatchArgsBuffer;
        public GraphicsBuffer recoveredMeshletRenderRequestsBuffer;
        public GraphicsBuffer recoveredRendererListMeshletCountsBuffer;
        public GraphicsBuffer recoveredMeshletIndirectDrawArgsBuffer;
        internal PrimitiveScene.VividPrimitiveDrawSet primitiveDrawSet;
        internal PrimitiveScene.VividPrimitiveDrawSet primitiveShadowDrawSet;
        internal GPUDriven.VividGPUDrivenOcclusionCullingParameters observationRetestParameters;

        public override void Reset()
        {
            visibleMeshletRenderRequestsBuffer = null;
            visibleMeshletIndirectDrawArgsBuffer = null;
            requiresDualSlabSidecar = false;
            primitiveDrawSet = null;
            primitiveShadowDrawSet = null;
            ResetOcclusion();
        }

        internal void ResetOcclusion()
        {
            occlusionCullingEnabled = false;
            occlusionHistoryValid = false;
            occlusionObservationMode = false;
            occludedMeshletRenderRequestsBuffer = null;
            occludedMeshletRenderRequestCounterBuffer = null;
            occludedMeshletIndirectDispatchArgsBuffer = null;
            recoveredMeshletRenderRequestsBuffer = null;
            recoveredRendererListMeshletCountsBuffer = null;
            recoveredMeshletIndirectDrawArgsBuffer = null;
            observationRetestParameters = default;
        }
    }

    public sealed class VividGPUDrivenDecalData : ContextItem
    {
        public bool isEnabled;

        public override void Reset()
        {
            isEnabled = false;
        }
    }
}
