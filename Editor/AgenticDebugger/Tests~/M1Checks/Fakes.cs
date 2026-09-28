using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// These fakes verify production hook/queue scheduling and routing; they do not
// emulate Unity rendering or prove GPU synchronization on Unity's threads.
namespace UnityEngine { public class Camera { } }
namespace UnityEngine.Rendering
{
    public enum ComputeQueueType { Default, Background, Urgent }
    public enum CommandBufferExecutionFlags { None, AsyncCompute }
    public enum GraphicsFenceType { AsyncQueueSynchronisation }
    public enum SynchronisationStageFlags { AllGPUOperations }
    public struct GraphicsFence { public int Id; }
    public class CommandBuffer : IDisposable
    {
        public string name;
        public static int NextFence;
        public readonly List<string> Ops = new();
        public CommandBufferExecutionFlags Flags;
        public void SetExecutionFlags(CommandBufferExecutionFlags flags) => Flags = flags;
        public void BeginSample(string marker) => Ops.Add("marker:" + marker);
        public void EndSample(string marker) { }
        public GraphicsFence CreateGraphicsFence(GraphicsFenceType type, SynchronisationStageFlags stage)
        {
            var fence = new GraphicsFence { Id = ++NextFence }; Ops.Add("signal:" + fence.Id); return fence;
        }
        public void WaitOnAsyncGraphicsFence(GraphicsFence fence, SynchronisationStageFlags stage) => Ops.Add("wait:" + fence.Id);
        public void Clear() => Ops.Clear();
        public void Dispose() { }
    }
    public struct ScriptableRenderContext
    {
        public readonly List<string> Log;
        public ScriptableRenderContext(List<string> log) { Log = log; }
        public void ExecuteCommandBuffer(CommandBuffer commands)
        {
            foreach (var op in commands.Ops) Log.Add("graphics/" + op);
        }
        public void ExecuteCommandBufferAsync(CommandBuffer commands, ComputeQueueType queue)
        {
            if (commands.Flags != CommandBufferExecutionFlags.AsyncCompute) throw new Exception("Wrong queue flags");
            foreach (var op in commands.Ops) Log.Add(queue + "/" + op);
        }
    }
}
namespace VividRP.AgenticDebugger
{
    public static class PixCaptureBridge
    {
        public static JObject Result = new() { ["sessionId"] = "pix-owner", ["state"] = "armed", ["success"] = false };
        public static string LastAction;
        public static JObject Execute(string action = "status", string sessionId = null, string path = null,
            string cameraName = null, string expectedPass = null, float timeoutSeconds = 120,
            string pixInstall = null, string analyzerPath = null, int queueId = -1, int eventIndex = -1,
            int offset = 0, int count = 100, string marker = null, string expectedCaptureHash = null,
            int counterId = -1, int occupancyType = -1, int occupancyStage = -1, string experiment = null,
            string analysisId = null)
        { LastAction = action; return (JObject)Result.DeepClone(); }
    }
    public static class FrameDebuggerBridge
    {
        public static JObject Result = new() { ["sessionId"] = "fd-owner", ["state"] = "ready", ["success"] = true };
        public static string LastAction;
        public static JObject Execute(string action = "status", int eventIndex = -1, int offset = 0, int count = 100,
            string expectedEventsHash = null, string sessionId = null, string path = null, float timeoutSeconds = 60,
            bool includeShaderProperties = false, string source = "game_view", string cameraName = null)
        { LastAction = action; return (JObject)Result.DeepClone(); }
    }
}
