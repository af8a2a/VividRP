from pathlib import Path
import json,numpy as np,matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

out=Path('Temp~/tsr-local-clamp-final/analysis')
d=json.loads((out/'summary.json').read_text());s=d['stages']
regions=['roof','ledge','arches'];variants=['baseline','local']
result={}
for v in variants:
 if v+'_static' not in s:continue
 a=s[v+'_static'];b=s['baseline_static']
 item={'noise_change_percent':{},'mean_change_percent':{},'history_weights':{n:a['history'][n]['weight_mean'] for n in regions}}
 for n in regions:
  item['noise_change_percent'][n]=100*(a['temporal']['output'][n]['same_jitter_rms']/b['temporal']['output'][n]['same_jitter_rms']-1)
  item['mean_change_percent'][n]=100*(a['temporal']['output'][n]['mean']/b['temporal']['output'][n]['mean']-1)
 for scenario in ('angle','light'):
  if v+'_'+scenario not in s:continue
  rows=s[v+'_'+scenario]['post_stop'];item[scenario]={}
  for n in regions:
   item[scenario][n]={
    'early_rms':float(np.mean([r['error']['output'][n] for r in rows if r['after_stop']<=16])),
    'tail_rms':float(np.mean([r['error']['output'][n] for r in rows if r['after_stop']>=160])),
    'post_stop_source_rms_max':max(r['error']['source'][n] for r in rows),
    'post_stop_shadow_rms_max':max(r['error']['shadow'][n] for r in rows)
   }
 result[v]=item
(out/'decision.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
if len(s)!=6:raise SystemExit()
plt.rcParams['font.sans-serif']=['Microsoft YaHei'];plt.rcParams['axes.unicode_minus']=False
labels=['基线','局部裁剪'];colors=['#778395','#277daf']
fig,axs=plt.subplots(1,3,figsize=(16,4.3),layout='constrained');x=np.arange(3);w=.24
for i,v in enumerate(variants):
 axs[0].bar(x+(i-0.5)*w,[s[v+'_static']['temporal']['output'][n]['same_jitter_rms'] for n in regions],w,color=colors[i],label=labels[i])
 for ax,scenario in zip(axs[1:],('angle','light')):
  rows=s[v+'_'+scenario]['post_stop']
  ax.plot([r['after_stop'] for r in rows],[r['error']['output']['roof'] for r in rows],color=colors[i],label=labels[i])
axs[0].set_xticks(x,['屋檐','横梁','拱门']);axs[0].set_ylabel('最终亮度时域 RMS');axs[0].set_title('静止噪声：256 帧，同 jitter 去均值')
for ax,title in zip(axs[1:],('角直径恢复后的历史残差','光照强度恢复后的历史残差')):
 ax.set_title(title);ax.set_xlabel('恢复后的帧数');ax.set_ylabel('屋檐亮度 RMS');ax.set_yscale('log');ax.grid(alpha=.2)
for ax in axs:ax.legend(fontsize=8)
fig.suptitle('4096 · SMRT 4×8 · BND · 512 页 · 最终混合不变')
fig.savefig(out/'comparison.png',dpi=150);plt.close(fig)
