using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven.Meshlets;
{
    const bool cornell=true, timingOnly=false;
    const string output="E:/VividRP_Reborn/Logs/DirectionalHistory-20261111/final";
    const string scenePath=cornell ? "Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox/SurfaceCacheCornellBoxBright.unity"
        : "Assets/ClassicSponza/Scenes/SponzaNoon.unity";
    const string lightPath="Assets/ClassicSponza/Scenes/SponzaLightingNoon.unity";
    const string graphPath="Assets/ClassicSponza/Profiles/Sponza Noon Realtime GI/Sponza Noon Realtime GI.vrdg";
    const System.Reflection.BindingFlags priv=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    var type=typeof(SdfDiffuseGatherPass);
    var maxAgeField=type.GetField("m_PixelHistoryMaxAge",priv) ?? throw new InvalidOperationException("Wait for new GI history to compile.");
    if(typeof(SdfDiffuseGISettings).GetField("spatialDenoise")==null)throw new InvalidOperationException("Wait for A-Trous volume component to compile.");
    var footprintField=type.GetField("m_ProbeSpatialKernel",priv) ?? throw new InvalidOperationException("Wait for probe prefilter to compile."); var iterationsField=type.GetField("m_AtrousIterations",priv) ?? throw new InvalidOperationException("Wait for A-Trous pass to compile.");
    var scratchField=type.GetField("m_AtrousLighting",priv);
    var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGather.compute");
    if(shader==null||!shader.HasKernel("FilterSdfDiffuseAtrousFinal"))throw new InvalidOperationException("Wait for A-Trous shader to import.");
    var runLog=new List<string>{"arm,frame,footprint,iterations,pixelTemporal,directional"};
    if(type.GetMethod("ReadLight",priv).GetParameters().Length!=1||EditorApplication.isCompiling)
        throw new InvalidOperationException("Wait for the updated Unity assemblies.");
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
    var fields=new[]{"m_RadiositySamples","m_SpatialAnchor","m_PreparedAnchor","m_AtrousLighting","m_RecordProbeAnchorDiagnostics","m_Probes","m_Depth","m_GBuffer1","m_InverseViewProjection","m_OpenPbrParameters","m_OpenPbrAux0","m_OpenPbrAux1","m_MeshDetailDistance","m_RestirVariance","m_CurrentRestirVariance","m_GatherTemporalCompute","m_ScreenProbeTemporalKernel","m_GatherCompute","m_DirectionalResolveKernel","m_CurrentDirectional","m_CurrentDirectionalFiltered","m_DirectionalTemporal","m_CurrentPixelGeometry","m_ScreenMotion","m_DirectionalHierarchicalGuide","m_HitDistanceFilter","m_NearOcclusionKernel","m_SunUpdateCards","m_RadiosityUpdateCards","m_RestirOpenPbrDiffuse","m_DirectionalGuideTexture","m_DirectionalRayTexture","m_DirectionalCache","m_DirectionalGuide","m_PreviousScreenRadiance","m_ScreenRays","m_RadiosityTargetOrders","m_RadiosityInput","m_RadiosityOrder","m_RadiosityCursor","m_RadiosityUpdateCards","m_Scene","m_RawResolved","m_CurrentPixelLighting","m_PixelHistory","m_RestirGather","m_HasHistory","m_FirstHitDiagnostics","m_FilteredProbes","m_RayDiagnostics","m_FarAtlas","m_FarUpdateCount","m_FarClear","m_FarWindow","m_Bias","m_TraceDistance","m_FarDistance",
        "m_NearOcclusion","m_GBuffer0","m_ViewProjection","m_UseSdf","m_HasScreenHistory","m_RestirPixelIntegration","m_Output"}.Distinct().ToDictionary(n=>n,n=>type.GetField(n,priv));

    var state=type.GetField("m_State",priv);
    var frameField=state.FieldType.GetField("Frame",priv);
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
    var sun=lightRoots.SelectMany(o=>o.GetComponentsInChildren<Light>(true)).First(l=>l.type==LightType.Directional&&l.isActiveAndEnabled);
    var sunIntensity=sun.intensity;
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
    bool active=false,scroll=false,allowFar=false;
    string arm="";
    object pass=null;
        var errors=new List<string>();
    var baselineDx11=new HashSet<string>();
    var sunLog=new List<string>{"arm,frame,lux,lightR,lightG,lightB,traceDistance,history,realtimeOnly"};
    var budgetLog=new List<string>{"arm,frame,order,cursor,updated,cards"};
    var log=new List<string>{"arm,frame,screen,history,order,w,h,gw,gh,vp"}; var exercised=new HashSet<string>();
    void Log(string message,string stack,LogType kind)
    {
        if(message.Contains("on dx11") && message.Contains("uav",StringComparison.OrdinalIgnoreCase)
            && (message.Contains("maximum supported (8)") || message.Contains("8") && message.Contains("limit",StringComparison.OrdinalIgnoreCase)))
        { baselineDx11.Add(message);return; }
        if((kind==LogType.Error||kind==LogType.Assert||kind==LogType.Exception)&&errors.Count<20)errors.Add(message);
    }
    void Begin(ScriptableRenderContext _,Camera cam)
    {
        if(!active||cam!=camera)return;
        int i=frame-warm;
        float t=0f;
        camera.transform.SetPositionAndRotation(pose.Item1+pose.Item2*new Vector3(.28f*t,0,.06f*t),pose.Item2*Quaternion.Euler(0,-7f*t,0));
        if(frame==0)extra.ResetPostProcessingHistory();
        frameOverride.SetValue(null,1311+i);
        if(!timingOnly&&!arm.Contains("reference")&&i==measure-1)
            typeof(VividAdditionalCameraData).GetMethod("RequestFinalFrameScreenshot",priv)?.Invoke(extra,new object[]{output+"/"+arm+"-"+i+".png"});
    }
    Texture Resolve(string key)
    {
        var value=fields[key].GetValue(pass);
        return value is RenderGraphTexture rt ? (UnityEngine.Rendering.RenderGraphModule.TextureHandle)rt
            : (UnityEngine.Rendering.RenderGraphModule.TextureHandle)value;
    }
    var regions=new[]{(name:"ceiling",x:48,y:59,w:24,h:7)};
    int pointCount=regions.Sum(r=>r.w*r.h);
    int offset=0;
    var regionLog=new List<string>{"roi,x,y,w,h,offset"};
    foreach(var r in regions){regionLog.Add($"{r.name},{r.x},{r.y},{r.w},{r.h},{offset}");offset+=r.w*r.h;}
    System.IO.File.WriteAllLines(output+"/regions.csv",regionLog);
    void ReadProbes(CommandBuffer cmd,Texture texture,string tag,int planes,int bytesPerPixel)
    {
        pending++;
        cmd.RequestAsyncReadback(texture,0,0,texture.width,59,7,0,1,request=>{
            try {
                if(request.hasError)errors.Add("GPU readback error "+tag);
                else {
                    var src=request.GetData<byte>().ToArray();
                    var dst=new byte[planes*pointCount*bytesPerPixel];
                    int cursor=0;
                    for(int plane=0;plane<planes;plane++)foreach(var r in regions)for(int y=r.y;y<r.y+r.h;y++) {
                        Buffer.BlockCopy(src,(((y-59)*texture.width)+plane*120+r.x)*bytesPerPixel,dst,cursor,r.w*bytesPerPixel);
                        cursor+=r.w*bytesPerPixel;
                    }
                    System.IO.File.WriteAllBytes(output+"/"+tag,dst);
                }
            }catch(Exception e){errors.Add(tag+": "+e.Message);}finally{pending--;}
        });
    }
    void ReadLighting(CommandBuffer cmd,Texture texture,string tag)
    {
        pending++;
        cmd.RequestAsyncReadback(texture,0,912,96,970,48,0,1,request=>{
            try{if(request.hasError)errors.Add("GI ROI readback "+tag);else System.IO.File.WriteAllBytes(output+"/"+tag,request.GetData<byte>().ToArray());}
            catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }
    void ReadDirection(CommandBuffer cmd,Texture texture,string tag,int layer)
    {
        pending++;
        cmd.RequestAsyncReadback(texture,0,0,1920,59+136*layer,7,0,1,request=>{
            try{if(request.hasError)errors.Add(tag);else{
                var src=request.GetData<byte>().ToArray();var dst=new byte[16*168*8];int cursor=0;
                for(int plane=0;plane<16;plane++)for(int y=0;y<7;y++){
                    Buffer.BlockCopy(src,(y*1920+plane*120+48)*8,dst,cursor,24*8);cursor+=24*8;
                }
                System.IO.File.WriteAllBytes(output+"/"+tag,dst);
            }}catch(Exception e){errors.Add(e.Message);}finally{pending--;}
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
        var list=((System.Collections.IList)passes.GetValue(null)).Cast<object>().ToArray();
        pass=list.Single(p=>p.GetType()==type);
        bool screen=(bool)fields["m_HasScreenHistory"].GetValue(pass);
        bool history=(bool)fields["m_HasHistory"].GetValue(pass);
        int i=frame-warm;
        bool GetBool(string key)=>(bool)fields[key].GetValue(pass);
        bool dense=arm.Contains("restir");
        bool dir=arm.Contains("directional");
        bool light=arm.Contains("radiosity");
        if(light && (int)fields["m_SunUpdateCards"].GetValue(pass)>0)exercised.Add("sun update");
        if(light && (int)fields["m_RadiosityUpdateCards"].GetValue(pass)>0)exercised.Add("radiosity update");
        if((int)fields["m_FarUpdateCount"].GetValue(pass)>0)exercised.Add("far cache update");
        if(i>=0) {
            var rt=fields["m_Output"].GetValue(pass);
            var defer=typeof(DeferredLightingPass).GetField("m_RealtimeDiffuseGI",priv);
            if(!list.OfType<DeferredLightingPass>().Any(p=>object.ReferenceEquals(defer.GetValue(p),rt)))
                errors.Add("The GI output is not bound to Deferred.");
            if(GetBool("m_UseSdf")!=(!arm.Contains("screenonly")) || GetBool("m_RestirGather")!=dense
                || GetBool("m_RestirPixelIntegration")!=dense || (fields["m_PixelHistory"].GetValue(pass)!=null)==arm.Contains("nopixel")
                || GetBool("m_DirectionalCache")!=dir || GetBool("m_DirectionalGuide")!=arm.Contains("guided")
                || GetBool("m_DirectionalHierarchicalGuide")
                || GetBool("m_HitDistanceFilter")!=arm.Contains("hitfilter")
                || lighting.width!=1920 || lighting.height!=1080)
                errors.Add($"Wrong profile {arm}/{i}: screen={screen}, history={history}, {lighting.width}x{lighting.height}, dir={GetBool("m_DirectionalCache")}, dense={dense}, far={fields["m_FarUpdateCount"].GetValue(pass)}");
            if(dir && ((ComputeShader)fields["m_GatherCompute"].GetValue(pass)).FindKernel("ScreenProbeIntegrateCS")!=(int)fields["m_DirectionalResolveKernel"].GetValue(pass)) errors.Add("Wrong full resolution kernel.");
            if(dir && ((ComputeShader)fields["m_GatherTemporalCompute"].GetValue(pass)).FindKernel("ScreenProbeTemporalReprojectionCS")!=(int)fields["m_ScreenProbeTemporalKernel"].GetValue(pass)) errors.Add("Wrong full resolution temporal kernel.");
            if(GetBool("m_RestirOpenPbrDiffuse")!=dense)errors.Add("Expected different OpenPBR integration state for "+arm); if((float)fields["m_MeshDetailDistance"].GetValue(pass)<=0)errors.Add("Segmented SDF tracing is inactive."); exercised.Add(arm+(dense?(GetBool("m_RestirOpenPbrDiffuse")?" OpenPBR":" Lambert"):""));
            string Num(float x)=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            var vp=(Matrix4x4)fields["m_ViewProjection"].GetValue(pass);
            string matrix=string.Join(" ",Enumerable.Range(0,16).Select(j=>Num(vp[j/4,j%4])));
            log.Add($"{arm},{i},{screen},{history},{fields["m_RadiosityOrder"].GetValue(pass)},{lighting.width},{lighting.height},{geometry.width},{geometry.height},{matrix}");
            var cmd=(CommandBuffer)nativeCmd.GetValue(ctx.cmd);
            string tag=arm+"-"+i.ToString("00");
            // timing.Add($"{arm},{i},{totalTimer.gpuElapsedNanoseconds},{filterTimer.gpuElapsedNanoseconds},{temporalTimer.gpuElapsedNanoseconds}");
            var sunColor=(Vector4)type.GetField("m_LightColorShadow",priv).GetValue(pass);
            float traceDistance=(float)fields["m_TraceDistance"].GetValue(pass);
            bool realtimeOnly=(bool)type.GetField("m_RealtimeOnly",priv).GetValue(pass);
            if(!(sunColor.x>0f) || !realtimeOnly)
                errors.Add("Expected Cornell realtime-only directional input; " + sun.intensity+" / "+sunColor);
            sunLog.Add($"{arm},{i},{Num(sun.intensity)},{Num(sunColor.x)},{Num(sunColor.y)},{Num(sunColor.z)},{Num(traceDistance)},{history},{realtimeOnly}");
            if((int)maxAgeField.GetValue(pass)!=(arm.Contains("age32")?32:arm.Contains("age8")?8:16))errors.Add("Wrong pixel history cap."); int iterations=(int)iterationsField.GetValue(pass);
            int expected=arm.Contains("noatrous")?0:2; int footprint=(int)(SdfScreenProbeSpatialKernel)footprintField.GetValue(pass); if(footprint!=(dense?0:arm.StartsWith("grid5")?2:1))errors.Add("Wrong probe footprint.");
            if(iterations!=expected)errors.Add("Unexpected A-Trous iteration count "+arm+"/"+iterations);
            if(iterations>0 && !((UnityEngine.Rendering.RenderGraphModule.TextureHandle)(RenderGraphTexture)scratchField.GetValue(pass)).IsValid())
                errors.Add("Invalid A-Trous scratch");
            runLog.Add($"{arm},{i},{footprint},{iterations},{fields["m_PixelHistory"].GetValue(pass)!=null},{dir}");
            bool diagnostic=arm.Contains("diagnostics");
            if(GetBool("m_RecordProbeAnchorDiagnostics")!=diagnostic)errors.Add("Wrong diagnostic state");
            var shTexture=Resolve("m_FilteredProbes");
            if(shTexture.width!=(diagnostic?22:7)*120||geometry.width!=120||geometry.height!=136)errors.Add("Unexpected probe atlas descriptor");
            if(GetBool("m_SpatialAnchor")!=arm.Contains("spatial"))errors.Add("Wrong spatial anchor mode");
            ReadProbes(cmd,shTexture,tag+"-sh.f16",22,8);ReadDirection(cmd,Resolve("m_CurrentDirectional"),tag+"-draw.f16",0);ReadDirection(cmd,Resolve("m_CurrentDirectional"),tag+"-draw-meta.f16",1);
            ReadProbes(cmd,Resolve("m_Probes"),tag+(arm.Contains("fp32")?"-anchor.f32":"-anchor.f16"),1,arm.Contains("fp32")?16:8);
            ReadLighting(cmd,lighting,tag+"-final.f16");
            ReadLighting(cmd,Resolve("m_CurrentPixelLighting"),tag+"-history.f16");
            ReadLighting(cmd,Resolve("m_AtrousLighting"),tag+"-fine.f16");
            if(i==0||i==measure-1)ReadProbes(cmd,geometry,tag+"-geometry.f32",1,16);
            if(i%4==0){ReadDirection(cmd,Resolve("m_CurrentDirectional"),tag+"-composite-plane.f16",2);ReadDirection(cmd,Resolve("m_DirectionalTemporal"),tag+"-directional-plane.f16",2);ReadDirection(cmd,Resolve("m_CurrentDirectional"),tag+"-composite.f16",0);ReadDirection(cmd,Resolve("m_CurrentDirectional"),tag+"-composite-meta.f16",1);ReadDirection(cmd,Resolve("m_DirectionalTemporal"),tag+"-directional.f16",0);ReadDirection(cmd,Resolve("m_DirectionalTemporal"),tag+"-directional-meta.f16",1);}
            if((int)fields["m_RadiositySamples"].GetValue(pass)!=4096)errors.Add("Wrong radiosity samples");
            if((int)fields["m_RadiosityOrder"].GetValue(pass)!=8||(int)fields["m_RadiosityUpdateCards"].GetValue(pass)!=0)errors.Add("Radiosity is not frozen");
            if(i==0||i==measure-1){
                var atlas=(UnityEngine.Rendering.RenderGraphModule.TextureHandle)fields["m_RadiosityInput"].GetValue(pass);
                ReadAll(cmd,atlas,tag+"-radiosity.f16");
            }
            if((int)fields["m_RadiosityTargetOrders"].GetValue(pass)!=(arm.Contains("bounce0")?0:arm.Contains("bounce3")?3:8))errors.Add("Wrong bounce order "+arm); var sdfScene=fields["m_Scene"].GetValue(pass); var cards=sdfScene.GetType().GetProperty("RadiosityCardCount",priv|System.Reflection.BindingFlags.Public).GetValue(sdfScene);
            budgetLog.Add($"{arm},{i},{fields["m_RadiosityOrder"].GetValue(pass)},{fields["m_RadiosityCursor"].GetValue(pass)},{fields["m_RadiosityUpdateCards"].GetValue(pass)},{cards}");

        }
        rendered++;
        if(++frame>=warm+measure)active=false;
    };
    async System.Threading.Tasks.Task Run(string name,int warmup,int count,bool moved,bool far,float nearStrength)
    {
        arm=name;warm=warmup;measure=count;scroll=moved;allowFar=far;frame=0;active=true;
        bool dir=name.Contains("directional");
        bool restir=name.Contains("restir");
        bool light=name.Contains("radiosity");

        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
        gi.traceMode.Override(name.Contains("sdfonly")?SdfDiffuseTraceMode.SdfOnly:SdfDiffuseTraceMode.Hybrid);
        int orders=name.Contains("bounce0")?0:name.Contains("bounce3")?3:8;
        gi.surfaceCacheRadiosityAccumulate.Override(false); gi.farRadianceCache.Override(false); gi.surfaceCacheBounceOrders.Override(orders); gi.surfaceCacheRadiositySamples.Override(4096); gi.surfaceCacheCardsPerFrame.Override(1); gi.surfaceCacheFullUpdateCardLimit.Override(0); radioSeed.SetValue(null,0);
        gi.restirOpenPbrDiffuse.Override(restir);
        gi.restirGather.Override(restir);
        gi.restirPixelIntegration.Override(restir);
        gi.pixelTemporal.Override(!name.Contains("nopixel")); gi.pixelHistoryMaxAge.Override(name.Contains("age32")?32:name.Contains("age8")?8:16);
        gi.directionalScreenProbes.Override(dir);
        gi.screenProbeDirectionalCache.Override(dir);
        gi.screenProbeSamplingGuide.Override(name.Contains("guided"));
        gi.screenProbeHierarchicalGuide.Override(false); gi.recordRayDiagnostics.Override(false); gi.recordFirstHitDiagnostics.Override(false);
        gi.spatialDenoise.Override(name.Contains("noatrous")?SdfDiffuseSpatialDenoise.Legacy:SdfDiffuseSpatialDenoise.Atrous2);
        gi.screenProbeSpatialFilter.Override(true);
        gi.screenProbeSpatialAnchor.Override(name.Contains("spatial"));
        gi.screenProbeHitDistanceFilter.Override(name.Contains("hitfilter"));
        gi.realtimeOnly.Override(true);
        gi.adaptiveScreenProbes.Override(!name.Contains("noadaptive"));
        gi.screenProbeSpatialKernel.Override(name.StartsWith("grid3")?SdfScreenProbeSpatialKernel.Neighborhood3x3:
            name.StartsWith("grid5")?SdfScreenProbeSpatialKernel.Adaptive5x5:SdfScreenProbeSpatialKernel.Cross);

        diagnosticFlag.SetValue(null,true);
        int mask=name.Contains("legacy")?1024:name.Contains("decay")?2048:0;
        diagnosticMask.SetValue(null,mask);
        anchorBlend.SetValue(null,name.Contains("anchor97")?0.97f:0.85f);
        double end=EditorApplication.timeSinceStartup+600;
        while(active&&errors.Count==0&&EditorApplication.timeSinceStartup<end) {
            game.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
            await System.Threading.Tasks.Task.Delay(16);
        }
        AsyncGPUReadback.WaitAllRequests();
        for(int i=0;i<30&&pending>0;i++)await System.Threading.Tasks.Task.Delay(20);
        if(active||pending>0||errors.Count>0)
            throw new InvalidOperationException($"{arm} failed after {frame}: "+string.Join("\n",errors));
    }

    EditorApplication.LockReloadAssemblies();
    try {
        SceneManager.SetActiveScene(scene);
        RenderSettings.sun=sun;
        camera.enabled=true;
        extra.antialiasing=VividAntialiasingMode.None;
        volume.profile=profile;
        if(pipeline.RenderGraphAsset!=graph)throw new InvalidOperationException("The active graph differs from the Cornell OpenPBR graph."); PassRecorder.Dispose();
        pipeline.EnableGPUDrivenOcclusionCulling=false;
        foreach(var o in hidden)o.o.SetActive(false);
        foreach(var o in otherCameras)o.c.enabled=false;

        RenderPipelineManager.beginCameraRendering+=Begin;
        Application.logMessageReceived+=Log;
        hook.SetValue(null,capture);
        var full=AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>(
            "Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox/B - Inner Local Card.asset");
        if(full==null)throw new InvalidOperationException("Missing local card B.");
        gi.surfaceCache.Override(full);
        sun.intensity=3600f;
        stageHook.SetValue(null,(Action<ComputePassContext,int,Texture>)((ctx,stage,texture)=>{
            if(!active||ctx.Get<VividCameraData>().camera!=camera)return;
            int index=frame-warm;if(index<0)return;
            var cmd=(CommandBuffer)nativeCmd.GetValue(ctx.cmd);
            ReadLighting(cmd,texture,arm+"-"+index.ToString("00")+(stage==0?"-resolve.f16":"-temporal.f16"));
        }));
        foreach(var selected in new[]{"legacy","decay","borrow"})
            await Run("grid5-directional-hybrid-bounce8-spatial-"+selected+"-diagnostics",selected=="legacy"?424:128,512,false,false,0);

        System.IO.File.WriteAllLines(output+"/exercised.txt",exercised);
        return new {rendered,api=SystemInfo.graphicsDeviceType.ToString()};
    } finally {
        active=false;
        hook.SetValue(null,previousHook);
        diagnosticFlag.SetValue(null,previousDiagnosticFlag);
        stageHook.SetValue(null,previousStageHook);
        frameOverride.SetValue(null,previousFrameOverride);
        diagnosticMask.SetValue(null,previousMask);
        anchorBlend.SetValue(null,previousBlend);
        radioSeed.SetValue(null,previousRadioSeed);
        Application.logMessageReceived-=Log;
        RenderPipelineManager.beginCameraRendering-=Begin;
        volume.profile=priorProfile;
        sun.intensity=sunIntensity;
        camera.enabled=oldEnabled;
        camera.transform.SetPositionAndRotation(pose.Item1,pose.Item2);
        extra.antialiasing=oldAA;
        foreach(var x in hidden)if(x.o!=null)x.o.SetActive(x.activeSelf);
        foreach(var x in otherCameras)if(x.c!=null)x.c.enabled=x.enabled;

        pipeline.EnableGPUDrivenOcclusionCulling=oldOcclusion;
        AsyncGPUReadback.WaitAllRequests();
        foreach(var c in profile.components)UnityEngine.Object.DestroyImmediate(c);
        UnityEngine.Object.DestroyImmediate(profile);
        if(originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
        RenderSettings.sun=oldSun;
        if(ownLights)EditorSceneManager.CloseScene(lights,true);
        if(ownScene)EditorSceneManager.CloseScene(scene,true);
        System.IO.File.WriteAllLines(output+"/sun.csv",sunLog); System.IO.File.WriteAllLines(output+"/modes.csv",runLog);
        EditorApplication.UnlockReloadAssemblies();
        System.IO.File.WriteAllLines(output+"/frames.csv",log);
        System.IO.File.WriteAllLines(output+"/budget.csv",budgetLog);
        System.IO.File.WriteAllLines(output+"/errors.txt",errors.Distinct());
        System.IO.File.WriteAllLines(output+"/dx11-baseline.txt",baselineDx11);
    }
}
