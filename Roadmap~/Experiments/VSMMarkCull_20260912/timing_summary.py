"""Collect separate Editor GPU-stage diagnostics; never sum nested markers."""
from pathlib import Path
import csv,json,shutil
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[2]
def main():
 rows=[]; runs={}
 for run in sorted((ROOT/'Temp~/smrt-path/timing').glob('mark-*')):
  if not (run/'status.txt').exists() or (run/'status.txt').read_text().strip()!='complete':continue
  dest=HERE/'timing'/run.name
  shutil.copytree(run,dest,dirs_exist_ok=True)
  for file in sorted(run.rglob('case_000_summary.csv')):
   for row in csv.DictReader(file.open(encoding='utf-8-sig')):
    rows.append(dict(revision=run.name,**row))
  runs[run.name]={}
  for file in sorted(run.rglob('run.json')):
   record=json.loads(file.read_text(encoding='utf-8-sig'))
   runs[run.name][file.relative_to(run).parts[0]]={k:record.get(k) for k in ('gpu','graphicsApi','timingScope','status','qualityStatus')}
 with (HERE/'timing.csv').open('w',newline='') as file:
  w=csv.DictWriter(file,fieldnames=rows[0].keys());w.writeheader();w.writerows(rows)
 (HERE/'timing-context.json').write_text(json.dumps(runs,indent=2))
 for row in rows:
  if row['metric'] in ('VSM.MarkReceiverPages_gpu_ms','VSM.PageCull_gpu_ms'):
   print(row['revision'],row['case_name'],row['metric'],row['median'])
if __name__=='__main__':main()
