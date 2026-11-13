// Live GPU timing without texture readbacks. Shared markers are accepted only at count == 1.
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    const int warmup = 512, measuredFrames = 256;
    var type = typeof(SdfDiffuseGatherPass);
    var hookNames = new[] {"EditorLinearCapture", "EditorTraceCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride", "EditorGatherHistoryDiagnostics", "EditorRecordProbeAnchorDiagnostics"};
    var hooks = hookNames.ToDictionary(n => n, n => type.GetField(n, stat));
    var saved = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string n in hookNames.Take(3)) if (saved[n] != null) throw new InvalidOperationException("Another capture is active: " + n);
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded) throw new InvalidOperationException("Open the emissive Cornell fixture first.");
    var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var volume = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var previousScene = SceneManager.GetActiveScene();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var samplerNames = new[] {"DirectionalGuideSampler", "ScreenTraceSampler", "GatherSampler", "TotalSampler"};
    var samplers = samplerNames.Select(n => (ProfilingSampler)type.GetField(n, stat).GetValue(null)).ToArray();
    var gpuField = typeof(ProfilingSampler).GetField("m_GpuRecorder", priv | System.Reflection.BindingFlags.Public);
    var cpuField = typeof(ProfilingSampler).GetField("m_Recorder", priv | System.Reflection.BindingFlags.Public);
    if (gpuField == null || cpuField == null) throw new InvalidOperationException("CoreRP was compiled without GPU recorder support.");
    var driver = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditorInternal.ProfilerDriver", false)).FirstOrDefault(t => t != null);
    if (driver == null) throw new InvalidOperationException("Unity GPU profiler driver is unavailable.");
    var driverFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    var enabledProperty = driver.GetProperty("enabled", driverFlags);
    var gpuProperty = driver.GetProperty("profileGPU", driverFlags);
    var frameProperty = driver.GetProperty("lastFrameIndex", driverFlags);
    if (enabledProperty == null || gpuProperty == null || frameProperty == null)
        throw new InvalidOperationException("GPU profiler publication APIs are unavailable.");
    bool profilerWasEnabled = (bool)enabledProperty.GetValue(null);
    bool gpuWasEnabled = (bool)gpuProperty.GetValue(null);
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components) profile.components.Add(UnityEngine.Object.Instantiate(component));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    profile.TryGet<AutoExposure>(out var exposure);
    var wasRecording = samplers.Select(s => { var r = (ProfilerRecorder)cpuField.GetValue(s); return r.Valid && r.IsRunning; }).ToArray();
    var game = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
        "BlockedTemporalGpu-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
    System.IO.Directory.CreateDirectory(root);
    var records = new List<object>();
    var errors = new List<string>();
    int frame = 0;
    bool active = false;
    string arm = "";
    void Begin(ScriptableRenderContext context, Camera current)
    {
        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + frame);
    }
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (context.Get<VividCameraData>().camera != camera) return;
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("Timing requires the fixed 16px / 8-ray, 1920x1080 fixture.");
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (frame >= warmup)
        {
            var samples = new List<object>();
            for (int i = 0; i < samplers.Length; i++)
            {
                var r = (ProfilerRecorder)gpuField.GetValue(samplers[i]);
                bool valid = r.Valid && r.IsRunning && r.Count > 0;
                var sample = valid ? r.GetSample(0) : default;
                samples.Add(new {name = samplerNames[i], valid, count = sample.Count, milliseconds = sample.Value * 1e-6});
            }
            records.Add(new {arm, frame, engineFrame = Time.frameCount, profilerFrame = frameProperty.GetValue(null), samples});
        }
        if (++frame >= warmup + measuredFrames) active = false;
    };
    void Log(string message, string stack, LogType kind)
    {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    async System.Threading.Tasks.Task Run(string name, bool blocked)
    {
        arm = name; frame = 0;
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, blocked ? 8388608 : 0);
        extra.ResetPostProcessingHistory();
        active = true;
        double deadline = EditorApplication.timeSinceStartup + 300;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        if (active || errors.Count != 0) throw new InvalidOperationException(name + " failed at frame " + frame);
        System.IO.File.WriteAllText(root + "/progress.txt", name + " complete");
    }
    EditorApplication.LockReloadAssemblies();
    try
    {
        volume.profile = profile; SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        exposure.exposureMode.Override(AutoExposureExposureMode.Fixed); exposure.fixedExposure.Override(4f);
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);
        gi.realtimeOnly.Override(true); gi.pixelTemporal.Override(true); gi.nearOcclusionStrength.Override(0f);
        gi.surfaceCacheBounceOrders.Override(8); gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityAccumulate.Override(true); gi.surfaceCacheRadiosityBatchLimit.Override(32);
        gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous2);
        gi.recordRayDiagnostics.Override(false); gi.recordFirstHitDiagnostics.Override(false);
        hooks["EditorTraceCapture"].SetValue(null, trace); hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorGatherStageCapture"].SetValue(null, null); hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null, false);
        gpuProperty.SetValue(null, true); enabledProperty.SetValue(null, true);
        foreach (var sampler in samplers) sampler.enableRecording = true;
        RenderPipelineManager.beginCameraRendering += Begin; Application.logMessageReceived += Log;
        await Run("white-a1", false); await Run("blocked-b1", true); await Run("blocked-b2", true); await Run("white-a2", false);
        return new {root, records = records.Count, errors};
    }
    finally
    {
        active = false;
        RenderPipelineManager.beginCameraRendering -= Begin; Application.logMessageReceived -= Log;
        foreach (var h in hooks) h.Value.SetValue(null, saved[h.Key]);
        volume.profile = previousProfile;
        foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
        if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        try
        {
            for (int i = 0; i < samplers.Length; i++) samplers[i].enableRecording = wasRecording[i];
            enabledProperty.SetValue(null, profilerWasEnabled); gpuProperty.SetValue(null, gpuWasEnabled);
        }
        finally
        {
            foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(profile);
            try { System.IO.File.WriteAllText(root + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                warmup, measuredFrames, width = 1920, height = 1080, rays = 8, spacing = 16, textureReadbacks = false,
                scope = "shared Unity GPU markers; use count=1 and reject unavailable samples", records, errors
            }, Newtonsoft.Json.Formatting.Indented)); }
            finally { EditorApplication.UnlockReloadAssemblies(); }
        }
    }
}
