// Promote only the Cornell emissive fixture after a complete default-sampling comparison.
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
    const string evidence = "E:/VividRP_GI_Evidence/AtrousGuardedCombined-20261114-024357-560568";
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
    if (!(bool)capture["qualifyReset"] || !(bool)capture["sameModeWarmup"]
        || !(bool)qualification["exact"] || (int)qualification["filesChecked"] == 0)
        throw new InvalidOperationException("SDF A/A cold-reset qualification is required.");
    if (!(bool)capture["complete"] || (string)capture["control"] != "white" || (string)capture["candidate"] != "atrous3"
        || (int)capture["spacing"] != 16 || (int)capture["rays"] != 8)
        throw new InvalidOperationException("A complete default-sampling 16px / 8-ray comparison is required.");
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
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture" })
        if (typeof(SdfDiffuseGatherPass).GetField(name, stat).GetValue(null) != null)
            throw new InvalidOperationException("A GI diagnostic is still active.");
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
    gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous3);
    EditorUtility.SetDirty(gi); EditorUtility.SetDirty(profile);
    AssetDatabase.SaveAssetIfDirty(profile);
    if (scene.isDirty || gi.spatialDenoise.value != SdfDiffuseSpatialDenoise.Atrous3 || !gi.spatialDenoise.overrideState)
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
        observed = (int)type.GetField("m_AtrousIterations", inst).GetValue(pass) == 3
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
        if (!observed) throw new InvalidOperationException("The Cornell camera did not use the saved three-pass setting.");
    }
    finally { RenderPipelineManager.endCameraRendering -= End; }
    var result = new { assetPath, physicalPath, backup, previous = previous.ToString(), previousOverride,
        current = gi.spatialDenoise.value.ToString(), overrideState = gi.spatialDenoise.overrideState,
        effectiveIterations = observed ? 3 : 0, sceneDirty = scene.isDirty };
    System.IO.File.WriteAllText(evidence + "/profile-promotion.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
    return result;
}
