// Promote only the Cornell emissive fixture after a complete three-versus-four-pass comparison.
// Run after capture/analysis; refuses dirty assets and active diagnostic callbacks.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
using Newtonsoft.Json.Linq;
{
    const string evidence = "E:/VividRP_GI_Evidence/Atrous4Sampling-20261114-052735-802";
    var capture = JObject.Parse(System.IO.File.ReadAllText(evidence + "/capture/capture.json"));
    var analysis = JObject.Parse(System.IO.File.ReadAllText(evidence + "/analysis.json"));
    var edges = JObject.Parse(System.IO.File.ReadAllText(evidence + "/denoise-edges.json"));
    var qualification = JObject.Parse(System.IO.File.ReadAllText(evidence + "/capture/qualification.json"));
    var source = JObject.Parse(System.IO.File.ReadAllText(evidence + "/source-verification.json"));
    var compiled = capture["compiledVariants"].Single(v => (string)v["name"] == "FilterSdfDiffuseAtrousFinal");
    if ((string)compiled["sha256"] != "31fbe5302c2a893310ee72f0bcd4f53ad81578da0ad5b154326f4f829b3d3148")
        throw new InvalidOperationException("The guarded final shader was not verified.");
    if (!(bool)source["unchanged"] || (int)source["checkedFiles"] <= 0 || source["changedSources"].Any())
        throw new InvalidOperationException("Source/fixture changed during capture.");
    if (!(bool)capture["qualifyReset"] || !(bool)capture["freshCameraState"]
        || !(bool)qualification["exact"] || (int)qualification["filesChecked"] == 0)
        throw new InvalidOperationException("Full camera-state A/A qualification is required.");
    if (!(bool)capture["complete"] || (string)capture["control"] != "atrous3" || (string)capture["candidate"] != "atrous4"
        || (int)capture["spacing"] != 16 || (int)capture["rays"] != 8)
        throw new InvalidOperationException("A complete default-sampling 16px / 8-ray comparison is required.");
    var normal = capture["compiledVariants"].Single(v => (string)v["name"] == "FilterSdfDiffuseAtrous");
    if ((string)normal["sha256"] != "cd991be0dcb77733881d5dab8f1f44985901986c399c72fccaa9e179bce4660a"
        || !(bool)analysis["denoise_upstream_equivalence"]["first_three_wavelet_exact"]
        || !(bool)analysis["denoise_upstream_equivalence"]["final_coverage_exact"]
        || !(bool)analysis["atrous_bindings"]["color_and_variance_ping_pong"]
        || !(bool)analysis["atrous_bindings"]["distinct_input_output"])
        throw new InvalidOperationException("Intermediate shader, coverage, or binding gate failed.");
    foreach (var arm in ((JObject)analysis["motion"]).Properties())
        if ((int)arm.Value["newly_exposed_samples"] == 0 || (double)arm.Value["age_over_one_percent"] != 0
            || (double)arm.Value["accepted_history_percent"] != 0 || (double)arm.Value["temporal_minus_current_max"] != 0)
            throw new InvalidOperationException("Disocclusion gate failed.");
    foreach (string mode in new[] { "hybrid", "sdf" }) foreach (string region in new[] { "ceiling", "floor", "right" })
    {
        var row = analysis["pairs"][mode]["final-" + region];
        if ((double)row["block_bootstrap_variance_reduction_95_percent"][0] <= 0
            || Math.Abs((double)row["mean_change_percent"]) > 1
            || row["mean_rgb_change_percent"].Any(v => Math.Abs((double)v) > 1))
            throw new InvalidOperationException("Noise/energy gate failed: " + mode + "/" + region);
    }
    if ((double)analysis["motion_comparison"]["paired_block8_conditional_95_percent"][1] > 0
        || !(bool)analysis["denoise_upstream_equivalence"]["static_and_motion_exact"])
        throw new InvalidOperationException("Motion or upstream-equivalence gate failed.");
    foreach (var mode in ((JObject)edges["pairs"]).Properties()) foreach (var region in ((JObject)mode.Value).Properties())
    {
        if (!(bool)region.Value["coverage_exact"] || !(bool)region.Value["upstream_exact"])
            throw new InvalidOperationException("Contact coverage/upstream gate failed.");
        foreach (var item in ((JObject)region.Value["objects"]).Properties())
            if (Math.Abs((double)item.Value["mean_change_percent"]) > 1
                || item.Value["mean_rgb_change_percent"].Any(v => Math.Abs((double)v) > 1))
                throw new InvalidOperationException("Contact/corner energy gate failed: " + mode.Name + "/" + region.Name + "/" + item.Name);
    }
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorAtrousDispatchCapture" })
        if (typeof(SdfDiffuseGatherPass).GetField(name, stat).GetValue(null) != null)
            throw new InvalidOperationException("A GI diagnostic is still active.");
    if ((int)typeof(SdfDiffuseGatherPass).GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1)
        throw new InvalidOperationException("A diagnostic iteration override is still active.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Open the clean emissive Cornell fixture first.");
    var volume = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Volume>()).Single();
    var profile = volume.sharedProfile;
    if (volume.HasInstantiatedProfile())
        throw new InvalidOperationException("The fixture has a private profile; preserve it and verify before changing the shared profile.");
    if (!profile.TryGet<SdfDiffuseGISettings>(out var gi) || EditorUtility.IsDirty(profile) || EditorUtility.IsDirty(gi))
        throw new InvalidOperationException("The shared GI profile is missing or already dirty.");
    if (gi.gatherMethod.value != SdfDiffuseGatherMethod.ScreenProbeGather)
        throw new InvalidOperationException("The fixture must already use native Screen Probe Gather.");
    string assetPath = AssetDatabase.GetAssetPath(profile);
    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
    if (package == null) throw new InvalidOperationException("Expected a package-owned Cornell profile.");
    string physicalPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/')));
    string backup = evidence + "/cornell-profile-before.asset";
    if (System.IO.File.Exists(backup)) throw new InvalidOperationException("Refusing to overwrite an earlier profile backup.");
    System.IO.File.Copy(physicalPath, backup);
    var previous = gi.spatialDenoise.value;
    bool previousOverride = gi.spatialDenoise.overrideState;
    gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous4);
    EditorUtility.SetDirty(gi); EditorUtility.SetDirty(profile);
    AssetDatabase.SaveAssetIfDirty(profile);
    if (scene.isDirty || gi.spatialDenoise.value != SdfDiffuseSpatialDenoise.Atrous4 || !gi.spatialDenoise.overrideState)
        throw new InvalidOperationException("Cornell profile promotion did not persist cleanly.");
    var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var type = typeof(SdfDiffuseGatherPass);
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    bool observed = false;
    void End(ScriptableRenderContext context, Camera current)
    {
        if (current != camera) return;
        var passes = (System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null);
        var pass = passes.Cast<object>().Single(p => p.GetType() == type);
        observed = (int)type.GetField("m_AtrousIterations", inst).GetValue(pass) == 4
            && !(bool)type.GetField("m_RestirGather", inst).GetValue(pass);
    }
    RenderPipelineManager.endCameraRendering += End;
    try
    {
        double deadline = EditorApplication.timeSinceStartup + 60;
        while (!observed && EditorApplication.timeSinceStartup < deadline)
        {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        if (!observed) throw new InvalidOperationException("The Cornell camera did not use the saved four-pass setting.");
    }
    finally { RenderPipelineManager.endCameraRendering -= End; }
    var result = new { assetPath, physicalPath, backup, previous = previous.ToString(), previousOverride,
        current = gi.spatialDenoise.value.ToString(), overrideState = gi.spatialDenoise.overrideState,
        effectiveIterations = observed ? 4 : 0, sceneDirty = scene.isDirty };
    System.IO.File.WriteAllText(evidence + "/profile-promotion.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
    return result;
}
