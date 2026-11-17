"""GPU-only per-frame ledger; no readback until the existing capture finishes.

Integer fingerprints localize differences, but are not cryptographic equality
proof or a substitute for the full-byte A/A and final mean-map quality gates.
"""
from pathlib import Path


FIELDS = (
    'm_Geometry', 'm_AdaptivePlacement', 'm_HitLighting', 'm_Probes',
    'm_FilteredProbes', 'm_PreparedAnchor', 'm_ReconstructedAnchor',
    'm_CurrentAnchorStatistics', 'm_PreviousGeometry', 'm_PreviousProbes',
    'm_PreviousAnchorStatistics', 'm_CurrentDirectional', 'm_DirectionalTemporal',
    'm_PreviousPixelGeometry', 'm_DirectionalGuideTexture',
    'm_PreviousDirectionalFiltered', 'm_CurrentDirectionalFiltered',
    'm_DirectionalRayTexture', 'm_ScreenRays', 'm_GBuffer0', 'm_GBuffer1', 'm_Depth',
    'm_PreviousScreenRadiance', 'm_PreviousScreenGeometry', 'm_ScreenMotion',
)


def install(source, shaders, frames):
    names = FIELDS + ('resolved', 'temporal', 'final') + tuple('hzb-mip'+str(i) for i in range(12))
    helper = r'''    ComputeShader fingerprintShader=null;
    GraphicsBuffer fingerprintBuffer=null;
    string[] fingerprintNames={__NAMES__};
    const int fingerprintFrames=__FRAMES__,fingerprintStripes=16;
    void SetupFingerprints(){
        fingerprintShader=PrivateShader("gpu-fingerprint.compute.txt",new[]{"HashFloatCS","HashUIntCS"});
        int count=fingerprintFrames*fingerprintNames.Length*fingerprintStripes;
        fingerprintBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,16);
        var initial=new uint[count*4];Array.Fill(initial,0xdeadbeefu);fingerprintBuffer.SetData(initial);
    }
    void Fingerprint(ComputePassContext context,Texture texture,int slot,int mip=0){
        if(frame<0 || frame>=fingerprintFrames)throw new InvalidOperationException("Fingerprint frame outside ledger");
        int kernel=fingerprintShader.FindKernel(texture.graphicsFormat==UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt?"HashUIntCS":"HashFloatCS");
        var raw=(CommandBuffer)wrapped.GetValue(context.cmd);
        raw.SetComputeIntParam(fingerprintShader,"_Width",Mathf.Max(1,texture.width>>mip));raw.SetComputeIntParam(fingerprintShader,"_Height",Mathf.Max(1,texture.height>>mip));
        raw.SetComputeIntParam(fingerprintShader,"_Mip",mip);
        raw.SetComputeIntParam(fingerprintShader,"_Offset",(frame*fingerprintNames.Length+slot)*fingerprintStripes);
        raw.SetComputeTextureParam(fingerprintShader,kernel,"_Source",texture);
        raw.SetComputeBufferParam(fingerprintShader,kernel,"_Fingerprints",fingerprintBuffer);
        context.cmd.DispatchCompute(fingerprintShader,kernel,fingerprintStripes,1,1);
    }
    void FingerprintResolve(ComputePassContext context,Texture texture){
        int[] Bits(Matrix4x4 matrix)=>Enumerable.Range(0,16).Select(i=>BitConverter.SingleToInt32Bits(matrix[i])).ToArray();
        records.Add(new{frame,fingerprintConstants=true,frameOverride=hooks["EditorGatherFrameOverride"].GetValue(null),
            inverseVp=Bits((Matrix4x4)PrivateField("m_InverseViewProjection")),vp=Bits((Matrix4x4)PrivateField("m_ViewProjection")),
            previousVp=Bits((Matrix4x4)PrivateField("m_State").GetType().GetField("ViewProjection",inst|System.Reflection.BindingFlags.Public).GetValue(PrivateField("m_State"))),
            jitter=PrivateField("m_ScreenJitterDelta").ToString("R"),
            history=PrivateField("m_HasHistory"),screenHistory=PrivateField("m_HasScreenHistory"),guideValid=PrivateField("m_DirectionalGuideHistoryValid")});
        for(int slot=0;slot<__FIELDS__;++slot){
            string name=fingerprintNames[slot];
            if(frame<2 && name.StartsWith("m_Previous",StringComparison.Ordinal))continue;
            if((name=="m_ScreenRays" || name=="m_PreviousScreenRadiance" || name=="m_PreviousScreenGeometry") && !(bool)PrivateField("m_HasScreenHistory"))continue;
            Fingerprint(context,QualityResolve(name),slot);
        }
        Fingerprint(context,texture,__RESOLVED__);
        var hzb=QualityResolve("m_ScreenHzb");
        if(hzb.mipmapCount>12)throw new InvalidOperationException("HZB mip coverage exceeded");
        for(int mip=0;mip<hzb.mipmapCount;++mip)Fingerprint(context,hzb,__HZB__+mip,mip);
    }
    void PublishFingerprints(){
        var values=new uint[fingerprintBuffer.count*4];fingerprintBuffer.GetData(values);
        var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);
        string path=output+"/gpu-fingerprints.u32";Write(path,bytes);
        records.Add(new{gpuFingerprints=true,path,sha256=Hash(bytes),frames=fingerprintFrames,stripes=fingerprintStripes,names=fingerprintNames,
            limitation="Diagnostic integer fingerprints, not cryptographic proof. Full-byte and quality gates remain required."});
    }
'''
    helper = helper.replace('__NAMES__', ','.join('"'+n+'"' for n in names))
    helper = helper.replace('__FRAMES__', str(frames)).replace('__FIELDS__', str(len(FIELDS))).replace('__RESOLVED__', str(len(FIELDS)))
    helper = helper.replace('__HZB__', str(len(FIELDS)+3)).replace('PrivateField("m_ScreenJitterDelta").ToString("R")', '((Vector2)PrivateField("m_ScreenJitterDelta")).ToString("R")')
    replacements = {
        '    int coldOtherCameras=0,coldTargetCameras=0;': helper+'    int coldOtherCameras=0,coldTargetCameras=0;',
        '        coldDecode=PrivateShader(': '        SetupFingerprints();coldDecode=PrivateShader(',
        '        if(index==0){': '        if(index==0)FingerprintResolve(context,texture);\n        if(index==1)Fingerprint(context,texture,'+str(len(FIELDS)+1)+');\n        if(index==0){',
        '        CaptureEpochFirstFrame(context);': '        Fingerprint(context,QualityResolve("m_Output"),'+str(len(FIELDS)+2)+');CaptureEpochFirstFrame(context);',
        '        complete = true;': '        PublishFingerprints();complete = true;',
        '            if(coldDecode!=null)': '            fingerprintBuffer?.Dispose();if(fingerprintShader!=null)UnityEngine.Object.DestroyImmediate(fingerprintShader);\n            if(coldDecode!=null)',
    }
    for old, new in replacements.items():
        assert source.count(old) == 1, (old, source.count(old))
        source = source.replace(old, new, 1)
    shaders['gpu-fingerprint.compute.txt'] = Path(__file__).with_name('ScreenProbeGpuFingerprint.compute.txt').read_text()
    return source, shaders
