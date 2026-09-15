from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
s = (HERE.parent / "VSMDepthSearch_20260913/same-frame.cs.txt").read_text(encoding="utf-8-sig")
s = s.replace("VSMDepthSearch_20260913", "VSMDepthVariants_20260913")
s = s.replace('bool background=UnityEngine.Application.runInBackground;', '''var cube=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);cube.name="Temporary moving VSM caster";cube.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
var cubePosition=camera.ViewportToWorldPoint(new UnityEngine.Vector3(.5f,.5f,6));cube.transform.position=cubePosition;
foreach(var renderer in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None))
    if(renderer.gameObject!=cube && renderer.sharedMaterial!=null && renderer.sharedMaterial.shader.name.Contains("Vivid")){cube.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial=renderer.sharedMaterial;break;}
cube.SetActive(false);
bool background=UnityEngine.Application.runInBackground;''')
s = s.replace('int stage=0,warm=0,sample=0;', 'int stage=0,warm=0,sample=0;')
s = s.replace('stage<6?2048:4096', 'stage<12?2048:4096')
s = s.replace('settings.virtualShadowMapSMRTAdaptiveRays.Override((stage/3)%2!=0);', '''settings.virtualShadowMapSMRTAdaptiveRays.Override(false);
    resolve.SetInt("_VSMDepthExperiment",1+(stage/4)%3);resolve.SetInt("_VSMDepthLayoutInterleaved",0);UnityEngine.Shader.SetGlobalInt("_VSMDepthLayoutInterleaved",0);
    cube.SetActive(stage%4==3);cube.transform.position=cubePosition;''')
s = s.replace('resolve.SetInt("_VSMDepthSearchLinear",0);camera.transform', 'resolve.SetInt("_VSMDepthSearchLinear",0);resolve.SetInt("_VSMDepthExperiment",0);UnityEngine.Object.DestroyImmediate(cube);camera.transform')
s = s.replace('cmd.SetComputeIntParam(resolve,"_VSMDepthSearchLinear",1);', 'cmd.SetComputeIntParam(resolve,"_VSMDepthExperiment",0);')
s = s.replace('cmd.SetComputeIntParam(resolve,"_VSMDepthSearchLinear",0);', 'cmd.SetComputeIntParam(resolve,"_VSMDepthExperiment",1+(stage/4)%3);')
s = s.replace('adaptive=(stage/3)%2!=0,scenario=new[]{"static","camera_slide","sun_rotate"}[stage%3]', 'mode=1+(stage/4)%3,scenario=new[]{"static","camera_slide","sun_rotate","moving_caster"}[stage%4]')
s = s.replace('if(++sample==16){if(++stage==12)', 'if(++sample==8){if(++stage==24)')
s = s.replace('stage%3==1', 'stage%4==1').replace('stage%3==2', 'stage%4==2')
s = s.replace('(sample-8)', '(sample-4)')
s = s.replace('else if(stage%4==2)sun.transform.rotation=lightRotation*UnityEngine.Quaternion.Euler(0,(sample-4)*.15f);', 'else if(stage%4==2)sun.transform.rotation=lightRotation*UnityEngine.Quaternion.Euler(0,(sample-4)*.15f);\n        else if(stage%4==3)cube.transform.position=cubePosition+camera.transform.right*((sample-4)*.15f);')
(HERE / "same-frame.cs.txt").write_text(s, encoding="utf-8")
(ROOT / "Temp~/vsm-depth-variants/same-frame.cs").write_text(s, encoding="utf-8")
