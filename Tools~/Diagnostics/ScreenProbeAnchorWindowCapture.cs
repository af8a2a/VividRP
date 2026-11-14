// Requires Patches/ScreenProbeAnchorPrecision.patch. Fixed 16px/8 rays; no saved assets.
// Factorial scalar-anchor precision/window experiment with a source-settled history epoch. No assets saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const int warmup = 4096, measuredFrames = 2304, sampleStride = 9;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    const string sourceReference = "E:/VividRP_GI_Evidence/ScreenProbeResolveReplay-20261114-112421-783829";
    var type = typeof(SdfDiffuseGatherPass);
    if (type.GetField("m_CoveredProbeAnchor", inst) != null) throw new InvalidOperationException("Covered prototype remains loaded.");
    var hooks = new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorBindResolveProbe",
        "EditorGatherFrameOverride", "EditorGatherHistoryDiagnostics", "EditorRadiositySeed", "EditorProbeAnchorMaxAgeOverride" }.ToDictionary(n => n, n => type.GetField(n, stat));
    if (hooks.Values.Any(f => f == null)) throw new InvalidOperationException("Compile the diagnostic input hook first.");
    var previousHooks = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorBindResolveProbe", "EditorAtrousDispatchCapture" })
        if (type.GetField(name, stat).GetValue(null) != null) throw new InvalidOperationException("Another capture is active: " + name);
    if ((int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1)
        throw new InvalidOperationException("An Atrous override is active.");
    if ((int)hooks["EditorGatherFrameOverride"].GetValue(null) != -1
        || (int)hooks["EditorGatherHistoryDiagnostics"].GetValue(null) != 0
        || (int)hooks["EditorProbeAnchorMaxAgeOverride"].GetValue(null) != -1)
        throw new InvalidOperationException("Another diagnostic override is configured.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Open the clean emissive Cornell fixture.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0, 1.5f, -3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4 || Mathf.Abs(camera.fieldOfView - 42) > 1e-5)
        throw new InvalidOperationException("Reference camera pose required.");
    var volume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var previousScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    VolumeProfile profile = null; SdfDiffuseGISettings gi = null;
    var passesField = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    string output = "E:/VividRP_GI_Evidence/ScreenProbeAnchorWindow-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var cpuFrames = new List<object>();
    var records = new List<object>(); var errors = new List<string>(); var variants = new List<object>();
    string arm = "";
    int frame = 0, phase = 0, ready = 0, pending = 0;
    bool active = false, complete = false, fullPrecision = false, resetPending = false;
    int requestedAge = 128;
    object pass = ((System.Collections.IList)passesField.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
    object Field(string n) => type.GetField(n, inst).GetValue(pass);
    bool Sample() => active && phase == 2 && frame >= warmup && (frame-warmup) % sampleStride == 0;
    Texture Resolve(string n) {
        var value = Field(n); var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        return handle.IsValid() ? (Texture)handle : null;
    }
    void Read(CommandBuffer cmd, Texture texture, string tag, int x = 0, int y = 0, int width = 0, int height = 0) {
        if (texture == null) throw new InvalidOperationException("Missing texture: " + tag);
        if (width == 0) width = texture.width;
        if (height == 0) height = texture.height;
        string path = output + "/" + arm + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        string readArm = arm, format = texture.graphicsFormat.ToString(); int readFrame = frame, readWidth = width, readHeight = height;
        bool saved = (tag != "geometry" && tag != "raw" && tag != "sh") || frame == 0 || frame == warmup-1 || frame == warmup || frame == warmup+measuredFrames-1;
        pending++;
        cmd.RequestAsyncReadback(texture, 0, x, width, y, height, 0, 1, request => {
            try {
                if (request.hasError) { errors.Add("Readback failed: " + path); return; }
                var bytes = request.GetData<byte>().ToArray();
                using var hash = System.Security.Cryptography.SHA256.Create();
                string sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (saved) System.IO.File.WriteAllBytes(path, bytes);
                records.Add(new { arm = readArm, frame = readFrame, tag, x, y, width = readWidth, height = readHeight,
                    format, sha256, saved, path = saved ? path : null });
            }
            catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }

    void Pixels(CommandBuffer cmd, Texture texture, string tag) {
        foreach (var r in new[] { ("left", 382, 514, 96, 96), ("right", 1442, 514, 96, 96), ("back", 1132, 612, 96, 96),
            ("ceiling", 912, 970, 96, 48), ("floor", 912, 130, 96, 64) })
            Read(cmd, texture, tag + "-" + r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
    }
    void Begin(ScriptableRenderContext context, Camera current) {
        if (!active || current != camera) return;
        if (resetPending) {
            // Invalidate only Gather's signature. Preserve the fully converged source cache.
            var owner = type.GetField("m_Cameras", inst).GetValue(pass);
            var state = owner.GetType().GetMethod("GetOrCreateBase").Invoke(owner, new object[] { camera });
            state.GetType().GetField("ProbeAnchorMaxAge", inst).SetValue(state, -1);
            resetPending = false; phase = 2; frame = 0;
        }
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + (phase == 2 ? frame : 0));
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
        if (phase == 0) {
            bool settled = (int)Field("m_RadiosityPublishedOrder") == 8 && (int)Field("m_RadiosityBatch") == 32 && (int)Field("m_RadiosityUpdateCards") == 0;
            ready = settled ? ready+1 : 0; if (ready < 8) return;
            phase = 1; resetPending = true; return;
        }
        bool epoch = phase == 2 && frame == 0;
        if (phase == 2) cpuFrames.Add(new { arm, frame, hasHistory = Field("m_HasHistory"), order = Field("m_RadiosityPublishedOrder"),
            batch = Field("m_RadiosityBatch"), updateCards = Field("m_RadiosityUpdateCards"), age = Field("m_ProbeAnchorMaxAge"), fullPrecision });
        if (!Sample() && !epoch && frame != warmup-1 && frame != warmup+measuredFrames-1) return;
        if ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32
            || !(bool)Field("m_AnchorPreFilter") || (int)Field("m_ProbeAnchorMaxAge") != requestedAge || (int)Field("m_AtrousIterations") != 4 || (bool)Field("m_RestirGather"))
            throw new InvalidOperationException("Requested precision/window configuration not active.");
        if (epoch && (bool)Field("m_HasHistory")) throw new InvalidOperationException("Source-settled Gather epoch did not reject old history.");
        string expectedFormat = fullPrecision ? "R32G32B32A32_SFloat" : "R16G16B16A16_SFloat";
        foreach (string texture in new[] { "m_Probes", "m_PreviousProbes", "m_PreparedAnchor", "m_ReconstructedAnchor" })
            if (Resolve(texture).graphicsFormat.ToString() != expectedFormat) throw new InvalidOperationException("Wrong actual anchor format: " + texture);
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("Expected 1080p 16px/8-ray probe grid.");
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        Read(cmd, geometry, "geometry"); Read(cmd, Resolve("m_HitLighting"), "raw");
        Read(cmd, Resolve("m_Probes"), "anchor", 0, 0, 120, 136);
        Read(cmd, Resolve("m_CurrentAnchorStatistics"), "statistics"); Read(cmd, Resolve("m_FilteredProbes"), "sh");
        if (Sample()) Pixels(cmd, Resolve("m_CurrentPixelLighting"), "history");
        if (epoch || frame == warmup || frame == warmup + measuredFrames - 1) {
            Read(cmd, Resolve("m_RadiosityInput"), "radiosity");
            if (!epoch) Read(cmd, lighting, "full-final");
        }
        var sun = (Vector4)Field("m_LightColorShadow");
        var near = (Vector4)Field("m_NearOcclusion");
        if (near.y != 0 || near.z != 0) throw new InvalidOperationException("Near AO must remain disabled for input isolation.");
        var vp = (Matrix4x4)Field("m_ViewProjection"); var matrix = new float[16];
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) matrix[r*4+c] = vp[r,c];
        records.Add(new { arm, frame, vp = matrix, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
            anchorMaxAge = Field("m_ProbeAnchorMaxAge"), preFilter = Field("m_AnchorPreFilter"), atrous = Field("m_AtrousIterations"),
            samples = Field("m_RadiositySamples"), restir = Field("m_RestirGather"), seed = Field("m_RadiositySeed"),
            guide = Field("m_DirectionalGuide"), hierarchicalGuide = Field("m_DirectionalHierarchicalGuide"),
            traceMode = Field("m_TraceMode").ToString(), nearOcclusion = new[] { near.x, near.y, near.z, near.w },
            fullPrecision, requestedAge, hasHistory = Field("m_HasHistory"), epoch,
            anchorFormat = Resolve("m_Probes").graphicsFormat.ToString(), previousFormat = Resolve("m_PreviousProbes").graphicsFormat.ToString(),
            preparedFormat = Resolve("m_PreparedAnchor").graphicsFormat.ToString(), reconstructedFormat = Resolve("m_ReconstructedAnchor").graphicsFormat.ToString(),
            sun = new[] { sun.x, sun.y, sun.z }, sky = Field("m_SkyTraceDistance") } });
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (!active || phase != 2 || context.Get<VividCameraData>().camera != camera) return;
        if (Sample()) Pixels((CommandBuffer)wrapped.GetValue(context.cmd), lighting, "final");
        if ((frame & 127) == 0) System.IO.File.WriteAllText(output + "/progress-state.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { arm, frame, phase, pending }));
        if (++frame >= warmup + measuredFrames) active = false;
    };
    void WriteManifest() => System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
        schema = "screen-probe-anchor-window.v1", complete, completedArm = arm, width = 1920, height = 1080, spacing = 16, rays = 8, warmup, measuredFrames, sampleStride,
        sourceReference, scope = "Half128 A/A, Float128, Float512 after 4096 source-settled epoch frames; 2304 measured renders sampled every 9 frames; 512 only at stationary world receivers", cpuFrames, records, errors, variants }, Newtonsoft.Json.Formatting.Indented));
    async System.Threading.Tasks.Task Run(string name, int age, bool precise = false) {
        var owner = type.GetField("m_Cameras", inst).GetValue(pass);
        owner.GetType().GetMethod("CleanupCamera").Invoke(owner, new object[] { camera });
        var historyType = type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem");
        var history = historyType.GetField("s_Histories", stat).GetValue(null);
        history.GetType().GetMethod("CleanupCamera").Invoke(history, new object[] { camera });
        arm = name; frame = phase = ready = 0; resetPending = false; requestedAge = age; fullPrecision = precise;
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, precise ? 32 : 0);
        hooks["EditorRadiositySeed"].SetValue(null, 0);
        hooks["EditorProbeAnchorMaxAgeOverride"].SetValue(null, age);
        System.IO.Directory.CreateDirectory(output + "/" + arm); extra.ResetPostProcessingHistory(); active = true;
        double deadline = EditorApplication.timeSinceStartup + 2400;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline) {
            if (System.IO.File.Exists(output + "/cancel.txt")) throw new OperationCanceledException("Owner cancelled anchor precision diagnostic.");
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending != 0 || errors.Count != 0) throw new InvalidOperationException(name + ": " + string.Join("; ", errors));
        WriteManifest(); System.IO.File.WriteAllText(output + "/progress.txt", name + " complete");
    }
    EditorApplication.LockReloadAssemblies();
    try {
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        foreach (var component in (previousProfile ?? volume.sharedProfile).components) profile.components.Add(UnityEngine.Object.Instantiate(component));
        if (!profile.TryGet<SdfDiffuseGISettings>(out gi)) throw new InvalidOperationException("Missing GI profile.");
        var compile = typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Single(m => m.Name == "CompileComputeShaderVariant");
        foreach (var target in new[] { ("m_GatherCompute", "ScreenProbeIntegrateCS"), ("m_FilterCompute", "ScreenProbePrepareAnchorCS"),
            ("m_FilterCompute", "ScreenProbeFilterAnchorCS"), ("m_FilterCompute", "ScreenProbeConvertToIrradianceCS") }) {
            var shader = (ComputeShader)Field(target.Item1);
            var variant = compile.Invoke(null, new object[] { shader, shader.FindKernel(target.Item2), Array.Empty<string>(), GraphicsDeviceType.Direct3D12, BuildTarget.StandaloneWindows64 });
            if (!(bool)variant.GetType().GetProperty("Success").GetValue(variant)) throw new InvalidOperationException("Kernel compilation failed: " + target.Item2);
            var bytes = (byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
            using (var hash = System.Security.Cryptography.SHA256.Create()) {
                string sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                string path = output + "/compiled-" + target.Item2 + ".bin"; System.IO.File.WriteAllBytes(path, bytes);
                variants.Add(new { name = target.Item2, path, sha256, bytes = bytes.Length });
            }
        }
        volume.profile = profile; SceneManager.SetActiveScene(scene); foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeAnchorPreFilter.Override(true); gi.screenProbeAnchorMaxAge.Override(128);
        gi.surfaceCacheBounceOrders.Override(8); gi.surfaceCacheRadiositySamples.Override(64); gi.surfaceCacheRadiosityAccumulate.Override(true);
        gi.surfaceCacheRadiosityBatchLimit.Override(32); gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous4);
        hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorGatherStageCapture"].SetValue(null, stage); hooks["EditorBindResolveProbe"].SetValue(null, null);
        RenderPipelineManager.beginCameraRendering += Begin; Application.logMessageReceived += Log;
        await Run("half128-a", 128); await Run("half128-b", 128);
        var rows = Newtonsoft.Json.Linq.JArray.FromObject(records).OfType<Newtonsoft.Json.Linq.JObject>().Where(r => r["tag"] != null).ToArray();
        var baseline = rows.Where(r => (string)r["arm"] == "half128-a").ToDictionary(r => (string)r["frame"] + "-" + (string)r["tag"]);
        var repeated = rows.Where(r => (string)r["arm"] == "half128-b").ToDictionary(r => (string)r["frame"] + "-" + (string)r["tag"]);
        var mismatch = baseline.Keys.Where(k => !repeated.ContainsKey(k) || (string)baseline[k]["sha256"] != (string)repeated[k]["sha256"]).ToArray();
        System.IO.File.WriteAllText(output + "/qualification.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { files = baseline.Count, mismatch }));
        if (baseline.Count == 0 || baseline.Count != repeated.Count || mismatch.Length != 0) {
            errors.Add("Anchor window baseline A/A failed; precision/window candidates did not run.");
            throw new InvalidOperationException(errors[errors.Count-1]);
        }
        await Run("float128", 128, true); await Run("float512", 512, true);
        complete = true; return new { output, complete, errors };
    }
    finally {
        try {
            active = false; foreach (var p in hooks) p.Value.SetValue(null, previousHooks[p.Key]);
            RenderPipelineManager.beginCameraRendering -= Begin; Application.logMessageReceived -= Log;
            AsyncGPUReadback.WaitAllRequests(); volume.profile = previousProfile;
            foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
            if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (profile != null) {
                foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
                UnityEngine.Object.DestroyImmediate(profile);
            }
            WriteManifest();
        }
        finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
