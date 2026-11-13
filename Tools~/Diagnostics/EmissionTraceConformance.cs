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
 AssetDatabase.Refresh();
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 var stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
 var type=typeof(SdfDiffuseGatherPass);
 var list=(System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses",stat).GetValue(null);
 var pass=list.Cast<object>().Single(p=>p.GetType()==type);
 var scene=type.GetField("m_Scene",flags).GetValue(pass);
 var sceneType=scene.GetType();
 GraphicsBuffer Buffer(string name)=>(GraphicsBuffer)sceneType.GetField(name,flags).GetValue(scene);
 var surfaces=Buffer("m_DiffuseSurfaceBuffer");
 var raw=new float[surfaces.count*16];surfaces.GetData(raw);
 int emitter=Enumerable.Range(0,surfaces.count).Single(i=>raw[i*16+4]>100);
 if(raw[emitter*16+7]!=2)throw new InvalidOperationException("Emitter is not Cull Back.");
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
 var sky=new Cubemap(1,TextureFormat.RGBAHalf,false);
 try{
  nodes.SetData(new[]{new EmissionTraceNode{Min=new Vector3(-1,2.8f,1),Max=new Vector3(1,3.1f,2.8f),EscapeIndex=1,LeafCount=1,LayerMask=uint.MaxValue}});
  leaves.SetData(new[]{(uint)emitter});work.SetData(new[]{0u});
  for(int face=0;face<6;face++)sky.SetPixels(new[]{Color.black},(CubemapFace)face);sky.Apply();
  shader.SetBuffer(kernel,"_MeshSdfVoxels",Buffer("m_Voxels"));
  shader.SetBuffer(kernel,"_MeshSdfFields",Buffer("m_FieldBuffer"));
  shader.SetBuffer(kernel,"_MeshSdfInstances",Buffer("m_InstanceBuffer"));
  shader.SetBuffer(kernel,"_MeshSdfNodes",nodes);shader.SetBuffer(kernel,"_MeshSdfLeafInstances",leaves);shader.SetInt("_MeshSdfNodeCount",1);
  shader.SetBuffer(kernel,"_SdfDiffuseSurfaces",surfaces);shader.SetBuffer(kernel,"_SdfRadiosityWork",work);shader.SetInt("_SdfRadiosityLayout",0);
  var cache=AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>(EmissiveCornellBoxSceneBuilder.CachePath);
  shader.SetTexture(kernel,"_SdfSurfaceAlbedoDepth",(Texture)typeof(VividSdfSurfaceCache).GetField("m_AlbedoDepth",flags).GetValue(cache));
  shader.SetTexture(kernel,"_SdfSurfaceNormalCoverage",(Texture)typeof(VividSdfSurfaceCache).GetField("m_NormalCoverage",flags).GetValue(cache));
  shader.SetBuffer(kernel,"_SdfLocalCardTable",(GraphicsBuffer)typeof(VividSdfSurfaceCache).GetProperty("LocalCardTable",flags).GetValue(cache));
  shader.SetTexture(kernel,"_SdfRadiosityInput",Texture2D.blackTexture);shader.SetTexture(kernel,"_SkyTexture",sky);shader.SetVector("_SkyTextureParams",Vector4.zero);
  shader.SetInt("_SdfDiffuseLayerMask",-1);shader.SetFloat("_SdfDiffuseMaxDistance",10);shader.SetFloat("_SdfFarTraceDistance",64);shader.SetFloat("_SdfDiffuseSkyTraceDistance",0);
  shader.SetVector("_SdfDiffuseLightPositionRange",Vector4.zero);shader.SetVector("_SdfDiffuseLightColorShadow",Vector4.zero);
  shader.SetBuffer(kernel,"_EmissionTraceValidation",result);shader.Dispatch(kernel,1,1,1);
  var actual=new Vector4[4];result.GetData(actual);
  for(int i=0;i<4;i++){
   float expected=i<2?200:0;
   if(Mathf.Abs(actual[i].x-expected)>.001f||Mathf.Abs(actual[i].y-expected)>.001f||Mathf.Abs(actual[i].z-expected)>.001f)throw new InvalidOperationException("Trace conformance mismatch at "+i+": "+actual[i]);
   if(actual[i].w<=0)throw new InvalidOperationException("Black far hit lost its distance/identity.");
  }
  var output=new{emitterIndex=emitter,order="radiosity front, far front, radiosity back, far back",actual=actual.Select(v=>new[]{v.x,v.y,v.z,v.w}).ToArray(),nodeStride=48};
  System.IO.File.WriteAllText("E:/VividRP_Reborn/Logs/EmissionSidedness-20261113/trace-conformance.json",Newtonsoft.Json.JsonConvert.SerializeObject(output,Newtonsoft.Json.Formatting.Indented));
  return output;
 }finally{nodes.Dispose();leaves.Dispose();work.Dispose();result.Dispose();UnityEngine.Object.DestroyImmediate(sky);}
}
