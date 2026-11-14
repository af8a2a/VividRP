// Promote the tested static-anchor window only for the emissive Cornell fixture.
// The reports retain the small mixed motion-error changes and finite relight tail gap.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
using Newtonsoft.Json.Linq;
{
    const string ageRoot = "E:/VividRP_GI_Evidence/ScalarAnchorAge-20261114-063528-201";
    const string referenceRoot = "E:/VividRP_GI_Evidence/ScalarAnchorPoseReferenceCombined-20261114-072002-644163";
    const string relightRoot = "E:/VividRP_GI_Evidence/ScalarAnchorRelight-20261114-073213-475";
    JObject Load(string path) => JObject.Parse(System.IO.File.ReadAllText(path));
    var capture = Load(ageRoot + "/capture.json");
    var analysis = Load(ageRoot + "/analysis.json");
    var reference = Load(referenceRoot + "/reference-analysis.json");
    var referenceCapture = Load(referenceRoot + "/capture.json");
    var relight = Load(relightRoot + "/analysis.json");
    var relightCapture = Load(relightRoot + "/capture.json");
    foreach (var manifest in new[] { capture, referenceCapture, relightCapture })
        if (!(bool)manifest["complete"] || manifest["errors"].Any())
            throw new InvalidOperationException("Incomplete or failed evidence.");
    if ((int)capture["spacing"] != 16 || (int)capture["rays"] != 8 || !(bool)analysis["complete"]
        || !(bool)analysis["static_raw_exact"] || !(bool)analysis["motion_raw_exact"]
        || (int)analysis["aa"]["files"] != 4868 || analysis["aa"]["mismatch"].Any())
        throw new InvalidOperationException("Sampling, input equivalence or A/A gate failed.");
    foreach (string region in new[] { "left", "right", "back", "floor", "ceiling" })
    {
        var row = analysis["static"][region]["final"];
        if ((double)row["block_bootstrap_variance_reduction_95_percent"][0] <= 0
            || Math.Abs((double)row["mean_change_percent"]) > 1
            || row["mean_rgb_change_percent"].Any(v => Math.Abs((double)v) > 1))
            throw new InvalidOperationException("Static variance/mean gate failed: " + region);
        var target = reference["static"][region]["age128"]["final"];
        if (Math.Abs((double)target["mean_energy_error_percent"]) > 1
            || target["mean_rgb_error_percent"].Any(v => Math.Abs((double)v) > 1))
            throw new InvalidOperationException("Independent Resolve reference gate failed: " + region);
        foreach (string age in new[] { "age32", "age128" })
        {
            if ((double)relight["arms"][age + "-off"]["regions"][region]["final"]["off_max_absolute_y"] != 0)
                throw new InvalidOperationException("Old emission survived switch-off.");
            foreach (string change in new[] { "up", "down" })
                if (Math.Abs((double)relight["arms"][age + "-" + change]["regions"][region]["final"]["first_frame_error_percent"]) > .1)
                    throw new InvalidOperationException("Uniform source change response failed.");
        }
    }
    foreach (var arm in ((JObject)analysis["motion"]).Properties())
        if ((int)arm.Value["newly_exposed_pixels"] != 28104 || (int)arm.Value["old_pixel_age"] != 0
            || (int)arm.Value["old_pixel_accepted"] != 0 || (double)arm.Value["temporal_current_max"] != 0
            || (int)arm.Value["newly_exposed_probes"] != 73 || (int)arm.Value["old_probe_age"] != 0)
            throw new InvalidOperationException("Disocclusion history gate failed.");
    foreach (var arm in ((JObject)relight["arms"]).Properties())
        if ((double)arm.Value["source_scaling"]["normalized_rms_percent"] > .001)
            throw new InvalidOperationException("Source cache did not scale linearly.");
    foreach (string root in new[] { ageRoot, relightRoot })
    {
        var verified = Load(root + "/source-verification.json");
        if (!(bool)verified["unchanged"] || verified["changedSources"].Any() || (int)verified["checkedFiles"] == 0)
            throw new InvalidOperationException("Capture source/fixture changed.");
    }
    var snapshot = Load(relightRoot + "/source-before-capture.json");
    using (var sha = System.Security.Cryptography.SHA256.Create())
        foreach (var entry in snapshot["files"].Where(v => ((string)v["path"]).StartsWith("Runtime/") || ((string)v["path"]).StartsWith("Shaders/")))
        {
            string physical = "E:/VividRP_Reborn/Packages/VividRP/" + (string)entry["path"];
            string hash = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(physical))).Replace("-", "").ToLowerInvariant();
            if (hash != (string)entry["sha256"]) throw new InvalidOperationException("Production source changed after capture: " + physical);
        }
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var type = typeof(SdfDiffuseGatherPass);
    foreach (string hook in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (type.GetField(hook, stat).GetValue(null) != null) throw new InvalidOperationException("Capture still active.");
    foreach (string flag in new[] { "EditorGatherHistoryDiagnostics", "EditorRadiositySeed" })
        if ((int)type.GetField(flag, stat).GetValue(null) != 0) throw new InvalidOperationException("Diagnostic flag still active.");
    if ((int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null) != -1
        || (int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1)
        throw new InvalidOperationException("Diagnostic override still active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Clean Cornell scene required.");
    var volume = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var profile = volume.sharedProfile;
    if (volume.HasInstantiatedProfile() || !profile.TryGet<SdfDiffuseGISettings>(out var gi)
        || EditorUtility.IsDirty(profile) || EditorUtility.IsDirty(gi)) throw new InvalidOperationException("Private or dirty fixture profile.");
    if (gi.gatherMethod.value != SdfDiffuseGatherMethod.ScreenProbeGather || gi.spatialDenoise.value != SdfDiffuseSpatialDenoise.Atrous4)
        throw new InvalidOperationException("Unexpected Cornell Gather/filter configuration.");
    string assetPath = AssetDatabase.GetAssetPath(profile);
    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
    if (package == null) throw new InvalidOperationException("Package-owned profile required.");
    string physicalPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/')));
    string backup = ageRoot + "/cornell-profile-before.asset";
    if (System.IO.File.Exists(backup)) throw new InvalidOperationException("Profile backup already exists.");
    System.IO.File.Copy(physicalPath, backup);
    int previous = gi.screenProbeAnchorMaxAge.value;
    bool previousOverride = gi.screenProbeAnchorMaxAge.overrideState;
    gi.screenProbeAnchorMaxAge.Override(128);
    EditorUtility.SetDirty(gi); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
    var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    bool observed = false;
    void End(ScriptableRenderContext context, Camera current)
    {
        if (current != camera) return;
        var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
        var pass = passes.Cast<object>().Single(p => p.GetType() == type);
        observed = (int)type.GetField("m_ProbeAnchorMaxAge", inst).GetValue(pass) == 128
            && (int)type.GetField("m_AtrousIterations", inst).GetValue(pass) == 4
            && !(bool)type.GetField("m_RestirGather", inst).GetValue(pass);
    }
    RenderPipelineManager.endCameraRendering += End;
    try
    {
        double deadline = EditorApplication.timeSinceStartup + 60;
        while (!observed && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        if (!observed || scene.isDirty || EditorUtility.IsDirty(profile) || EditorUtility.IsDirty(gi))
            throw new InvalidOperationException("Saved anchor window was not observed cleanly.");
    }
    finally { RenderPipelineManager.endCameraRendering -= End; }
    var result = new { assetPath, physicalPath, backup, previous, previousOverride,
        current = gi.screenProbeAnchorMaxAge.value, overrideState = gi.screenProbeAnchorMaxAge.overrideState,
        observed, sceneDirty = scene.isDirty, referenceRoot, relightRoot,
        scope = "Static noise and uniform emissive response; mixed finite motion errors and relight tail gap remain documented" };
    System.IO.File.WriteAllText(ageRoot + "/profile-promotion.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
    return result;
}
