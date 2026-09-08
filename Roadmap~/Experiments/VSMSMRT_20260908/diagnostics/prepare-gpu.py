from pathlib import Path
import re
r=Path.cwd();s=(r/'Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.cs').read_text()
fixture=s[s.index('        private sealed class Fixture'):s.index('        [Test]\n        public void SMRT_')]
fixture=fixture.replace('Assert.That(texture.Create(), Is.True);','Check(texture.Create(), "Create pool");').replace('Assert.That(source, Is.Not.Null, path);','Check(source != null, path);')
fixture=fixture[:fixture.index('            internal float4 RunDiagnostic')]+fixture[fixture.index('            public void Dispose()'):]
assert 'Assert.' not in fixture
fixture=fixture.replace('Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute','Packages/com.vivid.render-pipelines/Editor/Tools/VSMSMRTSampling.compute')
source=(r/'Shaders/Core/Private/CSMShadowResolve.compute').read_text()
source=re.sub(r'#include "([^"\n]+)"',lambda m:'#include "'+('Packages/com.vivid.render-pipelines/Shaders/Core/Private/'+m[1] if not m[1].startswith('Packages/') else m[1])+'"',source)
(r/'Editor/Tools/VSMSMRTBody.hlsl').write_text(source)
sampling=(r/'Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute').read_text().replace('../../../../Shaders/Core/Private/CSMShadowResolve.compute','VSMSMRTBody.hlsl')
(r/'Editor/Tools/VSMSMRTSampling.compute').write_text(sampling)
header=r'''using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;
namespace VividRP.Editor {
internal static class VSMSMRTGpuProbe {
static int checks;
static void Check(bool value, string message) { checks++; if(!value) throw new InvalidOperationException(message); }
[MenuItem("Tools/VividRP/Diagnostics/Run Temporary VSM SMRT GPU Probe")]
static void Run() {
 if(Application.dataPath != "E:/VividRP_Reborn/Assets") throw new InvalidOperationException("Wrong project");
 checks=0; string error=null;
 try { Rays(); Sparse(); Entry(); Contact(); Gap(); Grazing(); Fallback(); Parameters(); }
 catch(Exception e) { error=e.ToString(); }
 string report="GPU="+SystemInfo.graphicsDeviceType+" checks="+checks+" error="+(error??"none");
 File.WriteAllText("E:/VividRP_Reborn/Packages/VividRP/Temp~/vsm-smrt/gpu.txt",report); Debug.Log(report);
}
static void SetDepth(Fixture f,int x,int y,float depth) {
 int slot=(int)f.TableData[y/4*2+x/4]-1;
 f.StaticData[(slot/4*4+y%4)*16+slot%4*4+x%4]=math.asuint(depth);
}
static void Rays() {
 using var f=new Fixture();
 for(int p=0;p<12;p++)f.Map(p,11-p);
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,8,10,.5f));
 for(int y=0;y<8;y++)SetDepth(f,4,y,.3f);
 f.Upload();
 var inputs=new float4[400];var rays=new float4[400];var expected=new bool[400];
 for(int i=0;i<400;i++) {
  float x=2.051f+i%20*.18f,slope=-.6f+i/20*.06f;
  inputs[i]=new float4(x/8,.45f,.2f,0);rays[i]=new float4(slope,0,3,0);
  float hit=x+slope*2;expected[i]=hit>=4&&hit<5;
 }
 var result=f.Run("TraceSMRTRays",inputs,normals:rays);
 for(int i=0;i<400;i++){Check(result[i].x==1,"complete ray "+i);Check(result[i].y==(expected[i]?0:1),"analytic rod "+i+" actual="+result[i]);}
 // Bounded tail retains a far blocker; running off the map stays unavailable.
 for(int p=0;p<12;p++)f.Map(p,11-p);
 SetDepth(f,4,3,.8f);f.Upload();
 inputs=new[]{new float4(3.75f/8,3.5f/8,.2f,0)};
 result=f.Run("TraceSMRTRays",inputs,normals:new[]{new float4(.5f,0,1,0)});
 Check(result[0].x==1&&result[0].y==0,"far tail");
 Check(f.Run("TraceSMRTRays",inputs,normals:new[]{new float4(-10,0,10,0)})[0].x==0,"out of map unknown");
}
static void Sparse() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p);
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,4,1,.5f));
 var inputs=new[]{new float4(.48f,.48f,.2f,0)};
 for(int frame=0;frame<32;frame++) {
  f.Shader.SetInt("_CSMFrameIndex",frame);f.MetadataData[3].x=10;f.Upload();
  var a=f.Run("FilterSMRTFootprints",inputs)[0];Check(a.x==1&&a.y==1,"valid empty");
  f.MetadataData[3].x|=4;f.Upload();Check(f.Run("FilterSMRTFootprints",inputs)[0].x==0,"dirty union");
  f.MetadataData[3].x=10;uint slot=f.TableData[3];f.TableData[3]=0;f.Upload();
  Check(f.Run("FilterSMRTFootprints",inputs)[0].x==0,"missing union");f.TableData[3]=slot;
 }
 // Zero-angle/reference equivalence across an actual partly occluded hierarchy.
 for(int p=0;p<12;p++)f.Map(p,11-p,p%2==0?.8f:0);f.Upload();
 f.Shader.SetVector("_VSMReceiverParameters",new Vector4(1,0,0,0));
 f.Shader.SetVector("_VSMSMRTParameters",Vector4.zero);
 var position=new[]{new float4(.1f,.1f,0,0)};float baseline=f.Run("ResolveReceivers",position)[0].x;
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,8,10,0));
 Check(f.Run("ResolveReceivers",position)[0].x==baseline,"zero angle exact PCF");
}
static void Entry() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p);
 for(int y=0;y<8;y++)for(int x=4;x<8;x++)SetDepth(f,x,y,.205f);f.Upload();
 f.Shader.SetVector("_VSMReceiverParameters",new Vector4(1,0,0,0));
 var receiver=new[]{new float4(.1f,.1f,-.3f,0)};
 float baseline=f.Run("ResolveReceivers",receiver)[0].x;
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,4,1,.5f));
 float soft=f.Run("ResolveReceivers",receiver)[0].x;
 float direct=f.Run("FilterSMRTFootprints",new[]{new float4(.51f,.51f,.2f,0)})[0].y;
 Check(baseline>.1f&&baseline<.9f&&soft==direct&&soft!=baseline,"entry must select SMRT output PCF="+baseline+" soft="+soft+" direct="+direct);
}
static void Contact() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p);
 var projection=f.ProjectionData[0];projection.Parameters.x=.01f;
 projection.WorldToShadow.m22=.05f;f.ProjectionData[0]=projection;
 float tangent=Mathf.Tan(.25f*Mathf.Deg2Rad);
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(8,8,2,tangent));
 var inputs=new float4[2048];
 string report="distance,x,visibility,analytic,error\n";
 foreach(float distance in new[]{.1f,.5f,1.5f,5f})foreach(float x in new[]{3.8f,4.2f}) {
  for(int y=0;y<8;y++)for(int xx=4;xx<8;xx++)SetDepth(f,xx,y,.2f+distance*.05f);f.Upload();
  for(int i=0;i<inputs.Length;i++)inputs[i]=new float4(x/8,.48f,.2f,0);
  var a=f.Run("FilterSMRTFootprints",inputs);float sum=0;
  for(int i=0;i<a.Length;i++){Check(a[i].x==1,"contact complete");sum+=a[i].y;}
  float q=Mathf.Clamp((4-x)*.01f/(Mathf.Min(distance,2)*tangent),-1,1);
  float analytic=1-(Mathf.Acos(q)-q*Mathf.Sqrt(1-q*q))/Mathf.PI;
  float actual=sum/a.Length;
  Check(Mathf.Abs(actual-analytic)<.035f,"contact analytic distance="+distance+" x="+x+" measured="+actual+" expected="+analytic);
  report+=distance+","+x+","+actual+","+analytic+","+Mathf.Abs(actual-analytic)+"\n";
 }
 File.WriteAllText("E:/VividRP_Reborn/Packages/VividRP/Temp~/vsm-smrt/contact.csv",report);
}
static void Gap() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p);
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,8,10,.5f));
 SetDepth(f,3,3,.3f);SetDepth(f,4,3,.45f);f.Upload();
 var input=new[]{new float4(3.5f/8,3.5f/8,.2f,0)};var ray=new[]{new float4(.5f,0,4,0)};
 Check(f.Run("TraceSMRTRays",input,normals:ray)[0].y==0,"one cell hidden layer gap fill");
 SetDepth(f,4,3,0);f.Upload();Check(f.Run("TraceSMRTRays",input,normals:ray)[0].y==1,"gap fill never bridges empty");
}
static void Grazing() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p);
 var p0=f.ProjectionData[0];p0.Parameters.x=.01f;p0.WorldToShadow.m22=.05f;f.ProjectionData[0]=p0;
 f.Shader.SetVector("_VSMReceiverParameters",new Vector4(1,0,0,0));
 float tangent=Mathf.Tan(.25f*Mathf.Deg2Rad);
 var input=new float4[128];var bias=new float4[128];
 foreach(float gx in new[]{-.002f,0,.002f})foreach(float gy in new[]{-.002f,.002f}) {
  for(int y=0;y<8;y++)for(int x=0;x<8;x++)SetDepth(f,x,y,.2f+(x-3.5f)*gx+(y-3.5f)*gy);
  for(int i=0;i<128;i++) {float x=3.6f+(i%16)*.05f,y=3.6f+(i/16)*.1f;
   input[i]=new float4(x/8,y/8,.2f+(x-4)*gx+(y-4)*gy,0);bias[i]=new float4(gx,gy,.0005f,0);}
  f.Upload();
  for(int rays=4;rays<=8;rays++) {
   f.Shader.SetVector("_VSMSMRTParameters",new Vector4(rays,8,2,tangent));
   var a=f.Run("FilterSMRTFootprints",input,normals:bias);
   for(int i=0;i<128;i++)Check(a[i].x==1&&a[i].y==1,"unoccluded sloped receiver count="+rays+" sample="+i+" actual="+a[i]);
  }
 }
}
static void Fallback() {
 using var f=new Fixture();for(int p=0;p<12;p++)f.Map(p,11-p,p>=4&&p<8?.8f:0);
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,4,1,.5f));
 var input=new[]{new float4(0,0,0,0)};
 f.MetadataData[3].x|=4;f.Upload();Check(f.Run("ResolveReceivers",input)[0].x==0,"dirty fine retries full shadowed parent");
 uint[] requests=new uint[12];for(int p=0;p<12;p++)requests[p]=f.MetadataData[p].x&3841u;
 for(int p=0;p<12;p++)f.Map(p,11-p,p>=4&&p<8?.8f:0);f.Upload();f.Run("ResolveReceivers",input);
 for(int p=0;p<12;p++)Check((f.MetadataData[p].x&3841u)==requests[p],"residency independent SMRT feedback");
 // All soft supports extend outside these tiny maps; complete PCF is still valid.
 for(int p=0;p<12;p++)f.Map(p,11-p,.8f);
 f.Shader.SetVector("_VSMReceiverParameters",new Vector4(1,0,0,0));
 f.Shader.SetVector("_VSMSMRTParameters",new Vector4(4,8,100,.5f));f.Upload();
 Check(f.Run("ResolveReceivers",input)[0].x==0,"terminal full PCF fallback retains occlusion");
}
static void Parameters() {
 var s=ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();using var cmd=new CommandBuffer();
 try {
 Check(VirtualShadowMapReceiverQuality.BuildSMRTParameters(s,.5f)==Vector4.zero,"default off");
 s.virtualShadowMapSMRT.value=true;
 Check(VirtualShadowMapReceiverQuality.BuildSMRTParameters(s,0)==Vector4.zero,"zero angle");
 var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
 for(int i=0;i<100;i++)Record(cmd,s,shader);long before=GC.GetAllocatedBytesForCurrentThread();
 for(int i=0;i<10000;i++)Record(cmd,s,shader);long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
 Check(bytes==0,"stable managed allocation="+bytes);
 }finally{Object.DestroyImmediate(s);}
}
static void Record(CommandBuffer cmd,CascadedShadowSettingsVolume s,ComputeShader shader){cmd.Clear();cmd.SetComputeVectorParam(shader,VirtualShadowMapReceiverQuality.SMRTParametersId,VirtualShadowMapReceiverQuality.BuildSMRTParameters(s,.5f));}
'''
(r/'Editor/Tools/VSMSMRTGpuProbe.cs').write_text(header+fixture+'}}\n')
