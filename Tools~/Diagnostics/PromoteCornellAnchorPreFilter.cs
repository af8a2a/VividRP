// Enable only the Cornell configuration that passed the independent quality audit.
// Runtime/shader source and evidence are pinned; assets are saved through Unity.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
using Newtonsoft.Json.Linq;
{
    const string evidence = "E:/VividRP_GI_Evidence/ScalarAnchorPreFilterCombined-20261114-102230-265945";
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    JObject Load(string path) => JObject.Parse(System.IO.File.ReadAllText(path));
    var audit = Load(evidence + "/quality-audit.json");
    if (!(bool)audit["ready"]) throw new InvalidOperationException("Quality audit did not pass.");
    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
        string Hash(string path) => BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        foreach (var entry in audit["manifests"])
            if (Hash((string)entry["path"]) != (string)entry["sha256"])
                throw new InvalidOperationException("Audited evidence changed: " + entry["path"]);
        foreach (var entry in Load(evidence + "/source-before-capture.json")["files"]
            .Where(v => ((string)v["path"]).StartsWith("Runtime/") || ((string)v["path"]).StartsWith("Shaders/")))
            if (Hash("E:/VividRP_Reborn/Packages/VividRP/" + (string)entry["path"]) != (string)entry["sha256"])
                throw new InvalidOperationException("Tested production source changed: " + entry["path"]);
    }
    var type = typeof(SdfDiffuseGatherPass);
    foreach (string hook in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (type.GetField(hook, stat).GetValue(null) != null) throw new InvalidOperationException("Capture still active.");
    foreach (string flag in new[] { "EditorGatherHistoryDiagnostics", "EditorRadiositySeed" })
        if ((int)type.GetField(flag, stat).GetValue(null) != 0) throw new InvalidOperationException("Diagnostic flag active.");
    foreach (string flag in new[] { "EditorGatherFrameOverride", "EditorAtrousIterationOverride" })
        if ((int)type.GetField(flag, stat).GetValue(null) != -1) throw new InvalidOperationException("Diagnostic override active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Clean Cornell scene required.");
    var volume = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var profile = volume.sharedProfile;
    if (volume.HasInstantiatedProfile() || !profile.TryGet<SdfDiffuseGISettings>(out var gi)
        || EditorUtility.IsDirty(profile) || EditorUtility.IsDirty(gi))
        throw new InvalidOperationException("Private or dirty fixture profile.");
    if (gi.gatherMethod.value != SdfDiffuseGatherMethod.ScreenProbeGather
        || gi.spatialDenoise.value != SdfDiffuseSpatialDenoise.Atrous4 || gi.screenProbeAnchorMaxAge.value != 128
        || !gi.screenProbeSpatialAnchor.value || gi.screenProbeAnchorPreFilter.value)
        throw new InvalidOperationException("Unexpected Cornell baseline.");
    string assetPath = AssetDatabase.GetAssetPath(profile);
    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
    if (package == null) throw new InvalidOperationException("Package-owned profile required.");
    string physical = System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath,
        assetPath.Substring(package.assetPath.Length).TrimStart('/')));
    string backup = evidence + "/cornell-profile-before.asset";
    if (System.IO.File.Exists(backup)) throw new InvalidOperationException("Backup already exists.");
    System.IO.File.Copy(physical, backup);
    bool previous = gi.screenProbeAnchorPreFilter.value, previousOverride = gi.screenProbeAnchorPreFilter.overrideState;
    bool observed = false;
    var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    void End(ScriptableRenderContext context, Camera current)
    {
        if (current != camera) return;
        var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
        var pass = passes.Cast<object>().Single(p => p.GetType() == type);
        observed = (bool)type.GetField("m_AnchorPreFilter", inst).GetValue(pass)
            && (int)type.GetField("m_ProbeAnchorMaxAge", inst).GetValue(pass) == 128
            && (int)type.GetField("m_AtrousIterations", inst).GetValue(pass) == 4
            && !(bool)type.GetField("m_RestirGather", inst).GetValue(pass);
    }
    RenderPipelineManager.endCameraRendering += End;
    try
    {
        gi.screenProbeAnchorPreFilter.Override(true);
        EditorUtility.SetDirty(gi); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
        double deadline = EditorApplication.timeSinceStartup + 60;
        while (!observed && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        if (!observed || scene.isDirty || EditorUtility.IsDirty(profile) || EditorUtility.IsDirty(gi))
            throw new InvalidOperationException("Saved prefilter was not observed cleanly.");
        var result = new { assetPath, physical, backup, previous, previousOverride, observed,
            scope = (string)audit["scope"], limits = audit["limits"], sceneDirty = scene.isDirty };
        System.IO.File.WriteAllText(evidence + "/profile-promotion.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }
    catch
    {
        gi.screenProbeAnchorPreFilter.value = previous;
        gi.screenProbeAnchorPreFilter.overrideState = previousOverride;
        EditorUtility.SetDirty(gi); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
        throw;
    }
    finally { RenderPipelineManager.endCameraRendering -= End; }
}
