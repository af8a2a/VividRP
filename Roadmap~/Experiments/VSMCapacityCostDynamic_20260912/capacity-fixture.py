"""Independent integer fixture expectations; no implementation of the audit loop.

python capacity-fixture.py --write-expected DIR
python capacity-fixture.py --pages gpu-pages.bin --summary gpu-summary.bin
Append --metadata-mismatch after setting metadata[0].y=99 in the GPU fixture.
"""
import argparse
import json
from pathlib import Path
import struct

STRIDE, CAPACITY, LEVELS = 64, 6, 2


def expected_rows(metadata_mismatch=False):
    rows = [[0] * STRIDE for _ in range(CAPACITY * 2)]
    # Literal counts are derived from the fixture's seven distinct list shapes,
    # independently of how the GPU scans/reduces them.
    for pool in range(2):
        a = rows[pool * CAPACITY]
        a[:16] = [1, 1, 6, 0, 16, 26, 10, 1, 16, 1, 1, 1, 1, 6, 1, 1]
        a[16:32] = [6, 4, 3] + [1] * 13
        a[32:49] = [10, 1, 3, 1] + [0] * 12 + [1]
        b = rows[pool * CAPACITY + 1]
        if pool == 0:
            b[:16] = [1, 5, 2, 1, 16, 1, 15, 0, 1, 0, 0, 0, 0, 1, 0, 2]
            b[16] = 1
            b[32:34] = [15, 1]
        else:
            b[:16] = [1, 5, 2, 1, 16, 256, 0, 16, 16, 0, 0, 0, 0, 16, 0, 2]
            b[16:32] = [16] * 16
            b[48] = 16
        rows[pool * CAPACITY + 2][:4] = [0, 0, 0, 0xffffffff]
        rows[pool * CAPACITY + 3][:4] = [2, 999, 0, 0xffffffff]
        rows[pool * CAPACITY + 4][:4] = [3, 2, 2, 0]
        rows[pool * CAPACITY + 4][15] = 99
        rows[pool * CAPACITY + 5][:4] = [4, 3, 0, 0]
        rows[pool * CAPACITY + 5][15] = 6
        if metadata_mismatch:
            rows[pool * CAPACITY] = [5, 1, 6, 0] + [0] * 11 + [99] + [0] * 48
    summaries = []
    for pool in range(2):
        per_pool = rows[pool * CAPACITY:(pool + 1) * CAPACITY]
        for level in (None, 0, 1):
            selected = [r for r in per_pool if level is None or (r[0] != 0 and r[3] == level)]
            valid = [r for r in selected if r[0] == 1]
            row = [0] * STRIDE
            row[:4] = [sum(r[0] != 0 for r in selected), sum(r[0] == 0 for r in selected),
                       len(valid), sum(r[0] > 1 for r in selected)]
            for field in list(range(4, 15)) + list(range(16, 49)):
                row[field] = max((r[field] for r in valid), default=0) if field == 8 else sum(r[field] for r in valid)
            summaries.append(row)
    return rows, summaries


def verify_identities(rows):
    for row in rows:
        if row[0] != 1:
            continue
        histogram = row[32:49]
        assert sum(histogram) == row[4]
        assert sum(k * n for k, n in enumerate(histogram)) == row[5]
        assert sum(row[16:32]) == row[5]
        assert histogram[0] == row[6]
        assert row[13] == row[4] - row[6]


def check_binary(path, expected):
    blob = Path(path).read_bytes()
    flattened = [v for row in expected for v in row]
    if len(blob) != len(flattened) * 4:
        raise ValueError(f'{path}: expected {len(flattened) * 4} bytes, got {len(blob)}')
    values = struct.unpack('<' + 'I' * len(flattened), blob)
    errors = [(i // STRIDE, i % STRIDE, a, b) for i, (a, b) in enumerate(zip(values, flattened)) if a != b]
    if errors:
        raise AssertionError(f'{path}: {len(errors)} errors; first (row, field, actual, expected): {errors[:8]}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--write-expected', type=Path)
    parser.add_argument('--pages', type=Path)
    parser.add_argument('--summary', type=Path)
    parser.add_argument('--metadata-mismatch', action='store_true')
    args = parser.parse_args()
    pages, summary = expected_rows(args.metadata_mismatch)
    verify_identities(pages)
    if args.write_expected:
        args.write_expected.mkdir(parents=True, exist_ok=True)
        (args.write_expected / 'capacity-fixture-expected.json').write_text(
            json.dumps(dict(pages=pages, summary=summary), indent=2), encoding='utf-8')
    if args.pages:
        check_binary(args.pages, pages)
    if args.summary:
        check_binary(args.summary, summary)
    print(json.dumps(dict(cpu_identities='pass', gpu_pages='pass' if args.pages else 'not_run',
                         gpu_summary='pass' if args.summary else 'not_run',
                         metadata_mismatch=args.metadata_mismatch)))


if __name__ == '__main__':
    main()
