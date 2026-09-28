using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.AgenticDebugger;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); ++checks; }
    sealed class Observer : IVividCaptureObserver
    {
        internal int Begins, Ends, Skips;
        internal VividCaptureOutcome Outcome;
        public void BeginCamera(ScriptableRenderContext context, Camera camera, int frameIndex) { ++Begins; }
        public void EndCamera(ScriptableRenderContext context, Camera camera, int frameIndex, VividCaptureOutcome outcome) { ++Ends; Outcome = outcome; }
        public void CameraSkipped(Camera camera, VividCaptureOutcome outcome) { ++Skips; Outcome = outcome; }
    }
    static void Idle(Camera camera)
    {
        var observer = VividCaptureHooks.Begin(default, camera, 1);
        VividCaptureHooks.End(observer, default, camera, 1, VividCaptureOutcome.Submitted);
    }
    static void Main()
    {
        var camera = new Camera(); var first = new Observer(); var second = new Observer();
        Check(VividCaptureHooks.TryAcquire(first), "acquire observer");
        Check(!VividCaptureHooks.TryAcquire(second), "exclusive observer");
        VividCaptureHooks.Release(second);
        var token = VividCaptureHooks.Begin(default, camera, 3);
        VividCaptureHooks.End(token, default, camera, 3, VividCaptureOutcome.RenderGraphFailed);
        Check(first.Begins == 1 && first.Ends == 1 && first.Outcome == VividCaptureOutcome.RenderGraphFailed, "explicit camera failure");
        VividCaptureHooks.Skip(camera, VividCaptureOutcome.ResourcesUnavailable);
        Check(first.Skips == 1 && first.Outcome == VividCaptureOutcome.ResourcesUnavailable, "resource skip notification");
        VividCaptureHooks.Release(first);
        Check(VividCaptureHooks.TryAcquire(second), "acquire successor");
        VividCaptureHooks.End(token, default, camera, 3, VividCaptureOutcome.Submitted);
        Check(second.Ends == 0 && first.Ends == 1, "stale end cannot close successor");
        VividCaptureHooks.Release(second);
        for (int i = 0; i < 10000; ++i) Idle(camera);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; ++i) Idle(camera);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "idle hook allocates");

        var log = new List<string>(); var context = new ScriptableRenderContext(log);
        using var graphics = new CommandBuffer();
        using var queues = new PixQueueBoundary("abc", true);
        queues.Begin(context, graphics);
        Check(log[0] == "graphics/signal:1", "graphics start signal submitted first");
        foreach (var name in new[] { "Default", "Background", "Urgent" })
            Check(log.Contains(name + "/wait:1") && log.Contains(name + "/marker:VividRP.AgentCapture/abc/FrameBegin/" + name), "compute scope waits for graphics start");
        log.Clear(); queues.Join(context, graphics, true); context.ExecuteCommandBuffer(graphics); graphics.Clear();
        Check(log.Count == 9, "three tails and three graphics waits");
        Check(log[0] == "Default/marker:VividRP.AgentCapture/abc/FrameEnd/Default" && log[1] == "Default/signal:2", "end marker before queue tail");
        Check(log[6] == "graphics/wait:2" && log[7] == "graphics/wait:3" && log[8] == "graphics/wait:4", "graphics joins all compute tails");
        log.Clear(); queues.Join(context, graphics, false); context.ExecuteCommandBuffer(graphics); graphics.Clear();
        Check(log.Count == 6 && !log.Exists(x => x.Contains("marker:")), "preparation drains without capture markers");
        log.Clear(); using var noAsync = new PixQueueBoundary("abc", false);
        noAsync.Begin(context, graphics); noAsync.Join(context, graphics, true);
        Check(log.Count == 0 && graphics.Ops.Count == 0, "no compute commands without async support");

        var result = GpuDebuggerBridge.Execute();
        Check((bool)result["pending"] && !(bool)result["ready"] && (string)result["backend"] == "pix", "pending is not ready");
        result = GpuDebuggerBridge.Execute("frame_debugger", "events", "pix-owner");
        Check((string)result["code"] == "session_mismatch" && FrameDebuggerBridge.LastAction == "status", "cross-backend token rejected before action");
        result = GpuDebuggerBridge.Execute("frame_debugger", "events", "fd-owner");
        Check((bool)result["ready"] && FrameDebuggerBridge.LastAction == "events", "dispatch valid owner");
        Check((string)GpuDebuggerBridge.Execute("pix", "release")["code"] == "session_mismatch", "release requires owner");
        Check((string)GpuDebuggerBridge.Execute("pix", "status", "old")["code"] == "session_mismatch", "status rejects explicit stale owner");
        Check((string)GpuDebuggerBridge.Execute("other")["code"] == "invalid_backend", "reject unknown backend");
        FrameDebuggerBridge.Result["state"] = "capturing";
        result = GpuDebuggerBridge.Execute("frame_debugger");
        Check((bool)result["pending"] && !(bool)result["success"] && (string)result["code"] == "pending", "normalize Frame Debugger pending result");
        FrameDebuggerBridge.Result.Remove("state"); FrameDebuggerBridge.Result["code"] = "pending";
        result = GpuDebuggerBridge.Execute("frame_debugger", "event", "fd-owner");
        Check((bool)result["pending"] && !(bool)result["ready"], "Frame Debugger selected-event pending result");
        Console.WriteLine(checks + " M1 hook/queue/router checks passed; idle hooks: " + allocated + " B / 100000 calls (standalone .NET, Unity calls simulated).");
    }
}
