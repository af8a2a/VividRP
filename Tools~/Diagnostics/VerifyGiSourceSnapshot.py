"""Verify a pre-capture source/fixture hash list before changing or promoting it."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


def create(snapshot, workspace, extra, inputs=(), scripts=()):
    assert not snapshot.exists(), "Refusing to overwrite a pre-capture snapshot"
    tracked = subprocess.check_output(["git", "ls-files", "--", "Runtime", "Editor", "Shaders",
                                      "Samples/SurfaceCacheCornellBox/EmissiveCeiling"], cwd=workspace, text=True).splitlines()
    files = []
    for relative in sorted(set(tracked + extra)):
        path = (workspace / relative).resolve()
        assert path.is_relative_to(workspace.resolve()) and path.is_file()
        files.append(dict(path=path.relative_to(workspace.resolve()).as_posix(), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    def external(paths):
        return [dict(path=str(Path(p).resolve()), sha256=hashlib.sha256(Path(p).read_bytes()).hexdigest()) for p in paths]
    result = dict(utc=datetime.now(timezone.utc).isoformat(), files=files,
                  inputs=external(inputs), scripts=external(scripts))
    snapshot.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(dict(snapshot=str(snapshot), checkedFiles=len(files))))


def verify(snapshot, workspace, evidence):
    before = json.loads(snapshot.read_text(encoding="utf-8"))
    files = before["files"]
    assert files and len({v["path"] for v in files}) == len(files)
    changed = []
    for row in files:
        path = (workspace / row["path"]).resolve()
        assert path.is_relative_to(workspace.resolve()), "Source path escapes workspace"
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
            changed.append(row["path"])
    for kind in ("inputs", "scripts"):
        for row in before.get(kind, []):
            path = Path(row["path"])
            if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != row["sha256"]:
                changed.append(kind + ":" + row["path"])
    target = evidence / "source-before-capture.json"
    if target.exists():
        assert target.read_bytes() == snapshot.read_bytes(), "Refusing to overwrite another snapshot"
    else:
        shutil.copyfile(snapshot, target)
    result = dict(checkedFiles=len(files), files=len(files), inputs=len(before.get("inputs", [])),
                  scripts=len(before.get("scripts", [])), unchanged=not changed, changedSources=changed,
                  utc=datetime.now(timezone.utc).isoformat())
    (evidence / "source-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    assert not changed, "Capture source or fixture changed"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("snapshot", type=Path)
    parser.add_argument("evidence", type=Path, nargs="?")
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--create", action="store_true")
    parser.add_argument("--include", action="append", default=[])
    parser.add_argument("--input", action="append", default=[], help="External reference input to pin before capture")
    parser.add_argument("--script", action="append", default=[], help="Actual external executed script to pin before capture")
    args = parser.parse_args()
    if args.create:
        create(args.snapshot, args.workspace, args.include, args.input, args.script)
    else:
        assert args.evidence is not None, "Verification needs an evidence directory"
        verify(args.snapshot, args.workspace, args.evidence)
