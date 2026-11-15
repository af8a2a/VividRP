// Check actual GPU marker availability before scheduling an anchor A/B.
// Owned GPU-only recorders; no shared sampler changes, images or saved assets.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorBindResolveProbe", "EditorAtrousDispatchCapture" })
        if (type.GetField(name, stat).GetValue(null) != null) throw new InvalidOperationException("Another capture is active: " + name);
    if ((int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null) != -1
        || (int)type.GetField("EditorGatherHistoryDiagnostics", stat).GetValue(null) != 0
        || (int)type.GetField("EditorProbeAnchorMaxAgeOverride", stat).GetValue(null) != -1)
        throw new InvalidOperationException("An anchor experiment is active.");
    if (UnityEngine.Profiling.Profiler.enableBinaryLog) throw new InvalidOperationException("A binary Profiler recording is active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("The clean Cornell fixture must be loaded.");
    var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    var pass = ((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null))
        .Cast<object>().Single(p => p.GetType() == type);
    object Field(string name) => type.GetField(name, inst).GetValue(pass);
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var driver = typeof(UnityEditorInternal.ProfilerDriver);
    var getArea = driver.GetMethods().Single(m => m.Name == "IsAreaEnabled" && m.GetParameters().Length == 1);
    var setArea = driver.GetMethods().Single(m => m.Name == "SetAreaEnabled" && m.GetParameters().Length == 2);
    object gpuArea = Enum.Parse(getArea.GetParameters()[0].ParameterType, "GPU");
    bool previousArea = (bool)getArea.Invoke(null, new[] { gpuArea });
    var editorProfileProperty = driver.GetProperty("profileEditor");
    bool previousProfileEditor = (bool)editorProfileProperty.GetValue(null);
    bool previousProfiler = UnityEngine.Profiling.Profiler.enabled;
    bool playMode = Application.isPlaying;
    var previousScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    string[] names = { "Diffuse GI Total", "Diffuse GI Gather and SDF Fallback", "Diffuse GI Screen Trace",
        "Screen Probe Convert to Irradiance", "Screen Probe Anchor Prefilter", "Screen Probe Resolve", "Screen Probe Pixel Temporal" };
    var recorders = new ProfilerRecorder[names.Length];
    int rendered = 0, targetRenders = 0, otherRenders = 0;
    bool active = false, complete = false;
    var errors = new List<string>(); var events = new List<object>(); var samples = new List<object>();
    string output = "E:/VividRP_GI_Evidence/ScreenProbeAnchorGpuTimingProbe-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    void End(ScriptableRenderContext context, Camera current) {
        if (!active) return;
        if (current == camera) targetRenders++; else otherRenders++;
    }
    async System.Threading.Tasks.Task Render(int count) {
        int begin = targetRenders;
        double deadline = EditorApplication.timeSinceStartup + 120;
        while (targetRenders < begin + count && EditorApplication.timeSinceStartup < deadline) {
            if (!playMode) { game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); }
            await System.Threading.Tasks.Task.Delay(16);
        }
        if (targetRenders < begin + count) throw new InvalidOperationException("Timing probe did not render enough frames.");
        rendered += targetRenders-begin;
    }
    EditorApplication.LockReloadAssemblies();
    try {
        SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        setArea.Invoke(null, new object[] { gpuArea, true });
        editorProfileProperty.SetValue(null, !playMode);
        UnityEngine.Profiling.Profiler.enabled = true;
        RenderPipelineManager.endCameraRendering += End; active = true;
        await Render(64);
        if ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32
            || (int)Field("m_RadiosityUpdateCards") != 0 || (int)Field("m_ProbeAnchorMaxAge") != 128
            || (bool)Field("m_RestirGather")) throw new InvalidOperationException("Expected restored idle native-Gather source.");
        const ProfilerRecorderOptions options = ProfilerRecorderOptions.StartImmediately
            | ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.GpuRecorder;
        for (int i = 0; i < names.Length; ++i) recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, names[i], 256, options);
        int initialTarget = targetRenders, initialOther = otherRenders;
        await Render(192);
        for (int i = 0; i < names.Length; ++i) {
            int countBeforeStop = recorders[i].Count;
            long lastNanosecondsBeforeStop = recorders[i].LastValue;
            var values = new List<ProfilerRecorderSample>(); recorders[i].CopyTo(values);
            recorders[i].Stop();
            samples.Add(new { marker = names[i], recorders[i].Valid, recorders[i].Count, recorders[i].WrappedAround,
                countBeforeStop, lastNanosecondsBeforeStop,
                values = values.Select(s => new { nanoseconds = s.Value, count = s.Count }).ToArray() });
        }
        events.Add(new { targetRenders = targetRenders-initialTarget, otherRenders = otherRenders-initialOther,
            order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"), updateCards = Field("m_RadiosityUpdateCards") });
        complete = true; return new { output, complete, targetRenders, otherRenders, rendered };
    }
    catch (Exception e) { errors.Add(e.ToString()); throw; }
    finally {
        try {
            active = false; RenderPipelineManager.endCameraRendering -= End;
            for (int i = 0; i < recorders.Length; ++i) if (recorders[i].Valid) recorders[i].Dispose();
            UnityEngine.Profiling.Profiler.enabled = previousProfiler;
            editorProfileProperty.SetValue(null, previousProfileEditor);
            setArea.Invoke(null, new object[] { gpuArea, previousArea });
            foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
            if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            System.IO.File.WriteAllText(output + "/probe.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                schema = "anchor-gpu-timing-probe.v3", complete, errors, samples, events, targetRenders, otherRenders, rendered, playMode,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
                previousProfiler, previousArea, previousProfileEditor,
                restoredProfileEditor = editorProfileProperty.GetValue(null),
                restoredProfiler = UnityEngine.Profiling.Profiler.enabled, restoredGpuArea = getArea.Invoke(null, new[] { gpuArea })
            }, Newtonsoft.Json.Formatting.Indented));
        }
        finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
