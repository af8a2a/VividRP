"""Never replace bit-exact receiver matching with a positional tolerance."""
import unittest

import numpy as np

from AnalyzeScreenProbeGeometryReference import target_lookup,match_target_geometry,validate_variant_contracts,reference_conflicts,runtime_source_identity


class GeometryReferenceMatchingTests(unittest.TestCase):
    def fixture(self):
        geometry=np.zeros((60,74,4),dtype="<f4")
        geometry[59,46:74,0]=.25
        blocks=np.zeros((32,60,74,5))
        blocks[:,59,46:74,4]=np.arange(28)+1
        return geometry,blocks

    def test_variants_join_by_probe_and_exact_words(self):
        geometry,blocks=self.fixture();other=geometry.copy();values=blocks.copy()
        other[59,46,0]=np.nextafter(other[59,46,0],np.float32(np.inf))
        values[:,59,46,4]=9
        lookup=target_lookup([({},geometry,blocks),({},other,values)])
        self.assertEqual(len(lookup),29)
        missing,result=match_target_geometry(lookup,other)
        self.assertEqual(missing,[])
        self.assertTrue(np.all(result[:,0]==9))
        self.assertTrue(np.all(result[:,1]==2))

    def test_unseen_single_ulp_is_rejected(self):
        geometry,blocks=self.fixture();lookup=target_lookup([({},geometry,blocks)])
        changed=geometry.copy();changed[59,46,0]=np.nextafter(changed[59,46,0],np.float32(np.inf))
        missing,result=match_target_geometry(lookup,changed)
        self.assertEqual(missing,[46]);self.assertIsNone(result)

    def test_signed_zero_is_a_different_geometry_word(self):
        geometry,blocks=self.fixture();lookup=target_lookup([({},geometry,blocks)])
        changed=geometry.copy();changed[59,46,1]=-0.0
        missing,_=match_target_geometry(lookup,changed)
        self.assertEqual(missing,[46])

    def test_same_receiver_cannot_hide_inconsistent_reference_blocks(self):
        geometry,blocks=self.fixture();changed=blocks.copy();changed[0,59,46,4]+=1e-5
        with self.assertRaisesRegex(AssertionError,"inconsistent reference"):
            target_lookup([({},geometry,blocks),({},geometry,changed)])
        entries,conflicts=reference_conflicts([({},geometry,blocks),({},geometry,changed)])
        self.assertEqual(entries,28)
        self.assertEqual(conflicts[0]["probeX"],46)
        self.assertEqual(conflicts[0]["blocks"],[0])
        self.assertEqual(conflicts[0]["variants"],[0,1])

    def test_reference_variants_cannot_mix_modes_or_programs(self):
        report=dict(traceMode="Hybrid",runtimeSourceSha256="a",tracingPrograms=[1],inputs={},vp=[],normalSha256="b")
        for key,new in (("traceMode","SdfOnly"),("runtimeSourceSha256","c"),("tracingPrograms",[2])):
            with self.subTest(key=key):
                changed={**report,key:new}
                with self.assertRaisesRegex(AssertionError,"Incompatible reference "+key):
                    validate_variant_contracts([(report,None,None),(changed,None,None)])

    def test_source_identity_ignores_diagnostic_metadata_but_tracks_shader_inputs(self):
        shader=dict(path="Shaders/a.hlsl",sha256="a")
        runtime=dict(path="Runtime/a.cs",sha256="b")
        base=runtime_source_identity(dict(files=[shader,runtime]))
        self.assertEqual(base,runtime_source_identity(dict(files=[dict(path="Tools~/different.py",sha256="c"),runtime,shader])))
        self.assertNotEqual(base,runtime_source_identity(dict(files=[{**shader,"sha256":"d"},runtime])))


if __name__=="__main__":
    unittest.main()
