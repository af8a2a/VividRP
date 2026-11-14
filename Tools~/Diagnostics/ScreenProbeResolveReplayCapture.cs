// Isolate interpolation variance with immutable real SH fields. No assets saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const string root = "E:/VividRP_GI_Evidence/ScreenProbeResolveReplay-20261114-112421-783829";
    if (System.IO.Directory.EnumerateFiles(root, "????-*.bin").Any())
        throw new InvalidOperationException("Prepare a fresh input directory and update root; refusing to overwrite existing replay evidence.");
    const int measuredFrames = 64;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var type = typeof(SdfDiffuseGatherPass);
    var hook = type.GetField("EditorTraceCapture", stat);
    var stageHook = type.GetField("EditorGatherStageCapture", stat);
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (type.GetField(name, stat).GetValue(null) != null) throw new InvalidOperationException("Another capture is active.");
    if ((int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null) != -1
        || (int)type.GetField("EditorGatherHistoryDiagnostics", stat).GetValue(null) != 0)
        throw new InvalidOperationException("A diagnostic override is active.");
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Clean Cornell fixture required.");
    var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0, 1.5f, -3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4 || Mathf.Abs(camera.fieldOfView - 42) > 1e-5)
        throw new InvalidOperationException("Reference camera pose required.");
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    var passesField = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    Texture2D Load(string name) {
        var texture = new Texture2D(840, 136, TextureFormat.RGBAHalf, false, true) { name = name, hideFlags = HideFlags.HideAndDontSave };
        texture.LoadRawTextureData(System.IO.File.ReadAllBytes(root + "/" + name + ".bin")); texture.Apply(false, true);
        return texture;
    }
    var mean = Load("mean-sh"); var last = Load("last-sh");
    string[] arms = { "dynamic-jitter", "last-jitter", "mean-jitter", "mean-fixed", "last-fixed", "mean-blue", "last-blue" };
    var stbn = PipelineResourceManager.Get<BlueNoiseResources>().VSMSTBNVec2;
    if (stbn == null || stbn.width != 128 || stbn.height != 8192) throw new InvalidOperationException("Full STBN vec2 atlas required.");
    var outputs = new RenderTexture[arms.Length];
    for (int i = 0; i < outputs.Length; ++i) {
        var descriptor = new RenderTextureDescriptor(1920, 1080) { graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
            depthBufferBits = 0, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false, msaaSamples = 1 };
        outputs[i] = new RenderTexture(descriptor) { name = "Resolve replay " + arms[i], hideFlags = HideFlags.HideAndDontSave };
        if (!outputs[i].Create()) throw new InvalidOperationException("Replay output allocation failed.");
    }
    int frame = 0, readyFrames = 0, pending = 0;
    bool complete = false;
    var records = new List<object>(); var errors = new List<string>();
    int inputId = Shader.PropertyToID("_SdfDiffuseProbeInput"), outputId = Shader.PropertyToID("_SdfDiffuseOutput");
    int frameId = Shader.PropertyToID("_SdfDiffuseFrame"), jitterId = Shader.PropertyToID("_SdfScreenProbeIntegrationJitter");
    int blueId = Shader.PropertyToID("_SdfScreenProbeBlueNoiseInterpolation"), blueTextureId = Shader.PropertyToID("_SdfScreenProbeStbnVec2");
    void Read(CommandBuffer cmd, Texture texture, string tag, int x, int y, int width, int height) {
        string path = root + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        records.Add(new { frame, tag, x, y, width, height, format = texture.graphicsFormat.ToString(), path });
        pending++;
        cmd.RequestAsyncReadback(texture, 0, x, width, y, height, 0, 1, request => {
            try {
                if (request.hasError) throw new InvalidOperationException("Readback failed: " + path);
                var bytes = request.GetData<byte>().ToArray(); System.IO.File.WriteAllBytes(path, bytes);
                if (tag == "geometry" && !bytes.SequenceEqual(System.IO.File.ReadAllBytes(root + "/expected-geometry.bin")))
                    throw new InvalidOperationException("Frozen SH receiver geometry does not match.");
                if (tag == "radiosity" && !bytes.SequenceEqual(System.IO.File.ReadAllBytes(root + "/expected-radiosity.bin")))
                    throw new InvalidOperationException("Frozen SH source cache does not match.");
            } catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }
    void Pixels(CommandBuffer cmd, Texture texture, string tag) {
        foreach (var r in new[] { ("left", 350, 450, 160, 240), ("back", 1100, 600, 160, 120),
            ("right", 1410, 450, 160, 240), ("ceiling", 912, 970, 96, 48), ("floor", 912, 130, 96, 64) })
            Read(cmd, texture, tag + "-" + r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
    }
    void Log(string message, string stack, LogType kind) {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message);
    }
    Action<ComputePassContext, int, Texture> stage = (context, index, texture) => {
        // RawResolved becomes an Atrous ping-pong target later in this pass.
        // Capture its actual Resolve lifetime before temporal/wavelet writes.
        if (index == 0 && readyFrames >= 8 && frame < measuredFrames && context.Get<VividCameraData>().camera == camera)
            Pixels((CommandBuffer)wrapped.GetValue(context.cmd), texture, "original");
    };
    Action<ComputePassContext, Texture, Texture, Texture> capture = (context, sources, lighting, geometry) => {
        if (frame >= measuredFrames || context.Get<VividCameraData>().camera != camera) return;
        var pass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        object Field(string name) => type.GetField(name, inst).GetValue(pass);
        Texture Resolve(string name) {
            var value = Field(name); var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
            return handle.IsValid() ? (Texture)handle : null;
        }
        bool ready = (int)Field("m_RadiosityPublishedOrder") == 8 && (int)Field("m_RadiosityBatch") == 32
            && (int)Field("m_RadiosityUpdateCards") == 0;
        readyFrames = ready ? readyFrames + 1 : 0;
        if (readyFrames <= 8) return;
        if ((bool)Field("m_RestirGather") || !(bool)Field("m_AnchorPreFilter") || ((Vector4)Field("m_NearOcclusion")).z != 0)
            throw new InvalidOperationException("Expected current native Cornell with short-range occlusion disabled.");
        if ((bool)Field("m_BlueNoiseInterpolation")) throw new InvalidOperationException("White-noise baseline required for replay qualification.");
        var state = Field("m_State");
        if (!(bool)state.GetType().GetField("PixelTemporal", inst).GetValue(state))
            throw new InvalidOperationException("Expected jittered normal Resolve.");
        int originalFrame = unchecked((int)((uint)state.GetType().GetField("Frame", inst).GetValue(state) - 1u));
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        var shader = (ComputeShader)Field("m_GatherCompute"); int kernel = shader.FindKernel("ScreenProbeIntegrateCS");
        var actualInput = Resolve("m_FilteredProbes"); var actualOutput = Resolve("m_RawResolved");
        if (frame == 0 || frame == measuredFrames - 1) {
            Read(cmd, geometry, "geometry", 0, 0, geometry.width, geometry.height);
            var source = Resolve("m_RadiosityInput"); Read(cmd, source, "radiosity", 0, 0, source.width, source.height);
        }
        for (int i = 0; i < arms.Length; ++i) {
            Texture input = i == 0 ? actualInput : arms[i].StartsWith("last", StringComparison.Ordinal) ? last : mean;
            cmd.SetComputeTextureParam(shader, kernel, inputId, input);
            cmd.SetComputeTextureParam(shader, kernel, outputId, outputs[i]);
            cmd.SetComputeIntParam(shader, frameId, originalFrame);
            cmd.SetComputeIntParam(shader, jitterId, arms[i].EndsWith("fixed", StringComparison.Ordinal) ? 0 : 1);
            cmd.SetComputeIntParam(shader, blueId, arms[i].EndsWith("blue", StringComparison.Ordinal) ? 1 : 0);
            cmd.SetComputeTextureParam(shader, kernel, blueTextureId, stbn);
            cmd.DispatchCompute(shader, kernel, 240, 135, 1);
            Pixels(cmd, outputs[i], arms[i]);
        }
        cmd.SetComputeTextureParam(shader, kernel, inputId, actualInput);
        cmd.SetComputeTextureParam(shader, kernel, outputId, actualOutput);
        cmd.SetComputeIntParam(shader, frameId, originalFrame); cmd.SetComputeIntParam(shader, jitterId, 1);
        cmd.SetComputeIntParam(shader, blueId, 0); cmd.SetComputeTextureParam(shader, kernel, blueTextureId, Texture2D.blackTexture);
        var occlusion = (Vector4)Field("m_NearOcclusion");
        records.Add(new { frame, actualFrame = originalFrame, nearOcclusion = new[] { occlusion.x, occlusion.y, occlusion.z, occlusion.w },
            preFilter = Field("m_AnchorPreFilter"), order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch") });
        frame++;
    };
    EditorApplication.LockReloadAssemblies();
    hook.SetValue(null, capture); stageHook.SetValue(null, stage); Application.logMessageReceived += Log;
    try {
        double deadline = EditorApplication.timeSinceStartup + 240;
        while (frame < measuredFrames && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline) {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; ++i) await System.Threading.Tasks.Task.Delay(20);
        if (frame != measuredFrames || pending != 0 || errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
        complete = true; return new { root, complete, measuredFrames, errors };
    }
    finally { try {
        hook.SetValue(null, null); stageHook.SetValue(null, null); Application.logMessageReceived -= Log;
        AsyncGPUReadback.WaitAllRequests();
        foreach (var output in outputs) { output.Release(); UnityEngine.Object.DestroyImmediate(output); }
        UnityEngine.Object.DestroyImmediate(mean); UnityEngine.Object.DestroyImmediate(last);
        System.IO.File.WriteAllText(root + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            schema = "screen-probe-resolve-replay.v1", complete, measuredFrames, width = 1920, height = 1080,
            spacing = 16, rays = 8, arms, records, errors, scope = "Frozen actual SH interpolation isolation; no quality or performance claim" }, Newtonsoft.Json.Formatting.Indented));
    } finally { EditorApplication.UnlockReloadAssemblies(); } }
}
