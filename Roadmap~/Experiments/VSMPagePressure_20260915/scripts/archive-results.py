from pathlib import Path
import hashlib
import json
import shutil
import statistics
import subprocess

root = Path(__file__).resolve().parents[2]
src = root / 'Temp~/vsm-pressure'
out = root / 'Roadmap~/Experiments/VSMPagePressure_20260915'
out.mkdir(parents=True, exist_ok=True)

def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

for name in ['allocator-equivalence.cs', 'pressure-checks.cs', 'live-v5.cs',
             'allocator-completion.cs', 'allocator-timing-equal-v2.cs']:
    copy(src / name, out / 'scripts' / (name + '.txt'))
for name in ['compile-compute.py', 'compile-csharp.py', 'archive-results.py']:
    copy(src / name, out / 'scripts' / name)
for name in ['baseline.compute.txt', 'candidate.compute.txt']:
    copy(src / name, out / 'timing-shaders' / name)
for name in ['allocator-equivalence.json', 'pressure-checks.json']:
    copy(src / name, out / 'validation' / name)
for name in ['frames.json', 'stages.json', 'summary.json', 'before.json', 'after.json', 'status.txt']:
    copy(src / 'live-v5' / name, out / 'budget-sweep' / name)
for name in ['results.json', 'summary.json']:
    p = src / 'allocator-timing-equal-v2' / name
    if p.exists(): copy(p, out / 'excluded-profiler-timing' / name)
copy(src / 'allocator-completion/results.json', out / 'completion-timing/results.json')
for folder in ['dxc', 'roslyn']:
    for p in (src / folder).iterdir():
        if p.suffix in ['.log', '.json']:
            copy(p, out / 'validation' / folder / p.name)
for p in (src / 'before').rglob('*'):
    if p.is_file(): copy(p, out / 'source-before' / p.relative_to(src / 'before'))
snapshot = next((src / 'final-state').glob('*/snapshot.json'))
copy(snapshot, out / 'validation/final-editor-snapshot.json')

before = json.loads((src / 'live-v5/before.json').read_text())
after = json.loads((src / 'live-v5/after.json').read_text())
assert before == after, 'Scene or settings restoration differs'
rows = json.loads((src / 'allocator-completion/results.json').read_text())
timing = []
for stage in [0, 1]:
    subset = [r for r in rows if r['stage'] == stage]
    timing.append(dict(stage=stage, scenario='recycle' if stage else 'cold', samples=len(subset),
        oldMedianMs=statistics.median(r['oldMs'] for r in subset),
        newMedianMs=statistics.median(r['newMs'] for r in subset),
        oldRangeMs=[min(r['oldMs'] for r in subset), max(r['oldMs'] for r in subset)],
        newRangeMs=[min(r['newMs'] for r in subset), max(r['newMs'] for r in subset)]))
(out / 'completion-timing/summary.json').write_text(json.dumps(timing, indent=2))

current = (root / 'Shaders/Core/Private/CSMShadowResolve.compute').read_text(encoding='utf-8')
timed = (src / 'candidate.compute.txt').read_text(encoding='utf-8')
def allocator_section(s):
    start = s.index('void VSMPrototypePrepareAllocation(')
    end = s.index('\n[numthreads(', s.index('_VSMPagePressureRW[1] = detail;', start))
    return s[start:end].strip()
assert allocator_section(current) == allocator_section(timed), 'Timing allocator differs from final source'
files = subprocess.check_output(['git', 'diff', '--name-only', '--', 'Runtime', 'Shaders', 'Tests'], cwd=root, text=True).splitlines()
hashes = []
for file in files:
    path = root / file
    copy(path, out / 'source-final' / (file + '.txt'))
    hashes.append(dict(path=file, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
(out / 'validation/source-final-sha256.json').write_text(json.dumps(hashes, indent=2))
print(json.dumps(dict(output=str(out), restoreEqual=True, timedAllocatorMatches=True, timing=timing), indent=2))
