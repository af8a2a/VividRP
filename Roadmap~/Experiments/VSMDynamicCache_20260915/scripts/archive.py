from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET

root = Path.cwd()
work = root / 'Temp~/vsm-dynamic'
archive = root / 'Roadmap~/Experiments/VSMDynamicCache_20260915'
validation = archive / 'validation'
validation.mkdir(parents=True, exist_ok=True)
baseline = '689aab661581b578ffabb53b8e5fe91c7a5c9cab'
assert subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip() == baseline
files = subprocess.check_output(['git', 'diff', '--name-only'], text=True).splitlines()
files = [p for p in files if p.startswith(('Runtime/', 'Shaders/', 'Tests/'))]
manifest = []
for name in files:
    before = subprocess.check_output(['git', 'show', baseline + ':' + name])
    after = (root / name).read_bytes()
    for folder, data in [('source-before', before), ('source-final', after)]:
        target = archive / folder / (name + '.txt')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    manifest.append(dict(path=name, before_sha256=hashlib.sha256(before).hexdigest(),
                         final_sha256=hashlib.sha256(after).hexdigest()))
(archive / 'source-manifest.json').write_text(json.dumps(dict(baseline=baseline, files=manifest), indent=2))
reports = ['tests.xml', 'tests-final.xml', 'tests-sampling-repaired.xml', 'tests-sampling-final.xml']
runs = []
latest = {}
for name in reports:
    tree = ET.parse(work / name).getroot()
    # Preserve test results without copying startup logs or process command lines.
    for parent in tree.iter():
        for child in list(parent):
            if child.tag in ('command-line', 'environment'):
                parent.remove(child)
    ET.indent(tree)
    ET.ElementTree(tree).write(validation / name, encoding='utf-8', xml_declaration=True)
    runs.append(dict(file=name, total=int(tree.get('total')), passed=int(tree.get('passed')),
                     failed=int(tree.get('failed')), skipped=int(tree.get('skipped')),
                     test_body_seconds=float(tree.get('duration'))))
    if name in ('tests-final.xml', 'tests-sampling-final.xml'):
        for case in tree.iter('test-case'):
            latest[case.get('fullname')] = dict(result=case.get('result'), report=name)
summary = dict(runs=runs, final_unique_cases=len(latest),
               final_passed=sum(c['result'] == 'Passed' for c in latest.values()),
               latest_cases=latest)
assert summary['final_unique_cases'] == summary['final_passed'] == 260
(validation / 'test-summary.json').write_text(json.dumps(summary, indent=2))
for folder in ('dxc', 'caster-dxc', 'roslyn'):
    target = validation / folder
    target.mkdir(exist_ok=True)
    for source in (work / folder).iterdir():
        if source.suffix in ('.json', '.log'):
            shutil.copyfile(source, target / source.name)
shutil.copyfile(work / 'editor-device-hung.txt', validation / 'editor-device-hung.txt')
scripts = archive / 'scripts'
scripts.mkdir(exist_ok=True)
for name in ('compile-compute.py', 'compile-caster.py', 'compile-csharp.py', 'archive.py'):
    shutil.copyfile(work / name, scripts / name)
print(json.dumps(dict(source_files=len(files), final_unique_cases=summary['final_unique_cases'],
                      final_passed=summary['final_passed'])))
