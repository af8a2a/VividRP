// Run through unity command script eval --file ...; this is a live rendering diagnostic, not a Unity test.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven;
using VividRP.Editor.Tools;
{
    const string output = "E:/VividRP_Reborn/Logs/EmissiveCornellBox-20261112";
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded) throw new InvalidOperationException("Open the emissive Cornell fixture first.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var extra = camera.GetComponent<VividAdditionalCameraData>();
    var otherCameras = Camera.allCameras.Where(c => c != camera).Select(c => (camera: c, enabled: c.enabled)).ToArray();
    var emitter = roots.SelectMany(o => o.GetComponentsInChildren<MeshletRenderer>()).Single(r => r.name.StartsWith("Ceiling Emitter"));
    var proxy = emitter.materialProxies.Single();
    var material = proxy.SourceMaterial;
    Color originalEmission = proxy.EmissionColor, originalMaterialEmission = material.GetColor("_EmissionColor");
    if (originalEmission.maxColorComponent <= 0 || material.GetTexture("_EmissionMap") == null)
        throw new InvalidOperationException("The fixture needs a persistent white emission texture and nonzero emission.");
    bool proxyDirty = EditorUtility.IsDirty(proxy), materialDirty = EditorUtility.IsDirty(material);
    var type = typeof(SdfDiffuseGatherPass);
    var hook = type.GetField("EditorTraceCapture", stat);
    var previousHook = hook.GetValue(null);
    var passes = typeof(PassRecorder).GetField("s_RenderPasses", stat);
    var nativeCmd = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", priv);
    var sync = typeof(MeshletRenderer).GetMethod("UpdateDatabaseIfNeeded", priv);
    var screenshot = typeof(VividAdditionalCameraData).GetMethod("RequestFinalFrameScreenshot", priv);
    if (screenshot == null) throw new InvalidOperationException("Final frame screenshot API unavailable.");
    var game = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    System.IO.Directory.CreateDirectory(output);
    int frame = 0, count = 0, pending = 0, rendered = 0;
    bool active = false;
    string arm = "";
    var errors = new List<string>();
    var records = new List<object>();
    void Log(string message, string stack, LogType kind)
    {
        if (message.Contains("on dx11") && message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind == LogType.Error || kind == LogType.Assert || kind == LogType.Exception) && errors.Count < 20) errors.Add(message);
    }
    void Begin(ScriptableRenderContext context, Camera current)
    {
        if (active && current == camera && (frame == 32 || frame == count - 1))
            screenshot.Invoke(extra, new object[] { output + "/" + arm + (frame == 32 ? "-preview" : "") + ".png" });
    }
    Action<ComputePassContext, Texture, Texture, Texture> capture = (context, source, lighting, geometry) =>
    {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        if (frame % 64 == 0) System.IO.File.WriteAllText(output + "/progress.txt", arm + " " + frame + "/" + count);
        if (frame == count - 1)
        {
            var pass = ((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
            object Field(string name) => type.GetField(name, priv).GetValue(pass);
            var sdfScene = Field("m_Scene");
            object Property(string name) => sdfScene.GetType().GetProperty(name, priv | System.Reflection.BindingFlags.Public).GetValue(sdfScene);
            var light = (Vector4)Field("m_LightColorShadow");
            if (light.x != 0 || light.y != 0 || light.z != 0 || (float)Field("m_SkyTraceDistance") != 0)
                errors.Add("Non-emissive lighting entered the gather.");
            records.Add(new { arm, frame, width = lighting.width, height = lighting.height,
                probeWidth = geometry.width, probeHeight = geometry.height, light = new[] { light.x, light.y, light.z, light.w }, skyTrace = Field("m_SkyTraceDistance"),
                cards = Property("RadiosityCardCount"), emissive = Property("HasEmissiveSurfaces"),
                order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
                emission = new[] { proxy.EmissionColor.linear.r, proxy.EmissionColor.linear.g, proxy.EmissionColor.linear.b } });
            pending++;
            string path = output + "/" + arm + "-gi.f16";
            var cmd = (CommandBuffer)nativeCmd.GetValue(context.cmd);
            cmd.RequestAsyncReadback(lighting, 0, request =>
            {
                try
                {
                    if (request.hasError) errors.Add("GI readback failed: " + path);
                    else System.IO.File.WriteAllBytes(path, request.GetData<byte>().ToArray());
                }
                catch (Exception e) { errors.Add(e.Message); }
                finally { pending--; }
            });
        }
        rendered++;
        if (++frame >= count) active = false;
    };
    async System.Threading.Tasks.Task Run(string name, Color emission, int frames)
    {
        proxy.EmissionColor = emission;
        material.SetColor("_EmissionColor", emission);
        sync.Invoke(emitter, null);
        arm = name; frame = 0; count = frames; active = true;
        double deadline = EditorApplication.timeSinceStartup + 480;
        while (active && errors.Count == 0 && !System.IO.File.Exists(output + "/cancel") && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        if (active || pending != 0 || errors.Count != 0)
            throw new InvalidOperationException(name + " stopped at frame " + frame + ": " + string.Join("\n", errors));
    }
    EditorApplication.LockReloadAssemblies();
    try
    {
        Application.logMessageReceived += Log;
        RenderPipelineManager.beginCameraRendering += Begin;
        foreach (var other in otherCameras) other.camera.enabled = false;
        hook.SetValue(null, capture);
        await Run("emission-on", originalEmission, 512);
        await Run("emission-off", Color.black, 96);
        await Run("emission-restored", originalEmission, 384);
        return new { rendered, api = SystemInfo.graphicsDeviceType.ToString(), records, errors };
    }
    finally
    {
        active = false;
        proxy.EmissionColor = originalEmission;
        material.SetColor("_EmissionColor", originalMaterialEmission);
        sync.Invoke(emitter, null);
        if (!proxyDirty) EditorUtility.ClearDirty(proxy);
        if (!materialDirty) EditorUtility.ClearDirty(material);
        hook.SetValue(null, previousHook);
        Application.logMessageReceived -= Log;
        RenderPipelineManager.beginCameraRendering -= Begin;
        foreach (var other in otherCameras) if (other.camera != null) other.camera.enabled = other.enabled;
        EditorApplication.UnlockReloadAssemblies();
        System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { rendered, records, errors }, Newtonsoft.Json.Formatting.Indented));
    }
}
