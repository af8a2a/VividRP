from pathlib import Path
import re,subprocess,json,hashlib
root=Path.cwd();out=root/'Temp~/vsm-rebuild/caster-dxc';inc=out/'include';inc.mkdir(parents=True,exist_ok=True)
search=[root/'Temp~/vsm-bnd-compare/dxc2/include',Path('E:/Unity/6000.7.0a6/Editor/Data/Resources/CGIncludes')]
seen={}
def copy(src):
 src=src.resolve()
 if src in seen:return seen[src]
 dst=inc/(hashlib.sha256(str(src).encode()).hexdigest()[:12]+'_'+src.name);seen[src]=dst
 def include(m):
  name=m.group(1)
  for parent in [src.parent]+search:
   f=parent/name
   if f.exists():return '#include "'+copy(f).as_posix()+'"'
  return m.group(0).replace('#include_with_pragmas','#include')
 s=re.sub(r'#include(?:_with_pragmas)?\s+"([^"]+)"',include,src.read_text(encoding='utf-8-sig'))
 dst.write_text(s,encoding='utf-8');return dst
source=copy(root/'Shaders/Core/Private/GPUDriven/VisibilityBufferShadowCasterPass.shader')
s=source.read_text();s=s.split('HLSLPROGRAM',1)[1].split('ENDHLSL',1)[0]
p=out/'caster.hlsl';p.write_text(s)
results=[]
for page in [0,1]:
 for alpha in [0,1]:
  for vt in [0,1]:
   for caster in [0,1]:
    for stage,entry in [('vs','Vert'),('ps','Frag')]:
     name=f'{stage}_page{page}_alpha{alpha}_vt{vt}_caster{caster}'
     defs=['SHADER_API_D3D11=1','UNITY_COMPILER_DXC=1','SHADER_TARGET=66','SHADER_STAGE_'+('VERTEX' if stage=='vs' else 'FRAGMENT')+'=1']
     defs += [key+'=1' for key,value in [('VIVID_VSM_PAGE_CASTER',page),('_ALPHATEST_ON',alpha),('VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE',vt),('VIVID_VSM_CASTER',caster)] if value]
     cmd=['C:/VulkanSDK/1.4.350.0/Bin/dxc.exe','-T',stage+'_6_6','-E',entry,'-enable-16bit-types','-Wno-conversion','-Fo',str(out/(name+'.dxil'))]
     for d in defs:cmd+=['-D',d]
     proc=subprocess.run(cmd+[str(p)],capture_output=True,text=True)
     (out/(name+'.log')).write_text(proc.stdout+proc.stderr)
     results.append(dict(variant=name,exit_code=proc.returncode))
report=dict(total=len(results),passed=sum(x['exit_code']==0 for x in results),results=results)
(out/'validation.json').write_text(json.dumps(report,indent=2));print({k:v for k,v in report.items() if k!='results'})
assert report['total']==report['passed']
