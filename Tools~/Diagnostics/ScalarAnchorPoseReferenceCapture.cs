// Independent current-scalar Resolve references at matched trajectory poses. No assets are saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const int warmup = 512, measuredFrames = 1024;
    int[] requestedPoses = { 0, 24, 48, 80, 104 };
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    var hooks = new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride",
        "EditorGatherHistoryDiagnostics", "EditorRadiositySeed" }.ToDictionary(n => n, n => type.GetField(n, stat));
    var previousHooks = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (type.GetField(name, stat).GetValue(null) != null) throw new InvalidOperationException("Another GI capture is active: " + name);
    if ((int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1)
        throw new InvalidOperationException("An iteration override is active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Open the clean emissive Cornell fixture.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0, 1.5f, -3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4 || Mathf.Abs(camera.fieldOfView - 42) > 1e-5)
        throw new InvalidOperationException("Reference camera pose required.");
    var volume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components) profile.components.Add(UnityEngine.Object.Instantiate(component));
    if (!profile.TryGet<SdfDiffuseGISettings>(out var gi)) throw new InvalidOperationException("Missing GI profile.");
    var previousScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var passesField = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    string output = "E:/VividRP_GI_Evidence/ScalarAnchorPoseReference-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var records = new List<object>(); var errors = new List<string>();
    string arm = "";
    int frame = 0, rendered = 0, pending = 0, phase = 0, targetBatches = 32;
    bool moving = true;
    int referencePose = 0;
    int readyFrames = 0;
    var pose = (camera.transform.position, camera.transform.rotation);
    bool active = false, complete = false;
    object pass = null;
    object Field(string name) => type.GetField(name, inst).GetValue(pass);
    Texture Resolve(string name) {
        var value = Field(name);
        var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        return handle.IsValid() ? (Texture)handle : null;
    }
    bool Sample() => active && phase == 1 && frame >= warmup;
    void Read(CommandBuffer cmd, Texture texture, string tag, int x = 0, int y = 0, int width = 0, int height = 0) {
        if (texture == null) throw new InvalidOperationException("Missing texture: " + tag);
        if (width == 0) width = texture.width;
        if (height == 0) height = texture.height;
        string path = output + "/" + arm + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        records.Add(new { arm, frame, tag, x, y, width, height, format = texture.graphicsFormat.ToString(), path });
        pending++;
        cmd.RequestAsyncReadback(texture, 0, x, width, y, height, 0, 1, request => {
            try { if (request.hasError) errors.Add("Readback failed: " + path);
                else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray()); }
            catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }
    void Pixels(CommandBuffer cmd, Texture texture, string tag) {
        if (moving) {
            Read(cmd, texture, tag + "-motion", 800, 350, 320, 256);
            if (referencePose != 0) return;
        }
        foreach (var r in new[] { ("left", 350, 450, 160, 240), ("back", 1100, 600, 160, 120),
            ("right", 1410, 450, 160, 240), ("ceiling", 912, 970, 96, 48), ("floor", 912, 130, 96, 64) })
            Read(cmd, texture, tag + "-" + r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
    }
    void Begin(ScriptableRenderContext context, Camera current) {
        if (!active || current != camera) return;
        hooks["EditorGatherFrameOverride"].SetValue(null, 7919 + (phase == 1 ? frame : 0));
        float t = Mathf.Sin(2f * Mathf.PI * referencePose / 128f);
        camera.transform.SetPositionAndRotation(pose.Item1 + pose.Item2 * new Vector3(.28f*t, 0, .06f*t),
            pose.Item2 * Quaternion.Euler(0, -7f*t, 0));
    }
    void Log(string message, string stack, LogType kind) {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    Action<ComputePassContext, int, Texture> stage = (context, index, texture) => {
        if (index == 0 && Sample() && context.Get<VividCameraData>().camera == camera)
            Pixels((CommandBuffer)wrapped.GetValue(context.cmd), texture, index == 0 ? "resolved" : "temporal");
    };
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        pass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        rendered++;
        int order = (int)Field("m_RadiosityPublishedOrder"), batch = (int)Field("m_RadiosityBatch");
        bool sourceReady = order == 8 && batch == targetBatches && (int)Field("m_RadiosityUpdateCards") == 0;
        if (phase == 0) {
            readyFrames = sourceReady ? readyFrames + 1 : 0;
            if (readyFrames < 8) return;
            phase = 1; frame = 0;
            records.Add(new { arm, cacheReadyAtRenderedFrame = rendered, targetBatches });
        }
        else if (!sourceReady) {
            records.Add(new { arm, frame, sourceInvalidated = new { order, batch,
                cursor = Field("m_RadiosityCursor"), updates = Field("m_RadiosityUpdateCards"),
                revision = Field("m_Scene").GetType().GetProperty("Revision", inst).GetValue(Field("m_Scene")),
                beforeMeasurement = frame < warmup } });
            if (frame < warmup) { phase = 0; frame = 0; readyFrames = 0; return; }
            throw new InvalidOperationException("Source cache changed while measuring: " + arm + ", frame=" + frame + ", order=" + order + ", batch=" + batch);
        }
        if (!Sample()) return;
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("Expected 1080p, 16px, eight rays.");
        if ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != targetBatches)
            throw new InvalidOperationException("Source cache changed while measuring.");
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        if (frame == warmup || frame == warmup + measuredFrames - 1) {
            Read(cmd, geometry, "geometry"); Read(cmd, Resolve("m_RadiosityInput"), "radiosity");
            Read(cmd, Resolve("m_CurrentAnchorStatistics"), "statistics");
        }
        var sun = (Vector4)Field("m_LightColorShadow");
        records.Add(new { arm, frame, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
            samples = Field("m_RadiositySamples"), seed = hooks["EditorRadiositySeed"].GetValue(null),
            anchorMaxAge = Field("m_ProbeAnchorMaxAge"), guide = Field("m_DirectionalGuide"), restir = Field("m_RestirGather"), atrous = Field("m_AtrousIterations"),
            sun = new[] { sun.x, sun.y, sun.z }, sky = Field("m_SkyTraceDistance") } });
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (!active || phase != 1 || context.Get<VividCameraData>().camera != camera) return;
        if (Sample()) {
            var matrix = (Matrix4x4)Field("m_ViewProjection"); var values = new float[16];
            for (int r = 0; r < 4; ++r) for (int c = 0; c < 4; ++c) values[r * 4 + c] = matrix[r, c];
            records.Add(new { arm, frame, vp = values });
        }
        if (++frame >= warmup + measuredFrames) active = false;
    };
    async System.Threading.Tasks.Task Run(string name, int poseFrame) {
        AsyncGPUReadback.WaitAllRequests();
        var actualPass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var owner = type.GetField("m_Cameras", inst).GetValue(actualPass);
        owner.GetType().GetMethod("CleanupCamera").Invoke(owner, new object[] { camera });
        var historyType = type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem");
        var history = historyType.GetField("s_Histories", stat).GetValue(null);
        history.GetType().GetMethod("CleanupCamera").Invoke(history, new object[] { camera });
        arm = name; frame = 0; rendered = 0; phase = 0; moving = true; referencePose = poseFrame; targetBatches = 32; readyFrames = 0;
        gi.surfaceCacheRadiosityBatchLimit.Override(32);
        gi.screenProbeAnchorMaxAge.Override(32);
        hooks["EditorRadiositySeed"].SetValue(null, 0);
        System.IO.Directory.CreateDirectory(output + "/" + arm);
        extra.ResetPostProcessingHistory();
        active = true;
        double deadline = EditorApplication.timeSinceStartup + 1800;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline) {
            if (System.IO.File.Exists(output + "/cancel.txt")) throw new OperationCanceledException("Owner cancelled source comparison.");
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending != 0 || errors.Count != 0) throw new InvalidOperationException(name + ": " + string.Join("; ", errors));
        System.IO.File.WriteAllText(output + "/progress.txt", name + " complete");
        System.IO.File.WriteAllText(output + "/capture-progress.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { complete = false,
            completedArm = name, warmup, measuredFrames, records, errors }, Newtonsoft.Json.Formatting.Indented));
    }
    EditorApplication.LockReloadAssemblies();
    try {
        volume.profile = profile; SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);
        gi.surfaceCacheBounceOrders.Override(8); gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityAccumulate.Override(true); gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous4);
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 32768);
        hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorGatherStageCapture"].SetValue(null, stage);
        RenderPipelineManager.beginCameraRendering += Begin; Application.logMessageReceived += Log;
        foreach (int point in requestedPoses)
            await Run("pose" + point.ToString("000"), point);
        complete = true; return new { output, complete, errors };
    }
    finally {
      try {
        active = false;
        foreach (var p in hooks) p.Value.SetValue(null, previousHooks[p.Key]);
        RenderPipelineManager.beginCameraRendering -= Begin; Application.logMessageReceived -= Log;
        AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;
        camera.transform.SetPositionAndRotation(pose.Item1, pose.Item2);
        foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
        if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        foreach (var c in profile.components) UnityEngine.Object.DestroyImmediate(c);
        UnityEngine.Object.DestroyImmediate(profile);
        System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { schema = "scalar-anchor-pose-reference.v1", complete,
            width = 1920, height = 1080, spacing = 16, rays = 8, warmup, measuredFrames, requestedPoses, readinessVersion = 2,
            source = "fixed 8/64/32 source; independent uniform current-scalar Resolve; frame base 7919; matched static poses", records, errors }, Newtonsoft.Json.Formatting.Indented));
      }
      finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
