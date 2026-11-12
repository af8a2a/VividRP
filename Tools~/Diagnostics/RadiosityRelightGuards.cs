using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven.Meshlets;
using VividRP.Runtime.GPUDriven;
{
    const bool cornell=true, timingOnly=false;
    const string output="E:/VividRP_Reborn/Logs/RadiosityRelight-20261111/guards-final";
    const string scenePath=cornell ? "Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox/SurfaceCacheCornellBoxBright.unity"
        : "Assets/ClassicSponza/Scenes/SponzaNoon.unity";
    const string lightPath="Assets/ClassicSponza/Scenes/SponzaLightingNoon.unity";
    const string graphPath="Assets/ClassicSponza/Profiles/Sponza Noon Realtime GI/Sponza Noon Realtime GI.vrdg";
    const System.Reflection.BindingFlags priv=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    var type=typeof(SdfDiffuseGatherPass);
    var hook=type.GetField("EditorTraceCapture",stat);
    var previousHook=hook.GetValue(null);
    var diagnosticFlag=type.GetField("EditorRecordProbeAnchorDiagnostics",stat) ?? throw new InvalidOperationException("Wait for probe anchor diagnostics to compile.");
    var previousDiagnosticFlag=diagnosticFlag.GetValue(null);
    var stageHook=type.GetField("EditorGatherStageCapture",stat);
    var frameOverride=type.GetField("EditorGatherFrameOverride",stat);
    var diagnosticMask=type.GetField("EditorGatherHistoryDiagnostics",stat);
    var anchorBlend=type.GetField("EditorProbeAnchorHistoryBlend",stat);
    var radioSeed=type.GetField("EditorRadiositySeed",stat);
    var previousStageHook=stageHook.GetValue(null);
    var previousFrameOverride=frameOverride.GetValue(null);
    var previousMask=diagnosticMask.GetValue(null);
    var previousBlend=anchorBlend.GetValue(null);
    var previousRadioSeed=radioSeed.GetValue(null);
    var passes=typeof(PassRecorder).GetField("s_RenderPasses",stat);
    var fields=new[]{"m_RadiosityRescale","m_RadiosityHistoryScale","m_SkyTraceDistance","m_RadiosityProgressive","m_RadiosityPublishedOrder","m_RadiosityClear","m_RadiosityBoostFrames","m_HasHistory","m_RadiosityPublished","m_RadiosityBatch","m_RadiosityBounce","m_RadiosityAccumulate",
        "m_RadiositySamples","m_RadiosityOrder","m_RadiosityCursor","m_RadiosityUpdateCards","m_RadiosityInput","m_Scene"}
        .ToDictionary(n=>n,n=>type.GetField(n,priv));
    var nativeCmd=typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer",priv);
    System.IO.Directory.CreateDirectory(output);
    var graph=AssetDatabase.LoadAssetAtPath<RenderGraphData>(graphPath);
    var originalScene=SceneManager.GetActiveScene();
    var scene=SceneManager.GetSceneByPath(scenePath);
    var lights=cornell?scene:SceneManager.GetSceneByPath(lightPath);
    bool ownScene=!scene.isLoaded,ownLights=!cornell&&!lights.isLoaded;
    if(ownScene)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
    if(ownLights)lights=EditorSceneManager.OpenScene(lightPath,OpenSceneMode.Additive);
    if(cornell)lights=scene;
    var roots=scene.GetRootGameObjects();
    var lightRoots=lights.GetRootGameObjects();
    var camera=roots.SelectMany(o=>o.GetComponentsInChildren<Camera>(true)).Single(c=>cornell||c.name=="Camera_1");
    var oldEnabled=camera.enabled;
    var pose=(camera.transform.position,camera.transform.rotation);
    var panel=roots.SelectMany(r=>r.GetComponentsInChildren<MeshletRenderer>(true)).Single(r=>r.name=="Back");
    var originalMaterials=panel.sourceMaterials.ToArray();var originalProxies=panel.materialProxies.ToArray();var originalPanelPosition=panel.transform.localPosition;
    var syncPanel=typeof(MeshletRenderer).GetMethod("UpdateDatabaseIfNeeded",priv);
    var emissiveProxy=originalProxies[0]!=null?UnityEngine.Object.Instantiate(originalProxies[0]):ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
    emissiveProxy.name="Relight Guard Emissive Proxy";emissiveProxy.hideFlags=HideFlags.DontSave;emissiveProxy.SourceMaterial=originalMaterials[0];emissiveProxy.MaterialGraph=null;emissiveProxy.Model=GPUDrivenMaterialProxyModel.StandardLit;
    emissiveProxy.EmissionColor=new Color(4,2,1,1);
    var emitterProxies=new[]{emissiveProxy};
    var sun=lightRoots.SelectMany(o=>o.GetComponentsInChildren<Light>(true)).First(l=>l.type==LightType.Directional&&l.isActiveAndEnabled);
    var sunColor=sun.color;var sunRotation=sun.transform.rotation;var sunShadow=sun.shadowStrength;var sunIntensity=sun.intensity; var sunData=sun.GetComponent<VividAdditionalLightData>(); var oldSky=sunData.interactsWithSky;
    var oldSun=RenderSettings.sun;
    var extra=camera.GetComponent<VividAdditionalCameraData>();
    var oldAA=extra.antialiasing;
    var candidates=cornell?roots:roots.Concat(lightRoots);
    var volume=candidates.SelectMany(x=>x.GetComponentsInChildren<Volume>(true))
        .Single(v=>v.sharedProfile!=null&&v.sharedProfile.name==(cornell?"CornellBox Profile":"Sponza Noon Profile"));
    var priorProfile=volume.HasInstantiatedProfile()?volume.profile:null;
    var profile=ScriptableObject.CreateInstance<VolumeProfile>();
    foreach(var component in (priorProfile??volume.sharedProfile).components)profile.components.Add(UnityEngine.Object.Instantiate(component));
    if(!profile.TryGet<SdfDiffuseGISettings>(out var gi)||gi.surfaceCache.value==null||graph==null)
        throw new InvalidOperationException("Surface Cache fixture unavailable.");
    if(profile.TryGet<AutoExposure>(out var exposure)) {
        exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
        exposure.fixedExposure.Override(8f);
    }
    if(profile.TryGet<LocalExposure>(out var localExposure))localExposure.active=false;
    var hidden=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i))
        .Where(s=>s.isLoaded&&s!=scene&&s!=lights).SelectMany(s=>s.GetRootGameObjects()).Select(o=>(o,o.activeSelf)).ToArray();
    var otherCameras=Camera.allCameras.Where(c=>c!=camera).Select(c=>(c,c.enabled)).ToArray();
    var pipeline=(VividRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
    var originalGraph=pipeline.RenderGraphAsset;
    var oldOcclusion=pipeline.EnableGPUDrivenOcclusionCulling;
    var game=UnityEngine.Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();

    int frame=0,warm=0,measure=1,pending=0,rendered=0;
    bool active=false;
    string arm=""; float armLux=3600f, changedLux=1800f; int changeFrame=-1,armFullLimit=128;
    object pass=null;
        var errors=new List<string>();
    var baselineDx11=new HashSet<string>();
    var log=new List<string>{"arm,frame,lux,lightR,lightG,lightB,order,batch,bounce,cursor,updated,cards,published,publishedBatches,rays,seed,accumulate,progressive,publishedOrder,clear,boost,history,rescale,scaleR,scaleG,scaleB,emissive,skyTrace,revision,acceptedRevision,lightShadow,dirX,dirY,dirZ"};
    void Log(string message,string stack,LogType kind)
    {
        if(message.Contains("on dx11") && message.Contains("uav",StringComparison.OrdinalIgnoreCase)
            && (message.Contains("maximum supported (8)") || message.Contains("8") && message.Contains("limit",StringComparison.OrdinalIgnoreCase)))
        { baselineDx11.Add(message);return; }
        if((kind==LogType.Error||kind==LogType.Assert||kind==LogType.Exception)&&errors.Count<20)errors.Add(message);
    }
    object StateNumber(string name){var s=type.GetField("m_State",priv).GetValue(pass);return s.GetType().GetField(name,priv).GetValue(s);}
    object SceneNumber(string name){var s=fields["m_Scene"].GetValue(pass);return s.GetType().GetProperty(name,priv|System.Reflection.BindingFlags.Public).GetValue(s);}
    void Begin(ScriptableRenderContext _,Camera cam)
    {
        if(!active||cam!=camera)return;
        int i=frame-warm;
        sun.intensity=changeFrame>=0&&i>=changeFrame?changedLux:armLux;
        sun.color=arm.Contains("colour")&&i>=changeFrame?new Color(.5f,1,.75f,1):sunColor;
        sun.transform.rotation=sunRotation*(arm.Contains("direction")&&i>=changeFrame?Quaternion.Euler(0,10,0):Quaternion.identity);
        sun.shadowStrength=arm.Contains("shadow")&&i>=changeFrame?.5f:sunShadow;
        var targetPosition=originalPanelPosition+(arm.Contains("geometry")&&i>=changeFrame?new Vector3(.02f,0,0):Vector3.zero);
        if(!panel.transform.localPosition.Equals(targetPosition)){panel.transform.localPosition=targetPosition;syncPanel.Invoke(panel,null);}
        if(!arm.Contains("reference"))gi.surfaceCacheFullUpdateCardLimit.Override(i<0?128:armFullLimit);
        float t=0f;
        camera.transform.SetPositionAndRotation(pose.Item1+pose.Item2*new Vector3(.28f*t,0,.06f*t),pose.Item2*Quaternion.Euler(0,-7f*t,0));
        if(frame==0)extra.ResetPostProcessingHistory();
        frameOverride.SetValue(null,1311+i);
        if(!timingOnly&&!arm.Contains("reference")&&i==measure-1)
            typeof(VividAdditionalCameraData).GetMethod("RequestFinalFrameScreenshot",priv)?.Invoke(extra,new object[]{output+"/"+arm+"-"+i+".png"});
    }
    void ReadLighting(CommandBuffer cmd,Texture texture,string tag)
    {
        pending++;
        cmd.RequestAsyncReadback(texture,0,912,96,970,48,0,1,request=>{
            try{if(request.hasError)errors.Add("GI ROI readback "+tag);else System.IO.File.WriteAllBytes(output+"/"+tag,request.GetData<byte>().ToArray());}
            catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }
    void ReadAll(CommandBuffer cmd,Texture texture,string tag)
    {
        pending++;
        cmd.RequestAsyncReadback(texture,0,request=>{
            try{if(request.hasError)errors.Add(tag);else System.IO.File.WriteAllBytes(output+"/"+tag,request.GetData<byte>().ToArray());}
            catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }

    Action<ComputePassContext,Texture,Texture,Texture> capture=(ctx,source,lighting,geometry)=>{
        if(!active||ctx.Get<VividCameraData>().camera!=camera)return;
        pass=((System.Collections.IList)passes.GetValue(null)).Cast<object>().Single(p=>p.GetType()==type);
        int Value(string name)=>(int)fields[name].GetValue(pass);
        int i=frame-warm;
        if(i>=0){
            int order=Value("m_RadiosityOrder"),batch=Value("m_RadiosityBatch"),bounce=Value("m_RadiosityBounce");
            int cursor=Value("m_RadiosityCursor"),updated=Value("m_RadiosityUpdateCards");
            int cards=(int)fields["m_Scene"].GetValue(pass).GetType().GetProperty("RadiosityCardCount",priv|System.Reflection.BindingFlags.Public).GetValue(fields["m_Scene"].GetValue(pass));
            bool accum=(bool)fields["m_RadiosityAccumulate"].GetValue(pass);
            var input=(UnityEngine.Rendering.RenderGraphModule.TextureHandle)fields["m_RadiosityInput"].GetValue(pass);
            bool complete=accum&&bounce+1==gi.surfaceCacheBounceOrders.value&&updated>0&&cursor+updated==cards;
            bool published=input.IsValid()||complete;
            if(accum!=gi.surfaceCacheRadiosityAccumulate.value||Value("m_RadiositySamples")!=gi.surfaceCacheRadiositySamples.value)errors.Add("Unexpected radiosity parameters");
            if(lighting.width!=1920||lighting.height!=1080||geometry.width!=120||geometry.height!=136)errors.Add("Wrong Gather grid");
            var light=(Vector4)type.GetField("m_LightColorShadow",priv).GetValue(pass);
            string Num(float x)=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            log.Add($"{arm},{i},{Num(sun.intensity)},{Num(light.x)},{Num(light.y)},{Num(light.z)},{order},{batch},{bounce},{cursor},{updated},{cards},{published},{(accum?batch+(complete?1:0):0)},{Value("m_RadiositySamples")},{radioSeed.GetValue(null)},{accum},{fields["m_RadiosityProgressive"].GetValue(pass)},{Value("m_RadiosityPublishedOrder")},{fields["m_RadiosityClear"].GetValue(pass)},{Value("m_RadiosityBoostFrames")},{fields["m_HasHistory"].GetValue(pass)},{fields["m_RadiosityRescale"].GetValue(pass)},{Num(((Vector4)fields["m_RadiosityHistoryScale"].GetValue(pass)).x)},{Num(((Vector4)fields["m_RadiosityHistoryScale"].GetValue(pass)).y)},{Num(((Vector4)fields["m_RadiosityHistoryScale"].GetValue(pass)).z)},{fields["m_Scene"].GetValue(pass).GetType().GetProperty("HasEmissiveSurfaces",priv|System.Reflection.BindingFlags.Public).GetValue(fields["m_Scene"].GetValue(pass))},{fields["m_SkyTraceDistance"].GetValue(pass)},{SceneNumber("Revision")},{StateNumber("RadiosityInputSceneRevision")},{Num(light.w)},{Num(((Vector4)type.GetField("m_LightPositionRange",priv).GetValue(pass)).x)},{Num(((Vector4)type.GetField("m_LightPositionRange",priv).GetValue(pass)).y)},{Num(((Vector4)type.GetField("m_LightPositionRange",priv).GetValue(pass)).z)}");
            var cmd=(CommandBuffer)nativeCmd.GetValue(ctx.cmd);
            string tag=arm+"-"+i.ToString("0000");
            if(published){
                var atlas=accum?(UnityEngine.Rendering.RenderGraphModule.TextureHandle)fields["m_RadiosityPublished"].GetValue(pass):input;
                ReadAll(cmd,atlas,tag+"-atlas.f16");
            }
            ReadLighting(cmd,lighting,tag+"-gi.f16");
        }
        rendered++;
        if(++frame>=warm+measure)active=false;
    };
    async System.Threading.Tasks.Task Run(string name,bool accumulate,int rays,int budget,int fullLimit,
        int count,int warmup=0,float lux=3600f,int changeAt=-1,float targetLux=1800f,int seed=0,int orders=8)
    {
        panel.SetSourceMaterials(originalMaterials);panel.SetMaterialProxies(name.Contains("emissive")?emitterProxies:originalProxies);syncPanel.Invoke(panel,null);
        arm=name;armFullLimit=fullLimit;armLux=lux;changedLux=targetLux;changeFrame=changeAt;warm=warmup;measure=count;frame=0;active=true;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
        gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);
        gi.surfaceCacheBounceOrders.Override(orders);gi.surfaceCacheRadiositySamples.Override(rays);
        gi.surfaceCacheRadiosityAccumulate.Override(accumulate);gi.surfaceCacheRadiosityBatchLimit.Override(16);
        gi.surfaceCacheCardsPerFrame.Override(budget);gi.surfaceCacheFullUpdateCardLimit.Override(fullLimit);
        gi.surfaceCacheProgressiveRadiosity.Override(true); gi.surfaceCacheLightingTolerance.Override(.005f); gi.surfaceCacheUpdateBoost.Override(4); radioSeed.SetValue(null,seed);
        gi.restirGather.Override(false);gi.restirPixelIntegration.Override(false);
        gi.pixelTemporal.Override(true);gi.pixelHistoryMaxAge.Override(16);
        gi.directionalScreenProbes.Override(true);gi.screenProbeDirectionalCache.Override(true);
        gi.screenProbeSamplingGuide.Override(false);gi.screenProbeHierarchicalGuide.Override(false);
        gi.screenProbeSpatialFilter.Override(true);gi.screenProbeSpatialAnchor.Override(true);
        gi.screenProbeSpatialKernel.Override(SdfScreenProbeSpatialKernel.Adaptive5x5);
        gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous2);
        gi.screenProbeHitDistanceFilter.Override(false);gi.adaptiveScreenProbes.Override(true);
        gi.realtimeOnly.Override(true);gi.farRadianceCache.Override(false);
        gi.nearOcclusionStrength.Override(0);gi.recordRayDiagnostics.Override(false);gi.recordFirstHitDiagnostics.Override(false);
        diagnosticFlag.SetValue(null,false);diagnosticMask.SetValue(null,name.StartsWith("previous")?65536:0);anchorBlend.SetValue(null,.85f);
        double end=EditorApplication.timeSinceStartup+900;
        while(active&&errors.Count==0&&EditorApplication.timeSinceStartup<end){
            game.Repaint();EditorApplication.QueuePlayerLoopUpdate();await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for(int i=0;i<30&&pending>0;i++)await System.Threading.Tasks.Task.Delay(20);
        if(active||pending>0||errors.Count>0)throw new InvalidOperationException(name+" failed at "+frame+": "+string.Join("\n",errors));
    }
    EditorApplication.LockReloadAssemblies();
    try{
        SceneManager.SetActiveScene(scene);RenderSettings.sun=sun;camera.enabled=true;sunData.interactsWithSky=false;
        extra.antialiasing=VividAntialiasingMode.None;volume.profile=profile;
        if(pipeline.RenderGraphAsset!=graph)throw new InvalidOperationException("Unexpected render graph");
        PassRecorder.Dispose();pipeline.EnableGPUDrivenOcclusionCulling=false;
        foreach(var o in hidden)o.o.SetActive(false);foreach(var o in otherCameras)o.c.enabled=false;
        var full=AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>("Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox/B - Inner Local Card.asset");
        gi.surfaceCache.Override(full);
        RenderPipelineManager.beginCameraRendering+=Begin;Application.logMessageReceived+=Log;hook.SetValue(null,capture);
        stageHook.SetValue(null,(Action<ComputePassContext,int,Texture>)((ctx,stage,texture)=>{
            int index=frame-warm;if(!active||index<0||ctx.Get<VividCameraData>().camera!=camera)return;
            var cmd=(CommandBuffer)nativeCmd.GetValue(ctx.cmd);ReadLighting(cmd,texture,arm+"-"+index.ToString("0000")+(stage==0?"-resolve.f16":"-temporal.f16"));
        }));
        await Run("eligible",true,64,16,128,96,128,1800f,32,3600f);
        await Run("eligible-colour",true,64,16,128,96,128,1800f,32,3600f);
        await Run("emissive",true,64,16,128,96,128,1800f,32,3600f);
        await Run("geometry",true,64,16,128,96,128,1800f,32,3600f);
        await Run("direction",true,64,16,128,96,128,1800f,32,3600f);
        await Run("shadow",true,64,16,128,96,128,1800f,32,3600f);
        await Run("high-range",true,64,16,128,96,128,10000f,32,20000f);
        await Run("zero-to-on",true,64,16,128,96,128,0f,32,3600f);
        await Run("unconverged",true,16,16,128,64,0,1800f,1,3600f);
        return new{rendered,api=SystemInfo.graphicsDeviceType.ToString()};
    }finally{
        active=false;panel.SetSourceMaterials(originalMaterials);panel.SetMaterialProxies(originalProxies);panel.transform.localPosition=originalPanelPosition;syncPanel.Invoke(panel,null);
        UnityEngine.Object.DestroyImmediate(emissiveProxy);sun.color=sunColor;sun.transform.rotation=sunRotation;sun.shadowStrength=sunShadow;hook.SetValue(null,previousHook);diagnosticFlag.SetValue(null,previousDiagnosticFlag);
        stageHook.SetValue(null,previousStageHook);frameOverride.SetValue(null,previousFrameOverride);
        diagnosticMask.SetValue(null,previousMask);anchorBlend.SetValue(null,previousBlend);radioSeed.SetValue(null,previousRadioSeed);
        Application.logMessageReceived-=Log;RenderPipelineManager.beginCameraRendering-=Begin;
        volume.profile=priorProfile;sun.intensity=sunIntensity;sunData.interactsWithSky=oldSky;
        camera.enabled=oldEnabled;camera.transform.SetPositionAndRotation(pose.Item1,pose.Item2);extra.antialiasing=oldAA;
        foreach(var x in hidden)if(x.o!=null)x.o.SetActive(x.activeSelf);
        foreach(var x in otherCameras)if(x.c!=null)x.c.enabled=x.enabled;
        pipeline.EnableGPUDrivenOcclusionCulling=oldOcclusion;AsyncGPUReadback.WaitAllRequests();
        foreach(var c in profile.components)UnityEngine.Object.DestroyImmediate(c);UnityEngine.Object.DestroyImmediate(profile);
        if(originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);RenderSettings.sun=oldSun;
        if(ownLights)EditorSceneManager.CloseScene(lights,true);if(ownScene)EditorSceneManager.CloseScene(scene,true);
        System.IO.File.WriteAllLines(output+"/frames.csv",log);System.IO.File.WriteAllLines(output+"/errors.txt",errors.Distinct());
        System.IO.File.WriteAllLines(output+"/dx11-baseline.txt",baselineDx11);EditorApplication.UnlockReloadAssemblies();
    }
}
