"""Summarize the saved independent geometry/GPU run, without Unity."""
from pathlib import Path
import csv,json,sys,shutil
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent;WORK=HERE.parents[2]/'Temp~/smrt-path'
sys.path.insert(0,str(HERE));import path_geometry as g

def main():
    rows=list(csv.DictReader((WORK/'metrics.csv').open()))
    base=[r for r in rows if r['max_length']=='50.0' and r['scrolled']=='False' and r['terminal_levels']=='0' and r['reference']=='straight']
    summary=[]
    for scene in g.reference.SCENES:
        item={'scene':scene}
        for rule in ('old','continuation'):
            subset=[r for r in base if r['scene']==scene and r['rule']==rule];n=sum(int(r['rays']) for r in subset)
            for field in ('false_shadow','missed_shadow'):
                item[rule+'_'+field+'_percent']=100*sum(int(r[field]) for r in subset)/n
            item[rule+'_visibility_mae']=sum(float(r['visibility_mae'])*int(r['rays']) for r in subset)/n
        summary.append(item)
    with (HERE/'geometry-summary.csv').open('w',newline='') as f:
        w=csv.DictWriter(f,fieldnames=summary[0].keys());w.writeheader();w.writerows(summary)
    for name in ('metrics.csv','manifest.json','gpu-validation.json','gpu-status.txt'):
        shutil.copyfile(WORK/name,HERE/name)
    fig,axs=plt.subplots(1,2,figsize=(11,4),layout='constrained')
    for ax,budget in zip(axs,(4,8)):
        c=next(c for c in json.loads((WORK/'manifest.json').read_text()) if c['scene']=='far_sheet' and c['resolution']==512 and c['diameter']==7.1 and c['budget']==budget and c['max_length']==50 and not c['scrolled'] and not c['terminal_levels'])
        a=np.load(WORK/f"{c['id']:03}.npz");_,_,world,_=g.reference.inputs(c['scene'],512,7.1,budget,True,50)
        x=world[::64,0].reshape(3,65)[1]
        for field,label,color in [('straight','Exact geometry','#222222'),('old','Before: fine-level tail','#ce6031'),('new','After: clipmap continuation','#247d9b')]:
            v=(~a[field] if field=='straight' else a[field]==0).reshape(3,65,64).mean(2)[1]
            ax.plot(x,v,label=label,color=color,lw=2)
        ax.set(title=f'{budget} cells / segment; former cap {c["endpoints"][0]:.2f} m',xlabel='Receiver X (m)',ylabel='Visibility',ylim=(-.03,1.03));ax.grid(alpha=.15)
    axs[0].legend(fontsize=8);fig.suptitle('Occluder at 16 m; 7.1 degree light; 512 texels; configured length 50 m')
    fig.savefig(HERE/'far-sheet.png',dpi=160);plt.close(fig)
    print(json.dumps(summary,indent=2))

if __name__=='__main__':main()
