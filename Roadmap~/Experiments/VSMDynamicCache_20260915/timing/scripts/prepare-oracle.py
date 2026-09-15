from pathlib import Path
root=Path('Temp~/vsm-dynamic-timing')
s=(root/'live-abba-v2.cs').read_text(encoding='utf-8')
s=s[:s.index('for(int i=0;i<samplers.Length;i++) {')]
s=s.replace('live-abba-v2"','depth-oracle"')
s+='''
var create=typeof(UnityEditor.ShaderUtil).GetMethod("CreateComputeShaderAsset",flags,null,new[]{typeof(string)},null);
var compare=(UnityEngine.ComputeShader)create.Invoke(null,new object[]{System.IO.File.ReadAllText("E:/VividRP_Reborn/Packages/VividRP/Temp~/vsm-dynamic-timing/compare.compute.txt")});
int kernel=compare.FindKernel("ComparePools");
var captured=typeof(VividRP.Runtime.RenderPass.Core.CSMShadowResolvePass).GetEvent("EditorReceiverCapture",flags);
var inst=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var commandField=typeof(VividRP.Runtime.ComputePassContext).GetProperty("cmd").PropertyType.BaseType.GetField("m_WrappedCommandBuffer",inst);
var counts=new UnityEngine.GraphicsBuffer(UnityEngine.GraphicsBuffer.Target.Structured,4,4);var zero=new uint[4];var output=new uint[4];
var ownersBefore=new uint[1024];var ownersAfter=new uint[1024];
UnityEngine.RenderTexture referenceStatic=null,referenceDynamic=null;
System.Func<UnityEngine.RenderTexture> stat=()=>((UnityEngine.Rendering.RTHandle)runtime.GetProperty("StaticPhysicalPage",flags).GetValue(null)).rt;
System.Func<UnityEngine.RenderTexture> dyn=()=>((UnityEngine.Rendering.RTHandle)runtime.GetProperty("DynamicPhysicalPage",flags).GetValue(null)).rt;
int stage=0,phase=0,warm=0;bool disposed=false,pending=false,done=false;string error=null;double began=0;
var records=new System.Collections.Generic.List<object>();
UnityEditor.EditorApplication.CallbackFunction tick=null;UnityEditor.AssemblyReloadEvents.AssemblyReloadCallback reload=null;
System.Action<VividRP.Runtime.ComputePassContext,UnityEngine.Texture,UnityEngine.Texture,UnityEngine.Texture> capture=null;
System.Action<string> finish=null;
System.Action begin=()=>{
 settings.virtualShadowMapResolution.Override(stage<4?4096:8192);
 int pose=stage%4;cube.SetActive(pose!=3);cube.transform.position=cubePosition+camera.transform.right*(pose==1?.4f:pose==2?-.4f:0f);if(pose!=3)syncTransform();
 // Keep the cache through pose changes: candidate must remove old depth itself.
 if(pose==0){invalidate();extra.ResetPostProcessingHistory();}
 warm=0;phase=0;began=UnityEditor.EditorApplication.timeSinceStartup;System.IO.File.WriteAllText(root+"/progress.txt","stage="+stage);
};
finish=status=>{
 if(disposed)return;disposed=true;
 UnityEditor.EditorApplication.update-=tick;UnityEditor.AssemblyReloadEvents.beforeAssemblyReload-=reload;captured.GetRemoveMethod(true).Invoke(null,new object[]{capture});
 if(pending)UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
 light.intensity=intensity;light.transform.rotation=sunRotation;camera.transform.SetPositionAndRotation(position,rotation);
 UnityEngine.Object.DestroyImmediate(cube);UnityEngine.Object.DestroyImmediate(collection);UnityEngine.Object.DestroyImmediate(proxy);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);UnityEngine.Object.DestroyImmediate(profile);
 UnityEngine.Application.runInBackground=background;UnityEngine.Rendering.VolumeManager.instance.Update(camera.transform,extra.volumeLayerMask);invalidate();extra.ResetPostProcessingHistory();
 foreach(var texture in new[]{referenceStatic,referenceDynamic})if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
 counts.Dispose();UnityEngine.Object.DestroyImmediate(compare);
 System.IO.File.WriteAllText(root+"/results.json",Newtonsoft.Json.JsonConvert.SerializeObject(records));System.IO.File.WriteAllText(root+"/after.json",state());System.IO.File.WriteAllText(root+"/status.txt",status);
};
capture=(context,depth,normal,shadow)=>{
 if(disposed||pending||done||context.Get<VividRP.Runtime.VividCameraData>().camera!=camera)return;
 if(++warm<(phase==0?64:1))return;
 var cmd=(UnityEngine.Rendering.CommandBuffer)commandField.GetValue(context.cmd);
 var owners=(UnityEngine.GraphicsBuffer)runtime.GetProperty("PhysicalPageOwners",flags).GetValue(null);
 if(phase==0){
  if(referenceStatic==null){referenceStatic=new UnityEngine.RenderTexture(stat().descriptor);referenceDynamic=new UnityEngine.RenderTexture(dyn().descriptor);if(!referenceStatic.Create()||!referenceDynamic.Create())throw new System.Exception("Reference pool creation failed");}
  cmd.CopyTexture(stat(),referenceStatic);cmd.CopyTexture(dyn(),referenceDynamic);
  pending=true;cmd.RequestAsyncReadback(owners,r=>{if(r.hasError)error="Owner readback failed";else r.GetData<uint>().CopyTo(ownersBefore);pending=false;done=true;});
 }else{
  cmd.SetBufferData(counts,zero);cmd.SetComputeBufferParam(compare,kernel,"_Counts",counts);cmd.SetComputeBufferParam(compare,kernel,"_Owners",owners);
  cmd.SetComputeIntParam(compare,"_PagesPerRow",stat().width/128);
  cmd.SetComputeTextureParam(compare,kernel,"_Static",stat());cmd.SetComputeTextureParam(compare,kernel,"_Dynamic",dyn());cmd.SetComputeTextureParam(compare,kernel,"_ReferenceStatic",referenceStatic);cmd.SetComputeTextureParam(compare,kernel,"_ReferenceDynamic",referenceDynamic);
  cmd.DispatchCompute(compare,kernel,16,16,1024);
  pending=true;cmd.RequestAsyncReadback(counts,r=>{if(r.hasError)error="Depth compare readback failed";else r.GetData<uint>().CopyTo(output);pending=false;done=true;});
 }
};
tick=()=>{if(disposed)return;try{
 if(error!=null){finish("failed: "+error);return;}
 if(UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorApplication.isPlaying!=playing||camera.transform.position!=position||camera.transform.rotation!=rotation){finish("cancelled: editor/camera changed");return;}
 if(System.IO.File.Exists(root+"/cancel")){finish("cancelled");return;}
 if(UnityEditor.EditorApplication.timeSinceStartup-began>90){finish("failed: timeout");return;}
 if(done){
  done=false;
  if(phase==0){phase=1;warm=0;dynamicValid.SetValue(null,false);}
  else{
   ((UnityEngine.GraphicsBuffer)runtime.GetProperty("PhysicalPageOwners",flags).GetValue(null)).GetData(ownersAfter);int ownerDiff=0;for(int i=0;i<ownersBefore.Length;i++)if(ownersBefore[i]!=ownersAfter[i])ownerDiff++;
   records.Add(new{stage,resolution=stage<4?4096:8192,pose=stage%4,ownerDifferences=ownerDiff,staticDifferences=output[0],dynamicDifferences=output[1],valuesCheckedPerPool=output[2],dynamicNonzeroValues=output[3]});
   if(ownerDiff!=0||output[0]!=0||output[1]!=0||output[2]==0){finish("failed: depth/owner mismatch or zero work");return;}
   if(++stage==8){finish("complete");return;}begin();
  }
 }
 light.intensity=intensity+((warm&1)==0?.0001f:-.0001f);UnityEditor.EditorApplication.QueuePlayerLoopUpdate();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
 }catch(System.Exception e){finish("failed: "+e);}};
reload=()=>finish("cancelled: reload");begin();captured.GetAddMethod(true).Invoke(null,new object[]{capture});UnityEditor.AssemblyReloadEvents.beforeAssemblyReload+=reload;UnityEditor.EditorApplication.update+=tick;
return root;
'''
(root/'depth-oracle.cs').write_text(s,encoding='utf-8')
(root/'compare.compute.txt').write_bytes(Path('Roadmap~/Experiments/VSMRebuildCost_20260915/harness/compare.compute.txt').read_bytes())
