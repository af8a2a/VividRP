// Replay saved original temporal inputs outside the camera loop. No assets saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Newtonsoft.Json.Linq;
{
    const string inputRoot="E:/VividRP_GI_Evidence/ScreenProbeDirectionalBinding-20261115-061302-342892";
    const System.Reflection.BindingFlags stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    if(!EditorApplication.isPlaying || !EditorApplication.isPaused || UnityEngine.FrameDebugger.enabled)
        throw new InvalidOperationException("Expected paused PlayMode, Frame Debugger off.");
    var type=typeof(SdfDiffuseGatherPass);
    if(new[]{"EditorTraceCapture","EditorLinearCapture","EditorGatherStageCapture","EditorBindResolveProbe","EditorAtrousDispatchCapture"}
        .Any(n=>type.GetField(n,stat).GetValue(null)!=null)) throw new InvalidOperationException("A camera capture is active.");
    var capture=JObject.Parse(System.IO.File.ReadAllText(inputRoot+"/capture.json"));
    if(!(bool)capture["complete"] || ((JArray)capture["errors"]).Count!=0 || capture["firstBundle"].Type==JTokenType.Null)
        throw new InvalidOperationException("Missing complete first-divergence bundle.");
    var pass=((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses",stat).GetValue(null))
        .Cast<object>().Single(p=>p.GetType()==type);
    var original=(ComputeShader)type.GetField("m_FilterCompute",inst).GetValue(pass);
    var shader=UnityEngine.Object.Instantiate(original);
    var cmd=new CommandBuffer {name="Frozen directional temporal replay"};
    var resources=new List<UnityEngine.Object>();
    var observations=((JArray)capture["records"]).OfType<JObject>().Where(r=>r["tag"]!=null)
        .ToDictionary(r=>((string)r["arm"],(int)r["frame"],(string)r["tag"]));
    var states=((JArray)capture["records"]).OfType<JObject>().Where(r=>r["bindings"]!=null)
        .ToDictionary(r=>((string)r["arm"],(int)r["frame"]));
    var saved=((JArray)capture["savedPayloads"]).OfType<JObject>()
        .ToDictionary(r=>((string)r["arm"],(int)r["frame"],(string)r["tag"]));
    string output=inputRoot+"/replay";
    System.IO.Directory.CreateDirectory(output);
    var records=new List<object>();
    long savedBytes=0;
    bool complete=false;
    string Hash(byte[] bytes) {using var sha=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    byte[] Read(Texture texture) {
        var request=AsyncGPUReadback.Request(texture,0); request.WaitForCompletion();
        if(request.hasError) throw new InvalidOperationException("Frozen replay readback failed.");
        return request.GetData<byte>().ToArray();
    }
    RenderTexture Allocate(int width,int height,GraphicsFormat format) {
        var texture=new RenderTexture(new RenderTextureDescriptor(width,height) {
            graphicsFormat=format,depthBufferBits=0,enableRandomWrite=true,msaaSamples=1
        }) {hideFlags=HideFlags.HideAndDontSave};
        if(!texture.Create()) throw new InvalidOperationException("Replay texture allocation failed.");
        resources.Add(texture);return texture;
    }
    RenderTexture Upload(string arm,int frame,string tag) {
        var row=observations[(arm,frame,tag)];var payload=System.IO.File.ReadAllBytes((string)saved[(arm,frame,tag)]["path"]);
        if(Hash(payload)!=(string)row["sha256"]) throw new InvalidOperationException("Saved input hash mismatch: "+tag);
        bool half=(string)row["format"]=="R16G16B16A16_SFloat";
        if(!half && (string)row["format"]!="R32G32B32A32_SFloat") throw new InvalidOperationException("Unexpected input format.");
        int width=(int)row["width"],height=(int)row["height"];
        var staging=new Texture2D(width,height,half?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat,false,true) {hideFlags=HideFlags.HideAndDontSave};
        resources.Add(staging);staging.LoadRawTextureData(payload);staging.Apply(false,true);
        var texture=Allocate(width,height,half?GraphicsFormat.R16G16B16A16_SFloat:GraphicsFormat.R32G32B32A32_SFloat);
        cmd.CopyTexture(staging,texture);Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        string uploaded=Hash(Read(texture));
        if(uploaded!=(string)row["sha256"]) throw new InvalidOperationException("GPU upload changed input bytes: "+tag);
        records.Add(new {arm,frame,tag,uploadedInputHash=uploaded});return texture;
    }
    Vector4 Vector(JToken value) => new Vector4((float)value[0],(float)value[1],(float)value[2],(float)value[3]);
    try {
        int kernel=shader.FindKernel("ScreenProbeTemporallyAccumulateTraceRadianceCS");
        shader.GetKernelThreadGroupSizes(kernel,out uint tx,out uint ty,out uint tz);
        if(tx!=16 || ty!=1 || tz!=1) throw new InvalidOperationException("Unexpected temporal group size.");
        string reference=(string)capture["firstBundle"]["reference"],repeated=(string)capture["firstBundle"]["repeated"];
        int first=(int)capture["firstBundle"]["firstFrame"];
        foreach(int frame in new[]{Math.Max(0,first-1),first}.Distinct()) foreach(string arm in new[]{reference,repeated}) {
            var state=states[(arm,frame)];var constants=state["constants"];
            var geometry=Upload(arm,frame,"geometry");var previousGeometry=Upload(arm,frame,"previous-geometry");
            var composite=Upload(arm,frame,"composite");var previous=Upload(arm,frame,"previous-directional");
            var target=Allocate(composite.width,composite.height,GraphicsFormat.R16G16B16A16_SFloat);
            var matrix=new Matrix4x4(); for(int i=0;i<16;++i)matrix[i/4,i%4]=(float)constants["previousVP"][i];
            foreach(bool rejectHistory in frame==first?new[]{false,true}:new[]{false}) for(int repetition=0;repetition<(rejectHistory?1:8);++repetition) {
                cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",(int)constants["flags"]);
                cmd.SetComputeIntParam(shader,"_SdfDirectionalHasHistory",!rejectHistory && (bool)state["predicates"]["directionalHistory"]?1:0);
                cmd.SetComputeIntParam(shader,"_SdfRestirDense",(bool)constants["restirDense"]?1:0);
                cmd.SetComputeIntParam(shader,"_SdfDiffuseAdaptiveProbes",(bool)constants["adaptive"]?1:0);
                cmd.SetComputeMatrixParam(shader,"_SdfDiffusePreviousVP",matrix);
                cmd.SetComputeVectorParam(shader,"_SdfDiffuseScreenSize",Vector(constants["screenSize"]));
                cmd.SetComputeVectorParam(shader,"_SdfProbeCameraForward",Vector(constants["cameraForward"]));
                cmd.SetComputeVectorParam(shader,"_SdfHistoryLightScale",Vector(constants["historyLightScale"]));
                cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseGeometry",geometry);
                cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffusePreviousGeometry",previousGeometry);
                cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalComposite",composite);
                cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalPrevious",previous);
                cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalTemporalOutput",target);
                cmd.DispatchCompute(shader,kernel,geometry.width,geometry.height,1);
                Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
                byte[] bytes=Read(target);string hash=Hash(bytes);string path=null;
                if(repetition==0) {
                    if(savedBytes+bytes.LongLength>64L*1024*1024) throw new InvalidOperationException("Replay payload budget exceeded.");
                    path=output+"/"+arm+"-"+frame.ToString("0000")+(rejectHistory?"-without-history":"-normal")+".bin";
                    System.IO.File.WriteAllBytes(path,bytes);savedBytes+=bytes.LongLength;
                }
                records.Add(new {arm,frame,repetition,rejectHistory,sha256=hash,path,width=target.width,height=target.height,
                    matchesProduction=hash==(string)observations[(arm,frame,"directional-temporal")]["sha256"]});
            }
        }
        complete=true;return new {output,complete,savedBytes};
    } finally {
        cmd.Dispose();
        foreach(var resource in resources) {if(resource is RenderTexture rt)rt.Release();UnityEngine.Object.DestroyImmediate(resource);}
        UnityEngine.Object.DestroyImmediate(shader);
        System.IO.File.WriteAllText(output+"/replay.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {
            schema="screen-probe-frozen-directional-temporal.v1",complete,records,savedBytes,
            shaderPath=AssetDatabase.GetAssetPath(original),isPlaying=EditorApplication.isPlaying,isPaused=EditorApplication.isPaused
        },Newtonsoft.Json.Formatting.Indented));
    }
}
