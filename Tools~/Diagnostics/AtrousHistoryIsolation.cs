// Short live resource-identity diagnostic. Does not save scene or profile assets.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    // false: 48 identity-only frames; true: a warm source and 2/2/3/2 cold-start inputs.
    bool captureInputs = true;
    bool filterOnly = true; // Zero radiosity avoids waiting for a source rebuild during operator isolation.
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var type = typeof(SdfDiffuseGatherPass);
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Open the clean emissive Cornell fixture.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var volume = roots.SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components)
        profile.components.Add(UnityEngine.Object.Instantiate(component));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    if (gi == null) throw new InvalidOperationException("Cornell GI profile missing.");
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var previousScene = SceneManager.GetActiveScene();
    var hooks = new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride", "EditorGatherHistoryDiagnostics", "EditorRecordProbeAnchorDiagnostics" }
        .ToDictionary(n => n, n => type.GetField(n, stat));
    var saved = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (var name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture" })
        if (saved[name] != null) throw new InvalidOperationException("Another GI diagnostic is active.");
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var rows = new List<object>();
    var errors = new List<string>();
    int frame = 0, step = 0, frameLimit = 6, pending = 0;
    bool active = false, complete = false;
    string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
        "AtrousHistoryIsolation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
    System.IO.Directory.CreateDirectory(output);
    object firstPass = null, firstState = null, firstScene = null;
    string[] textures = type.GetFields(inst).Where(f => f.FieldType == typeof(RenderGraphTexture)).Select(f => f.Name).ToArray();
    var nativeCmd = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    Texture Resolve(object pass, string name)
    {
        var value = type.GetField(name, inst).GetValue(pass);
        if (value == null) return null;
        var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        return handle.IsValid() ? handle : null;
    }
    void Read(CommandBuffer cmd, Texture texture, string tag, int x, int y, int width, int height)
    {
        if (texture == null) throw new InvalidOperationException("Missing diagnostic texture: " + tag);
        string path = output + "/" + step + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        pending++;
        cmd.RequestAsyncReadback(texture, 0, x, width, y, height, 0, 1, request => {
            try {
                if (request.hasError) errors.Add("Readback failed: " + path);
                else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray());
            } finally { pending--; }
        });
    }
    void Begin(ScriptableRenderContext context, Camera current)
    {
        if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + frame);
    }
    void Log(string message, string stack, LogType kind)
    {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message + "\n" + stack);
    }
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (step < 0) { if (++frame == frameLimit) active = false; return; }
        var pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        object Field(string name) => type.GetField(name, inst).GetValue(pass);
        var state = Field("m_State");
        var gpuScene = Field("m_Scene");
        firstPass ??= pass; firstState ??= state; firstScene ??= gpuScene;
        var textureRows = new List<object>();
        foreach (string name in textures)
        {
            Texture texture = Resolve(pass, name);
            textureRows.Add(new { name, id = texture != null ? texture.GetEntityId().ToString() : "invalid",
                native = texture != null ? texture.GetNativeTexturePtr().ToInt64() : 0,
                format = texture != null ? texture.graphicsFormat.ToString() : "invalid",
                width = texture != null ? texture.width : 0, height = texture != null ? texture.height : 0 });
        }
        rows.Add(new { step, frame, iterations = Field("m_AtrousIterations"),
            samePass = object.ReferenceEquals(firstPass, pass), sameState = object.ReferenceEquals(firstState, state),
            sameScene = object.ReferenceEquals(firstScene, gpuScene),
            state = new[] { "Frame", "RadiosityPublishedOrder", "RadiosityBatch", "RadiosityCursor", "TraceMode" }
                .ToDictionary(n => n, n => state.GetType().GetField(n, inst).GetValue(state)),
            validity = new[] { "m_HasHistory", "m_DirectionalHistoryValid", "m_DirectionalGuideHistoryValid" }
                .ToDictionary(n => n, n => Field(n)), textures = textureRows });
        if (captureInputs)
        {
            var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
            Read(cmd, lighting, "final-contact", 720, 64, 64, 64);
            Read(cmd, Resolve(pass, "m_RawResolved"), "coarse-input-contact", 720, 64, 64, 64);
            if (frame == frameLimit - 1) Read(cmd, Resolve(pass, "m_GBuffer1"), "normals-contact", 712, 56, 80, 80);
            foreach (var item in new[] { ("m_DirectionalGuideTexture", "proposal", 8), ("m_HitLighting", "hit", 1),
                ("m_CurrentDirectionalFiltered", "filtered", 16), ("m_Geometry", "geometry", 1) })
                Read(cmd, Resolve(pass, item.Item1), item.Item2, 48 * item.Item3, 59, 24 * item.Item3, 7);
            if (!filterOnly && (frame == 0 || frame == frameLimit - 1))
            {
                Texture atlas = Resolve(pass, "m_RadiosityInput");
                Read(cmd, atlas, "radiosity", 0, 0, atlas.width, atlas.height);
            }
        }
        if (++frame == frameLimit) active = false;
    };
    Action<ComputePassContext, int, Texture> stage = (context, index, texture) => {
        if (!captureInputs || !active || step < 0 || context.Get<VividCameraData>().camera != camera) return;
        var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
        Read(cmd, texture, index == 0 ? "resolved" : "temporal", 912, 970, 96, 48);
    };
    EditorApplication.LockReloadAssemblies();
    try
    {
        volume.profile = profile;
        SceneManager.SetActiveScene(scene);
        foreach (var c in cameras) c.camera.enabled = c.camera == camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
        gi.surfaceCacheRadiositySamples.Override(64);
        gi.surfaceCacheRadiosityBatchLimit.Override(32);
        gi.surfaceCacheBounceOrders.Override(filterOnly ? 0 : 8);
        gi.surfaceCacheRadiosityAccumulate.Override(true);
        gi.pixelTemporal.Override(true);
        gi.traceMode.Override(SdfDiffuseTraceMode.SdfOnly);
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);
        hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null, true);
        hooks["EditorTraceCapture"].SetValue(null, trace);
        if (captureInputs) hooks["EditorGatherStageCapture"].SetValue(null, stage);
        RenderPipelineManager.beginCameraRendering += Begin;
        Application.logMessageReceived += Log;
        for (step = captureInputs ? -1 : 0; step < (captureInputs ? 4 : 8); step++)
        {
            bool three = captureInputs ? step == 2 : (step & 1) != 0;
            gi.spatialDenoise.Override(three ? SdfDiffuseSpatialDenoise.Atrous3 : SdfDiffuseSpatialDenoise.Atrous2);
            if (captureInputs || step >= 4) extra.ResetPostProcessingHistory();
            frameLimit = captureInputs ? step < 0 ? filterOnly ? 8 : 512 : 64 : 6;
            if (step >= 0) System.IO.Directory.CreateDirectory(output + "/" + step);
            frame = 0; active = true;
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline)
            {
                game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                await System.Threading.Tasks.Task.Delay(16);
            }
            AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
            if (active || errors.Count != 0) throw new InvalidOperationException("Identity diagnostic failed: " + string.Join("\n", errors));
        }
        complete = true;
        return new { output, frames = rows.Count, errors };
    }
    finally
    {
        active = false;
        AsyncGPUReadback.WaitAllRequests();
        foreach (var entry in hooks) entry.Value.SetValue(null, saved[entry.Key]);
        RenderPipelineManager.beginCameraRendering -= Begin;
        Application.logMessageReceived -= Log;
        volume.profile = previousProfile;
        foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
        if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
        UnityEngine.Object.DestroyImmediate(profile);
        System.IO.File.WriteAllText(output + "/identity.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { complete, captureInputs, filterOnly, rows, errors }, Newtonsoft.Json.Formatting.Indented));
        EditorApplication.UnlockReloadAssemblies();
    }
}
