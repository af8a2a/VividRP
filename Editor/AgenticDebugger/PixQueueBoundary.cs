using System;
using UnityEngine.Rendering;

namespace VividRP.AgenticDebugger
{
    // Boundaries are outside RenderGraph, so a culled pass cannot remove them.
    // The graph currently uses Background; join Default and Urgent as well for
    // work submitted by camera callbacks/subsystems. Copy uploads remain under
    // Unity's own resource-dependency synchronization.
    internal sealed class PixQueueBoundary : IDisposable
    {
        private static readonly ComputeQueueType[] QueueTypes =
            { ComputeQueueType.Default, ComputeQueueType.Background, ComputeQueueType.Urgent };
        private readonly CommandBuffer compute;
        private readonly string[] begins, ends;

        internal PixQueueBoundary(string session, bool asyncCompute)
        {
            if (!asyncCompute) return;
            compute = new CommandBuffer { name = "VividRP PIX compute boundary" };
            compute.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
            begins = new string[QueueTypes.Length];
            ends = new string[QueueTypes.Length];
            for (int i = 0; i < QueueTypes.Length; ++i)
            {
                begins[i] = "VividRP.AgentCapture/" + session + "/FrameBegin/" + QueueTypes[i];
                ends[i] = "VividRP.AgentCapture/" + session + "/FrameEnd/" + QueueTypes[i];
            }
        }

        internal void Begin(ScriptableRenderContext context, CommandBuffer graphics)
        {
            GraphicsFence start = default;
            if (compute != null)
                start = graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
            context.ExecuteCommandBuffer(graphics);
            graphics.Clear();
            if (compute == null) return;
            for (int i = 0; i < QueueTypes.Length; ++i)
            {
                compute.WaitOnAsyncGraphicsFence(start, SynchronisationStageFlags.AllGPUOperations);
                compute.BeginSample(begins[i]);
                compute.EndSample(begins[i]);
                context.ExecuteCommandBufferAsync(compute, QueueTypes[i]);
                ResetCompute();
            }
        }

        // Queue-tail fences make the later native DIRECT queue fence dominate
        // all captured compute work. A direct fence alone cannot prove this.
        internal void Join(ScriptableRenderContext context, CommandBuffer graphics, bool closingMarkers)
        {
            if (compute == null) return;
            for (int i = 0; i < QueueTypes.Length; ++i)
            {
                if (closingMarkers)
                {
                    compute.BeginSample(ends[i]);
                    compute.EndSample(ends[i]);
                }
                var tail = compute.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                context.ExecuteCommandBufferAsync(compute, QueueTypes[i]);
                ResetCompute();
                graphics.WaitOnAsyncGraphicsFence(tail, SynchronisationStageFlags.AllGPUOperations);
            }
        }

        private void ResetCompute()
        {
            // Unity Clear resets execution flags too; each reused recording
            // must explicitly opt into the async queue again.
            compute.Clear();
            compute.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
        }

        public void Dispose() => compute?.Dispose();
    }
}
