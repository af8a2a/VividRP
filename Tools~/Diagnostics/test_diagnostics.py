import gzip
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import vivid_diagnostics as d


class DiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def csv(self, rows):
        p = self.root / 'stages.csv'
        p.write_text('observation,camera_frame_observed,editor_time_observed,marker,available,gpu_ns,gpu_sample_count\n' + rows,
                     encoding='utf-8')
        return p

    def test_missing_and_unexecuted_are_not_zero_cost(self):
        p = self.csv('0,1,1,A,0,0,0\n1,2,2,A,1,0,0\n2,3,3,A,1,2000000,2\n')
        a = d.summarize(p)['markers']['A']
        self.assertEqual((a['unavailable'], a['no_execution'], a['executed_observations']), (1, 1, 1))
        self.assertEqual(a['p50_ms'], 2)
        self.assertEqual(a['max_sample_count'], 2)

    def test_unavailable_quantile_is_null(self):
        self.assertIsNone(d.summarize(self.csv('0,1,1,A,0,0,0\n'))['markers']['A']['p95_ms'])

    def test_duplicate_observations_rejected(self):
        with self.assertRaises(d.DiagnosticError):
            d.summarize(self.csv('0,1,1,A,1,10,1\n0,1,1,A,1,20,1\n'))

    def test_incomplete_vectors_rejected(self):
        with self.assertRaises(d.DiagnosticError):
            d.summarize(self.csv('0,1,1,A,1,10,1\n0,1,1,B,1,10,1\n1,2,2,A,1,20,1\n'))

    def test_equal_counts_with_different_observation_ids_rejected(self):
        with self.assertRaises(d.DiagnosticError):
            d.summarize(self.csv('0,1,1,A,1,10,1\n1,2,2,B,1,10,1\n'))

    def test_partial_run_rejected(self):
        (self.root / 'result.json').write_text('{"state":"cancelled","success":false}')
        with self.assertRaises(d.DiagnosticError):
            d.summarize(self.root)

    def test_raw_payload_verification_and_corruption(self):
        (self.root / 'result.json').write_text('{"state":"complete","success":true}')
        payload = b'\x00' * 16
        channel = dict(file='shadow.bin.gz', bytes=16, sha256=hashlib.sha256(payload).hexdigest(),
                       storageFormat='RGBAFloat', width=1, height=1, layers=1)
        (self.root / 'capture.json').write_text(json.dumps({'channels': [channel]}))
        with gzip.open(self.root / channel['file'], 'wb') as f:
            f.write(payload)
        self.assertEqual(d.verify_capture(self.root)['verified_channels'], 1)
        with gzip.open(self.root / channel['file'], 'wb') as f:
            f.write(b'\x01' * 16)
        with self.assertRaises(d.DiagnosticError):
            d.verify_capture(self.root)

    def test_unity_failure_is_not_success_when_data_present(self):
        result = subprocess.CompletedProcess([], 1, json.dumps({'success': False, 'data': {'partial': 1}, 'errors': ['offline']}), '')
        with patch.object(d.subprocess, 'run', return_value=result):
            with self.assertRaises(d.DiagnosticError):
                d.unity_call('unity', self.root, ['command', 'x'])

    def test_uint_array_texture_length_checks_all_layers(self):
        payload = bytes(range(16))
        (self.root / 'result.json').write_text('{"state":"complete","success":true}')
        channel = dict(file='pool.gz', bytes=16, sha256=hashlib.sha256(payload).hexdigest(),
                       storageFormat='R32_UInt', width=2, height=1, layers=2)
        with gzip.open(self.root / 'pool.gz', 'wb') as f:
            f.write(payload)
        manifest = self.root / 'capture.json'
        manifest.write_text(json.dumps({'channels': [channel]}))
        self.assertEqual(d.verify_capture(self.root)['verified_channels'], 1)
        channel['layers'] = 1
        manifest.write_text(json.dumps({'channels': [channel]}))
        with self.assertRaises(d.DiagnosticError):
            d.verify_capture(self.root)

    def test_json_argument_is_passed_without_a_shell(self):
        result = subprocess.CompletedProcess([], 0, json.dumps({'success': True, 'data': 'ok'}), '')
        value = '{"path":"A B", "text":"`$(not code)"}'
        with patch.object(d.subprocess, 'run', return_value=result) as run:
            d.unity_call('unity', self.root, ['command', 'vivid_diagnostics', '--request', value])
            self.assertIn(value, run.call_args.args[0])
            self.assertFalse(run.call_args.kwargs['shell'])

    def test_unknown_response_schema_rejected(self):
        with patch.object(d, 'unity_call', return_value={'success': True}):
            with self.assertRaises(d.DiagnosticError):
                d.request('unity', self.root, {'action': 'status'})

    def test_actual_unity_cli_command_envelope(self):
        data = dict(command='vivid_diagnostics', success=True,
                    result=json.dumps(dict(schemaVersion=1, success=True, state='idle')))
        with patch.object(d, 'unity_call', return_value=data):
            self.assertEqual(d.request('unity', self.root, {'action': 'status'})['state'], 'idle')

    def test_project_root_from_nested_package(self):
        (self.root / 'ProjectSettings').mkdir()
        (self.root / 'ProjectSettings/ProjectVersion.txt').touch()
        nested = self.root / 'Packages/My Package/Tools'
        nested.mkdir(parents=True)
        self.assertEqual(d.project_root(nested), self.root)


if __name__ == '__main__':
    unittest.main()
