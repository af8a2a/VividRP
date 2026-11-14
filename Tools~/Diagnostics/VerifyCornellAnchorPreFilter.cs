// Verify the saved prefilter fixture, current kernels, scratch descriptor and actual GPU anchor ages.
// Read-only live check, not a Unity Test Framework test.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Clean Cornell fixture required.");
    var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    var volume = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Volume>()).Single();
    if (volume.HasInstantiatedProfile() || !volume.sharedProfile.TryGet<SdfDiffuseGISettings>(out var gi)
        || gi.screenProbeAnchorMaxAge.value != 128 || !gi.screenProbeAnchorMaxAge.overrideState
        || !gi.screenProbeAnchorPreFilter.value || !gi.screenProbeAnchorPreFilter.overrideState)
        throw new InvalidOperationException("Expected the saved Cornell anchor override.");
    var hook = type.GetField("EditorTraceCapture", stat);
    string[] hooks = { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" };
    if (hooks.Any(n => type.GetField(n, stat).GetValue(null) != null)) throw new InvalidOperationException("Capture already active.");
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    int frames = 0;
    bool requested = false, complete = false;
    float maximumAge = 0; int oldEnough = 0;
    string error = null;
    object observed = null;
    var messages = new List<object>();
    Action<ComputePassContext, Texture, Texture, Texture> capture = (context, sources, lighting, geometry) =>
    {
        if (requested || context.Get<VividCameraData>().camera != camera) return;
        var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
        var pass = passes.Cast<object>().Single(p => p.GetType() == type);
        object Field(string n) => type.GetField(n, inst).GetValue(pass);
        if ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32) { frames = 0; return; }
        if (++frames < 512) return;
        if ((int)Field("m_ProbeAnchorMaxAge") != 128 || (int)Field("m_AtrousIterations") != 4
            || (bool)Field("m_RestirGather") || (int)Field("m_RadiositySeed") != 0 || !(bool)Field("m_AnchorPreFilter"))
            throw new InvalidOperationException("Saved Cornell configuration was not used by the renderer.");
        foreach (string field in new[] { "m_TraceCompute", "m_GatherCompute", "m_ImportanceSamplingCompute", "m_FilterCompute", "m_GatherTemporalCompute" })
            foreach (var message in ShaderUtil.GetComputeShaderMessages((ComputeShader)Field(field)))
            {
                string platform = message.platform.ToString();
                bool uav8 = (platform.Contains("D3D11", StringComparison.OrdinalIgnoreCase) || platform.Contains("Direct3D11", StringComparison.OrdinalIgnoreCase))
                    && message.message.Contains("uav", StringComparison.OrdinalIgnoreCase) && message.message.Contains("8");
                if (message.severity.ToString() == "Error" && !uav8) throw new InvalidOperationException(message.message);
                messages.Add(new { shader = field, message.message, severity = message.severity.ToString(), platform });
            }
        var failed = typeof(EditorUtility).GetProperty("scriptCompilationFailed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (EditorApplication.isCompiling || failed == null || (bool)failed.GetValue(null)) throw new InvalidOperationException("C# compilation failed or pending.");
        var reconstructed = (RenderGraphTexture)Field("m_ReconstructedAnchor");
        TextureHandle reconstructedHandle = reconstructed;
        if (!reconstructedHandle.IsValid()) throw new InvalidOperationException("Missing reconstruction scratch.");
        var reconstructedTexture = (Texture)reconstructedHandle;
        if (reconstructedTexture.width != 120 || reconstructedTexture.height != 136)
            throw new InvalidOperationException("Unexpected reconstruction scratch size.");
        var factoryTime = System.IO.File.GetLastWriteTimeUtc("E:/VividRP_Reborn/Packages/VividRP/Editor/Tools/EmissiveCornellBoxSceneBuilder.cs");
        var assemblyTime = System.IO.File.GetLastWriteTimeUtc(typeof(EmissiveCornellBoxSceneBuilder).Assembly.Location);
        if (assemblyTime < factoryTime) throw new InvalidOperationException("Factory source was not recompiled.");
        var value = Field("m_CurrentAnchorStatistics");
        var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        if (!handle.IsValid()) throw new InvalidOperationException("Missing GPU anchor statistics.");
        var texture = (Texture)handle;
        observed = new { preFilterConfigured = gi.screenProbeAnchorPreFilter.value, preFilterActual = Field("m_AnchorPreFilter"),
            scratchWidth = reconstructedTexture.width, scratchHeight = reconstructedTexture.height,
            scratchFormat = reconstructedTexture.graphicsFormat.ToString(), configuredAge = gi.screenProbeAnchorMaxAge.value, effectiveAge = Field("m_ProbeAnchorMaxAge"),
            iterations = Field("m_AtrousIterations"), restir = Field("m_RestirGather"), order = Field("m_RadiosityPublishedOrder"),
            batch = Field("m_RadiosityBatch"), seed = Field("m_RadiositySeed"), configuredBias = Field("m_RayBias"), effectiveBias = Field("m_Bias"),
            spacing = type.GetField("ProbeSpacing", stat).GetRawConstantValue(), rays = type.GetField("RaysPerProbe", stat).GetRawConstantValue(),
            cameraPosition = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z }, camera.fieldOfView,
            editorCompilationPassed = true, editorAssemblyUtc = System.IO.File.GetLastWriteTimeUtc(typeof(EmissiveCornellBoxSceneBuilder).Assembly.Location),
            factorySourceUtc = System.IO.File.GetLastWriteTimeUtc("E:/VividRP_Reborn/Packages/VividRP/Editor/Tools/EmissiveCornellBoxSceneBuilder.cs"), messages };
        requested = true;
        ((CommandBuffer)wrapped.GetValue(context.cmd)).RequestAsyncReadback(texture, 0, request =>
        {
            try
            {
                if (request.hasError) throw new InvalidOperationException("GPU age readback failed.");
                var data = request.GetData<ushort>();
                for (int i = 0; i < data.Length; i += 4)
                {
                    float age = Mathf.HalfToFloat(data[i]);
                    if (!float.IsFinite(age) || age < 0 || age > 128) throw new InvalidOperationException("Invalid anchor age.");
                    maximumAge = Mathf.Max(maximumAge, age); if (age >= 96) oldEnough++;
                }
                if (maximumAge != 128 || oldEnough == 0) throw new InvalidOperationException("GPU anchor did not reach the saved convergence window.");
            }
            catch (Exception e) { error = e.Message; }
            finally { complete = true; }
        });
    };
    hook.SetValue(null, capture);
    try
    {
        double deadline = EditorApplication.timeSinceStartup + 240;
        while (!complete && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        if (!complete || error != null) throw new InvalidOperationException(error ?? "Cornell verification timed out.");
    }
    finally { hook.SetValue(null, null); }
    bool callbacksNull = hooks.All(n => type.GetField(n, stat).GetValue(null) == null);
    int frameOverride = (int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null);
    int atrousOverride = (int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null);
    int diagnostics = (int)type.GetField("EditorGatherHistoryDiagnostics", stat).GetValue(null);
    var sdfScene = type.Assembly.GetType("VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene");
    bool forceTwoSided = (bool)sdfScene.GetField("EditorForceTwoSidedEmission", stat).GetValue(null);
    bool dirty = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty);
    if (!callbacksNull || frameOverride != -1 || atrousOverride != -1 || diagnostics != 0 || forceTwoSided || dirty
        || EditorUtility.IsDirty(gi) || EditorUtility.IsDirty(volume.sharedProfile)) throw new InvalidOperationException("Final diagnostic or asset state is not clean.");
    var result = new { observed, gpuMaximumAge = maximumAge, probesAtLeast96Frames = oldEnough,
        callbacksNull, frameOverride, atrousOverride, diagnostics, forceTwoSided, sceneDirty = dirty };
    System.IO.File.WriteAllText("E:/VividRP_GI_Evidence/ScalarAnchorPreFilterCombined-20261114-102230-265945/post-promotion-verification.json",
        Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
    return result;
}
