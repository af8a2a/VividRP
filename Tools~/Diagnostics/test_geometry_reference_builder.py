"""Exact receiver input provenance and immutable reference captures."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import numpy as np


class GeometryReferenceBuilderTests(unittest.TestCase):
    def test_exact_geometry_and_written_shader_are_pinned(self):
        script=Path(__file__).with_name("BuildScreenProbeGeometryReference.py")
        with tempfile.TemporaryDirectory(prefix="vivid-geometry-reference-") as temporary:
            root=Path(temporary);geometry=root/"geometry.bin";output=root/"reference"
            values=np.zeros((136,120,4),dtype="<f4");values[...,3]=-1;values[:68,46:74,0]=.125
            geometry.write_bytes(values.tobytes())
            args=[sys.executable,"-X","utf8","-B",str(script),str(output),"--geometry",str(geometry),"--trace-mode","SdfOnly"]
            p=subprocess.run(args,capture_output=True,text=True,encoding="utf-8")
            self.assertEqual(p.returncode,0,p.stderr)
            protocol=json.loads((output/"protocol.json").read_text(encoding="utf-8"))
            self.assertEqual(protocol["baseGeometrySha256"],hashlib.sha256(values[:68].tobytes()).hexdigest())
            self.assertEqual(protocol["tracingSourceSha256"],hashlib.sha256((output/"reference-tracing.compute.txt").read_bytes()).hexdigest())
            self.assertEqual(geometry.read_bytes(),(output/"geometry-input.bin").read_bytes())
            source=(output/"capture-script.cs").read_text(encoding="utf-8")
            self.assertNotIn("__",source)
            self.assertIn("gi.traceMode.Override(SdfDiffuseTraceMode.SdfOnly)",source)
            self.assertIn("CleanupReference(); volume.profile",source)
            before={p.name:p.read_bytes() for p in output.iterdir()}
            p=subprocess.run(args,capture_output=True,text=True,encoding="utf-8")
            self.assertNotEqual(p.returncode,0)
            self.assertEqual(before,{p.name:p.read_bytes() for p in output.iterdir()})

    def test_reject_wrong_geometry_shape_before_output(self):
        script=Path(__file__).with_name("BuildScreenProbeGeometryReference.py")
        with tempfile.TemporaryDirectory(prefix="vivid-geometry-reference-invalid-") as temporary:
            root=Path(temporary);geometry=root/"bad.bin";geometry.write_bytes(b"bad")
            p=subprocess.run([sys.executable,"-X","utf8","-B",str(script),str(root/"output"),"--geometry",str(geometry),"--trace-mode","Hybrid"],capture_output=True,text=True,encoding="utf-8")
            self.assertNotEqual(p.returncode,0)
            self.assertFalse((root/"output").exists())

    def test_hybrid_contract_is_explicit_in_source_and_protocol(self):
        from BuildScreenProbeGeometryReference import build
        with tempfile.TemporaryDirectory(prefix="vivid-geometry-reference-hybrid-") as temporary:
            root=Path(temporary);geometry=root/"geometry.bin"
            geometry.write_bytes(np.zeros((136,120,4),dtype="<f4").tobytes())
            source,_,_=build(root/"out",geometry,"Hybrid")
            self.assertIn("gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid)",source)
            self.assertIn('Field("m_TraceMode")!=SdfDiffuseTraceMode.Hybrid',source)
            self.assertIn('traceMode = Field("m_TraceMode").ToString()',source)
            self.assertNotIn("__",source)

    def test_native_mode_control_retains_original_program_and_both_arms(self):
        from BuildScreenProbeTraceModeConformance import build
        source=build(Path("C:/reference"))
        self.assertNotIn("__",source)
        self.assertIn('currentArm=="hybrid"?1:0',source)
        self.assertIn('warmup = 512, count = 64',source)
        self.assertIn('blockFrames=64,blocks=2',source)
        self.assertIn('SdfDiffuseTraceMode.SdfOnly);await Run("sdf-only",7919)',source)
        self.assertIn('SdfDiffuseTraceMode.Hybrid);await Run("hybrid",7919)',source)
        self.assertNotIn('SetValue(referencePass,referenceShader)',source)


if __name__=="__main__":
    unittest.main()
