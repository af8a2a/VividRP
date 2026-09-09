from pathlib import Path
import json,numpy as np,matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
plt.rcParams['font.sans-serif']=['Microsoft YaHei'];plt.rcParams['axes.unicode_minus']=False
archive=Path('Roadmap~/Experiments/TSRClampMix4096_20260909')
st=json.loads((archive/'static-summary.json').read_text())['stages']
dynamic=json.loads((archive/'dynamic-and-initial-summary.json').read_text())['stages']
variants=['baseline','clamp_half','mix_half','both_half'];labels=['基线','裁剪位移 ×0.5','当前帧占比 ×0.5','两项 ×0.5'];colors=['#778395','#277daf','#d58439','#4a9965'];regions=['roof','ledge','arches']
fig,axs=plt.subplots(1,2,figsize=(13,4.5),layout='constrained')
x=np.arange(3);w=.19
for i,v in enumerate(variants):
 a=st[v+'_static'];ys=[a['temporal']['output'][n]['same_jitter_rms'] for n in regions]
 axs[0].bar(x+(i-1.5)*w,ys,w,label=labels[i],color=colors[i])
axs[0].set_xticks(x,['屋檐','横梁下方','拱门']);axs[0].set_ylabel('同 jitter 最终亮度 RMS（越低越好）');axs[0].set_title('静止：256 个统计帧');axs[0].legend(fontsize=8)
for i,v in enumerate(variants):
 name=v+'_angle'
 if name not in dynamic:continue
 a=dynamic[name]['post_stop'];axs[1].plot([z['after_stop'] for z in a],[z['error']['output']['roof'] for z in a],color=colors[i],label=labels[i])
axs[1].set_title('角直径返回：相对各自静止参考');axs[1].set_ylabel('屋檐区域亮度 RMS（输入已匹配）');axs[1].set_xlabel('停止变化后的帧数');axs[1].set_yscale('log');axs[1].grid(alpha=.2);axs[1].legend(fontsize=8)
fig.suptitle('4096 · BND 4×8 · 512 页 · 角直径 7.1° · TSR 历史 16',fontsize=13)
fig.savefig(archive/'comparison.png',dpi=160);plt.close(fig)
