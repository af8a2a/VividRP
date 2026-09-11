from pathlib import Path
import json,gzip,numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
out=Path('Roadmap~/Experiments/VSMGeneralCoverage_20260911')
fig,axes=plt.subplots(3,1,figsize=(10,8),sharex=True,layout='constrained')
for root,variant,baseline,label,color in [
 ('20260911_112543_125','view768005','base768','768 before fix','#cf7d1c'),
 ('20260911_113933_147','view1024005','base1024','1024 before fix','#6d7591'),
 ('20260911_115011_007','view1024005','base1024','1024 final','#168c72')]:
 r=Path('Temp~/vsm-general-captures')/root;d=r/(variant+'_translate');b=r/(baseline+'_translate')
 rec=sorted([json.loads(l) for l in (d/'frames.jsonl').read_text().splitlines()],key=lambda x:x['step'])
 c=np.stack([np.frombuffer(gzip.decompress((d/f'frame_{f["step"]:03}_counters.bin.gz').read_bytes()),'<u4') for f in rec])
 axes[0].plot(np.arange(len(rec)),c[:,0],label=label+' resident',color=color)
 if label=='1024 final':axes[0].plot(np.arange(len(rec)),c[:,1],color='#555555',linestyle='--',label='Current requests')
 points=[]
 for f in rec:
  if not f['capture']:continue
  name=f'frame_{f["step"]:03}_debug0.bin.gz'
  a=np.frombuffer(gzip.decompress((d/name).read_bytes()),'<f4').reshape(-1,4);z=np.frombuffer(gzip.decompress((b/name).read_bytes()),'<f4').reshape(-1,4)
  valid=(a[:,1]>=0)&(z[:,1]>=0);delta=a[valid,1]-z[valid,1];points.append((f['step'],int((delta>0).sum()),max(0,float(delta.max()))))
 p=np.array(points);axes[1].plot(p[:,0],p[:,1],'.-',label=label,color=color);axes[2].plot(p[:,0],p[:,2],'.-',label=label,color=color)
axes[0].set_ylabel('Pages');axes[0].set_title('Movement history reaches 987 pages; settled views now restore coverage')
axes[1].set_ylabel('Coarser samples vs centred layout')
axes[2].set_ylabel('Maximum extra LOD levels');axes[2].set_xlabel('Measured frame (motion ends at frame 64)');axes[2].set_yticks(range(5))
for ax in axes:
 ax.axvline(64,color='#777777',linestyle=':',linewidth=1);ax.grid(alpha=.15);ax.spines[['top','right']].set_visible(False);ax.legend(loc='upper right')
fig.savefig(out/'budget-history.png',dpi=160)
