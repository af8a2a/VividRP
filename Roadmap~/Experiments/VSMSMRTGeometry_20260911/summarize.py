"""Archive compact evidence and figures after geometry.py verify succeeds."""
import csv, hashlib, json, shutil, subprocess, sys
sys.dont_write_bytecode = True
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle
from geometry import HERE,ROOT,WORK,RULES

validation=json.loads((WORK/'gpu-validation.json').read_text())
assert validation['mismatches']==0
manifest=json.loads((WORK/'manifest.json').read_text())
assert validation['comparisons']==sum(c['rays'] for c in manifest)*len(RULES)
metrics=list(csv.DictReader((WORK/'metrics.csv').open()))
base=[r for r in metrics if r['centered']=='True' and float(r['max_length'])==10]

def aggregate(rows):
    n=sum(int(r['rays']) for r in rows)
    return dict(rays=n,false_shadow=sum(int(r['false_shadow']) for r in rows),missed_shadow=sum(int(r['missed_shadow']) for r in rows),unavailable=sum(int(r['unavailable']) for r in rows),visibility_mae=sum(float(r['visibility_mae']) for r in rows)/len(rows),visibility_max=max(float(r['visibility_max']) for r in rows))

summary=dict(datasets=len(manifest),rays=sum(c['rays'] for c in manifest),gpu=validation,aggregate={},ambiguity=[],gap=[],path=[],raw_receiver=[])
for ref in ('straight','bent'):
    summary['aggregate'][ref]={rule:aggregate([r for r in base if r['reference']==ref and r['rule']==rule]) for rule in RULES}
for scene in ('slope_receiver','steep_receiver'):
    for centered in ('True','False'):
        row=aggregate([r for r in metrics if r['scene']==scene and r['rule']=='current' and r['reference']=='straight' and r['centered']==centered])
        summary['raw_receiver'].append(dict(scene=scene,centered=centered=='True',**row))
for a_name,b_name in (('thin_sheet','solid_block'),('gap_trap','gap_hidden')):
    counts=dict(straight=0,bent=0);n=0
    for c in [c for c in manifest if c['scene']==a_name]:
        d=next(d for d in manifest if d['scene']==b_name and all(c[k]==d[k] for k in ('resolution','diameter','budget','centered','max_length')))
        assert c['depth_sha256']==d['depth_sha256']
        a=np.load(WORK/f"{c['id']:03}.npz");b=np.load(WORK/f"{d['id']:03}.npz")
        assert np.array_equal(a['predictions'],b['predictions'])
        for ref in counts:counts[ref]+=int(np.count_nonzero(a[ref]!=b[ref]))
        n+=c['rays']
    summary['ambiguity'].append(dict(scenes=[a_name,b_name],paired_rays=n,identical_depth_and_predictions=True,truth_disagreements=counts))
for scene in ('gap_trap','gap_hidden'):
    n=changed=helped=harmed=0
    for c in [c for c in manifest if c['scene']==scene]:
        a=np.load(WORK/f"{c['id']:03}.npz");pred=a['predictions'];ref=a['straight'];n+=len(ref)
        different=pred[:,0]!=pred[:,1]
        changed+=int(different.sum());helped+=int(np.count_nonzero(different&((pred[:,0]==1)==ref)));harmed+=int(np.count_nonzero(different&((pred[:,1]==1)==ref)))
    summary['gap'].append(dict(scene=scene,rays=n,changed=changed,helped=helped,harmed=harmed))
for c in [c for c in manifest if c['scene']=='far_sheet']:
    a=np.load(WORK/f"{c['id']:03}.npz")
    assert np.array_equal(a['predictions'][:,0]==1,a['bent'])
    summary['path'].append({k:c[k] for k in ('resolution','diameter','budget','max_length','length','rays','path_disagreement')})
summary['revision']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
summary['sha256']={str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'Shaders/Core/Private/VSMSMRT.hlsl',ROOT/'Shaders/Core/Private/CSMShadowResolve.compute',HERE/'geometry.py',HERE/'VSMSMRTGeometryProbe.compute.txt',HERE/'VSMSMRTGeometryProbe.cs.txt']}
summary['gpu_status']=(WORK/'gpu-status.txt').read_text()
(HERE/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
for name in ('metrics.csv','curves.csv','manifest.json','gpu-validation.json'):
    shutil.copyfile(WORK/name,HERE/name)

plt.rcParams.update({'font.size':10,'axes.spines.top':False,'axes.spines.right':False})
fig,axes=plt.subplots(2,2,figsize=(12.4,8.2),layout='constrained')
ax=axes[0,0]
ax.add_patch(Rectangle((0,.2),.35,3.8,facecolor='#76a8d3',alpha=.28,label='Solid: z=0.2..4'))
ax.add_patch(Rectangle((0,3.98),.35,.02,facecolor='#233d72',label='Thin sheet: z=3.98..4'))
z=np.linspace(.001,5,100);ax.plot(.1-.04*(z-.001),z,color='#ce5c28',label='Same oblique ray')
ax.scatter([.1],[.001],c='black',s=24);ax.set(xlim=(-.13,.37),ylim=(-.1,5),xlabel='x (world units)',ylabel='z toward light',title='A. Identical top depth, different true occlusion')
ax.text(.015,2.5,'Ray hits solid\nbut misses sheet',fontsize=10)
ax.legend(loc='upper right',fontsize=8)
curves=list(csv.DictReader((HERE/'curves.csv').open()));a=[r for r in curves if r['scene']=='far_sheet']
ax=axes[0,1]
for key,label,color,style in [('straight','True straight ray','#242d40','-'),('bent','Geometry with bounded tail','#36a181','--'),('current','Current GPU SMRT','#d5702e',':')]:
    ax.plot([float(r['x']) for r in a],[float(r[key]) for r in a],label=label,color=color,linestyle=style,lw=2)
ax.set(xlim=(-1.3,1.3),ylim=(-.04,1.04),xlabel='Receiver x (world units)',ylabel='Visibility',title='B. Far sheet: 16 m, 7.1 deg, 256 texels, 8 steps')
ax.legend(fontsize=8)
ax=axes[1,0]
x=np.arange(4);a=summary['aggregate']['straight']
fp=np.array([a[r]['false_shadow']/a[r]['rays']*100 for r in RULES]);fn=np.array([a[r]['missed_shadow']/a[r]['rays']*100 for r in RULES])
ax.bar(x-.18,fp,.36,label='False shadow',color='#cf7157');ax.bar(x+.18,fn,.36,label='Missed shadow',color='#527da8')
ax.set(xticks=x,xticklabels=['Current','Slab only','Surface','Halfspace'],ylabel='% of matched rays',title='C. Error against true geometry (centered, max 10 m)');ax.legend(fontsize=8)
ax=axes[1,1]
for max_length,style in ((10.,'-'),(50.,'--')):
    for budget,color in ((4,'#8a659f'),(8,'#278b83')):
        a=[r for r in summary['path'] if r['diameter']==7.1 and r['max_length']==max_length and r['budget']==budget]
        ax.plot([r['resolution'] for r in a],[r['path_disagreement']/r['rays']*100 for r in a],style,color=color,marker='o',label=f'{budget} steps, max {max_length:g} m')
ax.set(xlabel='Virtual resolution over fixed 16 m span',ylabel='Path-only disagreement (%)',title='D. Finer texels shorten the budget-limited bend')
ax.legend(fontsize=8)
fig.savefig(HERE/'comparison.png',dpi=180);fig.savefig(HERE/'comparison.svg');plt.close(fig)
print(json.dumps({k:summary[k] for k in ('datasets','rays','aggregate','ambiguity','gap','raw_receiver')},indent=2))
