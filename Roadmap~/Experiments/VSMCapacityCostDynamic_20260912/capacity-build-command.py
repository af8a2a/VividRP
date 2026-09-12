"""Build a public-API Unity_RunCommand payload; does not contact Unity."""
import base64
import importlib.util
import json
from pathlib import Path
import struct
import sys

sys.dont_write_bytecode = True

here = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('capacity_fixture', here / 'capacity-fixture.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
code = (here / 'capacity-gpu.cs.txt').read_text(encoding='utf-8')
for mismatch in (False, True):
    pages, summary = fixture.expected_rows(mismatch)
    fixture.verify_identities(pages)
    prefix = 'CAPACITY_MISMATCH_' if mismatch else 'CAPACITY_'
    for name, rows in [('PAGES', pages), ('SUMMARY', summary)]:
        values = [value for row in rows for value in row]
        encoded = base64.b64encode(struct.pack('<' + 'I' * len(values), *values)).decode('ascii')
        code = code.replace('__' + prefix + name + '__', encoded)
assert '__CAPACITY_' not in code
output = here / 'capacity-gpu-command.json'
output.write_text(json.dumps(dict(Code=code, Title='验证16层VSM容量统计、所有权异常与生产插入的真实容量边界'), ensure_ascii=False, indent=2), encoding='utf-8')
print(str(output))
