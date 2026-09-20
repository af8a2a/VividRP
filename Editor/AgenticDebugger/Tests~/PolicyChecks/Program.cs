using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

// Exercise the compiled adapter's policy/serialization without constructing the
// native adapter or calling Unity. Args: adapter DLL, UnityEngine DLL directory,
// Newtonsoft.Json DLL. No Editor or GPU connection is made.
internal static class Program
{
    private enum Kind { ComputeDispatch, RayTracingDispatch, Mesh, DrawProcedural, SetRenderTarget, BeginSubpass, SRPBatch, Unknown }
    private sealed class Event { public Kind m_Type; }
    private sealed class Poison { public override string ToString() => throw new Exception("Raster field was inspected"); }
    private sealed class Dispatch
    {
        public int m_FrameEventIndex = 17;
        public string m_ComputeShaderName = "Probe";
        public int m_ComputeShaderThreadGroupsX = 8;
        public string m_RayTracingShaderName = "";
        public object m_RenderTargetRenderTexture = new Poison();
        public object m_RasterState = new Poison();
        public string m_ShaderInfo = "properties";
    }

    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("adapter DLL, UnityEngine DLL directory, Newtonsoft.Json DLL required");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = name.Name == "Newtonsoft.Json" ? args[2] : Path.Combine(args[1], name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        };
        var adapter = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]))
            .GetType("VividRP.AgenticDebugger.FrameDebuggerApi", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var inspectable = adapter.GetMethod("Inspectable", flags);
        int checks = 0;
        foreach (Kind kind in Enum.GetValues<Kind>())
        {
            bool actual = (bool)inspectable.Invoke(null, new object[] { new Event { m_Type = kind } });
            bool expected = kind == Kind.ComputeDispatch || kind == Kind.RayTracingDispatch;
            if (actual != expected) throw new Exception("Incorrect inspection policy: " + kind);
            checks++;
        }
        var describe = adapter.GetMethod("DescribeDispatch", flags);
        foreach (bool includeProperties in new[] { false, true })
        {
            var result = describe.Invoke(null, new object[] { new Dispatch(), includeProperties });
            using var json = JsonDocument.Parse(result.ToString());
            var root = json.RootElement;
            if (root.GetProperty("m_FrameEventIndex").GetInt32() != 17
                || root.GetProperty("m_ComputeShaderThreadGroupsX").GetInt32() != 8
                || root.TryGetProperty("m_RenderTargetRenderTexture", out _)
                || root.TryGetProperty("m_RasterState", out _)
                || root.TryGetProperty("m_ShaderInfo", out _) != includeProperties)
                throw new Exception("Incorrect dispatch serialization");
            checks++;
        }
        Console.WriteLine("Passed " + checks + " offline policy/serialization checks; no Unity native calls.");
        return 0;
    }
}
