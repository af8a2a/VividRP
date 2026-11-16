"""Keep native-chain attribution separate from shared-input replay quality."""
import copy
from pathlib import Path
import unittest

import numpy as np

from AnalyzeScreenProbeNativeQuadrature import control_pass,geometry_error
from BuildScreenProbeNativeQuadrature import build,shader_source


class NativeQuadratureTests(unittest.TestCase):
    def test_native_coverage_is_positive_unless_owned_witness_requests_marker(self):
        source=shader_source()
        self.assertIn('134217728u) != 0u ? -covered.a : covered.a',source)
        self.assertNotIn('max(covered.a, 1e-5), -covered.a)',source)
        self.assertIn('67108864u',source)

    def test_original_keeps_persistent_gather_and_controls_install_same_shader(self):
        for variant,flags in (('original',0),('control',0),('candidate',67108864)):
            source=build(Path('C:/native-quadrature'),variant,1733)
            self.assertEqual(source.count('SetupQuality();SetupQuadrature();'),int(variant!='original'))
            self.assertIn('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, '+str(flags)+');',source)
            self.assertIn('CleanupNativeQuadratureWitness();CleanupQuadrature();CleanupExperimentalTracing();',source)
            self.assertIn('count = 128',source)
            self.assertIn('ResetGatherHistoryEpoch();phase=1;frame=0;active=true;',source)
            self.assertEqual(source.count('RecordNativeQuadratureWitness(context,texture);'),int(variant=='candidate'))

    def test_witness_restores_native_output_before_temporal(self):
        source=build(Path('C:/native-quadrature'),'candidate',1733)
        self.assertIn('frame==warmup || frame==warmup+count-1',source)
        self.assertIn('finally {\n            cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseOutput",native);',source)
        self.assertIn('cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32|67108864);',source)
        self.assertIn('ReadQuality(context,texture,index==0?"resolved":"temporal","pixel");',source)

    def test_position_bound_never_allows_normal_or_validity_changes(self):
        a=np.zeros((2,3,4),np.float32);b=a.copy();b[0,0,0]=1e-6
        self.assertLess(geometry_error(a,b,2e-6),2e-6)
        b[0,0,0]=3e-6
        with self.assertRaisesRegex(AssertionError,'world-position'):geometry_error(a,b,2e-6)
        b=a.copy();b[0,0,3]=-1
        with self.assertRaisesRegex(AssertionError,'normal/validity'):geometry_error(a,b,2e-6)

    def test_failed_control_blocks_mean_or_noise_attribution(self):
        gates=dict(controlMeanChangeAbsPercent=.1,controlVarianceChangeAbsPercent=2)
        good=dict(inputsQualified=True,regions=dict(ceiling=dict(comparison=dict(mean_change_percent=0,
            variance_reduction_percent=0),rgbMeanChangePercent=[0,0,0])))
        self.assertTrue(control_pass(good,gates))
        for key,value in (('mean_change_percent',.11),('variance_reduction_percent',2.1),('variance_reduction_percent',-2.1)):
            bad=copy.deepcopy(good);bad['regions']['ceiling']['comparison'][key]=value
            self.assertFalse(control_pass(bad,gates))
        bad=copy.deepcopy(good);bad['regions']['ceiling']['rgbMeanChangePercent'][0]=.11
        self.assertFalse(control_pass(bad,gates))
        self.assertFalse(control_pass(dict(inputsQualified=False),gates))


if __name__=='__main__':unittest.main()
