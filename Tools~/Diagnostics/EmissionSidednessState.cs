// Live post-capture state check, not a Unity Test Framework test. No assets are saved.
using UnityEngine;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
{
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
    var pass = passes.Cast<object>().Single(p => p.GetType() == type);
    object Field(string name) => type.GetField(name, priv).GetValue(pass);
    var sceneType = type.Assembly.GetType("VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene");
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    double deadline = EditorApplication.timeSinceStartup + 180;
    while ((int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32)
    {
        if (EditorApplication.timeSinceStartup > deadline)
            throw new InvalidOperationException("Normal radiosity did not finish restoring.");
        game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        await System.Threading.Tasks.Task.Delay(16);
    }
    bool callbacksNull = new[] { "EditorLinearCapture", "EditorTraceCapture", "EditorGatherStageCapture" }
        .All(n => type.GetField(n, stat).GetValue(null) == null);
    var scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).ToArray();
    int frameOverride = (int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null);
    int diagnostics = (int)type.GetField("EditorGatherHistoryDiagnostics", stat).GetValue(null);
    bool forceTwoSided = (bool)sceneType.GetField("EditorForceTwoSidedEmission", stat).GetValue(null);
    float configuredBias = (float)type.GetField("m_RayBias", priv).GetValue(pass), effectiveBias = (float)Field("m_Bias");
    if (!callbacksNull || frameOverride != -1 || diagnostics != 0 || forceTwoSided || scenes.Any(s => s.isDirty)
        || Mathf.Abs(configuredBias - .04f) > 1e-6 || Mathf.Abs(effectiveBias - .04f) > 1e-6)
        throw new InvalidOperationException("Emission validation left altered editor state.");
    return new { callbacksNull, frameOverride, diagnostics, forceTwoSided,
        scenes = scenes.Select(s => new { s.path, s.isDirty }).ToArray(), configuredBias, effectiveBias,
        order = Field("m_RadiosityPublishedOrder"), batch = Field("m_RadiosityBatch"),
        spacing = type.GetField("ProbeSpacing", stat).GetRawConstantValue(),
        rays = type.GetField("RaysPerProbe", stat).GetRawConstantValue(),
        graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
}
