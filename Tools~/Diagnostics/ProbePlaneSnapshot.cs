// Read-only snapshot of the active Cornell probe plane histories. No assets are saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    var hook = type.GetField("EditorTraceCapture", stat);
    if (hook.GetValue(null) != null) throw new InvalidOperationException("Another GI capture is active.");
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    string output = "E:/VividRP_GI_Evidence/ProbePlaneSnapshot-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var records = new List<object>();
    var errors = new List<string>();
    int pending = 0;
    bool captured = false;
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    Action<ComputePassContext, Texture, Texture, Texture> capture = (context, sources, lighting, geometry) => {
        if (captured || context.Get<VividCameraData>().camera != camera) return;
        var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
        var pass = passes.Cast<object>().Single(p => p.GetType() == type);
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        Texture Resolve(string name) {
            var value = type.GetField(name, inst).GetValue(pass);
            var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
            return handle.IsValid() ? (Texture)handle : null;
        }
        foreach (var entry in new[] { ("m_Geometry", "geometry"), ("m_Probes", "anchor"),
            ("m_CurrentAnchorStatistics", "statistics"), ("m_HitLighting", "raw"),
            ("m_FilteredProbes", "sh"), ("m_Output", "final") }) {
            var texture = Resolve(entry.Item1);
            if (texture == null) throw new InvalidOperationException("Missing snapshot texture: " + entry.Item1);
            string path = output + "/" + entry.Item2 + ".bin";
            records.Add(new { tag = entry.Item2, texture.width, texture.height, format = texture.graphicsFormat.ToString(), path });
            pending++;
            cmd.RequestAsyncReadback(texture, 0, request => {
                try { if (request.hasError) errors.Add("Readback: " + path);
                    else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray()); }
                catch (Exception e) { errors.Add(e.Message); }
                finally { pending--; }
            });
        }
        records.Add(new { state = new { order = type.GetField("m_RadiosityPublishedOrder", inst).GetValue(pass),
            batch = type.GetField("m_RadiosityBatch", inst).GetValue(pass), iterations = type.GetField("m_AtrousIterations", inst).GetValue(pass) } });
        captured = true;
    };
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    hook.SetValue(null, capture);
    try {
        double deadline = EditorApplication.timeSinceStartup + 60;
        while ((!captured || pending > 0) && EditorApplication.timeSinceStartup < deadline) {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        if (!captured || pending != 0 || errors.Count != 0) throw new InvalidOperationException("Incomplete probe snapshot: " + string.Join("; ", errors));
        System.IO.File.WriteAllText(output + "/snapshot.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { records, errors }, Newtonsoft.Json.Formatting.Indented));
        return new { output, captured, errors };
    }
    finally { hook.SetValue(null, null); }
}
