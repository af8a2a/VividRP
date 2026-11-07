"""Inspect native postprocessed irradiance.shl2 (not the padded raw radiance files)."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct


def measure(path: Path):
    data = path.read_bytes()
    if len(data) < 8:
        raise ValueError(f"Missing SH header: {path}")
    count, = struct.unpack_from("<Q", data)
    if len(data) != 8 + count * 27 * 4:
        raise ValueError(f"Unexpected postprocessed SH layout: {path}")
    l0 = []
    for coefficients in struct.iter_unpack("<27f", data[8:]):
        if not all(math.isfinite(value) for value in coefficients):
            raise ValueError(f"Non-finite SH: {path}")
        l0.append((coefficients[0], coefficients[9], coefficients[18]))
    return {
        "path": str(path.resolve()),
        "probes": count,
        "colored_l0_above_0_01": sum(max(rgb) - min(rgb) > .01 for rgb in l0),
        "max_l0_rgb": list(map(max, zip(*l0))) if count else [],
        "sha256": hashlib.sha256(data).hexdigest(),
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", type=Path, nargs="+")
    print(json.dumps([measure(path) for path in parser.parse_args().files], indent=2))
