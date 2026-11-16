// Focused live GPU conformance check; not a Unity Test Framework test.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using VividRP.Runtime.RenderPass.Core;
{
    const string asset="Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute";
    var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset);
    string source=System.IO.File.ReadAllText(package.resolvedPath+"/Tools~/Diagnostics/SdfBlackEnvironmentCandidate.hlsl.txt");
    if(!source.StartsWith("bool SdfKnownBlackEnvironmentMiss(",StringComparison.Ordinal))throw new InvalidOperationException("Experimental black-boundary helper not found.");
    string body=source;
    string program="#pragma kernel ValidateBlackBoundaryCS\n#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/MeshSdfTrace.hlsl\"\n"
        +"uint _SdfGatherHistoryDiagnostics;uint _CaseCount;uint _OutputBase;StructuredBuffer<float4> _Cases;RWStructuredBuffer<uint> _Results;\n"
        +body+"\n[numthreads(64,1,1)]void ValidateBlackBoundaryCS(uint3 id:SV_DispatchThreadID){if(id.x>=_CaseCount)return;float4 o=_Cases[id.x*3],d=_Cases[id.x*3+1],s=_Cases[id.x*3+2];_Results[_OutputBase+id.x]=SdfKnownBlackEnvironmentMiss(o.xyz,d.xyz,o.w,s.x!=0.0,uint(s.y))?1u:0u;}\n";
    var cases=new[]{
        ("inside-escape",Vector3.zero,Vector3.forward,2f,false,0u,true),
        ("finite-range-inside",Vector3.zero,Vector3.forward,.5f,false,0u,false),
        ("boundary-equality",Vector3.zero,Vector3.forward,1f,false,0u,false),
        ("boundary-margin",Vector3.zero,Vector3.forward,1.0005f,false,0u,false),
        ("beyond-margin",Vector3.zero,Vector3.forward,1.002f,false,0u,true),
        ("outside-away",new Vector3(0,0,2),Vector3.forward,2f,false,0u,true),
        ("outside-through-short",new Vector3(0,0,2),Vector3.back,2f,false,0u,false),
        ("outside-through-complete",new Vector3(0,0,2),Vector3.back,4f,false,0u,true),
        ("parallel-outside",new Vector3(2,0,0),Vector3.forward,2f,false,0u,true),
        ("parallel-inside",Vector3.zero,Vector3.up,2f,false,0u,true),
        ("incomplete",Vector3.zero,Vector3.forward,2f,true,0u,false),
        ("screen-rejected",Vector3.zero,Vector3.forward,2f,false,8u,false),
        ("screen-blocked",Vector3.zero,Vector3.forward,2f,false,1u,false),
        ("no-travel",Vector3.zero,Vector3.forward,0f,false,0u,false)};
    var inputs=new Vector4[cases.Length*3];
    for(int i=0;i<cases.Length;++i){var c=cases[i];inputs[i*3]=new Vector4(c.Item2.x,c.Item2.y,c.Item2.z,c.Item4);inputs[i*3+1]=new Vector4(c.Item3.x,c.Item3.y,c.Item3.z,0);inputs[i*3+2]=new Vector4(c.Item5?1:0,c.Item6,0,0);}
    var nodeType=typeof(SdfDiffuseGatherPass).Assembly.GetType("VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene")
        .GetNestedType("Node",System.Reflection.BindingFlags.NonPublic);
    if(System.Runtime.InteropServices.Marshal.SizeOf(nodeType)!=48
        || System.Runtime.InteropServices.Marshal.OffsetOf(nodeType,"Min").ToInt32()!=0
        || System.Runtime.InteropServices.Marshal.OffsetOf(nodeType,"Max").ToInt32()!=16)
        throw new InvalidOperationException("SDF GPU node layout mismatch.");
    var node=Activator.CreateInstance(nodeType);nodeType.GetField("Min").SetValue(node,-Vector3.one);nodeType.GetField("Max").SetValue(node,Vector3.one);
    var nodes=Array.CreateInstance(nodeType,1);nodes.SetValue(node,0);
    ComputeShader shader=null;GraphicsBuffer input=null,root=null,output=null;CommandBuffer cmd=null;
    try {
        var create=typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Single(m=>m.Name=="CreateComputeShaderAsset" && m.GetParameters().Length==1);
        shader=(ComputeShader)create.Invoke(null,new object[]{program});int kernel=shader.FindKernel("ValidateBlackBoundaryCS");
        var compile=typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Single(m=>m.Name=="CompileComputeShaderVariant");
        var result=compile.Invoke(null,new object[]{shader,kernel,Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64});
        if(!(bool)result.GetType().GetProperty("Success").GetValue(result))throw new InvalidOperationException("Boundary conformance shader compilation failed.");
        input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,inputs.Length,16);input.SetData(inputs);
        root=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);root.SetData(nodes);
        output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cases.Length*3,4);
        cmd=new CommandBuffer();cmd.SetComputeBufferParam(shader,kernel,"_Cases",input);cmd.SetComputeBufferParam(shader,kernel,"_Results",output);cmd.SetComputeBufferParam(shader,kernel,"_MeshSdfNodes",root);
        cmd.SetComputeIntParam(shader,"_CaseCount",cases.Length);
        for(int mode=0;mode<3;++mode){cmd.SetComputeIntParam(shader,"_OutputBase",mode*cases.Length);cmd.SetComputeIntParam(shader,"_MeshSdfNodeCount",mode==1?0:1);cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",mode==0?0:33554432);cmd.DispatchCompute(shader,kernel,1,1,1);}
        Graphics.ExecuteCommandBuffer(cmd);var request=AsyncGPUReadback.Request(output);request.WaitForCompletion();if(request.hasError)throw new InvalidOperationException("Conformance readback failed.");
        var actual=request.GetData<uint>();var records=new List<object>();
        for(int mode=0;mode<3;++mode)for(int i=0;i<cases.Length;++i){bool expected=mode==2 && cases[i].Item7;bool value=actual[mode*cases.Length+i]!=0;if(value!=expected)throw new InvalidOperationException("Black-boundary GPU case failed: "+cases[i].Item1+" mode="+mode);records.Add(new{mode,label=cases[i].Item1,expected,actual=value});}
        using var sha=System.Security.Cryptography.SHA256.Create();
        return new{passed=true,cases=records.Count,nodeStride=48,nodeMinOffset=0,nodeMaxOffset=16,candidateHelperSha256=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source))).Replace("-","").ToLowerInvariant(),records};
    }finally{cmd?.Dispose();input?.Dispose();root?.Dispose();output?.Dispose();if(shader!=null)UnityEngine.Object.DestroyImmediate(shader);}
}
