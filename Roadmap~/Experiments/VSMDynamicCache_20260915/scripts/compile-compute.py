from pathlib import Path
import subprocess,json,hashlib
root=Path.cwd();out=root/'Temp~/vsm-dynamic/dxc';out.mkdir(exist_ok=True)
results=[]
for source in ['Shaders/Core/Private/CSMShadowResolve.compute','Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute']:
 p=root/source
 for line in p.read_text(encoding='utf-8').splitlines():
  if not line.startswith('#pragma kernel '):continue
  tokens=line.split()[2:];kernel=tokens[0]
  for variant in [0]:
   name=p.stem+'.'+kernel+'.'+str(variant)
   cmd=['C:/VulkanSDK/1.4.350.0/Bin/dxc.exe','-T','cs_6_2','-E',kernel,'-D','SHADER_API_D3D11=1','-D','UNITY_COMPILER_DXC=1','-enable-16bit-types','-Wno-conversion','-I',str(root/'Temp~/vsm-bnd-compare/dxc2/include'),'-Fo',str(out/(name+'.dxil'))]
   for define in tokens[1:]+(['VIVID_SMRT_NOISE_EXPERIMENT=1'] if variant else []):cmd+=['-D',define]
   proc=subprocess.run(cmd+[str(p)],capture_output=True,text=True);(out/(name+'.log')).write_text(proc.stdout+proc.stderr)
   results.append(dict(source=source,kernel=kernel,variant=variant,exit_code=proc.returncode))
report={'total':len(results),'passed':sum(x['exit_code']==0 for x in results),'results':results}
(out/'validation.json').write_text(json.dumps(report,indent=2));print({k:v for k,v in report.items() if k!='results'})
assert report['total']==report['passed']

