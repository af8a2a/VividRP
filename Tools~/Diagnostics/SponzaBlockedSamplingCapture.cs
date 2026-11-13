// Controlled live diagnostic, not a Unity Test Framework test. No assets are saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    const string experiment = "sponza-blocked", candidate = "blocked";
    const int warmup = 512, measuredFrames = 256, captureStride = 1, frameBase = 1733;
    bool scroll = false, complete = false;
    const string scenePath = "Assets/ClassicSponza/Scenes/SponzaNoon.unity";
    const string lightPath = "Assets/ClassicSponza/Scenes/SponzaLightingNoon.unity";
    const string graphPath = "Assets/ClassicSponza/Profiles/Sponza Noon Realtime GI/Sponza Noon Realtime GI.vrdg";
    var pipeline = GraphicsSettings.currentRenderPipeline as VividRenderPipelineAsset;
    if (pipeline == null || pipeline.RenderGraphAsset != AssetDatabase.LoadAssetAtPath<RenderGraphData>(graphPath))
        throw new InvalidOperationException("Activate the Sponza realtime GI graph first.");
    string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
        "SponzaBlockedSampling-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), "capture"));
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    if (System.IO.Directory.Exists(output)) throw new InvalidOperationException("Refusing to overwrite " + output);
    System.IO.Directory.CreateDirectory(output);
    var previousScene = SceneManager.GetActiveScene();
    var originalSun = RenderSettings.sun;
    var scene = SceneManager.GetSceneByPath(scenePath);
    var lights = SceneManager.GetSceneByPath(lightPath);
    bool ownScene = !scene.isLoaded, ownLights = !lights.isLoaded;
    try
    {
        if (ownScene) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        if (ownLights) lights = EditorSceneManager.OpenScene(lightPath, OpenSceneMode.Additive);
        var hidden = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
            .Where(s => s.isLoaded && s != scene && s != lights).SelectMany(s => s.GetRootGameObjects())
            .Select(o => (root: o, active: o.activeSelf)).ToArray();
        var lightRoots = lights.GetRootGameObjects();
        var sun = lightRoots.SelectMany(o => o.GetComponentsInChildren<Light>(true))
            .First(l => l.type == LightType.Directional && l.isActiveAndEnabled);
        var roots = scene.GetRootGameObjects();
        var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>(true)).Single(c => c.name == "Camera_1");
        var volume = roots.Concat(lightRoots).SelectMany(o => o.GetComponentsInChildren<Volume>(true))
            .Single(v => v.sharedProfile != null && v.sharedProfile.name == "Sponza Noon Profile");
        var extra = camera.GetComponent<VividAdditionalCameraData>();
        var originalAA = extra.antialiasing;
        var originalPose = (camera.transform.position, camera.transform.rotation);
        var wasDirty = scene.isDirty;
        var lightsWereDirty = lights.isDirty;
        var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
        var cameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
        var cameraWasEnabled = camera.enabled;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        foreach (var component in (previousProfile ?? volume.sharedProfile).components)
            profile.components.Add(UnityEngine.Object.Instantiate(component));
        profile.TryGet<SdfDiffuseGISettings>(out var gi);
        profile.TryGet<AutoExposure>(out var exposure);
        if (gi == null || exposure == null || gi.surfaceCache.value == null) throw new InvalidOperationException("Missing Sponza GI fixture.");
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
        Light previousTargetSun = null;

        bool Sample() => active && arm != "prime" && frame >= warmup && (frame - warmup) % captureStride == 0;
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
            if (scroll) ReadRegion(cmd, texture, tag + "-motion", 500, 250, 920, 480);
            else
            {
                ReadRegion(cmd, texture, tag + "-left", 430, 300, 160, 240);
                ReadRegion(cmd, texture, tag + "-right", 1330, 300, 160, 240);
                ReadRegion(cmd, texture, tag + "-center", 880, 380, 160, 160);
            }
        }

        void Begin(ScriptableRenderContext context, Camera current)
        {
            if (!active || current != camera) return;
            hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + frame);
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
            if (!active || context.Get<VividCameraData>().camera != camera) return;
            pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
            if (arm == "prime")
            {
                if (frame >= warmup && (int)Field("m_RadiosityPublishedOrder") == 2 && (int)Field("m_RadiosityBatch") == 32)
                    active = false;
                return;
            }
            if (!Sample()) return;
            if ((int)Field("m_RadiosityPublishedOrder") != 2 || (int)Field("m_RadiosityBatch") != 32)
                throw new InvalidOperationException("Source radiosity did not remain converged.");
            var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
            if (geometry.width != 120 || geometry.height != 136 || lighting.width != 1920 || lighting.height != 1080)
                throw new InvalidOperationException("The experiment must remain 16px / 8 rays at 1920x1080.");
            if ((bool)Field("m_FrozenGuideActive"))
                ReadRegion(cmd, Resolve("m_CurrentProposal"), "proposal", 48 * 8, 24, 24 * 8, 16);
            if (!scroll) ReadPixels(cmd, Resolve("m_CurrentPixelGeometry"), "geometry");
            if (scroll)
            {
                ReadPixels(cmd, Resolve("m_CurrentPixelLighting"), "history");
                ReadPixels(cmd, Resolve("m_CurrentPixelGeometry"), "geometry");
            }
            if (frame == warmup || frame == warmup + measuredFrames - 1)
            {
                Read(cmd, Resolve("m_RadiosityInput"), "radiosity");
                if (!scroll) Read(cmd, lighting, "full-final");
            }
            var vp = (Matrix4x4)Field("m_ViewProjection");
            records.Add(new {arm, frame, vp = Enumerable.Range(0,16).Select(j=>vp[j/4,j%4]).ToArray()});
            var light = (Vector4)Field("m_LightColorShadow");
            records.Add(new { arm, frame, state = new { order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
                rays = 8, spacing = 16, traceMode = gi.traceMode.value.ToString(), radioSamples = Field("m_RadiositySamples"),
                radioTargetOrders = Field("m_RadiosityTargetOrders"), radioCursor = Field("m_RadiosityCursor"),
                directionalCache = Field("m_DirectionalCache"), restir = Field("m_RestirGather"),
                guide = Field("m_DirectionalGuide"), hierarchical = Field("m_DirectionalHierarchicalGuide"),
                frozenProposal = Field("m_FrozenGuideActive"), proposalHistoryValid = Field("m_ProposalHistoryValid"),
                spatialKernel = Field("m_ProbeSpatialKernel").ToString(), spatialAnchor = Field("m_SpatialAnchor"),
                samplingFlags = hooks["EditorGatherHistoryDiagnostics"].GetValue(null),
                rayBias = Field("m_Bias"), sun = new[] { light.x, light.y, light.z, light.w }, sky = Field("m_SkyTraceDistance") } });
        };
        Action<ComputePassContext, Texture, Texture, Texture, Texture> linear = (context, lighting, depth, rt0, rt1) => {
            if (!active || context.Get<VividCameraData>().camera != camera) return;
            if (Sample()) ReadPixels((CommandBuffer)nativeCmd.GetValue(context.cmd), lighting, "final");
            if (++frame >= (arm == "prime" ? 8192 : warmup + measuredFrames)) active = false;
        };
        async System.Threading.Tasks.Task Run(string name, SdfDiffuseTraceMode mode, bool legacy, bool moving = false)
        {
            arm = name; frame = 0; scroll = moving;
            hooks["EditorGatherHistoryDiagnostics"].SetValue(null, legacy ? 0 : 8388608);
            System.IO.Directory.CreateDirectory(output + "/" + arm);
            gi.traceMode.Override(mode);
            gi.surfaceCacheBounceOrders.Override(2);
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
            System.IO.File.WriteAllText(output + "/capture-progress.json", Newtonsoft.Json.JsonConvert.SerializeObject(
                new { completedArm = name, complete = false, records, errors }, Newtonsoft.Json.Formatting.Indented));
        }
        EditorApplication.LockReloadAssemblies();
        try
        {
            volume.profile = profile;
            foreach (var entry in hidden) if (entry.root != null) entry.root.SetActive(false);
            SceneManager.SetActiveScene(scene);
            previousTargetSun = RenderSettings.sun;
            RenderSettings.sun = sun;
            camera.enabled = true;
            extra.antialiasing = VividAntialiasingMode.None;
            foreach (var c in cameras) c.camera.enabled = c.camera == camera;
            exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
            exposure.fixedExposure.Override(10f);
            if (profile.TryGet<LocalExposure>(out var localExposure)) localExposure.active = false;
            gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
            gi.nearOcclusionStrength.Override(0f);
            gi.realtimeOnly.Override(true);
            gi.pixelTemporal.Override(true);
            gi.pixelHistoryMaxAge.Override(32);
            gi.screenProbeSpatialKernel.Override(SdfScreenProbeSpatialKernel.Adaptive5x5);
            gi.screenProbeHitDistanceFilter.Override(true);
            gi.correctReceiverSelfHits.Override(true);
            gi.surfaceCacheRadiositySamples.Override(64);
            gi.surfaceCacheRadiosityAccumulate.Override(true);
            gi.surfaceCacheRadiosityBatchLimit.Override(32);
            gi.recordRayDiagnostics.Override(false);
            gi.recordFirstHitDiagnostics.Override(false);
            gi.surfaceCacheCardsPerFrame.Override(128);
            gi.surfaceCacheFullUpdateCardLimit.Override(128);
            gi.screenProbeDirectionalCache.Override(true);
            gi.screenProbeSamplingGuide.Override(true);
            gi.screenProbeHierarchicalGuide.Override(true);
            gi.screenProbeSpatialAnchor.Override(true);
            hooks["EditorLinearCapture"].SetValue(null, linear);
            hooks["EditorTraceCapture"].SetValue(null, trace);
            hooks["EditorGatherStageCapture"].SetValue(null, stage);
            hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null, true);
            RenderPipelineManager.beginCameraRendering += Begin;
            Application.logMessageReceived += Log;
            await Run("prime", SdfDiffuseTraceMode.Hybrid, true);
            if ((int)Field("m_RadiosityPublishedOrder") != 2 || (int)Field("m_RadiosityBatch") != 32)
                throw new InvalidOperationException("Sponza radiosity did not converge within the prime frame limit.");
            await Run("white-hybrid", SdfDiffuseTraceMode.Hybrid, true);
            await Run("blocked-hybrid", SdfDiffuseTraceMode.Hybrid, false);
            await Run("white-motion", SdfDiffuseTraceMode.Hybrid, true, true);
            await Run("blocked-motion", SdfDiffuseTraceMode.Hybrid, false, true);
            complete = true;
            return new { success = true, output, errors };
        }
        finally
        {
            active = false;
            foreach (var entry in hooks) entry.Value.SetValue(null, previousHooks[entry.Key]);
            camera.transform.SetPositionAndRotation(originalPose.Item1, originalPose.Item2);
            extra.antialiasing = originalAA;
            RenderPipelineManager.beginCameraRendering -= Begin;
            Application.logMessageReceived -= Log;
            AsyncGPUReadback.WaitAllRequests();
            volume.profile = previousProfile;
            camera.enabled = cameraWasEnabled;
            RenderSettings.sun = previousTargetSun;
            foreach (var entry in hidden) if (entry.root != null) entry.root.SetActive(entry.active);
            foreach (var c in cameras) if (c.camera != null) c.camera.enabled = c.enabled;
            if (previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (RenderSettings.sun != originalSun) RenderSettings.sun = originalSun;
            foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(profile);
            try
            {
                System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    experiment, candidate, frameBase, complete,
                    scenePath, lightPath, original_scene_dirty = wasDirty, original_lights_dirty = lightsWereDirty,
                    lights_dirty_after = lights.isDirty,
                    track = new { periodFrames = 128, lateralMeters = .28, forwardMeters = .06, yawDegrees = -7 },
                    width = 1920, height = 1080, spacing = 16, rays = 8, warmup, measuredFrames, captureStride,
                    scene_dirty_before = wasDirty, scene_dirty_after = scene.isDirty,
                    row_origin = "bottom_left", pre_exposure = 1.0, quantity = "conditional E/pi RGB + GI coverage alpha",
                    camera = new { position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                        vertical_fov = camera.fieldOfView }, records, errors }, Newtonsoft.Json.Formatting.Indented));
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }
    }
    finally
    {
        if (ownLights && lights.isLoaded) EditorSceneManager.CloseScene(lights, true);
        if (ownScene && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
    }
}
