// Independent uniform current-frame scalar grid, same frozen Cornell source.
// No assets saved. Two 1024-frame sequences; excludes temporal scalar accumulation.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const int warmup = 512, count = 1024;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    var hooks = new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride", "EditorGatherHistoryDiagnostics", "EditorRadiositySeed" }
        .ToDictionary(n => n, n => type.GetField(n, stat));
    var previous = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string n in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (type.GetField(n, stat).GetValue(null) != null) throw new InvalidOperationException("Capture already active.");
    if ((int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1) throw new InvalidOperationException("Denoise override active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Clean Cornell fixture required.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0, 1.5f, -3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4f || camera.fieldOfView != 42)
        throw new InvalidOperationException("Reference Cornell pose required.");
    var volume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var c in (previousProfile ?? volume.sharedProfile).components) profile.components.Add(UnityEngine.Object.Instantiate(c));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    var activeScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (c, c.enabled)).ToArray();
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    string output = "E:/VividRP_GI_Evidence/ScalarAnchorGridReference-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var records = new List<object>(); var errors = new List<string>();
    bool active = false, complete = false;
    int frame = 0, ready = 0, phase = 0, pending = 0, frameBase = 0;
    string arm = "";
    void Log(string message, string stack, LogType kind) {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    void Begin(ScriptableRenderContext context, Camera current) {
        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));
    }
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        var pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        object Field(string n) => type.GetField(n, inst).GetValue(pass);
        int order = (int)Field("m_RadiosityPublishedOrder"), batch = (int)Field("m_RadiosityBatch");
        bool frozen = order == 8 && batch == 32 && (int)Field("m_RadiosityUpdateCards") == 0;
        if (phase == 0) { ready = frozen ? ready + 1 : 0; if (ready < 8) return; phase = 1; frame = 0; }
        else if (!frozen) {
            if (frame < warmup) { phase = 0; frame = 0; ready = 0; records.Add(new { arm, warmupRestart = true }); return; }
            throw new InvalidOperationException("Source invalidated during reference measurement.");
        }
        if (frame < warmup) return;
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        Texture Resolve(string n) {
            var value = Field(n); var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
            return handle.IsValid() ? (Texture)handle : null;
        }
        void Read(Texture texture, string tag) {
            if (texture == null) throw new InvalidOperationException("Missing " + tag);
            string path = output + "/" + arm + "/" + frame.ToString("0000") + "-" + tag + ".bin";
            records.Add(new { arm, frame, tag, texture.width, texture.height, format = texture.graphicsFormat.ToString(), path });
            pending++;
            cmd.RequestAsyncReadback(texture, 0, request => {
                try { if (request.hasError) errors.Add("Readback failed: " + path);
                    else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray()); }
                catch (Exception e) { errors.Add(e.Message); }
                finally { pending--; }
            });
        }
        Read(Resolve("m_HitLighting"), "raw");
        if (frame == warmup || frame == warmup + count - 1) {
            Read(geometry, "geometry"); Read(Resolve("m_RadiosityInput"), "radiosity"); Read(Resolve("m_CurrentAnchorStatistics"), "statistics");
        }
        var vp = (Matrix4x4)Field("m_ViewProjection"); var matrix = new float[16];
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) matrix[r*4+c] = vp[r, c];
        var sun = (Vector4)Field("m_LightColorShadow");
        records.Add(new { arm, frame, vp = matrix, state = new { order, batch, samples = Field("m_RadiositySamples"), seed = Field("m_RadiositySeed"),
            guide = Field("m_DirectionalGuide"), restir = Field("m_RestirGather"), sun = new[] { sun.x, sun.y, sun.z }, sky = Field("m_SkyTraceDistance") } });
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (active && phase == 1 && context.Get<VividCameraData>().camera == camera && ++frame >= warmup + count) active = false;
    };
    async System.Threading.Tasks.Task Run(string name, int sequence) {
        AsyncGPUReadback.WaitAllRequests();
        var pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var owner = type.GetField("m_Cameras", inst).GetValue(pass);
        owner.GetType().GetMethod("CleanupCamera").Invoke(owner, new object[] { camera });
        var histories = type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem").GetField("s_Histories", stat).GetValue(null);
        histories.GetType().GetMethod("CleanupCamera").Invoke(histories, new object[] { camera });
        arm = name; frameBase = sequence; frame = ready = phase = 0;
        System.IO.Directory.CreateDirectory(output + "/" + arm); active = true;
        double deadline = EditorApplication.timeSinceStartup + 1800;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline) {
            if (System.IO.File.Exists(output + "/cancel.txt")) throw new OperationCanceledException("Owner cancelled.");
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending != 0 || errors.Count != 0) throw new InvalidOperationException("Reference failed: " + string.Join("; ", errors));
        System.IO.File.WriteAllText(output + "/progress.txt", arm + " complete");
    }
    EditorApplication.LockReloadAssemblies();
    try {
        volume.profile = profile; SceneManager.SetActiveScene(scene);
        foreach (var item in cameras) item.c.enabled = item.c == camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);
        gi.surfaceCacheBounceOrders.Override(8); gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityAccumulate.Override(true); gi.surfaceCacheRadiosityBatchLimit.Override(32);
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768); hooks["EditorRadiositySeed"].SetValue(null, 0);
        hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);
        RenderPipelineManager.beginCameraRendering += Begin; Application.logMessageReceived += Log;
        await Run("uniform-a", 7919); await Run("uniform-b", 25387);
        complete = true; return new { output, complete, errors };
    }
    finally {
        try {
            active = false;
            foreach (var pair in hooks) pair.Value.SetValue(null, previous[pair.Key]);
            RenderPipelineManager.beginCameraRendering -= Begin; Application.logMessageReceived -= Log;
            AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;
            foreach (var item in cameras) if (item.c != null) item.c.enabled = item.enabled;
            if (activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            foreach (var c in profile.components) UnityEngine.Object.DestroyImmediate(c); UnityEngine.Object.DestroyImmediate(profile);
            System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                schema = "scalar-anchor-grid-reference.v1", complete, warmup, measuredFrames = count, width = 1920, height = 1080,
                spacing = 16, rays = 8, quantity = "Current-frame consumed E/pi grid; independent uniform sequences; same-renderer target", records, errors
            }, Newtonsoft.Json.Formatting.Indented));
        } finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
