from pathlib import Path
import json,numpy as np,matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib import font_manager
font_manager.fontManager.addfont('C:/Windows/Fonts/msyh.ttc')
plt.rcParams.update({'font.family':'Microsoft YaHei','font.size':11,'axes.unicode_minus':False})
o=Path('Roadmap~/Experiments/VSMBNDComparison_20260909');j=json.loads((o/'summary.json').read_text())
fig,axes=plt.subplots(1,2,figsize=(12.8,4.3),layout='constrained')
names=['legacy4_512_static','bnd4_512_static','legacy8_512_static','bnd8_512_static'];labels=['旧相位 4×8','BND 4×8','旧相位 8×8','BND 8×8'];colors=['#78909c','#d27935','#a7c4c1','#276a62'];regions=['roof','ledge','arches'];x=np.arange(3)
for n,l,c,k in zip(names,labels,colors,range(4)):
 axes[0].bar(x+(k-1.5)*.19,[j['stages'][n]['temporal']['output'][r]['same_jitter_rms'] for r in regions],width=.18,label=l,color=c)
axes[0].set_xticks(x,['屋檐','横梁','拱顶']);axes[0].set_ylabel('TSR 亮度的同 jitter 时间 RMS');axes[0].set_title('更换相位未降低最终噪声；增加光线有收益');axes[0].legend(ncol=2,fontsize=9);axes[0].grid(axis='y',alpha=.2)
for n,l,c in [('legacy4_512_angle','旧相位 4×8','#78909c'),('bnd4_512_angle','BND 4×8','#d27935')]:
 rows=j['stages'][n]['post_stop_same_sample'];axes[1].plot([r['frames_after_stop'] for r in rows],[r['error']['output']['ledge'] for r in rows],marker='o',ms=3,label=l,color=c)
axes[1].set_xlabel('角直径恢复后帧数');axes[1].set_ylabel('相对同采样步静止参考的 TSR 亮度 RMS');axes[1].set_title('原始阴影与 TSR 输入一致，输出仍有历史残差');axes[1].legend();axes[1].grid(alpha=.2)
fig.suptitle('4096 · 512 页 · 7.1° · NativeAA TSR history 16\n固定曝光；画质捕获不用于性能计时',fontsize=13)
fig.savefig(o/'comparison.png',dpi=160);plt.close(fig)
print(o/'comparison.png')
