"""Install/remove only the two temporary probe sources; Unity owns their .meta files."""
import argparse
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
parser=argparse.ArgumentParser();parser.add_argument('action',choices=['install','remove']);args=parser.parse_args()
for extension in ('cs','compute'):
    source=HERE/f'VSMSMRTPathProbe.{extension}.txt'
    target=ROOT/f'Editor/Tools/VSMSMRTPathProbe.{extension}'
    if args.action=='install':
        if target.exists(): raise FileExistsError(target)
        target.write_text(source.read_text(encoding='utf-8'),encoding='utf-8',newline='\r\n')
    elif target.exists():
        if target.read_text(encoding='utf-8')!=source.read_text(encoding='utf-8'): raise RuntimeError(f'Modified probe: {target}')
        target.unlink()
print('Refresh Unity to import/remove the diagnostic. No .meta files are edited by this script.')
