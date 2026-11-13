// Live Editor diagnostic. No scene/profile assets are saved; run via unity command script eval.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const string output = "E:/VividRP_Reborn/Logs/LinearGI-20261113/unity";
    if (System.IO.Directory.Exists(output)) throw new InvalidOperationException("Refusing to overwrite " + output + "; choose a new output directory.");
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Editor/Resources/LinearGiExport.compute");
    if (compute == null) throw new InvalidOperationException("Linear GI shader unavailable.");
    int kernel = compute.FindKernel("ExportLinearGI");
    var originalScene = SceneManager.GetActiveScene();
    var previousCameras = Camera.allCameras.Select(c => (camera: c, enabled: c.enabled)).ToArray();
    bool ownScene = !SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath).isLoaded;
    EmissiveCornellBoxSceneBuilder.Open();
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var volume = roots.SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var component in (previousProfile ?? volume.sharedProfile).components)
        profile.components.Add(UnityEngine.Object.Instantiate(component));
    profile.TryGet<SdfDiffuseGISettings>(out var gi);
    profile.TryGet<AutoExposure>(out var exposure);
    if (gi == null || exposure == null) throw new InvalidOperationException("Linear GI fixture unavailable.");
    var type = typeof(SdfDiffuseGatherPass);
    var hook = type.GetField("EditorLinearCapture", stat);
    var previousHook = hook.GetValue(null);
    var nativeCmd = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", priv);
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var game = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var textures = new RenderTexture[4];
    var errors = new List<string>();
    int frame = 0, pending = 0, target = 0;
    bool active = false;
    string arm = "";
    string[] names = { "transport", "conditional", "albedo", "geometry" };
    string[] bindings = { "_LinearTransport", "_LinearConditional", "_LinearAlbedo", "_LinearGeometry" };
    void Log(string text, string stack, LogType kind)
    {
        if (text.Contains("on dx11") && text.Contains("uav", StringComparison.OrdinalIgnoreCase) && text.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && errors.Count < 12) errors.Add(text);
    }
    float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    Action<ComputePassContext, Texture, Texture, Texture, Texture> capture = (context, lighting, depth, rt0, rt1) =>
    {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (++frame < target) return;
        active = false;
        int width = lighting.width, height = lighting.height;
        var cameraData = context.Get<VividCameraData>();
        var view = cameraData.GetViewMatrix();
        var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
        cmd.SetComputeVectorParam(compute, "_LinearSize", new Vector4(width, height, 1f / width, 1f / height));
        cmd.SetComputeMatrixParam(compute, "_LinearInverseVP", cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true).inverse);
        cmd.SetComputeVectorParam(compute, "_LinearCameraForward", -view.GetRow(2));
        cmd.SetComputeFloatParam(compute, "_LinearFarDepth", SystemInfo.usesReversedZBuffer ? 0f : 1f);
        cmd.SetComputeTextureParam(compute, kernel, "_LinearGI", lighting);
        cmd.SetComputeTextureParam(compute, kernel, "_LinearDepth", depth);
        cmd.SetComputeTextureParam(compute, kernel, "_LinearGBuffer0", rt0);
        cmd.SetComputeTextureParam(compute, kernel, "_LinearGBuffer1", rt1);
        for (int i = 0; i < 4; i++)
        {
            textures[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { name = "Linear GI " + names[i], enableRandomWrite = true };
            textures[i].Create();
            cmd.SetComputeTextureParam(compute, kernel, bindings[i], textures[i]);
        }
        cmd.DispatchCompute(compute, kernel, (width + 7) / 8, (height + 7) / 8, 1);
        string directory = output + "/" + arm;
        System.IO.Directory.CreateDirectory(directory);
        var command = cmd;
        for (int i = 0; i < 4; i++)
        {
            string path = directory + "/" + names[i] + ".f32";
            pending++;
            command.RequestAsyncReadback(textures[i], 0, request =>
            {
                try
                {
                    if (request.hasError) errors.Add("Readback failed: " + path);
                    else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray());
                }
                catch (Exception e) { errors.Add(e.Message); }
                finally { pending--; }
            });
        }
        var pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
        object Field(string name) => type.GetField(name, priv).GetValue(pass);
        var metadata = new { schema = "vivid.linear_gi.raw.v1", engine = "VividRP", unity = Application.unityVersion,
            width, height, format = "RGBA32F little endian", frames = frame, pre_exposure = 1.0,
            graphics_api = SystemInfo.graphicsDeviceType.ToString(), raw_row_origin = "bottom_left (validated by geometry)",
            quantity = "irradiance_over_pi", rgb_space = "linear_sRGB_D65", receiving_albedo_in_rgb = false,
            transport_coverage_applied = true, conditional_coverage_applied = false, display_transform_applied = false,
            ao_strength = gi.nearOcclusionStrength.value, ev100 = exposure.fixedExposure.value,
            camera_position_unity_m = V(camera.transform.position), camera_euler = V(camera.transform.eulerAngles),
            vertical_fov_degrees = camera.fieldOfView, aspect = (float)width / height,
            radiosity_order = Field("m_RadiosityPublishedOrder"), radiosity_batch = Field("m_RadiosityBatch"),
            probes = "16px / 8 rays", receiver = "nonmetallic Cornell fixture", source_scene = scene.path,
            source_formats = new { lighting = lighting.graphicsFormat.ToString(), rt0 = rt0.graphicsFormat.ToString(), rt1 = rt1.graphicsFormat.ToString() },
            geometry_channels = "world normal xyz (Unity axes), eye depth metres", albedo_channels = "linear base color rgb, geometric validity a" };
        System.IO.File.WriteAllText(directory + "/raw-metadata.json", Newtonsoft.Json.JsonConvert.SerializeObject(metadata, Newtonsoft.Json.Formatting.Indented));
    };
    async System.Threading.Tasks.Task Run(string name, float ev, int warmup)
    {
        arm = name; frame = 0; target = warmup; active = true;
        exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
        exposure.fixedExposure.Override(ev);
        double deadline = EditorApplication.timeSinceStartup + 300;
        while (active && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; pending > 0 && i < 50; i++) await System.Threading.Tasks.Task.Delay(20);
        if (active || pending != 0 || errors.Count > 0) throw new InvalidOperationException("Linear capture " + arm + ": " + string.Join("\n", errors));
        foreach (var texture in textures) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        Array.Clear(textures, 0, textures.Length);
    }
    EditorApplication.LockReloadAssemblies();
    try
    {
        volume.profile = profile;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
        gi.nearOcclusionStrength.Override(0f);
        gi.realtimeOnly.Override(true);
        gi.pixelTemporal.Override(true);
        Application.logMessageReceived += Log;
        hook.SetValue(null, capture);
        await Run("ev4", 4f, 512);
        await Run("ev6", 6f, 256);
        return new { success = true, output, errors };
    }
    finally
    {
        active = false;
        hook.SetValue(null, previousHook);
        Application.logMessageReceived -= Log;
        AsyncGPUReadback.WaitAllRequests();
        foreach (var texture in textures) if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        volume.profile = previousProfile;
        foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component);
        UnityEngine.Object.DestroyImmediate(profile);
        if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
        if (ownScene && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        foreach (var item in previousCameras) if (item.camera != null) item.camera.enabled = item.enabled;
        EditorApplication.UnlockReloadAssemblies();
    }
}
