from pathlib import Path
import subprocess,json,re
project=Path('E:/VividRP_Reborn');out=Path(__file__).resolve().parent/'roslyn';out.mkdir(exist_ok=True)
results=[]
for name in ['VividRP.Runtime','VividRP.Editor','VividRP.Editor.Tests']:
 src=project/'Library/Bee/artifacts/1900b0aE.dag'/f'{name}.rsp'
 s=src.read_text(encoding='utf-8-sig')
 s=re.sub(r'^-out:.*$',f'-out:"{out.as_posix()}/{name}.dll"',s,flags=re.M)
 s=re.sub(r'^-refout:.*$',f'-refout:"{out.as_posix()}/{name}.ref.dll"',s,flags=re.M)
 for previous in results:
  n=previous['assembly']
  s=s.replace(f'Library/Bee/artifacts/1900b0aE.dag/{n}.ref.dll',f'{out.as_posix()}/{n}.ref.dll')
 rsp=out/f'{name}.rsp';rsp.write_text(s,encoding='utf-8')
 cmd=['E:/Unity/6000.7.0a6/Editor/Data/DotNetSdk/dotnet.exe','E:/Unity/6000.7.0a6/Editor/Data/DotNetSdk/sdk/10.0.301/Roslyn/bincore/csc.dll','@'+str(rsp)]
 p=subprocess.run(cmd,cwd=project,capture_output=True,text=True)
 (out/f'{name}.log').write_text(p.stdout+p.stderr,encoding='utf-8')
 results.append(dict(assembly=name,exit_code=p.returncode));print(name,p.returncode)
 if p.returncode:print(p.stdout[-4000:]+p.stderr[-4000:]);break
(out/'validation.json').write_text(json.dumps(results,indent=2))
assert len(results)==3 and all(x['exit_code']==0 for x in results)
