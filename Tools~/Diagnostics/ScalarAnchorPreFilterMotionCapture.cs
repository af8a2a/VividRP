// Recenter and flush camera data before cold history reset; qualify motion A/A first.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const int warmup = 1024, measuredFrames = 256;
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
    string output = "E:/VividRP_GI_Evidence/ScalarAnchorPreFilterMotion-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var records = new List<object>(); var errors = new List<string>();
    var variants = new List<object>();
    void ArchiveVariants() {
        var actualPass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var shader = (ComputeShader)type.GetField("m_FilterCompute", inst).GetValue(actualPass);
        var compile = typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Single(m => m.Name == "CompileComputeShaderVariant");
        foreach (string name in new[] { "ScreenProbeFilterAnchorCS", "ScreenProbeConvertToIrradianceCS" }) {
            var variant = compile.Invoke(null, new object[] { shader, shader.FindKernel(name), Array.Empty<string>(),
                UnityEngine.Rendering.GraphicsDeviceType.Direct3D12, BuildTarget.StandaloneWindows64 });
            if (!(bool)variant.GetType().GetProperty("Success").GetValue(variant)) throw new InvalidOperationException("Kernel compilation failed: " + name);
            var bytes = (byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
            using var hash = System.Security.Cryptography.SHA256.Create();
            string sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string path = output + "/compiled-" + name + ".bin"; System.IO.File.WriteAllBytes(path, bytes);
            variants.Add(new { name, path, sha256, bytes = bytes.Length });
        }
        var messages = ShaderUtil.GetComputeShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
        if (messages.Length != 0) throw new InvalidOperationException(string.Join("; ", messages.Select(m => m.message)));
    }
    string arm = "";
    int frame = 0, rendered = 0, pending = 0, phase = 0, targetBatches = 32, readyFrames = 0;
    bool expectedPreFilter = false;
    bool moving = false;
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
        if (moving) { Read(cmd, texture, tag + "-motion", 800, 350, 320, 256); return; }
        foreach (var r in new[] { ("left", 350, 450, 160, 240), ("back", 1100, 600, 160, 120),
            ("right", 1410, 450, 160, 240), ("ceiling", 912, 970, 96, 48), ("floor", 912, 130, 96, 64),
            ("short-contact", 720, 64, 64, 64), ("tall-contact", 1093, 153, 64, 64), ("wall-corner", 1288, 468, 64, 64) })
            Read(cmd, texture, tag + "-" + r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
    }
    void Begin(ScriptableRenderContext context, Camera current) {
        if (!active || current != camera) return;
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + (phase == 1 ? frame : 0));
        float t = moving && phase == 1 && frame >= warmup ? Mathf.Sin(2f * Mathf.PI * ((frame - warmup) % 128) / 128f) : 0f;
        camera.transform.SetPositionAndRotation(pose.Item1 + pose.Item2 * new Vector3(.28f*t, 0, .06f*t),
            pose.Item2 * Quaternion.Euler(0, -7f*t, 0));
    }
    void Log(string message, string stack, LogType kind) {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    Action<ComputePassContext, int, Texture> stage = (context, index, texture) => {
        if (Sample() && context.Get<VividCameraData>().camera == camera)
            Pixels((CommandBuffer)wrapped.GetValue(context.cmd), texture, index == 0 ? "resolved" : "temporal");
    };
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        pass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        rendered++;
        if (rendered <= 4) {
            var initialVp = (Matrix4x4)Field("m_ViewProjection"); var initialMatrix = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) initialMatrix[r*4+c] = initialVp[r,c];
            records.Add(new { arm, rendered, initialVp = initialMatrix, hasHistory = Field("m_HasHistory"),
                order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"), hierarchicalGuide = Field("m_DirectionalHierarchicalGuide") });
        }
        if (phase == 0) {
            bool ready = (int)Field("m_RadiosityPublishedOrder") == 8 && (int)Field("m_RadiosityBatch") == targetBatches
                && (int)Field("m_RadiosityUpdateCards") == 0;
            readyFrames = ready ? readyFrames + 1 : 0;
            if (readyFrames < 8) return;
            phase = 1; frame = 0;
            records.Add(new { arm, cacheReadyAtRenderedFrame = rendered, targetBatches });
        }
        if (phase == 1 && frame < warmup && ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != targetBatches)) {
            phase = 0; frame = readyFrames = 0; records.Add(new { arm, warmupRestart = true }); return;
        }
        if (!Sample()) return;
        if ((bool)Field("m_AnchorPreFilter") != expectedPreFilter) throw new InvalidOperationException("Requested prefilter not active.");
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("Expected 1080p, 16px, eight rays.");
        if ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != targetBatches)
            throw new InvalidOperationException("Source cache changed while measuring.");
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        Read(cmd, geometry, "geometry");
        Read(cmd, Resolve("m_HitLighting"), "raw");
        Read(cmd, Resolve("m_Probes"), "anchor", 0, 0, 120, 136);
        Read(cmd, Resolve("m_CurrentAnchorStatistics"), "statistics");
        if (frame < warmup+32) {
            Read(cmd, Resolve("m_DirectionalGuideTexture"), "proposal");
            Read(cmd, Resolve("m_DirectionalRayTexture"), "ray-data");
            Read(cmd, Resolve("m_CurrentDirectionalFiltered"), "directional-filtered");
        }
        Read(cmd, Resolve("m_FilteredProbes"), "sh");
        if (expectedPreFilter) Read(cmd, Resolve("m_ReconstructedAnchor"), "reconstructed");
        if (moving) {
            Pixels(cmd, Resolve("m_CurrentPixelLighting"), "history");
            Pixels(cmd, Resolve("m_CurrentPixelGeometry"), "pixel-geometry");
        }
        Pixels(cmd, lighting, "final");
        if (frame == warmup || frame == warmup + measuredFrames - 1) {
            Read(cmd, Resolve("m_RadiosityInput"), "radiosity");
            if (!moving) Read(cmd, lighting, "full-final");
        }
        var sun = (Vector4)Field("m_LightColorShadow");
        records.Add(new { arm, frame, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
            samples = Field("m_RadiositySamples"), seed = hooks["EditorRadiositySeed"].GetValue(null),
            anchorMaxAge = Field("m_ProbeAnchorMaxAge"), preFilter = Field("m_AnchorPreFilter"), guide = Field("m_DirectionalGuide"), restir = Field("m_RestirGather"), atrous = Field("m_AtrousIterations"),
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
    async System.Threading.Tasks.Task Run(string name, bool preFilter, bool motion = false) {
        gi.screenProbeAnchorMaxAge.Override(128); gi.screenProbeAnchorPreFilter.Override(preFilter);
        camera.transform.SetPositionAndRotation(pose.Item1, pose.Item2);
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733);
        int flushed = 0;
        void FlushEnd(ScriptableRenderContext context, Camera current) {
            if (current != camera) return;
            var currentPass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
            var vp = (Matrix4x4)type.GetField("m_ViewProjection", inst).GetValue(currentPass);
            bool correct = Mathf.Abs(vp.m00 - 1.4653625f) < 1e-5f && Mathf.Abs(vp.m11 + 2.605089f) < 1e-5f
                && Mathf.Abs(vp.m13 - 3.9076333f) < 1e-5f && Mathf.Abs(vp.m32 - 1) < 1e-5f && Mathf.Abs(vp.m33 - 3.8f) < 1e-5f
                && Mathf.Abs(vp.m02) < 1e-5f && Mathf.Abs(vp.m30) < 1e-5f;
            flushed = correct ? flushed + 1 : 0;
        }
        RenderPipelineManager.endCameraRendering += FlushEnd;
        try {
            double flushDeadline = EditorApplication.timeSinceStartup + 60;
            while (flushed < 2 && EditorApplication.timeSinceStartup < flushDeadline) {
                game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
            }
            if (flushed < 2) throw new InvalidOperationException("Reference camera data did not flush before reset.");
        } finally { RenderPipelineManager.endCameraRendering -= FlushEnd; }
        AsyncGPUReadback.WaitAllRequests();
        var actualPass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var owner = type.GetField("m_Cameras", inst).GetValue(actualPass);
        owner.GetType().GetMethod("CleanupCamera").Invoke(owner, new object[] { camera });
        var historyType = type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem");
        var history = historyType.GetField("s_Histories", stat).GetValue(null);
        history.GetType().GetMethod("CleanupCamera").Invoke(history, new object[] { camera });
        arm = name; frame = 0; rendered = 0; phase = 0; moving = motion; targetBatches = 32;
        gi.surfaceCacheRadiosityBatchLimit.Override(32);
        gi.screenProbeAnchorMaxAge.Override(128);
        gi.screenProbeAnchorPreFilter.Override(preFilter);
        expectedPreFilter = preFilter; readyFrames = 0;
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
        ArchiveVariants();
        volume.profile = profile; SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(true);
        gi.surfaceCacheBounceOrders.Override(8); gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityAccumulate.Override(true); gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous4);
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);
        hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorGatherStageCapture"].SetValue(null, stage);
        RenderPipelineManager.beginCameraRendering += Begin; Application.logMessageReceived += Log;
        await Run("motion-qualify-a", false, true); await Run("baseline-motion", false, true);
        var qualificationFiles = System.IO.Directory.GetFiles(output + "/motion-qualify-a", "*.bin");
        bool EqualPayload(string path) {
            string file = System.IO.Path.GetFileName(path), other = output + "/baseline-motion/" + file;
            var a = System.IO.File.ReadAllBytes(path); var b = System.IO.File.ReadAllBytes(other);
            if (a.SequenceEqual(b)) return true;
            if (!file.EndsWith("-ray-data.bin", StringComparison.Ordinal)) return false;
            // TraceProbe leaves invalid receiver slots unused. Composite explicitly
            // ignores them. Compare every byte of all three planes for valid probes.
            string geometryFile = file.Substring(0, 4) + "-geometry.bin";
            var ga = System.IO.File.ReadAllBytes(output + "/motion-qualify-a/" + geometryFile);
            var gb = System.IO.File.ReadAllBytes(output + "/baseline-motion/" + geometryFile);
            if (!ga.SequenceEqual(gb) || ga.Length != 136 * 120 * 16
                || a.Length != 3 * 136 * 120 * 8 * 8 || a.Length != b.Length) return false;
            for (int y = 0; y < 136; ++y) for (int x = 0; x < 120; ++x) {
                float normal = BitConverter.ToSingle(ga, (y * 120 + x) * 16 + 12);
                if (!float.IsFinite(normal)) return false;
                if (normal < 0) continue;
                for (int plane = 0; plane < 3; ++plane) {
                    int offset = ((plane * 136 + y) * 120 + x) * 8 * 8;
                    for (int word = 0; word < 8 * 8; ++word)
                        if (a[offset + word] != b[offset + word]) return false;
                }
            }
            return true;
        }
        var mismatch = qualificationFiles.Where(p => !EqualPayload(p)).Select(System.IO.Path.GetFileName).ToArray();
        System.IO.File.WriteAllText(output + "/qualification.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            files = qualificationFiles.Length, mismatch, rayComparison = "All three payload planes for geometry.w >= 0; other payloads exact" }));
        if (mismatch.Length != 0 || qualificationFiles.Length == 0) throw new InvalidOperationException("Motion A/A failed.");
        await Run("candidate-motion", true, true);
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
        System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { schema = "scalar-anchor-prefilter-motion.v1", geometryGuard = true, recenteredBeforeReset = true, complete,
            width = 1920, height = 1080, spacing = 16, rays = 8, warmup, measuredFrames,
            source = "pure emissive Cornell; fixed 8/64/32 source, guided 16px/8-ray Gather, age128; guarded half-strength anchor prefilter only; eight static time constants after source freeze", variants, records, errors }, Newtonsoft.Json.Formatting.Indented));
      }
      finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
