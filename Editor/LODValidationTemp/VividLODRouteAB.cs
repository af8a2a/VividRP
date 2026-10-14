using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Profiling;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    // Temporary diagnostic: deleted via AssetDatabase after collecting evidence.
    public static class VividLODRouteAB
    {
        static Job active;
        public static string Start(string config)
        {
            if(active!=null)throw new InvalidOperationException("Route already running");
            active=new Job(JObject.Parse(config));active.Start();return "started";
        }
        public static void Cancel(){if(active!=null)active.Fail("cancelled");}
        sealed class Job
        {
            const string Root="E:/VividRP_Reborn/Packages/VividRP/Temp~/lod-group16-dynamic/";
            const string Asset="Packages/com.vivid.render-pipelines/Editor/LODValidationTemp/LODSelectionAB.compute";
            static readonly BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            readonly JObject config; readonly Camera camera; readonly string path,kind;
            readonly float[] distances;readonly int loops,warmup;
            readonly Quaternion rotation=Quaternion.Euler(43.272297f,304.76556f,1.6177362f);
            readonly Vector3 roof=new(6.181255f,18.638472f,-10.503865f);
            readonly List<object> poses=new();readonly TimingSample[] timing;int timingCount;
            struct TimingSample { public int step,frame;public long gpu_ns,gpu_count,pre_ns,pre_count,graph_ns,graph_count; }
            readonly List<ProfilerRecorderSample> samples=new(32);
            readonly double started;
            ComputeShader shader; GraphicsBuffer cases,results; RenderTexture depthCopy;
            object buffers,main,shadow;int caseCount,step,lastBegin=-1,lastEnd=-1,lastResolve=-1,pending,maxPending,frames;
            string error;bool finishing,closed;ProfilerRecorder gpu,pre,graph;
            public Job(JObject c)
            {
                config=c;camera=Camera.main;kind=(string)c["kind"];path=Root+(string)c["label"];
                distances=JsonConvert.DeserializeObject<float[]>(File.ReadAllText(Root+(string)c["route"]));
                loops=(int?)c["loops"]??1;warmup=(int?)c["warmup"]??192;step=-warmup;started=EditorApplication.timeSinceStartup;
                timing=new TimingSample[kind=="timing"?distances.Length*Math.Max(0,loops-1):0];
            }
            GraphicsBuffer Buf(object obj,string name)=>(GraphicsBuffer)obj.GetType().GetProperty(name,F).GetValue(obj);
            uint[] Words(GraphicsBuffer b){var a=new uint[b.count*b.stride/4];b.GetData(a);return a;}
            void SaveWords(string name,uint[] a){var bytes=new byte[a.Length*4];Buffer.BlockCopy(a,0,bytes,0,bytes.Length);File.WriteAllBytes(path+"/"+name+".bin",bytes);}
            public void Start()
            {
                if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
                Directory.CreateDirectory(path);File.WriteAllText(path+"/config.json",config.ToString());
                var s=VividGPUDrivenSystem.instance;var scene=s.SceneData;
                buffers=s.GetType().GetProperty("BufferSet",F).GetValue(s);
                object dispatcher=s.GetType().GetField("m_CullingDispatcher",F).GetValue(s);
                main=dispatcher.GetType().GetProperty("BufferSet",F).GetValue(dispatcher);
                dispatcher=s.GetType().GetField("m_ShadowCullingDispatcher",F).GetValue(s);
                shadow=dispatcher.GetType().GetProperty("BufferSet",F).GetValue(dispatcher);
                if(Buf(buffers,"MeshLODGroupsBuffer").stride!=((string)config["mode"]=="A"?20:16))throw new Exception("Layout mismatch");
                if(kind!="timing")
                {
                    shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(Asset);
                    if(shader==null)throw new Exception("Probe unavailable");
                    if((string)config["mode"]=="A")shader.EnableKeyword("VIVID_LOD_AB_FP32");else shader.DisableKeyword("VIVID_LOD_AB_FP32");
                    var inputs=new List<Unity.Mathematics.uint2>();
                    for(int i=0;i<scene.Instances.Count;i++){var instance=scene.Instances[i];for(uint j=0;j<instance.TotalMeshLODCount;j++)inputs.Add(new Unity.Mathematics.uint2((uint)i,instance.TopMeshLODStartIndex+j));}
                    caseCount=inputs.Count;cases=new GraphicsBuffer(GraphicsBuffer.Target.Structured,caseCount,8);cases.SetData(inputs);
                    results=new GraphicsBuffer(GraphicsBuffer.Target.Structured,caseCount*64,16);
                    SaveWords("cases",Words(cases));SaveWords("nodes",Words(Buf(buffers,"MeshLODNodesBuffer")));SaveWords("groups",Words(Buf(buffers,"MeshLODGroupsBuffer")));SaveWords("instances",Words(Buf(buffers,"InstanceDataBuffer")));
                    var sources=new List<object>();
                    foreach(var source in scene.InstanceSources){var obj=EditorUtility.EntityIdToObject(source.PrimitiveEntityId);sources.Add(new {name=obj?obj.name:"?",id=source.PrimitiveEntityId.ToString()});}
                    File.WriteAllText(path+"/sources.json",JsonConvert.SerializeObject(sources));
                    if(kind=="capture")
                    {
                        depthCopy=new RenderTexture(camera.pixelWidth,camera.pixelHeight,0){graphicsFormat=GraphicsFormat.R32_SFloat,enableRandomWrite=true,hideFlags=HideFlags.HideAndDontSave};
                        if(!depthCopy.Create())throw new Exception("Depth staging allocation failed");
                    }
                    CSMShadowResolvePass.EditorReceiverCapture+=Resolve;
                }
                else
                {
                    var options=ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame|ProfilerRecorderOptions.GpuRecorder;
                    gpu=ProfilerRecorder.StartNew(ProfilerCategory.Render,"VividLODAB.CameraGPU",32,options);
                    pre=ProfilerRecorder.StartNew(ProfilerCategory.Render,"VividLODAB.PreGraphGPU",32,options);
                    graph=ProfilerRecorder.StartNew(ProfilerCategory.Render,"ExecuteRenderGraph",32,options);
                }
                Time.timeScale=0;
                RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;
                EditorApplication.update+=Update;AssemblyReloadEvents.beforeAssemblyReload+=Reload;
            }
            Vector3 Position(float distance)=>roof+(rotation*Vector3.forward)*(12f-distance);
            void Begin(ScriptableRenderContext ctx,Camera c)
            {
                if(c!=camera || finishing || lastBegin==Time.frameCount)return;
                lastBegin=Time.frameCount;
                int index=step<0?0:Math.Min(step,distances.Length*loops-1)%distances.Length;
                c.transform.SetPositionAndRotation(Position(distances[index]),rotation);
                if(kind=="capture" && step>=0)c.GetComponent<VividAdditionalCameraData>().RequestFinalFrameScreenshot(path+"/f"+step.ToString("D4")+".png");
            }
            long Read(ref ProfilerRecorder recorder,out long count)
            {
                samples.Clear();if(recorder.Valid)recorder.CopyTo(samples,true);count=0;
                if(samples.Count==0)return 0;
                var s=samples[samples.Count-1];count=s.Count;return s.Value;
            }
            void End(ScriptableRenderContext ctx,Camera c)
            {
                if(c!=camera || finishing || lastBegin!=Time.frameCount || lastEnd==Time.frameCount)return;
                lastEnd=Time.frameCount;
                if(step>=0)
                {
                    if(kind!="timing" && lastResolve!=Time.frameCount){Fail("Missing receiver capture at step "+step);return;}
                    var p=c.transform.position;
                    if(kind!="timing")poses.Add(new{step,index=step%distances.Length,distance=distances[step%distances.Length],frame=Time.frameCount,position=new[]{p.x,p.y,p.z},width=c.pixelWidth,height=c.pixelHeight});
                    frames++;
                }
                if(kind=="timing")
                {
                    long ns=Read(ref gpu,out long count),pn=Read(ref pre,out long pc),gn=Read(ref graph,out long gc);
                    // One moving cycle primes the temporal pipeline. Recorder observations
                    // are not asserted to identify the same GPU frame as the current pose.
                    if(step>=distances.Length)timing[timingCount++]=new TimingSample{step=step,frame=Time.frameCount,gpu_ns=ns,gpu_count=count,pre_ns=pn,pre_count=pc,graph_ns=gn,graph_count=gc};
                }
                step++;
                if(step>=distances.Length*loops)finishing=true;
            }
            void Save(AsyncGPUReadbackRequest readback,string name)
            {
                try
                {
                    if(readback.hasError)throw new Exception("Readback error "+name);
                    byte[] bytes=readback.GetData<byte>().ToArray();
                    using var file=File.Create(path+"/"+name+".gz");using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest);zip.Write(bytes,0,bytes.Length);
                }
                catch(Exception e){error=e.ToString();finishing=true;}
                finally{pending--;}
            }
            void Queue(CommandBuffer cmd,GraphicsBuffer buffer,string name,int size=-1)
            {
                pending++;maxPending=Math.Max(maxPending,pending);
                if(size<0)cmd.RequestAsyncReadback(buffer,r=>Save(r,name));else cmd.RequestAsyncReadback(buffer,size,0,r=>Save(r,name));
            }
            void Probe(CommandBuffer cmd,object cb,string domain,int frame)
            {
                int views=(int)cb.GetType().GetProperty("CullingContextCount",F).GetValue(cb);
                if(views<1 || views>64)throw new Exception("Invalid views "+views);
                cmd.SetComputeIntParam(shader,"_ViewCount",views);
                cmd.SetComputeBufferParam(shader,0,"_CullingContexts",Buf(cb,"CullingContextBuffer"));
                cmd.SetComputeBufferParam(shader,0,"_LODSelectionContexts",Buf(cb,"LodSelectionContextBuffer"));
                cmd.DispatchCompute(shader,0,(caseCount+63)/64,views,1);
                Queue(cmd,results,frame.ToString("D4")+"-"+domain+"-cut",caseCount*views*16);
            }
            void Resolve(ComputePassContext context,Texture depth,Texture normal,Texture shadowTexture)
            {
                if(finishing || step<0 || context.Get<VividCameraData>().camera!=camera || lastResolve==Time.frameCount)return;
                lastResolve=Time.frameCount;int frame=step;
                try
                {
                    var cmd=context.cmd.m_WrappedCommandBuffer;var s=VividGPUDrivenSystem.instance;
                    cmd.SetComputeBufferParam(shader,0,"_Cases",cases);cmd.SetComputeBufferParam(shader,0,"_Results",results);
                    cmd.SetComputeBufferParam(shader,0,"_InstanceData",Buf(buffers,"InstanceDataBuffer"));cmd.SetComputeBufferParam(shader,0,"_MeshLODNodes",Buf(buffers,"MeshLODNodesBuffer"));
                    cmd.SetComputeBufferParam(shader,0,"_MeshLODGroups",Buf(buffers,"MeshLODGroupsBuffer"));cmd.SetComputeBufferParam(shader,0,"_MeshLODGroupOverflow",Buf(buffers,"MeshLODGroupOverflowBuffer"));
                    cmd.SetComputeIntParam(shader,"_CaseCount",caseCount);cmd.SetComputeIntParam(shader,"_ForcedMeshLODNodeDepth",s.ForcedMeshLODNodeDepth);cmd.SetComputeFloatParam(shader,"_MeshLODErrorThreshold",s.MeshLODErrorThreshold);
                    Probe(cmd,main,"main",frame);
                    if(kind=="scan")return;
                    Probe(cmd,shadow,"shadow",frame);
                    string prefix=frame.ToString("D4")+"-";
                    Queue(cmd,Buf(main,"VisibleMeshletRenderRequestsBuffer"),prefix+"visible");Queue(cmd,Buf(main,"VisibleMeshletIndirectDrawArgsBuffer"),prefix+"args");
                    Queue(cmd,Buf(main,"RecoveredMeshletRenderRequestsBuffer"),prefix+"recovered");Queue(cmd,Buf(main,"RecoveredMeshletIndirectDrawArgsBuffer"),prefix+"recovered-args");
                    if(depth.width!=depthCopy.width || depth.height!=depthCopy.height)throw new Exception("Frame extent changed");
                    cmd.SetComputeTextureParam(shader,1,"_SourceDepth",depth);cmd.SetComputeTextureParam(shader,1,"_CopiedDepth",depthCopy);
                    cmd.DispatchCompute(shader,1,(depth.width+7)/8,(depth.height+7)/8,1);
                    pending++;maxPending=Math.Max(maxPending,pending);cmd.RequestAsyncReadback(depthCopy,0,r=>Save(r,prefix+"depth"));
                }
                catch(Exception e){Fail(e.ToString());}
            }
            void Update()
            {
                if(closed)return;
                if(EditorApplication.timeSinceStartup-started>600)Fail("Route timeout");
                if(finishing && pending==0){Finish();return;}
                EditorApplication.QueuePlayerLoopUpdate();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            public void Fail(string reason){error=reason;finishing=true;}
            void Reload(){Fail("Assembly reload");AsyncGPUReadback.WaitAllRequests();Finish();}
            void Finish()
            {
                if(closed)return;closed=true;
                RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;CSMShadowResolvePass.EditorReceiverCapture-=Resolve;
                EditorApplication.update-=Update;AssemblyReloadEvents.beforeAssemblyReload-=Reload;
                if(gpu.Valid)gpu.Dispose();if(pre.Valid)pre.Dispose();if(graph.Valid)graph.Dispose();cases?.Dispose();results?.Dispose();if(depthCopy)Object.DestroyImmediate(depthCopy);
                File.WriteAllText(path+"/poses.json",JsonConvert.SerializeObject(poses));File.WriteAllText(path+"/timings.json",JsonConvert.SerializeObject(timing));
                File.WriteAllText(path+"/result.json",JsonConvert.SerializeObject(new{success=error==null,error,frames,caseCount,maxPending,elapsed=EditorApplication.timeSinceStartup-started},Formatting.Indented));
                active=null;
            }
        }
    }
}
