#!/usr/bin/env python3
"""Reusable VividRP diagnostics over the official Unity CLI; stdlib only."""
import argparse
import csv
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import time


class DiagnosticError(Exception):
    pass


def project_root(path):
    path = Path(path).resolve()
    for candidate in (path, *path.parents):
        if (candidate / 'ProjectSettings/ProjectVersion.txt').is_file():
            return candidate
    raise DiagnosticError('Pass --project-path pointing to a Unity project.')


def unity_call(executable, project, arguments, timeout=30):
    command = [executable, *arguments, '--project-path', str(project), '--format', 'json',
               '--no-banner', '--no-pager', '--non-interactive']
    try:
        result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8',
                                timeout=timeout, shell=False)
        envelope = json.loads(result.stdout)
    except (OSError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
        raise DiagnosticError(f'Unity CLI transport failed: {exc}') from exc
    if result.returncode or envelope.get('success') is not True:
        raise DiagnosticError(json.dumps(envelope.get('errors', envelope), ensure_ascii=False))
    return envelope.get('data')


def request(executable, project, payload):
    data = unity_call(executable, project, ['command', 'vivid_diagnostics', '--request', json.dumps(payload)])
    # Unity CLI wraps the Pipeline result in data.command/parameters/result/target.
    # Validate this boundary before decoding the command's JSON string return.
    if not isinstance(data, dict) or data.get('command') != 'vivid_diagnostics' or data.get('success') is not True:
        raise DiagnosticError('Unity command envelope did not succeed.')
    data = data.get('result')
    if isinstance(data, str):
        data = json.loads(data)
    if not isinstance(data, dict) or data.get('schemaVersion') != 1:
        raise DiagnosticError('Unexpected VividRP response schema; inspect unity command --query vivid_diagnostics.')
    if data.get('success') is not True and data.get('state') not in ('cancelled',):
        raise DiagnosticError(data.get('error') or 'Diagnostic failed.')
    return data


def quantile(values, q):
    values = sorted(values)
    position = (len(values) - 1) * q
    lo = int(position)
    hi = min(lo + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (position - lo)


def summarize(path):
    path = Path(path)
    if path.is_dir():
        result = json.loads((path / 'result.json').read_text(encoding='utf-8-sig'))
        if result.get('state') != 'complete' or result.get('success') is not True:
            raise DiagnosticError('Run is incomplete; partial captures are not a completed baseline.')
        path = path / 'stages.csv'
    groups = {}
    seen = set()
    with path.open(encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream):
            key = (int(row['observation']), row['marker'])
            if key in seen:
                raise DiagnosticError(f'Duplicate observation/marker: {key}')
            seen.add(key)
            entry = groups.setdefault(row['marker'], {'observations': 0, 'unavailable': 0,
                                                       'no_execution': 0, 'values': [], 'max_sample_count': 0})
            entry['observations'] += 1
            available, count, ns = int(row['available']), int(row['gpu_sample_count']), int(row['gpu_ns'])
            if available not in (0, 1) or count < 0 or ns < 0:
                raise DiagnosticError('Invalid recorder sample.')
            if not available:
                entry['unavailable'] += 1
            elif count == 0:
                entry['no_execution'] += 1
            else:
                entry['values'].append(ns / 1_000_000)
                entry['max_sample_count'] = max(entry['max_sample_count'], count)
    if not groups:
        raise DiagnosticError('No observations in CSV.')
    observation_ids = {observation for observation, _ in seen}
    if len(seen) != len(observation_ids) * len(groups):
        raise DiagnosticError('Incomplete marker vectors.')
    for entry in groups.values():
        values = entry.pop('values')
        entry['executed_observations'] = len(values)
        entry['p50_ms'] = statistics.median(values) if values else None
        entry['p95_ms'] = quantile(values, .95) if values else None
        entry['p99_ms'] = quantile(values, .99) if values else None
    return {'schemaVersion': 1, 'source': str(path.resolve()), 'markers': groups,
            'scope': 'GPU samples are delayed and include all cameras. Observation timestamps are not GPU frame identities. '
                     'No-execution/unavailable samples are excluded, not zero. Parent/child scopes and their quantiles must not be summed.'}


def verify_capture(folder):
    folder = Path(folder).resolve()
    result = json.loads((folder / 'result.json').read_text(encoding='utf-8-sig'))
    if result.get('state') != 'complete' or result.get('success') is not True:
        raise DiagnosticError('Capture did not complete.')
    manifest = json.loads((folder / 'capture.json').read_text(encoding='utf-8-sig'))
    channels = manifest.get('channels', [])
    if not channels:
        raise DiagnosticError('Capture has no channels.')
    names = set()
    for channel in channels:
        file = (folder / channel['file']).resolve()
        if file.parent != folder or file.name in names:
            raise DiagnosticError('Invalid or duplicate channel path.')
        names.add(file.name)
        digest, size = hashlib.sha256(), 0
        with gzip.open(file, 'rb') as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b''):
                digest.update(block)
                size += len(block)
        if digest.hexdigest() != channel['sha256'] or size != channel['bytes']:
            raise DiagnosticError(f'Payload hash/length mismatch: {file.name}')
        if channel['storageFormat'] == 'raw-buffer' and size != channel['count'] * channel['stride']:
            raise DiagnosticError(f'Buffer descriptor mismatch: {file.name}')
        if channel['storageFormat'] == 'RGBAFloat' and size != channel['width'] * channel['height'] * channel['layers'] * 16:
            raise DiagnosticError(f'Texture descriptor mismatch: {file.name}')
        if channel['storageFormat'] == 'R32_UInt' and size != channel['width'] * channel['height'] * channel['layers'] * 4:
            raise DiagnosticError(f'UInt texture descriptor mismatch: {file.name}')
    return {'schemaVersion': 1, 'verified_channels': len(channels), 'scope': 'Decoded bytes, SHA256 and supported descriptor lengths; not shadow correctness.'}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project-path', default=str(Path(__file__).resolve().parents[2]))
    parser.add_argument('--unity', default='unity', help='Unity CLI executable, not Unity.exe')
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('doctor')
    sub.add_parser('cameras')
    status = sub.add_parser('status'); status.add_argument('--job-id')
    cancel = sub.add_parser('cancel'); cancel.add_argument('--job-id', required=True)
    for name in ('snapshot', 'profile', 'capture'):
        p = sub.add_parser(name)
        p.add_argument('--camera-id', help='Camera component ID as an exact decimal string (not its GameObject ID)')
        p.add_argument('--output-directory')
        if name != 'snapshot':
            p.add_argument('--warmup', type=int, default=64)
            p.add_argument('--timeout', type=float, default=120)
            p.add_argument('--wait', action='store_true')
            p.add_argument('--repaint', action='store_true', help='Request existing Editor views to repaint; recorded in request metadata')
        if name == 'profile':
            p.add_argument('--frames', type=int, default=128)
            p.add_argument('--marker', action='append', help='Repeat for custom Render GPU markers; default VSM stages')
        if name == 'capture':
            p.add_argument('--include-depth-pools', action='store_true', help='Also read the complete static/dynamic layered pools (large)')
    for name in ('summarize', 'verify-capture'):
        sub.add_parser(name).add_argument('path')
    args = parser.parse_args(argv)
    try:
        if args.command == 'summarize':
            data = summarize(args.path)
        elif args.command == 'verify-capture':
            data = verify_capture(args.path)
        else:
            executable = shutil.which(args.unity)
            if not executable:
                raise DiagnosticError('Unity CLI not found. Install/configure it explicitly; no Editor is launched automatically.')
            project = project_root(args.project_path)
            if args.command == 'doctor':
                data = unity_call(executable, project, ['command', '--query', 'vivid_diagnostics', '--detail', 'compact'])
                if not any(c.get('name') == 'vivid_diagnostics' for c in data.get('commands', [])):
                    raise DiagnosticError('VividRP command is not registered. Reimport the package; check com.unity.pipeline and compilation errors.')
            else:
                payload = {'action': args.command}
                for field, key in [('camera_id', 'cameraId'), ('output_directory', 'outputDirectory'),
                                   ('job_id', 'jobId'), ('warmup', 'warmupFrames'), ('timeout', 'timeoutSeconds'),
                                   ('frames', 'frames'), ('marker', 'markers'), ('include_depth_pools', 'includeDepthPools'), ('repaint', 'repaintViews')]:
                    value = getattr(args, field, None)
                    if value is not None:
                        payload[key] = value
                data = request(executable, project, payload)
                if getattr(args, 'wait', False):
                    deadline = time.monotonic() + args.timeout + 10
                    job_id = data['jobId']
                    try:
                        while data['state'] in ('warming', 'sampling', 'readback'):
                            if time.monotonic() >= deadline:
                                raise DiagnosticError(f'Wait expired; job {job_id} may still be active. Query status or cancel explicitly.')
                            time.sleep(1)
                            data = request(executable, project, {'action': 'status', 'jobId': job_id})
                    except KeyboardInterrupt:
                        raise DiagnosticError(f'Wait interrupted; Editor job {job_id} remains bounded by its timeout. Use cancel --job-id.')
        print(json.dumps({'success': True, 'data': data}, ensure_ascii=False, indent=2))
        return 0
    except (DiagnosticError, OSError, ValueError, KeyError) as exc:
        print(json.dumps({'success': False, 'error': str(exc)}, ensure_ascii=False))
        return 1


if __name__ == '__main__':
    sys.exit(main())
