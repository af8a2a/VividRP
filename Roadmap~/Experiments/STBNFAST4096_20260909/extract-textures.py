from pathlib import Path
import io, zipfile, hashlib, json, shutil, re
import numpy as np
from PIL import Image
root=Path.cwd(); out=root/'Temp~/stbn-fast'
sources=[('stbn','STBN.zip','STBN/stbn_scalar_2Dx1Dx1D_128x128x64x1_',64),
 ('fast_sep','FAST.zip','noise/real/temporal/exp/real_uniform_gauss1_0_exp0101_separate05_',32),
 ('fast_product','FAST.zip','noise/real/temporal/exp/real_uniform_gauss1_0_exp0101_product_',32),
 ('fast_binomial','FAST.zip','noise/real/temporal/exp/real_uniform_binomial3x3_exp0101_product_',32)]
manifest=[]
for name,archive,prefix,depth in sources:
 with zipfile.ZipFile(out/archive) as z:
  data=[]; entries=[]
  for i in range(depth):
   b=z.read(prefix+str(i)+'.png'); a=np.asarray(Image.open(io.BytesIO(b)))
   if a.ndim==3:a=a[...,0]
   assert a.shape==(128,128) and a.dtype==np.uint8
   data.append(a); entries.append({'name':prefix+str(i)+'.png','sha256':hashlib.sha256(b).hexdigest()})
  volume=np.stack(data); raw=volume.tobytes(); (out/(name+'.r8')).write_bytes(raw)
  manifest.append({'name':name,'archive':archive,'archive_sha256':hashlib.sha256((out/archive).read_bytes()).hexdigest(),'shape':list(volume.shape),'r8_sha256':hashlib.sha256(raw).hexdigest(),'slices':entries})
(out/'texture-provenance.json').write_text(json.dumps(manifest,indent=2))
