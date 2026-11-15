// Validate normal rendering and persist the tested window only for emissive Cornell.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
using Newtonsoft.Json.Linq;
{
    const string evidence="E:/VividRP_GI_Evidence/ScreenProbeAnchorFinalQuality-20261115-092158-108905";
    JObject Load(string path)=>JObject.Parse(System.IO.File.ReadAllText(path));
    string Hash(string path) {
        using var sha=System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
    }
    var quality=Load(evidence+"/final-production-vs-half128.json");
    if(!(bool)quality["qualityComparisonQualified"] || !(bool)Load(evidence+"/timing/timing-qualification.json")["qualified"])
        throw new InvalidOperationException("Final quality or GPU cost inputs are not qualified.");
    foreach(string region in new[]{"final-ceiling","final-floor","final-back"}) foreach(var row in quality["staticComparisons"][region])
        if((double)row["variance_reduction_percent"]<20 || Math.Abs((double)row["mean_change_percent"])>1
            || (double)row["block_bootstrap_variance_reduction_95_percent"][0]<=0)
            throw new InvalidOperationException("Final variance/energy check failed: "+region);
    var staticRoot="E:/VividRP_GI_Evidence/ScreenProbeAnchorWindowRevisit-20261114-183607-958";
    var motionRoot="E:/VividRP_GI_Evidence/ScreenProbeAnchorWindowMotion-20261114-195008-007";
    var relightRoot="E:/VividRP_GI_Evidence/ScreenProbeAnchorWindowRelight-20261114-205334-330163";
    if(!(bool)Load(staticRoot+"/decision.json")["staticGatePassed"]
        || !(bool)Load(motionRoot+"/analysis.json")["decision"]["motionGatePassed"]
        || !(bool)Load(relightRoot+"/decision.json")["passed"]
        || !(bool)Load(relightRoot+"/analysis.json")["additionalBlackConformancePassed"])
        throw new InvalidOperationException("Historical long-window, motion or relight check failed.");
    foreach(var row in JArray.Parse(System.IO.File.ReadAllText(evidence+"/production-final-program-equivalence.json")))
        if(!(bool)row["matchesTested"] || Hash(evidence+"/production-final-compiled/"+(string)row["kernel"]+".bin")!=(string)row["sha256"])
            throw new InvalidOperationException("Tested GPU program changed.");
    const System.Reflection.BindingFlags stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    var type=typeof(SdfDiffuseGatherPass);
    foreach(string n in new[]{"EditorTraceCapture","EditorLinearCapture","EditorGatherStageCapture","EditorAtrousDispatchCapture","EditorBindResolveProbe"})
        if(type.GetField(n,stat).GetValue(null)!=null)throw new InvalidOperationException("A GI observer is active.");
    if((int)type.GetField("EditorGatherHistoryDiagnostics",stat).GetValue(null)!=0
        || (int)type.GetField("EditorGatherFrameOverride",stat).GetValue(null)!=-1
        || (int)type.GetField("EditorProbeAnchorMaxAgeOverride",stat).GetValue(null)!=-1)
        throw new InvalidOperationException("An Editor diagnostic override is active.");
    if(!EditorApplication.isPlaying || !EditorApplication.isPaused || UnityEngine.FrameDebugger.enabled)
        throw new InvalidOperationException("Expected paused controlled PlayMode.");
    var scene=SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if(!scene.isLoaded || scene.isDirty)throw new InvalidOperationException("Clean emissive Cornell required.");
    var roots=scene.GetRootGameObjects();var camera=roots.SelectMany(r=>r.GetComponentsInChildren<Camera>()).Single();
    var volume=roots.SelectMany(r=>r.GetComponentsInChildren<Volume>()).Single();var profile=volume.sharedProfile;
    if(volume.HasInstantiatedProfile() || !profile.TryGet<SdfDiffuseGISettings>(out var gi) || EditorUtility.IsDirty(profile)
        || EditorUtility.IsDirty(gi) || gi.screenProbeAnchorMaxAge.value!=128 || gi.screenProbeAnchorMaxAge.max!=512)
        throw new InvalidOperationException("Unexpected or dirty Cornell profile.");
    string asset=AssetDatabase.GetAssetPath(profile);var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset);
    string physical=System.IO.Path.GetFullPath(System.IO.Path.Combine(package.resolvedPath,asset.Substring(package.assetPath.Length).TrimStart('/')));
    var backup=JArray.Parse(System.IO.File.ReadAllText(evidence+"/production-before/manifest.json")).Single(r=>((string)r["path"]).EndsWith("/Emissive Ceiling Profile.asset",StringComparison.Ordinal));
    if(Hash(physical)!=(string)backup["sha256"])throw new InvalidOperationException("Profile changed after backup.");
    int oldAge=gi.screenProbeAnchorMaxAge.value;bool oldOverride=gi.screenProbeAnchorMaxAge.overrideState;
    bool previousPaused=EditorApplication.isPaused,accepted=false;int requested=512,count=0;
    var stages=new List<object>();var textureIds=new HashSet<string>();bool firstHasHistory=false,firstObserved=false;
    Exception failure=null;
    void End(ScriptableRenderContext context,Camera current) {
        if(current!=camera || failure!=null)return;
        try {
            var pass=((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses",stat).GetValue(null)).Cast<object>().Single(p=>p.GetType()==type);
            object Field(string n)=>type.GetField(n,inst).GetValue(pass);
            if((int)Field("m_ProbeAnchorMaxAge")!=requested)return;
            var history=(CameraHistoryTexture)Field("m_ProbeHistory");
            var currentTexture=history.GetCurrent().rt;var previousTexture=history.GetPrevious().rt;
            string format=requested>128?"R32G32B32A32_SFloat":"R16G16B16A16_SFloat";
            if(currentTexture.graphicsFormat.ToString()!=format || previousTexture.graphicsFormat.ToString()!=format)
                throw new InvalidOperationException("Public setting did not select the requested GPU format.");
            if(!firstObserved){firstHasHistory=(bool)Field("m_HasHistory");firstObserved=true;}
            if(++count>4){textureIds.Add(currentTexture.GetEntityId().ToString());textureIds.Add(previousTexture.GetEntityId().ToString());}
            if(textureIds.Count>2)throw new InvalidOperationException("Stable anchor history was recreated.");
        }catch(Exception e){failure=e;}
    }
    RenderPipelineManager.endCameraRendering+=End;
    try {
        EditorApplication.isPaused=false;
        foreach(var step in new[]{(512,32),(128,16),(512,640)}) {
            requested=step.Item1;count=0;firstObserved=false;textureIds.Clear();gi.screenProbeAnchorMaxAge.Override(requested);
            double deadline=EditorApplication.timeSinceStartup+120;
            while(count<step.Item2 && failure==null && EditorApplication.timeSinceStartup<deadline)
                await System.Threading.Tasks.Task.Delay(16);
            if(failure!=null)throw failure;
            if(count<step.Item2 || firstHasHistory || textureIds.Count!=2)throw new InvalidOperationException("History transition/reuse check failed.");
            stages.Add(new{age=requested,observedFrames=count,firstHasHistory,stableTextureCount=textureIds.Count});
        }
        if(scene.isDirty || Hash(physical)!=(string)backup["sha256"])throw new InvalidOperationException("Scene/profile changed during normal validation.");
        EditorUtility.SetDirty(gi);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
        if(EditorUtility.IsDirty(gi) || EditorUtility.IsDirty(profile) || gi.screenProbeAnchorMaxAge.value!=512)
            throw new InvalidOperationException("Profile did not save cleanly.");
        accepted=true;
        var result=new{asset,age=512,stages,editorDiagnostics=0,automaticPrecisionBit=32,profileSha256=Hash(physical),backup=(string)backup["backup"],sceneDirty=scene.isDirty};
        System.IO.File.WriteAllText(evidence+"/promotion.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }finally {
        RenderPipelineManager.endCameraRendering-=End;
        if(!accepted){gi.screenProbeAnchorMaxAge.value=oldAge;gi.screenProbeAnchorMaxAge.overrideState=oldOverride;}
        EditorApplication.isPaused=previousPaused;
    }
}
