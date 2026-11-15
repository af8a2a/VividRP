// PlayMode-only steady GPU timing. Cold-epoch A/A, then two ABBA blocks.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;
using UnityEditor;
using Unity.Profiling;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const int warmup = 1024, flush = 128, measured = 512, drain = 64;
    const int timingStart = warmup + flush, lastFrame = timingStart + measured + drain;
    const long savedPayloadBudget = 1024L * 1024 * 1024;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    if (!Application.isPlaying || EditorApplication.isPaused) throw new InvalidOperationException("An unpaused PlayMode session is required.");
    if (UnityEngine.Profiling.Profiler.enableBinaryLog) throw new InvalidOperationException("A binary Profiler recording is active.");
    var type = typeof(SdfDiffuseGatherPass);
    var hookNames = new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherFrameOverride",
        "EditorGatherHistoryDiagnostics", "EditorProbeAnchorMaxAgeOverride", "EditorRadiositySeed" };
    var hooks = hookNames.ToDictionary(n => n, n => type.GetField(n, stat));
    var previousHooks = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));
    foreach (string n in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorBindResolveProbe", "EditorAtrousDispatchCapture" })
        if (type.GetField(n, stat).GetValue(null) != null) throw new InvalidOperationException("Another GI capture is active: " + n);
    if ((int)hooks["EditorGatherFrameOverride"].GetValue(null) != -1 || (int)hooks["EditorGatherHistoryDiagnostics"].GetValue(null) != 0
        || (int)hooks["EditorProbeAnchorMaxAgeOverride"].GetValue(null) != -1) throw new InvalidOperationException("Another diagnostic is configured.");
    var scene = SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("The clean Cornell fixture must be loaded.");
    var roots = scene.GetRootGameObjects();
    var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    if (Vector3.Distance(camera.transform.position, new Vector3(0,1.5f,-3.8f)) > 1e-5f
        || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 1e-4f || Mathf.Abs(camera.fieldOfView-42) > 1e-5f)
        throw new InvalidOperationException("The reference camera pose is required.");
    var volume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>()).Single();
    var cameraExtra = camera.GetComponent<VividAdditionalCameraData>();
    var previousProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
    var profile = ScriptableObject.CreateInstance<VolumeProfile>();
    foreach (var c in (previousProfile ?? volume.sharedProfile).components) profile.components.Add(UnityEngine.Object.Instantiate(c));
    if (!profile.TryGet<SdfDiffuseGISettings>(out var gi)) throw new InvalidOperationException("Missing GI profile.");
    var previousScene = SceneManager.GetActiveScene();
    // Isolate database contents as well as camera visibility. Restore only the
    // runtime active flags; no scene or asset is saved by this experiment.
    var otherRoots = Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .Where(s=>s!=scene && s.isLoaded).SelectMany(s=>s.GetRootGameObjects())
        .Select(r=>(root:r,active:r.activeSelf)).ToArray();
    var cameras = Camera.allCameras.Select(c => (camera:c, enabled:c.enabled)).ToArray();
    var pass = ((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null))
        .Cast<object>().Single(p => p.GetType() == type);
    object Field(string n) => type.GetField(n, inst).GetValue(pass);
    Texture Resolve(string n) {
        var value = Field(n); var handle = value is RenderGraphTexture graph ? (TextureHandle)graph : (TextureHandle)value;
        return handle.IsValid() ? (Texture)handle : null;
    }
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    bool previousProfiler = UnityEngine.Profiling.Profiler.enabled;
    bool previousGpu = UnityEditorInternal.ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU);
    bool previousEditorProfile = UnityEditorInternal.ProfilerDriver.profileEditor;
    bool previousBackground = Application.runInBackground;
    ComputeShader depthCopy=null; int depthCopyKernel=-1; RenderTexture depthReadback=null;
    string[] names = { "Diffuse GI Total", "Diffuse GI Gather and SDF Fallback", "Diffuse GI Screen Trace",
        "Screen Probe Convert to Irradiance", "Screen Probe Anchor Prefilter", "Screen Probe Resolve", "Screen Probe Pixel Temporal" };
    var recorders = new ProfilerRecorder[names.Length];
    var records = new List<object>(); var timings = new List<object>(); var errors = new List<string>();
    string output = "E:/VividRP_GI_Evidence/ScreenProbeAnchorGpuTiming-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    string arm = ""; int frame = 0, age = 128, pending = 0, targetCount = 0, otherCount = 0;
    int phase = 0, readyFrames = 0, sourceFrames = 0;
    bool active = false, complete = false, timing = false, resetPending = false;
    var referenceHashes = new Dictionary<string,string>();
    long savedPayloadBytes=0;
    void Read(CommandBuffer cmd, Texture texture, string tag, bool save = true, int mip = 0) {
        if (texture == null) throw new InvalidOperationException("Missing texture: " + tag);
        string name = arm; int f = frame, width = Mathf.Max(1,texture.width>>mip), height = Mathf.Max(1,texture.height>>mip); string format = texture.graphicsFormat.ToString();
        string path = output + "/" + name + "/" + f.ToString("0000") + "-" + tag + ".bin";
        pending++;
        cmd.RequestAsyncReadback(texture, mip, request => {
            try {
                if (request.hasError) throw new InvalidOperationException("Readback failed: " + path);
                var bytes = request.GetData<byte>().ToArray();
                using var hash = System.Security.Cryptography.SHA256.Create();
                string sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                string key = f+"-"+tag;
                bool mismatch=false;
                bool densePreflight=f>0 && f<warmup-1 && (name=="00-half128" || name=="01-half128");
                if (densePreflight || ((f==warmup-1 || f==lastFrame-1) && (tag=="geometry" || tag=="raw" || tag=="radiosity"))) {
                    if (name == "00-half128") referenceHashes.Add(key,sha256);
                    else if (!referenceHashes.TryGetValue(key,out string expected) || expected != sha256)
                    { mismatch=true; errors.Add("Timing input mismatch: "+name+"/"+key); }
                }
                if (save || mismatch) {
                    if(bytes.LongLength > savedPayloadBudget-savedPayloadBytes)
                        throw new System.IO.IOException("Saved payload budget exceeded; capture stopped without deleting evidence.");
                    System.IO.File.WriteAllBytes(path, bytes); savedPayloadBytes+=bytes.LongLength;
                }
                records.Add(new { arm=name, frame=f, tag, width, height, format, sha256, saved=save||mismatch, path=save||mismatch?path:null });
            } catch (Exception e) { errors.Add(e.Message); }
            finally { pending--; }
        });
    }
    void Begin(ScriptableRenderContext context, Camera current) {
        if (!active) return;
        if (current != camera) { otherCount++; return; }
        if (resetPending) {
            var owner=type.GetField("m_Cameras",inst).GetValue(pass);
            var state=owner.GetType().GetMethod("GetOrCreateBase").Invoke(owner,new object[]{camera});
            state.GetType().GetField("ProbeAnchorMaxAge",inst).SetValue(state,-1);
            resetPending=false; phase=1; frame=0;
        }
        hooks["EditorGatherFrameOverride"].SetValue(null, 1733 + (phase==1 ? frame : 0));
        if (phase==1 && frame == timingStart) {
            if (pending != 0) { errors.Add("Quality readback did not drain before timing."); return; }
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.StartImmediately
                | ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.GpuRecorder;
            for (int i=0;i<names.Length;++i) recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render,names[i],1024,options);
            timing = true;
        }
    }
    Action<ComputePassContext,Texture,Texture,Texture> trace = (context,sources,lighting,geometry) => {
        if (!active || context.Get<VividCameraData>().camera != camera) return;
        bool sourceReady=(int)Field("m_RadiosityPublishedOrder")==8 && (int)Field("m_RadiosityBatch")==32
            && (int)Field("m_RadiosityUpdateCards")==0;
        if (phase==0) {
            sourceFrames++; readyFrames=sourceReady?readyFrames+1:0;
            if (readyFrames>=8) resetPending=true;
            return;
        }
        if (lighting.width != 1920 || lighting.height != 1080 || geometry.width != 120 || geometry.height != 136
            || (int)Field("m_ProbeAnchorMaxAge") != age || !(bool)Field("m_AnchorPreFilter") || (bool)Field("m_RestirGather")
            || (int)Field("m_AtrousIterations") != 4 || !sourceReady)
            throw new InvalidOperationException("Measured workload changed.");
        var currentState=type.GetField("m_State",inst).GetValue(pass);
        var temporal=context.Get<VividTemporalData>();
        records.Add(new { arm,frame,cpu=new {
            frameOverride=hooks["EditorGatherFrameOverride"].GetValue(null),
            stateFrame=currentState.GetType().GetField("Frame",inst).GetValue(currentState),
            hasHistory=Field("m_HasHistory"),hasScreenHistory=Field("m_HasScreenHistory"),
            directionalGuide=Field("m_DirectionalGuide"),hierarchicalGuide=Field("m_DirectionalHierarchicalGuide"),
            farUpdate=Field("m_FarUpdateCount"),pipelineFrame=typeof(VividCameraData).GetField("frameIndex",inst).GetValue(context.Get<VividCameraData>()),
            unityFrame=Time.frameCount,renderedFrame=Time.renderedFrameCount,
            jitter=new[]{temporal.jitter.x,temporal.jitter.y},previousJitter=new[]{temporal.previousJitter.x,temporal.previousJitter.y} } });
        if ((arm=="00-half128" || arm=="01-half128") && frame<warmup-1) {
            var diagnostics=(CommandBuffer)wrapped.GetValue(context.cmd);
            bool save=frame<3;
            Read(diagnostics,geometry,"geometry",save); Read(diagnostics,Resolve("m_HitLighting"),"raw",save);
            Read(diagnostics,Resolve("m_DirectionalGuideTexture"),"proposal",save);
            Read(diagnostics,Resolve("m_DirectionalRayTexture"),"rays",save);
            Read(diagnostics,Resolve("m_GBuffer0"),"gbuffer0",false);
            Read(diagnostics,Resolve("m_GBuffer1"),"gbuffer1",false);
            diagnostics.SetComputeTextureParam(depthCopy,depthCopyKernel,"_InputDepth",Resolve("m_Depth"));
            diagnostics.SetComputeTextureParam(depthCopy,depthCopyKernel,"rw_spd_mip0",depthReadback);
            diagnostics.DispatchCompute(depthCopy,depthCopyKernel,240,135,1);
            Read(diagnostics,depthReadback,"depth-copy",save);
            Read(diagnostics,Resolve("m_ScreenMotion"),"motion",false);
            Read(diagnostics,Resolve("m_PreviousScreenGeometry"),"previous-screen-geometry",false);
            // HZB mip0 is the existing R32F copy of full-resolution depth.
            // Native depth/UInt visibility readback is unsupported on this Editor.
            var hzb=Resolve("m_ScreenHzb");
            for(int mip=0;mip<5;mip++) Read(diagnostics,hzb,"hzb-"+mip,false,mip);
            Read(diagnostics,Resolve("m_ScreenRays"),"screen-rays",save);
            if ((bool)Field("m_RecordRayDiagnostics")) Read(diagnostics,Resolve("m_RayDiagnostics"),"ray-diagnostics",save);
            if ((bool)Field("m_RecordFirstHitDiagnostics")) Read(diagnostics,Resolve("m_FirstHitDiagnostics"),"first-hit",save);
            Read(diagnostics,Resolve("m_CurrentDirectional"),"composite",save);
            Read(diagnostics,Resolve("m_DirectionalTemporal"),"directional-temporal",save);
            Read(diagnostics,Resolve("m_CurrentDirectionalFiltered"),"directional-filtered",save);
            if (frame<=16) {
                Read(diagnostics,Resolve("m_PreviousDirectional"),"previous-directional",save);
                Read(diagnostics,Resolve("m_PreviousGeometry"),"previous-geometry",save);
                Read(diagnostics,Resolve("m_PreviousDirectionalFiltered"),"previous-directional-filtered",save);
                Read(diagnostics,Resolve("m_PreviousScreenRadiance"),"previous-screen-radiance",false);
                var currentVP=(Matrix4x4)Field("m_ViewProjection");
                var previousVP=(Matrix4x4)currentState.GetType().GetField("ViewProjection",inst).GetValue(currentState);
                float[] Matrix(Matrix4x4 m) => Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();
                float[] Vector(Vector4 v) => new[]{v.x,v.y,v.z,v.w};
                records.Add(new { arm,frame,matrices=new { current=Matrix(currentVP),previous=Matrix(previousVP),
                    inverse=Matrix((Matrix4x4)Field("m_InverseViewProjection")),
                    cameraForward=Vector((Vector4)Field("m_ProbeCameraForward")),lightScale=Vector((Vector4)Field("m_RadiosityHistoryScale")) } });
            }
        }
        if (frame != warmup-1 && frame != lastFrame-1) return;
        var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
        Read(cmd,geometry,"geometry"); Read(cmd,Resolve("m_HitLighting"),"raw"); Read(cmd,Resolve("m_RadiosityInput"),"radiosity");
        if (frame == warmup-1) {
            foreach (string resource in new[] { "m_Probes","m_PreviousProbes","m_PreparedAnchor","m_ReconstructedAnchor" }) {
                var texture = Resolve(resource); string format = texture.graphicsFormat.ToString();
                if (format != (age > 128 ? "R32G32B32A32_SFloat" : "R16G16B16A16_SFloat")) throw new InvalidOperationException("Wrong anchor format.");
                records.Add(new { arm,frame,resource,format,texture.width,texture.height });
            }
        }
    };
    Action<ComputePassContext,Texture,Texture,Texture,Texture> linear = (context,lighting,depth,g0,g1) => {
        if (!active || phase!=1 || context.Get<VividCameraData>().camera != camera) return;
        targetCount++;
        if (++frame < lastFrame) return;
        // GPU samples arrive asynchronously; keep their real counts and censor
        // the first/last 64 returned samples during analysis, not CPU-frame labels.
        for (int i=0;i<names.Length;++i) {
            var samples = new List<ProfilerRecorderSample>(); recorders[i].CopyTo(samples);
            int count=recorders[i].Count; bool valid=recorders[i].Valid, wrappedAround=recorders[i].WrappedAround;
            recorders[i].Stop(); recorders[i].Dispose(); recorders[i]=default;
            timings.Add(new { arm,age,marker=names[i],valid,count,wrappedAround,
                samples=samples.Select(s=>new { nanoseconds=s.Value,count=s.Count }).ToArray() });
        }
        timing = active = false;
    };
    void Log(string message,string stack,LogType kind) {
        if (message.Contains("on dx11") && message.Contains("uav",StringComparison.OrdinalIgnoreCase) && message.Contains("8")) return;
        if ((kind==LogType.Error || kind==LogType.Exception || kind==LogType.Assert) && errors.Count<12) errors.Add(message);
    }
    EditorApplication.LockReloadAssemblies();
    try {
        depthCopy=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Shaders/Core/Private/DownSample/HZBGenerate.compute");
        if(depthCopy==null) throw new InvalidOperationException("Missing existing depth-copy shader.");
        depthCopyKernel=depthCopy.FindKernel("KCopyDepth");
        depthReadback=new RenderTexture(new RenderTextureDescriptor(1920,1080) {
            graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
            depthBufferBits=0,enableRandomWrite=true,msaaSamples=1 }) { hideFlags=HideFlags.HideAndDontSave };
        if(!depthReadback.Create()) throw new InvalidOperationException("Depth readback texture creation failed.");
        volume.profile=profile; SceneManager.SetActiveScene(scene);
        foreach(var r in otherRoots) if(r.active) r.root.SetActive(false);
        foreach(var c in cameras) c.camera.enabled=c.camera==camera;
        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeAnchorPreFilter.Override(true);
        gi.screenProbeAnchorMaxAge.Override(128); gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous4);
        UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU,true);
        UnityEditorInternal.ProfilerDriver.profileEditor=false; UnityEngine.Profiling.Profiler.enabled=true;
        Application.runInBackground=true;
        hooks["EditorTraceCapture"].SetValue(null,trace); hooks["EditorLinearCapture"].SetValue(null,linear);
        RenderPipelineManager.beginCameraRendering+=Begin; Application.logMessageReceived+=Log;
        int index=0;
        foreach(int requestedAge in new[] {128,128,128,512,512,128,128,512,512,128}) {
            age=requestedAge; arm=(index++).ToString("00")+"-"+(age>128?"float512":"half128");
            hooks["EditorGatherHistoryDiagnostics"].SetValue(null,age>128?32:0);
            hooks["EditorProbeAnchorMaxAgeOverride"].SetValue(null,age);
            hooks["EditorRadiositySeed"].SetValue(null,0);
            hooks["EditorGatherFrameOverride"].SetValue(null,1733);
            var owner=type.GetField("m_Cameras",inst).GetValue(pass);
            owner.GetType().GetMethod("CleanupCamera").Invoke(owner,new object[]{camera});
            var historyType=type.Assembly.GetType("VividRP.Runtime.CameraHistorySystem");
            var history=historyType.GetField("s_Histories",stat).GetValue(null);
            history.GetType().GetMethod("CleanupCamera").Invoke(history,new object[]{camera});
            frame=targetCount=otherCount=phase=readyFrames=sourceFrames=0; resetPending=false;
            System.IO.Directory.CreateDirectory(output+"/"+arm); cameraExtra.ResetPostProcessingHistory(); active=true;
            double deadline=EditorApplication.timeSinceStartup+300;
            while(active && errors.Count==0 && EditorApplication.timeSinceStartup<deadline) {
                if (System.IO.File.Exists(output+"/cancel.txt")) throw new OperationCanceledException("Owner cancelled timing capture.");
                // PlayMode already advances the player loop. Extra Editor renders
                // can reuse Time.frameCount while advancing texture histories.
                await System.Threading.Tasks.Task.Delay(16);
            }
            AsyncGPUReadback.WaitAllRequests();
            if(active || pending!=0 || errors.Count!=0 || otherCount!=0) throw new InvalidOperationException("Timing arm failed: "+arm+" "+string.Join("; ",errors));
            records.Add(new { arm,age,targetCount,otherCount,sourceFrames,role=index<=2?"qualification":"measured",completed=true });
            System.IO.File.WriteAllText(output+"/progress.txt",arm+" complete");
        }
        complete=true; return new { output,complete,errors };
    }
    finally {
        try {
            active=false; RenderPipelineManager.beginCameraRendering-=Begin; Application.logMessageReceived-=Log;
            for(int i=0;i<recorders.Length;++i) if(recorders[i].Valid) recorders[i].Dispose();
            foreach(var p in hooks) p.Value.SetValue(null,previousHooks[p.Key]);
            AsyncGPUReadback.WaitAllRequests(); volume.profile=previousProfile;
            if(depthReadback!=null) { depthReadback.Release(); UnityEngine.Object.DestroyImmediate(depthReadback); }
            foreach(var c in cameras) if(c.camera!=null) c.camera.enabled=c.enabled;
            foreach(var r in otherRoots) if(r.root!=null) r.root.SetActive(r.active);
            if(previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            UnityEngine.Profiling.Profiler.enabled=previousProfiler;
            UnityEditorInternal.ProfilerDriver.profileEditor=previousEditorProfile;
            UnityEditorInternal.ProfilerDriver.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU,previousGpu);
            Application.runInBackground=previousBackground;
            foreach(var c in profile.components) UnityEngine.Object.DestroyImmediate(c); UnityEngine.Object.DestroyImmediate(profile);
            System.IO.File.WriteAllText(output+"/capture.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {
                schema="anchor-gpu-timing.v11",complete,errors,warmup,flush,measured,drain,timingStart,lastFrame,timing,records,timings,
                savedPayloadBudget,savedPayloadBytes,
                isolatedRoots=otherRoots.Select(r=>new {r.root.name,r.active,restored=r.root.activeSelf}).ToArray(),
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),gpu=SystemInfo.graphicsDeviceName,deviceVersion=SystemInfo.graphicsDeviceVersion,
                isPlaying=Application.isPlaying,previousProfiler,previousGpu,previousEditorProfile,
                restoredProfiler=UnityEngine.Profiling.Profiler.enabled,restoredGpu=UnityEditorInternal.ProfilerDriver.IsAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU),
                restoredEditorProfile=UnityEditorInternal.ProfilerDriver.profileEditor,
                previousBackground,restoredBackground=Application.runInBackground
            },Newtonsoft.Json.Formatting.Indented));
        } finally { EditorApplication.UnlockReloadAssemblies(); }
    }
}
