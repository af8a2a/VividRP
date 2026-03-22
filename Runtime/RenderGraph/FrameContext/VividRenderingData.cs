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

        public override void Reset()
        {
            visibleMeshletRenderRequestsBuffer = null;
            visibleMeshletIndirectDrawArgsBuffer = null;
        }
    }
}
