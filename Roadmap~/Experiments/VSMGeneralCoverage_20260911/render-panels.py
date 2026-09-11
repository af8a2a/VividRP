from pathlib import Path
import gzip,json,numpy as np
from PIL import Image,ImageDraw,ImageFont
root=Path('Temp~/vsm-general-captures/20260911_112543_125')
high=Path('Temp~/vsm-general-captures/20260911_115011_007')
out=Path('Roadmap~/Experiments/VSMGeneralCoverage_20260911')
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',18)
shown=[('base256',root),('view512005',root),('view1024005',high)]
panel=Image.new('RGB',(1680,690),(20,23,29));draw=ImageDraw.Draw(panel)
for i,(v,r) in enumerate(shown):
 im=Image.open(r/(v+'_static')/'last.png').convert('RGB').crop((680,170,1240,820));panel.paste(im,(i*560,40));draw.text((i*560+8,8),v+' / native pixels',font=font,fill='white')
panel.save(out/'native-comparison.png')
for name,box in {'roof':(735,190,380,70),'ledge':(715,485,390,80),'arches':(730,575,360,110)}.items():
 x,y,w,h=box;panel=Image.new('RGB',(w*3,h*2+90),(20,23,29));draw=ImageDraw.Draw(panel)
 for i,(v,r) in enumerate(shown):
  d=r/(v+'_static');a=np.stack([np.frombuffer(gzip.decompress((d/f'frame_{s:03}_shadow.bin.gz').read_bytes()),'<f2').reshape(650,560)[::-1].astype('f4') for s in range(32)]).mean(axis=0)
  raw=Image.fromarray(np.round(np.clip(a[y-170:y-170+h,x-680:x-680+w],0,1)*255).astype('uint8')).convert('RGB')
  im=Image.open(d/'last.png').convert('RGB').crop((x,y,x+w,y+h));panel.paste(im,(i*w,32));panel.paste(raw,(i*w,h+82));draw.text((i*w+5,5),v+' / final',font=font,fill='white');draw.text((i*w+5,h+53),'raw shadow mean',font=font,fill='white')
 panel.save(out/(name+'-comparison.png'))
