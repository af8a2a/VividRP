from pathlib import Path
import hashlib,json,subprocess
here=Path(__file__).resolve().parent;root=here.parents[2];out=root/'Temp~/vsm-depth-search/validation/dxc';out.mkdir(parents=True,exist_ok=True)
results=[]
for source in [root/'Shaders/Core/Private/CSMShadowResolve.compute',root/'Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute']:
 for line in source.read_text(encoding='utf-8').splitlines():
  if not line.startswith('#pragma kernel '):continue
  fields=line.split();entry=fields[2];tag=source.stem+'-'+entry
  cmd=['C:/VulkanSDK/1.4.350.0/Bin/dxc.exe','-T','cs_6_6','-E',entry,'-enable-16bit-types','-D','SHADER_API_D3D11=1','-D','UNITY_COMPILER_DXC=1','-I',str(root/'Temp~/vsm-bnd-compare/dxc2/include'),'-Fo',str(out/(tag+'.dxil')),str(source)]
  for define in fields[3:]:cmd+=['-D',define]
  result=subprocess.run(cmd,capture_output=True,text=True);(out/(tag+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
  results.append({'source':str(source.relative_to(root)),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'entry':entry,'exit_code':result.returncode})
  if result.returncode:print(tag,(result.stdout+result.stderr)[-2500:])
report={'status':'pass' if all(x['exit_code']==0 for x in results) else 'fail','passed':sum(x['exit_code']==0 for x in results),'total':len(results),'results':results}
(here/'dxc-final.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:report[k] for k in ['status','passed','total']}));raise SystemExit(report['status']!='pass')

