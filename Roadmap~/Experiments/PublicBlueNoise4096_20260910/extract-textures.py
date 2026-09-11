from pathlib import Path
import zipfile,io,json,hashlib
import numpy as np
from PIL import Image
root=Path.cwd()/'Temp~/public-noise'; root.mkdir(parents=True,exist_ok=True); src=root.parent/'stbn-fast'
definitions=[
 ('stbn_scalar','STBN.zip','STBN/stbn_scalar_2Dx1Dx1D_128x128x64x1',64,1),
 ('stbn_vec2','STBN.zip','STBN/stbn_vec2_2Dx1D_128x128x64',64,2),
 ('fast_scalar','FAST.zip','noise/real/temporal/exp/real_uniform_gauss1_0_exp0101_separate05',32,1),
 ('fast_vec2','FAST.zip','noise/vector2/temporal/exp/vector2_uniform_gauss1_0_exp0101_separate05',32,2)]
result={'note':'Only publicly pregenerated PNG data. No generator invocation, resampling, temporal shuffling or repeated-slice construction.', 'textures':{}}
for name,archive,prefix,depth,channels in definitions:
 z=zipfile.ZipFile(src/archive); chunks=[]; slices=[]
 for i in range(depth):
  data=z.read(prefix+f'_{i}.png'); im=Image.open(io.BytesIO(data)); a=np.asarray(im)
  assert im.size==(128,128) and a.dtype==np.uint8
  if a.ndim==2:a=a[:,:,None]
  assert a.shape[2]>=channels
  chunks.append(a[:,:,:channels].tobytes())
  slices.append({'slice':i,'png_sha256':hashlib.sha256(data).hexdigest(),'channels_sha256':hashlib.sha256(chunks[-1]).hexdigest()})
 data=b''.join(chunks);(root/(name+'.raw')).write_bytes(data)
 result['textures'][name]={'archive':archive,'archive_sha256':hashlib.sha256((src/archive).read_bytes()).hexdigest(),'prefix':prefix,'depth':depth,'channels':channels,'format':'R8_UNorm' if channels==1 else 'R8G8_UNorm','bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'unique_slices':len(set(chunks)),'slices':slices}
(root/'texture-provenance.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print({n:{k:v for k,v in r.items() if k!='slices'} for n,r in result['textures'].items()})
