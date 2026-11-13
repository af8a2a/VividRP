using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven.Meshlets;
using VividRP.Editor.Tools;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)]
struct EmissionTraceNode
{
 public Vector3 Min; public uint FirstLeaf; public Vector3 Max; public uint EscapeIndex;
 public uint LeafCount,LayerMask,Padding0,Padding1;
}
{
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 var stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
 var type=typeof(SdfDiffuseGatherPass);
 var list=(System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses",stat).GetValue(null);
 var pass=list.Cast<object>().Single(p=>p.GetType()==type);
 var scene=type.GetField("m_Scene",flags).GetValue(pass);
 var sceneType=scene.GetType();
 GraphicsBuffer Buffer(string name)=>(GraphicsBuffer)sceneType.GetField(name,flags).GetValue(scene);
 foreach(string hook in new[]{"EditorTraceCapture","EditorLinearCapture","EditorGatherStageCapture"})
  if(type.GetField(hook,stat).GetValue(null)!=null)throw new InvalidOperationException("Another GI capture is active: "+hook);
 var surfaces=Buffer("m_DiffuseSurfaceBuffer");
 var surfaceType=sceneType.GetNestedType("DiffuseSurface",System.Reflection.BindingFlags.NonPublic);
 if(surfaces.stride!=64||Marshal.SizeOf(surfaceType)!=64)throw new InvalidOperationException("Surface stride mismatch.");
 string[] surfaceFields={"AlbedoAndValid","Emission","CardMinAndFirst","CardExtent"};
 for(int i=0;i<surfaceFields.Length;i++)
  if(Marshal.OffsetOf(surfaceType,surfaceFields[i]).ToInt32()!=16*i)throw new InvalidOperationException("Surface offset mismatch: "+surfaceFields[i]);
 var surfaceData=Array.CreateInstance(surfaceType,surfaces.count);surfaces.GetData(surfaceData);
 var emissionField=surfaceType.GetField("Emission");
 int emitter=Enumerable.Range(0,surfaces.count).Single(i=>((Vector4)emissionField.GetValue(surfaceData.GetValue(i))).x>100);
 var emission=(Vector4)emissionField.GetValue(surfaceData.GetValue(emitter));
 if(emission.w!=2)throw new InvalidOperationException("Emitter is not Cull Back.");
 if(Marshal.SizeOf<EmissionTraceNode>()!=48)throw new InvalidOperationException("Node stride mismatch.");
 string[] nodeFields={"Min","FirstLeaf","Max","EscapeIndex","LeafCount","LayerMask","Padding0","Padding1"};
 int[] nodeOffsets={0,12,16,28,32,36,40,44};
 for(int i=0;i<nodeFields.Length;i++)if(Marshal.OffsetOf<EmissionTraceNode>(nodeFields[i]).ToInt32()!=nodeOffsets[i])throw new InvalidOperationException("Node field offset mismatch: "+nodeFields[i]);
 const string shaderPath="Packages/com.vivid.render-pipelines/Editor/Resources/SdfEmissionTraceValidation.compute";
 AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
 var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
 int kernel=shader.FindKernel("ValidateSdfEmissionTraces");
 int findKernel=shader.FindKernel("FindSdfEmissionReconnectCases");
 int reconnectKernel=shader.FindKernel("ValidateSdfEmissionReconnect");
 int[] kernels={kernel,findKernel,reconnectKernel};
 void CheckShader(){
  var errors=ShaderUtil.GetComputeShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message).ToArray();
  if(errors.Length!=0)throw new InvalidOperationException("Emission trace shader errors: "+string.Join("\n",errors));
 }
 CheckShader();
 var nodes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);
 var leaves=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
 var work=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
 var result=new GraphicsBuffer(GraphicsBuffer.Target.Structured,384,16);
 var diagnosticSurfaces=new GraphicsBuffer(GraphicsBuffer.Target.Structured,surfaces.count,surfaces.stride);
 var sky=new Cubemap(1,TextureFormat.RGBAHalf,false);
 var farAtlas=new Texture2D(256,1040,TextureFormat.RGBAFloat,false,true);
 try{
  nodes.SetData(new[]{new EmissionTraceNode{Min=new Vector3(-1,2.8f,1),Max=new Vector3(1,3.1f,2.8f),EscapeIndex=1,LeafCount=1,LayerMask=uint.MaxValue}});
  leaves.SetData(new[]{(uint)emitter});work.SetData(new[]{0u});
  for(int face=0;face<6;face++)sky.SetPixels(new[]{Color.black},(CubemapFace)face);sky.Apply();
  shader.SetInt("_MeshSdfNodeCount",1);shader.SetInt("_SdfRadiosityLayout",0);
  var cache=AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>(EmissiveCornellBoxSceneBuilder.CachePath);
  if(!(bool)typeof(VividSdfSurfaceCache).GetProperty("HasOriginalNormals",flags).GetValue(cache))
   throw new InvalidOperationException("Recapture the surface cache with original material normals first.");
  foreach(int entry in kernels){
   shader.SetBuffer(entry,"_MeshSdfVoxels",Buffer("m_Voxels"));
   shader.SetBuffer(entry,"_MeshSdfFields",Buffer("m_FieldBuffer"));
   shader.SetBuffer(entry,"_MeshSdfInstances",Buffer("m_InstanceBuffer"));
   shader.SetBuffer(entry,"_MeshSdfNodes",nodes);shader.SetBuffer(entry,"_MeshSdfLeafInstances",leaves);
   shader.SetBuffer(entry,"_SdfDiffuseSurfaces",diagnosticSurfaces);shader.SetBuffer(entry,"_SdfRadiosityWork",work);
   shader.SetTexture(entry,"_SdfSurfaceAlbedoDepth",(Texture)typeof(VividSdfSurfaceCache).GetField("m_AlbedoDepth",flags).GetValue(cache));
   shader.SetTexture(entry,"_SdfSurfaceNormalCoverage",(Texture)typeof(VividSdfSurfaceCache).GetField("m_NormalCoverage",flags).GetValue(cache));
   shader.SetBuffer(entry,"_SdfLocalCardTable",(GraphicsBuffer)typeof(VividSdfSurfaceCache).GetProperty("LocalCardTable",flags).GetValue(cache));
   shader.SetTexture(entry,"_SdfRadiosityInput",Texture2D.blackTexture);shader.SetTexture(entry,"_SkyTexture",sky);
   shader.SetTexture(entry,"_SdfFarRadiance",farAtlas);shader.SetBuffer(entry,"_EmissionTraceValidation",result);
  }
  shader.SetVector("_SkyTextureParams",Vector4.zero);
  shader.SetInt("_SdfDiffuseLayerMask",-1);shader.SetFloat("_SdfDiffuseMaxDistance",10);shader.SetFloat("_SdfFarTraceDistance",64);shader.SetFloat("_SdfDiffuseSkyTraceDistance",0);
  shader.SetFloat("_SdfDiffuseRayBias",.04f);
  shader.SetVector("_SdfDiffuseLightPositionRange",Vector4.zero);shader.SetVector("_SdfDiffuseLightColorShadow",Vector4.zero);
  // Find real front/back cache rays once with an owned two-sided material buffer.
  // Cache both as bright: consumption must recheck the side instead of trusting RGB.
  var emitterSurface=surfaceData.GetValue(emitter);var twoSided=emission;twoSided.w=0;
  emissionField.SetValue(emitterSurface,twoSided);surfaceData.SetValue(emitterSurface,emitter);diagnosticSurfaces.SetData(surfaceData);
  shader.Dispatch(findKernel,3,1,1);
  CheckShader();
  var candidates=new Vector4[384];result.GetData(candidates);
  for(int i=0;i<192;i++){
   float directionLengthSquared=new Vector3(candidates[2*i].x,candidates[2*i].y,candidates[2*i].z).sqrMagnitude;
   if(!float.IsFinite(candidates[2*i].w)||!float.IsFinite(directionLengthSquared)||Mathf.Abs(directionLengthSquared-1)>.001f)
    throw new InvalidOperationException("Invalid far discovery output at "+i);
  }
  int FindCase(int first,int end){
   int found=Enumerable.Range(first,end-first).Where(i=>candidates[2*i].w==emitter+1&&candidates[2*i+1].w>0).DefaultIfEmpty(-1).First();
   if(found<0)throw new InvalidOperationException("No far reconnect case in "+first+".."+end+"; hit candidates: "+string.Join(",",Enumerable.Range(0,192).Where(i=>candidates[2*i].w!=0).Select(i=>i+":"+candidates[2*i]+"/"+candidates[2*i+1])));
   return found;
  }
  int frontCase=FindCase(0,64),backCase=FindCase(64,192);
  int frontY=frontCase/64,backY=backCase/64;
  var pixels=new Color[256*1040];
  foreach(int index in new[]{frontCase,backCase}){
   int cellY=index/64,bin=index%64;
   pixels[(cellY+16*64)*256]=new Color(0,cellY,0,1);
   pixels[(cellY+16*bin)*256]=new Color(emission.x,emission.y,emission.z,candidates[2*index+1].w);
  }
  farAtlas.SetPixels(pixels);farAtlas.Apply(false,false);
  shader.SetVector("_SdfFarWindow",new Vector4(-8,-8,-8,0));
  shader.SetInts("_EmissionReconnectCases",frontY,frontCase%64,backY,backCase%64);
  var cases=new List<object>();
  for(int cull=0;cull<3;cull++){
   // Only this owned diagnostic buffer changes. The live scene and proxy stay untouched.
   var surface=surfaceData.GetValue(emitter);var diagnosticEmission=emission;diagnosticEmission.w=cull;
   emissionField.SetValue(surface,diagnosticEmission);surfaceData.SetValue(surface,emitter);diagnosticSurfaces.SetData(surfaceData);
   shader.Dispatch(kernel,1,1,1);
   CheckShader();
   var actual=new Vector4[4];result.GetData(actual);
   for(int i=0;i<4;i++){
    if(!float.IsFinite(actual[i].x)||!float.IsFinite(actual[i].y)||!float.IsFinite(actual[i].z)||!float.IsFinite(actual[i].w))
     throw new InvalidOperationException("Non-finite trace result for cull "+cull+" at "+i);
    bool front=i<2;
    bool visible=cull==0||(cull==1?!front:front);
    var expected=visible?emission:Vector4.zero;
    if(Mathf.Abs(actual[i].x-expected.x)>.001f||Mathf.Abs(actual[i].y-expected.y)>.001f||Mathf.Abs(actual[i].z-expected.z)>.001f)
     throw new InvalidOperationException("Trace conformance mismatch for cull "+cull+" at "+i+": "+actual[i]);
    if((i&1)==0?actual[i].w!=emitter+1:actual[i].w<=0)
     throw new InvalidOperationException("Black far hit lost its distance/identity.");
   }
   shader.Dispatch(reconnectKernel,1,1,1);
   CheckShader();
   var reconnected=new Vector4[4];result.GetData(reconnected,0,0,4);
   for(int side=0;side<2;side++){
    bool visible=cull==0||(cull==1?side==1:side==0);
    var expected=visible?emission:Vector4.zero;
    var rgb=reconnected[2*side];float distance=reconnected[2*side+1].w;
    if(!float.IsFinite(rgb.x)||!float.IsFinite(rgb.y)||!float.IsFinite(rgb.z)||!float.IsFinite(distance)
      ||Mathf.Abs(rgb.x-expected.x)>.001f||Mathf.Abs(rgb.y-expected.y)>.001f||Mathf.Abs(rgb.z-expected.z)>.001f
      ||rgb.w!=1||distance<=10)
     throw new InvalidOperationException("Far reconnect lost covered black or reused wrong-side light: cull "+cull+", side "+side+", "+rgb);
   }
   cases.Add(new{cullMode=((CullMode)cull).ToString(),actual=actual.Select(v=>new[]{v.x,v.y,v.z,v.w}).ToArray(),
    reconnect=reconnected.Select(v=>new[]{v.x,v.y,v.z,v.w}).ToArray()});
  }
  var unchanged=Array.CreateInstance(surfaceType,surfaces.count);surfaces.GetData(unchanged);
  if(!emission.Equals((Vector4)emissionField.GetValue(unchanged.GetValue(emitter))))throw new InvalidOperationException("Live emitter changed during validation.");
  string evidenceRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","..","VividRP_GI_Evidence",
   "EmissionTraceConformance-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
  System.IO.Directory.CreateDirectory(evidenceRoot);
  var shaderMessages=ShaderUtil.GetComputeShaderMessages(shader).Select(m=>new{m.message,severity=m.severity.ToString()}).ToArray();
  if(shaderMessages.Any(m=>m.severity=="Error"))throw new InvalidOperationException("Emission trace shader has compilation errors.");
  var output=new{evidenceRoot,emitterIndex=emitter,order="radiosity front, far front, radiosity back, far back",
   reconnectOrder="front RGB/valid, front direction/distance, back RGB/valid, back direction/distance",
   reconnectCachedRadiance=new[]{emission.x,emission.y,emission.z},reconnectCellsAndBins=new[]{frontY,frontCase%64,backY,backCase%64},
   cases,nodeStride=48,surfaceStride=64,liveEmitterUnchanged=true,shaderMessages};
  System.IO.File.WriteAllText(evidenceRoot+"/trace-conformance.json",Newtonsoft.Json.JsonConvert.SerializeObject(output,Newtonsoft.Json.Formatting.Indented));
  return output;
 }finally{nodes.Dispose();leaves.Dispose();work.Dispose();result.Dispose();diagnosticSurfaces.Dispose();UnityEngine.Object.DestroyImmediate(sky);UnityEngine.Object.DestroyImmediate(farAtlas);}
}
