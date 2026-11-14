// Controlled live diagnostic, not a Unity Test Framework test. No assets are saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    float traceBias = 0.04f;
    // Keep evidence outside Unity's disposable Logs/ and use a fresh directory per run.
    string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..",
        "VividRP_GI_Evidence", "EmissionSidedness-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
    string output = System.IO.Path.Combine(evidenceRoot, "capture");
    const int warmup = 512, measuredFrames = 32, captureStride = 4;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    if (System.IO.Directory.Exists(output)) throw new InvalidOperationException("Refusing to overwrite " + output);
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded) throw new InvalidOperationException("Open the emissive Cornell fixture first.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0, 1.5f, -3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4f
        || Mathf.Abs(camera.fieldOfView - 42f) > 1e-5f)
        throw new InvalidOperationException("The Cornell comparison requires the reference camera pose and 42 degree vertical FOV.");
    bool wasDirty = scene.isDirty;
    var volume = roots.SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var previousScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var type = typeof(SdfDiffuseGatherPass);
    var sceneType = type.Assembly.GetType("VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene");
    var forceSide = sceneType.GetField("EditorForceTwoSidedEmission", stat);
    var previousForce = forceSide.GetValue(null);
    var hooks = new[] { "EditorLinearCapture", "EditorTraceCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride", "EditorRecordProbeAnchorDiagnostics", "EditorGatherHistoryDiagnostics" }
        .ToDictionary(n => n, n => type.GetField(n, stat));
    var previousHooks = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string name in new[] { "EditorLinearCapture", "EditorTraceCapture", "EditorGatherStageCapture" })
        if (previousHooks[name] != null) throw new InvalidOperationException("Another GI capture is active: " + name);
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components)
        profile.components.Add(UnityEngine.Object.Instantiate(component));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    profile.TryGet<AutoExposure>(out var exposure);
    if (gi == null || exposure == null) throw new InvalidOperationException("Missing Cornell profile.");
    // Validate the fixture's current denoiser instead of silently testing an older preset.
    var spatialDenoise = gi.spatialDenoise.value;
    var nativeCmd = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", priv);
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var game = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var records = new List<object>();
    var errors = new List<string>();
    int frame = 0, pending = 0;
    bool active = false, complete = false;
    string arm = "";
    object pass = null;
    object biasPass = null, previousBias = null;
    var biasField = type.GetField("m_RayBias", priv);
    bool Sample() => active && frame >= warmup && (frame - warmup) % captureStride == 0;
    object Field(string name) => type.GetField(name, priv).GetValue(pass);
    Texture Resolve(string name)
    {
        var value = Field(name);
        var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        if (!handle.IsValid()) return null;
        return handle;
    }
    void Read(CommandBuffer cmd, Texture texture, string tag)
    {
        if (texture == null) return;
        string path = output + "/" + arm + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        records.Add(new { arm, frame, tag, width = texture.width, height = texture.height, format = texture.graphicsFormat.ToString(), path });
        pending++;
        cmd.RequestAsyncReadback(texture, 0, request => {
            try
            {
                if (request.hasError) errors.Add("GPU readback failed: " + path);
                else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray());
            }
            catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }
    void Begin(ScriptableRenderContext context, Camera current)
    {
        if (!active || current != camera) return;
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + frame);
        {
            var currentPass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
            if (biasPass == null) { biasPass = currentPass; previousBias = biasField.GetValue(currentPass); }
            if (!object.ReferenceEquals(biasPass, currentPass)) throw new InvalidOperationException("Diagnostic pass was recreated.");
            biasField.SetValue(currentPass, traceBias);
        }
    }
    void Log(string message, string stack, LogType kind)
    {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!Sample() || context.Get<VividCameraData>().camera != camera) return;
        pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("The experiment must remain 16px / 8 rays at 1920x1080.");
        Read(cmd, geometry, "probe-geometry");
        Read(cmd, sources, "sources");
        foreach (var entry in new[] { ("m_HitLighting", "hit-lighting"), ("m_RayDiagnostics", "rays"),
            ("m_FirstHitDiagnostics", "first-hit"), ("m_RadiosityInput", "radiosity") })
            Read(cmd, Resolve(entry.Item1), entry.Item2);
        if (frame == warmup)
        {
            var gpuScene = Field("m_Scene");
            var buffer = (GraphicsBuffer)sceneType.GetProperty("DiffuseSurfaces", priv | System.Reflection.BindingFlags.Public).GetValue(gpuScene);
            string path = output + "/" + arm + "/surfaces.bin";
            records.Add(new { arm, frame, bufferTag = "surfaces", count = buffer.count, stride = buffer.stride, path });
            pending++;
            cmd.RequestAsyncReadback(buffer, request => {
                try { if (request.hasError) errors.Add("Surface buffer readback failed");
                    else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray()); }
                finally { pending--; }
            });
        }
        var light = (Vector4)Field("m_LightColorShadow");
        records.Add(new { arm, frame, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
            rays = 8, spacing = 16, traceMode = gi.traceMode.value.ToString(), radioSamples = Field("m_RadiositySamples"),
            radioTargetOrders = Field("m_RadiosityTargetOrders"), radioCursor = Field("m_RadiosityCursor"),
            directionalCache = Field("m_DirectionalCache"), restir = Field("m_RestirGather"),
            spatialDenoise = gi.spatialDenoise.value.ToString(), atrousIterations = Field("m_AtrousIterations"),
            forceTwoSided = forceSide.GetValue(null), rayBias = Field("m_Bias"), sun = new[] { light.x, light.y, light.z, light.w }, sky = Field("m_SkyTraceDistance") } });
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (Sample()) Read((CommandBuffer)nativeCmd.GetValue(context.cmd), lighting, "final");
        if (++frame >= warmup + measuredFrames) active = false;
    };
    async System.Threading.Tasks.Task Run(string name, SdfDiffuseTraceMode mode, int orders, bool legacy, float bias = 0.04f)
    {
        arm = name; frame = 0; traceBias = bias; forceSide.SetValue(null, legacy);
        System.IO.Directory.CreateDirectory(output + "/" + arm);
        gi.traceMode.Override(mode);
        gi.surfaceCacheBounceOrders.Override(orders);
        // Probe filters stay fixed: the raw/temporal/final taps isolate pixel reconstruction.
        extra.ResetPostProcessingHistory();
        active = true;
        double deadline = EditorApplication.timeSinceStartup + 480;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending > 0 || errors.Count > 0) throw new InvalidOperationException(name + " failed at " + frame + ": " + string.Join("\n", errors));
        System.IO.File.WriteAllText(output + "/progress.txt", name + " complete");
        WriteManifest();
    }
    void WriteManifest()
    {
        System.IO.Directory.CreateDirectory(output);
        System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            schema = "emission-sidedness.v2", spatialDenoise = spatialDenoise.ToString(),
            complete, completedArm = arm, width = 1920, height = 1080, spacing = 16, rays = 8,
            warmup, measuredFrames, captureStride, scene_dirty_before = wasDirty, scene_dirty_after = scene.isDirty,
            row_origin = "bottom_left", pre_exposure = 1.0, quantity = "conditional E/pi RGB + GI coverage alpha",
            camera = new { position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                vertical_fov = camera.fieldOfView }, records, errors }, Newtonsoft.Json.Formatting.Indented));
    }
    EditorApplication.LockReloadAssemblies();
    try
    {
        volume.profile = profile;
        SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
        exposure.fixedExposure.Override(4f);
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
        gi.nearOcclusionStrength.Override(0f);
        gi.realtimeOnly.Override(true);
        gi.pixelTemporal.Override(true);
        gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityAccumulate.Override(true);
        gi.surfaceCacheRadiosityBatchLimit.Override(32);
        gi.recordRayDiagnostics.Override(true);
        gi.recordFirstHitDiagnostics.Override(true);
        hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorTraceCapture"].SetValue(null, trace);
        hooks["EditorGatherStageCapture"].SetValue(null, null);
        hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null, false);
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);
        RenderPipelineManager.beginCameraRendering += Begin;
        Application.logMessageReceived += Log;
        await Run("legacy-hybrid-order0", SdfDiffuseTraceMode.Hybrid, 0, true);
        await Run("fixed-hybrid-order0", SdfDiffuseTraceMode.Hybrid, 0, false);
        await Run("legacy-sdf-order0", SdfDiffuseTraceMode.SdfOnly, 0, true);
        await Run("fixed-sdf-order0", SdfDiffuseTraceMode.SdfOnly, 0, false);
        await Run("legacy-hybrid-order8", SdfDiffuseTraceMode.Hybrid, 8, true);
        await Run("fixed-hybrid-order8", SdfDiffuseTraceMode.Hybrid, 8, false);
        await Run("legacy-stress-sdf-order0", SdfDiffuseTraceMode.SdfOnly, 0, true, 0.005f);
        await Run("fixed-stress-sdf-order0", SdfDiffuseTraceMode.SdfOnly, 0, false, 0.005f);
        complete = true;
        return new { success = true, evidenceRoot, output, errors };
    }
    finally
    {
        active = false;
        forceSide.SetValue(null, previousForce);
        foreach (var entry in hooks) entry.Value.SetValue(null, previousHooks[entry.Key]);
        if (biasPass != null) biasField.SetValue(biasPass, previousBias);
        RenderPipelineManager.beginCameraRendering -= Begin;
        Application.logMessageReceived -= Log;
        AsyncGPUReadback.WaitAllRequests();
        volume.profile = previousProfile;
        foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
        if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
        UnityEngine.Object.DestroyImmediate(profile);
        try
        {
            WriteManifest();
        }
        finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
