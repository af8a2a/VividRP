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
 var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Editor/Resources/SdfEmissionTraceValidation.compute");
 int kernel=shader.FindKernel("ValidateSdfEmissionTraces");
 var nodes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);
 var leaves=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
 var work=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
 var result=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,16);
 var diagnosticSurfaces=new GraphicsBuffer(GraphicsBuffer.Target.Structured,surfaces.count,surfaces.stride);
 var sky=new Cubemap(1,TextureFormat.RGBAHalf,false);
 try{
  nodes.SetData(new[]{new EmissionTraceNode{Min=new Vector3(-1,2.8f,1),Max=new Vector3(1,3.1f,2.8f),EscapeIndex=1,LeafCount=1,LayerMask=uint.MaxValue}});
  leaves.SetData(new[]{(uint)emitter});work.SetData(new[]{0u});
  for(int face=0;face<6;face++)sky.SetPixels(new[]{Color.black},(CubemapFace)face);sky.Apply();
  shader.SetBuffer(kernel,"_MeshSdfVoxels",Buffer("m_Voxels"));
  shader.SetBuffer(kernel,"_MeshSdfFields",Buffer("m_FieldBuffer"));
  shader.SetBuffer(kernel,"_MeshSdfInstances",Buffer("m_InstanceBuffer"));
  shader.SetBuffer(kernel,"_MeshSdfNodes",nodes);shader.SetBuffer(kernel,"_MeshSdfLeafInstances",leaves);shader.SetInt("_MeshSdfNodeCount",1);
  shader.SetBuffer(kernel,"_SdfDiffuseSurfaces",diagnosticSurfaces);shader.SetBuffer(kernel,"_SdfRadiosityWork",work);shader.SetInt("_SdfRadiosityLayout",0);
  var cache=AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>(EmissiveCornellBoxSceneBuilder.CachePath);
  if(!(bool)typeof(VividSdfSurfaceCache).GetProperty("HasOriginalNormals",flags).GetValue(cache))
   throw new InvalidOperationException("Recapture the surface cache with original material normals first.");
  shader.SetTexture(kernel,"_SdfSurfaceAlbedoDepth",(Texture)typeof(VividSdfSurfaceCache).GetField("m_AlbedoDepth",flags).GetValue(cache));
  shader.SetTexture(kernel,"_SdfSurfaceNormalCoverage",(Texture)typeof(VividSdfSurfaceCache).GetField("m_NormalCoverage",flags).GetValue(cache));
  shader.SetBuffer(kernel,"_SdfLocalCardTable",(GraphicsBuffer)typeof(VividSdfSurfaceCache).GetProperty("LocalCardTable",flags).GetValue(cache));
  shader.SetTexture(kernel,"_SdfRadiosityInput",Texture2D.blackTexture);shader.SetTexture(kernel,"_SkyTexture",sky);shader.SetVector("_SkyTextureParams",Vector4.zero);
  shader.SetInt("_SdfDiffuseLayerMask",-1);shader.SetFloat("_SdfDiffuseMaxDistance",10);shader.SetFloat("_SdfFarTraceDistance",64);shader.SetFloat("_SdfDiffuseSkyTraceDistance",0);
  shader.SetFloat("_SdfDiffuseRayBias",.04f);
  shader.SetVector("_SdfDiffuseLightPositionRange",Vector4.zero);shader.SetVector("_SdfDiffuseLightColorShadow",Vector4.zero);
  shader.SetBuffer(kernel,"_EmissionTraceValidation",result);
  var cases=new List<object>();
  for(int cull=0;cull<3;cull++){
   // Only this owned diagnostic buffer changes. The live scene and proxy stay untouched.
   var surface=surfaceData.GetValue(emitter);var diagnosticEmission=emission;diagnosticEmission.w=cull;
   emissionField.SetValue(surface,diagnosticEmission);surfaceData.SetValue(surface,emitter);diagnosticSurfaces.SetData(surfaceData);
   shader.Dispatch(kernel,1,1,1);
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
   cases.Add(new{cullMode=((CullMode)cull).ToString(),actual=actual.Select(v=>new[]{v.x,v.y,v.z,v.w}).ToArray()});
  }
  var unchanged=Array.CreateInstance(surfaceType,surfaces.count);surfaces.GetData(unchanged);
  if(!emission.Equals((Vector4)emissionField.GetValue(unchanged.GetValue(emitter))))throw new InvalidOperationException("Live emitter changed during validation.");
  string evidenceRoot=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","..","VividRP_GI_Evidence",
   "EmissionTraceConformance-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
  System.IO.Directory.CreateDirectory(evidenceRoot);
  var shaderMessages=ShaderUtil.GetComputeShaderMessages(shader).Select(m=>new{m.message,severity=m.severity.ToString()}).ToArray();
  if(shaderMessages.Any(m=>m.severity=="Error"))throw new InvalidOperationException("Emission trace shader has compilation errors.");
  var output=new{evidenceRoot,emitterIndex=emitter,order="radiosity front, far front, radiosity back, far back",cases,nodeStride=48,surfaceStride=64,liveEmitterUnchanged=true,shaderMessages};
  System.IO.File.WriteAllText(evidenceRoot+"/trace-conformance.json",Newtonsoft.Json.JsonConvert.SerializeObject(output,Newtonsoft.Json.Formatting.Indented));
  return output;
 }finally{nodes.Dispose();leaves.Dispose();work.Dispose();result.Dispose();diagnosticSurfaces.Dispose();UnityEngine.Object.DestroyImmediate(sky);}
}
