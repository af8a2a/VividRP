from pathlib import Path
import json,numpy as np
from scipy.ndimage import gaussian_filter,convolve1d
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
p=Path('Temp~/stbn-fast'); result={};fig,axs=plt.subplots(1,3,figsize=(13,3.5))
for name in ['stbn','fast_sep','fast_product','fast_binomial']:
 a=np.frombuffer((p/(name+'.r8')).read_bytes(),np.uint8).reshape(-1,128,128).astype(np.float32)/255
 # A discontinuous test integrand, not a claim of SMRT rendering performance.
 f=(a<.5).astype(np.float32)-.5
 trace=np.zeros_like(f[0]); filtered=[]
 for i in range(4*len(a)):
  trace=.9*trace+.1*f[i%len(a)]
  if i>=3*len(a):filtered.append(trace.copy())
 ema=np.stack(filtered)
 fftxy=abs(np.fft.fft2(f,axes=(1,2)))**2/128**2
 fftz=(abs(np.fft.rfft(f,axis=0))**2/len(f)).mean((1,2))
 freq=np.fft.fftfreq(128); r=np.hypot(freq[:,None],freq[None,:]); bands=np.linspace(0,.5,26)
 radial=[float(fftxy[:,(r>=lo)&(r<hi)].mean()) for lo,hi in zip(bands[:-1],bands[1:])]
 binom=convolve1d(convolve1d(ema,[.25,.5,.25],axis=1,mode='wrap'),[.25,.5,.25],axis=2,mode='wrap')
 offsets=(128*np.mod(np.arange(4)[:,None]*np.array([.754877666,.569840296]),1)).astype(int)
 phases=np.stack([np.roll(a,(-int(y),-int(x)),(1,2)).ravel() for x,y in offsets])
 result[name]={'shape':list(a.shape),'range':[float(a.min()),float(a.max())],'mean':float(a.mean()),'variance':float(a.var()),'r2_offsets':offsets.tolist(),'phase_correlation':np.corrcoef(phases).tolist(), 'threshold_ema_rms':float(np.sqrt((ema**2).mean())), 'threshold_gauss_ema_rms':float(np.sqrt((gaussian_filter(ema,(0,1,1),mode='wrap')**2).mean())),'threshold_binomial_ema_rms':float(np.sqrt((binom**2).mean())), 'threshold_cycle_mean_rms':float(np.sqrt((f.mean(0)**2).mean()))}
 axs[0].plot((bands[1:]+bands[:-1])/2,radial,label=name)
 axs[1].plot(np.fft.rfftfreq(len(f)),fftz,label=name)
 axs[2].bar(name,result[name]['threshold_ema_rms'])
axs[0].set(title='Thresholded input: spatial spectrum',xlabel='cycles / pixel',ylabel='power')
axs[1].set(title='Thresholded input: temporal spectrum',xlabel='cycles / frame',ylabel='power')
axs[2].set(title='EMA alpha=0.1 residual RMS',ylabel='RMS')
axs[2].tick_params(axis='x',labelrotation=30);axs[0].legend(fontsize=8)
fig.tight_layout();fig.savefig(p/'source-spectra.png',dpi=160);plt.close(fig)
(p/'source-noise-audit.json').write_text(json.dumps(result,indent=2))
print({n:{k:round(v,6) for k,v in r.items() if isinstance(v,float)} for n,r in result.items()})
