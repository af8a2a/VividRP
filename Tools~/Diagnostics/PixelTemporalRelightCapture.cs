// Controlled live diagnostic, not a Unity Test Framework test. No assets are saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
{
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    string experiment = "temporal-relight";
    bool skyShift = experiment == "blocked-skyshift";
    bool blockedComparison = experiment == "blocked-relight" || skyShift;
    bool scroll = false;
    string output = blockedComparison ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..",
        "VividRP_GI_Evidence", (skyShift ? "BlockedTemporalSkyShift-" : "BlockedTemporalRelight-")
        + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), "capture"))
        : "E:/VividRP_Reborn/Logs/PixelTemporalEnergy-20261113/" + experiment;
    const int warmup = 512, captureStride = 1;
    int measuredFrames = 256;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    if (System.IO.Directory.Exists(output)) throw new InvalidOperationException("Refusing to overwrite " + output);
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded) throw new InvalidOperationException("Open the emissive Cornell fixture first.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var volume = roots.SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var originalPose = (camera.transform.position, camera.transform.rotation);
    var wasDirty = scene.isDirty;
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var previousScene = SceneManager.GetActiveScene();
    var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components)
        profile.components.Add(UnityEngine.Object.Instantiate(component));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    profile.TryGet<AutoExposure>(out var exposure);
    if (gi == null || exposure == null) throw new InvalidOperationException("Missing Cornell profile.");
    SkySettingsVolume skySettings = null;
    HDRISkyVolume hdri = null;
    Cubemap skyCube = null;
    if (skyShift)
    {
        if (!profile.TryGet(out skySettings)) skySettings = profile.Add<SkySettingsVolume>(true);
        if (!profile.TryGet(out hdri)) hdri = profile.Add<HDRISkyVolume>(true);
        hdri.active = true;
        skyCube = new Cubemap(16, TextureFormat.RGBAHalf, true) {name = "Directional distribution step sky", hideFlags = HideFlags.DontSave};
        var pixels = Enumerable.Repeat(new Color(200, 240, 280, 1), 256).ToArray();
        for (int face = 0; face < 6; face++) skyCube.SetPixels(pixels, (CubemapFace)face);
        skyCube.Apply(true, false);
        hdri.skyCubemap.Override(skyCube); hdri.skyIntensityMode.Override(SkyIntensityMode.Multiplier); hdri.multiplier.Override(1f);
        skySettings.updateMode.Override(SkyUpdateMode.OnChanged); skySettings.includeSunInBaking.Override(false);
        skySettings.skyType.Override(SkyType.None);
        gi.occludedSky.Override(true); gi.skyTraceDistance.Override(64f);
    }
    var type = typeof(SdfDiffuseGatherPass);
    var hooks = new[] { "EditorLinearCapture", "EditorTraceCapture", "EditorGatherStageCapture", "EditorGatherFrameOverride", "EditorRecordProbeAnchorDiagnostics", "EditorGatherHistoryDiagnostics" }
        .ToDictionary(n => n, n => type.GetField(n, stat));
    var previousHooks = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    var nativeCmd = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", priv);
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var game = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var records = new List<object>();
    var errors = new List<string>();
    int frame = 0, pending = 0;
    bool active = false;
    string arm = "";
    object pass = null;
    var emitter = roots.SelectMany(o => o.GetComponentsInChildren<MeshletRenderer>()).Single(r => r.name == "Ceiling Emitter - Downward Only");
    var originalProxies = emitter.materialProxies.ToArray();
    var proxy = UnityEngine.Object.Instantiate(originalProxies[0]);
    proxy.hideFlags = HideFlags.DontSave;
    var originalEmission = proxy.EmissionColor;
    var syncEmitter = typeof(MeshletRenderer).GetMethod("UpdateDatabaseIfNeeded", priv);
    float targetLevel = 1f;

    bool Sample() => active && frame >= warmup && (frame - warmup) % captureStride == 0;
    object Field(string name) => type.GetField(name, priv).GetValue(pass);
    Texture Resolve(string name)
    {
        var value = Field(name);
        var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        if (!handle.IsValid()) return null;
        return handle;
    }
    void ReadRegion(CommandBuffer cmd, Texture texture, string tag, int x, int y, int width, int height)
    {
        if (texture == null) throw new InvalidOperationException("Missing texture: " + tag);
        string path = output + "/" + arm + "/" + frame.ToString("0000") + "-" + tag + ".bin";
        records.Add(new { arm, frame, tag, x, y, width, height, format = texture.graphicsFormat.ToString(), path });
        pending++;
        cmd.RequestAsyncReadback(texture, 0, x, width, y, height, 0, 1, request => {
            try { if (request.hasError) errors.Add("GPU readback failed: " + path);
                else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray()); }
            catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }
    void Read(CommandBuffer cmd, Texture texture, string tag) => ReadRegion(cmd, texture, tag, 0, 0, texture.width, texture.height);
    void ReadPixels(CommandBuffer cmd, Texture texture, string tag)
    {
        if (scroll) ReadRegion(cmd, texture, tag + "-motion", 800, 350, 320, 256);
        else
        {
            ReadRegion(cmd, texture, tag + "-ceiling", 912, 970, 96, 48);
            ReadRegion(cmd, texture, tag + "-floor", 912, 130, 96, 64);
            ReadRegion(cmd, texture, tag + "-right", 1430, 500, 64, 96);
        }
    }
    void Begin(ScriptableRenderContext context, Camera current)
    {
        if (!active || current != camera) return;
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + frame);
        float level = frame >= warmup + 32 ? targetLevel : 1f;
        if (skyShift) skySettings.skyType.Override(frame >= warmup + 32 ? SkyType.HDRI : SkyType.None);
        Color linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? originalEmission.linear : originalEmission;
        linear = new Color(linear.r*level, linear.g*level, linear.b*level, 1f);
        Color requested = level == 1f ? originalEmission
            : QualitySettings.activeColorSpace == ColorSpace.Linear ? linear.gamma : linear;
        if (proxy.EmissionColor != requested) {proxy.EmissionColor = requested; syncEmitter.Invoke(emitter, null);}
        float t = scroll && frame >= warmup ? Mathf.Sin(2f * Mathf.PI * ((frame - warmup) % 128) / 128f) : 0f;
        camera.transform.SetPositionAndRotation(originalPose.Item1 + originalPose.Item2 * new Vector3(.28f*t, 0f, .06f*t),
            originalPose.Item2 * Quaternion.Euler(0f, -7f*t, 0f));
        if (!scroll && frame == warmup + measuredFrames - 1)
            typeof(VividAdditionalCameraData).GetMethod("RequestFinalFrameScreenshot", priv)?.Invoke(extra, new object[] {output + "/" + arm + ".png"});
    }
    void Log(string message, string stack, LogType kind)
    {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(message);
    }
    Action<ComputePassContext, int, Texture> stage = (context, index, texture) => {
        if (Sample() && context.Get<VividCameraData>().camera == camera)
            ReadPixels((CommandBuffer)nativeCmd.GetValue(context.cmd), texture, index == 0 ? "resolved" : "pixel-temporal");
    };
    Action<ComputePassContext, Texture, Texture, Texture> trace = (context, sources, lighting, geometry) => {
        if (!Sample() || context.Get<VividCameraData>().camera != camera) return;
        pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
        if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
            throw new InvalidOperationException("The experiment must remain 16px / 8 rays at 1920x1080.");
        ReadRegion(cmd, geometry, "probe-geometry", 48, 59, 24, 7);
        ReadRegion(cmd, Resolve("m_HitLighting"), "hit-lighting", 48, 59, 24, 7);
        ReadRegion(cmd, Resolve("m_Probes"), "anchor", 48, 59, 24, 7);
        ReadRegion(cmd, Resolve("m_FilteredProbes"), "sh", 0, 59, 2640, 7);
        if (blockedComparison && (bool)Field("m_FrozenGuideActive"))
            ReadRegion(cmd, Resolve("m_CurrentProposal"), "proposal", 48 * 8, 59, 24 * 8, 7);
        ReadPixels(cmd, Resolve("m_CurrentPixelLighting"), "history");
        if (scroll) ReadPixels(cmd, Resolve("m_CurrentPixelGeometry"), "geometry");
        if (frame == warmup || frame == warmup + measuredFrames - 1)
        {
            Read(cmd, Resolve("m_RadiosityInput"), "radiosity");
            if (!scroll) Read(cmd, lighting, "full-final");
        }
        var vp = (Matrix4x4)Field("m_ViewProjection");
        records.Add(new {arm, frame, vp = Enumerable.Range(0,16).Select(j=>vp[j/4,j%4]).ToArray()});
        var light = (Vector4)Field("m_LightColorShadow");
        var gpuScene = Field("m_Scene");
        var emission = (Vector4)gpuScene.GetType().GetField("m_EmissionMaximum", priv).GetValue(gpuScene);
        var lightScale = (Vector4)Field("m_RadiosityHistoryScale");
        records.Add(new {arm, frame, emission = new[]{emission.x, emission.y, emission.z},
            historyScale = new[]{lightScale.x, lightScale.y, lightScale.z}, rescale = Field("m_RadiosityRescale"),
            skyBound = Field("m_SkyRadianceBound"), skyBoundValid = Field("m_SkyBoundValid")});
        records.Add(new { arm, frame, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
            rays = 8, spacing = 16, traceMode = gi.traceMode.value.ToString(), radioSamples = Field("m_RadiositySamples"),
            radioTargetOrders = Field("m_RadiosityTargetOrders"), radioCursor = Field("m_RadiosityCursor"),
            directionalCache = Field("m_DirectionalCache"), restir = Field("m_RestirGather"),
            guide = Field("m_DirectionalGuide"), hierarchical = Field("m_DirectionalHierarchicalGuide"),
            samplingFlags = hooks["EditorGatherHistoryDiagnostics"].GetValue(null),
            frozenProposal = Field("m_FrozenGuideActive"), proposalHistoryValid = Field("m_ProposalHistoryValid"),
            rayBias = Field("m_Bias"), sun = new[] { light.x, light.y, light.z, light.w }, sky = Field("m_SkyTraceDistance") } });
    };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (Sample()) ReadPixels((CommandBuffer)nativeCmd.GetValue(context.cmd), lighting, "final");
        if (++frame >= warmup + measuredFrames) active = false;
    };
    async System.Threading.Tasks.Task Run(string name, SdfDiffuseTraceMode mode, int diagnostics, bool moving = false)
    {
        arm = name; frame = 0; scroll = moving;
        hooks["EditorGatherHistoryDiagnostics"].SetValue(null, diagnostics);
        System.IO.Directory.CreateDirectory(output + "/" + arm);
        gi.traceMode.Override(mode);
        gi.surfaceCacheBounceOrders.Override(8);
        gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous2);
        // Probe filters stay fixed: the raw/temporal/final taps isolate pixel reconstruction.
        extra.ResetPostProcessingHistory();
        active = true;
        double deadline = EditorApplication.timeSinceStartup + 1800;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline)
        {
            if (System.IO.File.Exists(output + "/cancel.txt")) throw new OperationCanceledException("Diagnostic cancelled by its owner.");
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending > 0 || errors.Count > 0) throw new InvalidOperationException(name + " failed at " + frame + ": " + string.Join("\n", errors));
        System.IO.File.WriteAllText(output + "/progress.txt", name + " complete");
    }
    EditorApplication.LockReloadAssemblies();
    try
    {
        emitter.SetMaterialProxies(new[]{proxy}); syncEmitter.Invoke(emitter, null);
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
        gi.recordRayDiagnostics.Override(false);
        gi.recordFirstHitDiagnostics.Override(false);
        gi.screenProbeSamplingGuide.Override(false);
        hooks["EditorLinearCapture"].SetValue(null, linear);
        hooks["EditorTraceCapture"].SetValue(null, trace);
        hooks["EditorGatherStageCapture"].SetValue(null, stage);
        hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null, true);
        RenderPipelineManager.beginCameraRendering += Begin;
        Application.logMessageReceived += Log;
        const int legacy = 2097152 | 4194304;
        gi.screenProbeSamplingGuide.Override(true);
        foreach (var step in skyShift ? new[]{("skyshift",0f)} : new[]{("up",4f),("down",.25f),("off",0f)})
        {
            targetLevel = step.Item2;
            await Run((blockedComparison ? "white" : "legacy") + "-light-" + step.Item1,
                SdfDiffuseTraceMode.Hybrid, blockedComparison ? 0 : legacy);
            await Run((blockedComparison ? "blocked" : "corrected") + "-light-" + step.Item1,
                SdfDiffuseTraceMode.Hybrid, blockedComparison ? 8388608 : 0);
        }
        return new { success = true, output, errors };
    }
    finally
    {
        active = false;
        emitter.SetMaterialProxies(originalProxies); syncEmitter.Invoke(emitter, null);
        UnityEngine.Object.DestroyImmediate(proxy);
        foreach (var entry in hooks) entry.Value.SetValue(null, previousHooks[entry.Key]);
        camera.transform.SetPositionAndRotation(originalPose.Item1, originalPose.Item2);
        RenderPipelineManager.beginCameraRendering -= Begin;
        Application.logMessageReceived -= Log;
        AsyncGPUReadback.WaitAllRequests();
        volume.profile = previousProfile;
        foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
        if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
        UnityEngine.Object.DestroyImmediate(profile);
        if (skyCube != null) UnityEngine.Object.DestroyImmediate(skyCube);
        try
        {
            System.IO.Directory.CreateDirectory(output);
            System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                experiment, blockedComparison, skyShift,
                width = 1920, height = 1080, spacing = 16, rays = 8, warmup, measuredFrames, captureStride,
                scene_dirty_before = wasDirty, scene_dirty_after = scene.isDirty,
                row_origin = "bottom_left", pre_exposure = 1.0, quantity = "conditional E/pi RGB + GI coverage alpha",
                camera = new { position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                    vertical_fov = camera.fieldOfView }, records, errors }, Newtonsoft.Json.Formatting.Indented));
        }
        finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
