// Focused live allocation check, not a Unity Test Framework test or GPU timing.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Editor.Tools;
{
    const System.Reflection.BindingFlags stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags inst=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    var type=typeof(SdfDiffuseGatherPass);var hook=type.GetField("EditorTraceCapture",stat);
    if(hook.GetValue(null)!=null || !EditorApplication.isPlaying)throw new InvalidOperationException("Idle PlayMode required.");
    var scene=SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>()).Single();
    var pass=((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses",stat).GetValue(null)).Cast<object>().Single(p=>p.GetType()==type);
    var bind=(Action<ComputeCommandBuffer,ComputeShader>)type.GetMethod("BindProbeFrame",inst)
        .CreateDelegate(typeof(Action<ComputeCommandBuffer,ComputeShader>),pass);
    var shader=(ComputeShader)type.GetField("m_FilterCompute",inst).GetValue(pass);
    bool previousPaused=EditorApplication.isPaused,done=false;long bytes=-1;int thread=-1;
    Action<ComputePassContext,Texture,Texture,Texture> callback=(context,sources,lighting,geometry)=> {
        if(done || context.Get<VividCameraData>().camera!=camera)return;
        if((int)type.GetField("m_ProbeAnchorMaxAge",inst).GetValue(pass)!=512)
            throw new InvalidOperationException("Normal Cornell age512 must be active.");
        var cmd=context.cmd;
        var gather=(ComputeShader)type.GetField("m_GatherCompute",inst).GetValue(pass);
        int filterKernel=(int)type.GetField("m_AnchorPreFilterKernel",inst).GetValue(pass);
        int resolveKernel=(int)type.GetField("m_DirectionalResolveKernel",inst).GetValue(pass);
        var prepared=(TextureHandle)(RenderGraphTexture)type.GetField("m_PreparedAnchor",inst).GetValue(pass);
        var statistics=(TextureHandle)(RenderGraphTexture)type.GetField("m_CurrentAnchorStatistics",inst).GetValue(pass);
        int preparedId=Shader.PropertyToID("_SdfPreparedProbeAnchor"),enabledId=Shader.PropertyToID("_SdfPreparedAnchorEnabled");
        int statisticsId=Shader.PropertyToID("_SdfAnchorStatistics");
        if(!prepared.IsValid() || !statistics.IsValid())throw new InvalidOperationException("Missing guidance resources.");
        void BindAll() {
            bind(cmd,shader);
            cmd.SetComputeTextureParam(shader,filterKernel,preparedId,prepared);
            cmd.SetComputeIntParam(gather,enabledId,1);
            cmd.SetComputeTextureParam(gather,resolveKernel,preparedId,prepared);
            cmd.SetComputeTextureParam(gather,resolveKernel,statisticsId,statistics);
        }
        for(int i=0;i<128;++i)BindAll();
        thread=Environment.CurrentManagedThreadId;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<128;++i)BindAll();
        bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        done=true;
    };
    hook.SetValue(null,callback);
    try {
        EditorApplication.isPaused=false;
        double deadline=EditorApplication.timeSinceStartup+60;
        while(!done && EditorApplication.timeSinceStartup<deadline)await System.Threading.Tasks.Task.Delay(16);
        if(!done || bytes!=0)throw new InvalidOperationException("Stable BindProbeFrame allocated managed bytes: "+bytes);
        return new {method="BindProbeFrame and anchor guidance bindings",warmupCalls=128,measuredCalls=128,managedBytes=bytes,thread,
            scope="Calling thread and frame binding only; not whole-frame allocation or GPU cost"};
    }finally {
        hook.SetValue(null,null);EditorApplication.isPaused=previousPaused;
    }
}
