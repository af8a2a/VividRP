from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root=Path(__file__).resolve().parent
data=np.load(root/'final-guarded-production.npz')
fig=plt.figure(figsize=(12,7),facecolor='white')
columns=['Mean visibility', 'Temporal standard deviation (0–0.06)', 'Mean adjacent difference (0–0.04)']
for row,(key,label) in enumerate([('baseline4','Original'),('clip4','Guarded clipping')]):
    signal=data[key]
    maps=[signal.mean(0),signal.std(0),np.abs(np.diff(signal,axis=0)).mean(0)]
    for col,field in enumerate(maps):
        x=.01+col*.33;y=.54-row*.47
        ax=fig.add_axes([x,y,.32,.37])
        ax.imshow(field,origin='lower',cmap='gray' if col==0 else 'magma',vmin=0,vmax=[1,.06,.04][col])
        ax.set_axis_off()
        fig.text(x+.16,y+.395,label+' / '+columns[col],ha='center',va='bottom',fontsize=9,color='black')
fig.text(.5,.015,'Same 256 phases; 4-frame history and adaptive 2/4 rays on both sides.',ha='center',fontsize=11,color='black')
fig.savefig(root/'final-guarded-production.png',dpi=150)
