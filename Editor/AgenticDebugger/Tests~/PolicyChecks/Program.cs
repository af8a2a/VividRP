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
    private sealed class RasterState { public string m_CullMode = "Back"; }
    private sealed class ShaderInfo { public string[] m_Keywords = { "TEST" }; }
    private sealed class EventData
    {
        public int m_FrameEventIndex = 17;
        public string m_ComputeShaderName = "Probe";
        public int m_ComputeShaderThreadGroupsX = 8;
        public string m_RayTracingShaderName = "";
        public object m_RenderTargetRenderTexture = null;
        public string m_RenderTargetName = "Color";
        public int m_RenderTargetWidth = 128;
        public int m_VertexCount = 3;
        public RasterState m_RasterState = new RasterState();
        public string m_BlendState = "One Zero";
        public string m_DepthState = "LessEqual";
        public string m_StencilState = "Keep";
        public ShaderInfo m_ShaderInfo = new ShaderInfo();
    }

    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("adapter DLL, UnityEngine DLL directory, Newtonsoft.Json DLL required");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = name.Name == "Newtonsoft.Json" ? args[2] : Path.Combine(args[1], name.Name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(args[1], "..", name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        var adapter = assembly.GetType("VividRP.AgenticDebugger.FrameDebuggerApi", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var describeValue = adapter.GetMethod("Describe", flags);
        int checks = 0;
        foreach (Kind kind in Enum.GetValues<Kind>())
        {
            var result = describeValue.Invoke(null, new object[] { new Event { m_Type = kind } });
            using var json = JsonDocument.Parse(result.ToString());
            if (json.RootElement.GetProperty("m_Type").GetString() != kind.ToString())
                throw new Exception("Missing event type: " + kind);
            checks++;
        }
        var describe = adapter.GetMethod("DescribeEvent", flags);
        foreach (bool includeProperties in new[] { false, true })
        {
            var result = describe.Invoke(null, new object[] { new EventData(), includeProperties });
            using var json = JsonDocument.Parse(result.ToString());
            var root = json.RootElement;
            if (root.GetProperty("m_FrameEventIndex").GetInt32() != 17
                || root.GetProperty("m_ComputeShaderThreadGroupsX").GetInt32() != 8
                || root.GetProperty("m_RenderTargetRenderTexture").ValueKind != JsonValueKind.Null
                || root.GetProperty("m_RenderTargetName").GetString() != "Color"
                || root.GetProperty("m_RenderTargetWidth").GetInt32() != 128
                || root.GetProperty("m_VertexCount").GetInt32() != 3
                || root.GetProperty("m_RasterState").GetProperty("m_CullMode").GetString() != "Back"
                || root.GetProperty("m_BlendState").GetString() != "One Zero"
                || root.GetProperty("m_DepthState").GetString() != "LessEqual"
                || root.GetProperty("m_StencilState").GetString() != "Keep"
                || root.TryGetProperty("m_ShaderInfo", out _) != includeProperties)
                throw new Exception("Incorrect event serialization");
            if (includeProperties && root.GetProperty("m_ShaderInfo").GetProperty("m_Keywords")[0].GetString() != "TEST")
                throw new Exception("Missing shader properties");
            checks++;
        }
        var bridge = assembly.GetType("VividRP.AgenticDebugger.FrameDebuggerBridge", true);
        var outputPath = bridge.GetMethod("OutputPath", flags);
        string directory = Path.Combine(Path.GetTempPath(), "FrameDebuggerPolicy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string extension in new[] { ".png", ".exr" })
            {
                string path = Path.Combine(directory, "target" + extension);
                if ((string)outputPath.Invoke(null, new object[] { path, extension }) != path)
                    throw new Exception("Incorrect output path");
                checks++;
                File.WriteAllText(path, "preserve existing output");
                Reject<IOException>(() => outputPath.Invoke(null, new object[] { path, extension }));
                if (File.ReadAllText(path) != "preserve existing output") throw new Exception("Output overwritten");
                checks++;
            }
            Reject<ArgumentException>(() => outputPath.Invoke(null, new object[] { Path.Combine(directory, "target.jpg"), ".png" }));
            checks++;
            Reject<ArgumentException>(() => outputPath.Invoke(null, new object[] { "", ".png" }));
            checks++;
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("Passed " + checks + " offline policy/serialization checks; no Unity native calls.");
        return 0;
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (TargetInvocationException exception) when (exception.InnerException is T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
