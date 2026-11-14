"""Inventory GI evidence; prune explicitly archived frame readbacks, preserving reports."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat


FRAME = re.compile(r"^(\d{4,6})-.+\.bin$", re.IGNORECASE)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def no_link(path):
    for part in (path, *path.parents):
        info = part.lstat()
        if part.is_symlink() or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Refusing linked evidence path: " + str(part))


def files_under(root):
    no_link(root)
    for directory, children, files in os.walk(root, followlinks=False):
        for name in children + files:
            path = Path(directory) / name
            info = path.lstat()
            if path.is_symlink() or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Refusing linked evidence path: " + str(path))
        for name in files:
            yield Path(directory) / name


def write(path, value):
    # Atomic metadata publication; no directory deletion or traversal through links.
    no_link(path.parent)
    if path.exists():
        no_link(path)
    temporary = path.with_name(path.name + ".pending")
    if temporary.exists():
        raise ValueError("Unfinished retention journal: " + str(temporary))
    with temporary.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
    temporary.replace(path)


def inventory(root):
    rows = []
    for run in sorted(root.iterdir()):
        no_link(run)
        if run.is_dir():
            paths = list(files_under(run))
            rows.append(dict(run=run.name, bytes=sum(p.stat().st_size for p in paths), files=len(paths)))
    usage = shutil.disk_usage(root)
    return dict(root=str(root), utc=now(), bytes=sum(r["bytes"] for r in rows),
                freeBytes=usage.free, runs=sorted(rows, key=lambda r: r["bytes"], reverse=True))


def plan_runs(root, policy):
    protected = set(policy["protectedRuns"])
    result = []
    seen = set()
    for entry in policy["archiveRuns"]:
        name = entry["run"]
        if name in seen or name in protected or Path(name).name != name or name in (".", "..") or "/" in name or "\\" in name:
            raise ValueError("Unsafe, duplicate or protected run: " + name)
        seen.add(name)
        run = root / name
        no_link(run)
        if run.resolve() != run.absolute() or run.parent != root or not entry["reason"].strip():
            raise ValueError("Invalid archive boundary: " + str(run))
        evidence = []
        if not entry.get("requiredFiles"):
            raise ValueError("Archive reason requires existing evidence files: " + name)
        for relative in entry["requiredFiles"]:
            path = run / relative
            if path.resolve() != path.absolute() or not path.resolve().is_relative_to(run):
                raise ValueError("Archive evidence escaped the run: " + str(path))
            no_link(path)
            with path.open("rb") as source:
                evidence.append(dict(path=relative, sha256=hashlib.file_digest(source, "sha256").hexdigest()))
        groups = {}
        for path in files_under(run):
            match = FRAME.fullmatch(path.name)
            if match:
                groups.setdefault(path.parent, []).append((int(match[1]), path))
        removed = []
        kept = 0
        for group in groups.values():
            # Keep complete first/last frame bundles in every arm/subdirectory.
            endpoints = {min(f for f, _ in group), max(f for f, _ in group)}
            for frame, path in sorted(group):
                if frame in endpoints:
                    kept += 1
                    continue
                info = path.stat()
                removed.append(dict(path=str(path.relative_to(run)), bytes=info.st_size, mtimeNs=info.st_mtime_ns))
        result.append(dict(run=name, reason=entry["reason"], bytes=sum(p["bytes"] for p in removed),
                           files=len(removed), keptFrameFiles=kept, archiveEvidence=evidence, paths=removed))
    return result


def prune(root, plans):
    summaries = []
    for plan in plans:
        if not plan["paths"]:
            continue
        run = root / plan["run"]
        journal = run / "raw-retention.json"
        file_journal = run / "raw-retention-files.jsonl"
        # Never overwrite an earlier deletion audit; reruns have no new candidates.
        if journal.exists() or file_journal.exists():
            raise ValueError("Run already has a retention audit: " + str(run))
        audit = dict(schema="gi-raw-retention.v1", utc=now(), complete=False, reason=plan["reason"],
                     archiveEvidence=plan["archiveEvidence"],
                     rawReanalysisAvailable=False, kept="All non-frame files and first/last frame bundles per directory",
                     plannedFiles=plan["files"], plannedBytes=plan["bytes"], removedFiles=0, freedBytes=0,
                     fileJournal=file_journal.name, errors=[])
        write(journal, audit)
        try:
            with file_journal.open("x", encoding="utf-8") as stream:
                # Publish each batch before unlink; append instead of rewriting a growing log.
                for start in range(0, len(plan["paths"]), 128):
                    batch = []
                    for record in plan["paths"][start:start+128]:
                        path = run / record["path"]
                        if path.resolve() != path.absolute() or not path.resolve().is_relative_to(run) or not FRAME.fullmatch(path.name):
                            raise ValueError("Raw path escaped the run: " + str(path))
                        no_link(path)
                        info = path.stat()
                        if info.st_size != record["bytes"] or info.st_mtime_ns != record["mtimeNs"]:
                            raise ValueError("Evidence changed after planning: " + str(path))
                        with path.open("rb") as raw:
                            digest = hashlib.file_digest(raw, "sha256").hexdigest()
                        batch.append(dict(**record, sha256=digest))
                    stream.write(json.dumps(dict(batch=start, deletion="planned", files=batch)) + "\n")
                    stream.flush()
                    os.fsync(stream.fileno())
                    for record in batch:
                        path = run / record["path"]
                        no_link(path)
                        info = path.stat()
                        if info.st_size != record["bytes"] or info.st_mtime_ns != record["mtimeNs"]:
                            raise ValueError("Evidence changed while journaling: " + str(path))
                        path.unlink()
                        audit["removedFiles"] += 1
                        audit["freedBytes"] += record["bytes"]
                    stream.write(json.dumps(dict(batch=start, deletion="complete")) + "\n")
                    stream.flush()
            audit["complete"] = True
        except Exception as error:
            audit["errors"].append(str(error))
            raise
        finally:
            write(journal, audit)
        summaries.append(dict(run=plan["run"], removedFiles=plan["files"], freedBytes=plan["bytes"], audit=str(journal)))
        print(json.dumps(summaries[-1]), flush=True)
    return summaries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("E:/VividRP_GI_Evidence"))
    parser.add_argument("--policy", type=Path)
    parser.add_argument("--apply", action="store_true", help="Delete only planned interior frame .bin readbacks")
    args = parser.parse_args()
    root = args.root.absolute()
    no_link(root)
    if root == Path(root.anchor) or not root.is_dir():
        raise ValueError("Expected an explicit evidence directory, not a drive root.")
    if not args.policy:
        if args.apply:
            raise ValueError("An explicit archival policy is required.")
        print(json.dumps(inventory(root), indent=2))
        return
    policy = json.loads(args.policy.read_text(encoding="utf-8"))
    if Path(policy["root"]).absolute() != root:
        raise ValueError("Policy root does not match the requested directory.")
    plans = plan_runs(root, policy)
    print(json.dumps(dict(dryRun=not args.apply, root=str(root), plannedBytes=sum(p["bytes"] for p in plans),
        runs=[{k: v for k, v in p.items() if k != "paths"} for p in plans]), indent=2), flush=True)
    if args.apply:
        summaries = prune(root, plans)
        after = inventory(root)
        summary = dict(schema="gi-retention-summary.v1", utc=now(), runs=summaries,
            freedBytes=sum(r["freedBytes"] for r in summaries), evidenceBytes=after["bytes"], freeBytes=after["freeBytes"],
            softLimitGiB=policy["softLimitGiB"], minimumFreeGiB=policy["minimumFreeGiB"],
            needsFurtherReview=after["bytes"] > policy["softLimitGiB"] * 2**30 or after["freeBytes"] < policy["minimumFreeGiB"] * 2**30)
        write(root / ("retention-" + datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d-%H%M%S-%f") + ".json"), summary)
        print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
