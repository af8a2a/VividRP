"""Protect same-frame native replay ownership and correlated variance accounting."""
import unittest
from pathlib import Path
import numpy as np

from AnalyzeScreenProbeResolveComponents import decompose
from BuildScreenProbeResolveComponents import build


class ResolveComponentsTests(unittest.TestCase):
    def test_covariance_explains_cancellation(self):
        a=np.array([1.,3.,1.,3.]).reshape(4,1,1);y=np.full_like(a,2.)
        v=decompose(a,y)
        self.assertAlmostEqual(v['uncalibratedVariance'],1)
        self.assertAlmostEqual(v['netCalibrationDeltaVariance'],1)
        self.assertAlmostEqual(v['twiceCovariance'],-2)
        self.assertAlmostEqual(v['calibratedVariance'],0)

    def test_additive_correlated_noise_is_not_counted_as_independent(self):
        a=np.array([1.,3.,1.,3.]).reshape(4,1,1);v=decompose(a,2*a)
        self.assertAlmostEqual(v['calibratedVariance'],4)
        self.assertAlmostEqual(v['twiceCovariance'],2)

    def test_replay_uses_the_native_kernel_and_restores_both_bindings(self):
        source=build(Path('C:/components'))
        self.assertNotIn('__',source)
        self.assertIn('shader.FindKernel("ScreenProbeIntegrateCS")',source)
        self.assertIn('offset:componentWithoutJitter?0:840',source)
        self.assertIn('cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseProbeInput",atlas)',source)
        self.assertIn('cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseOutput",native)',source)
        self.assertIn('componentAaPairs!=count',source)
        self.assertIn('CleanupComponents();CleanupExperimentalTracing();',source)
        self.assertLess(source.index('object qualityPass=null;'),source.index('void ReplayComponents('))

    def test_jitter_counterfactual_restores_the_native_constant(self):
        source=build(Path('C:/components'),True)
        self.assertIn('const bool componentWithoutJitter=true',source)
        self.assertIn('"_SdfScreenProbeIntegrationJitter",mode.aa?1:0',source)
        self.assertIn('"_SdfScreenProbeIntegrationJitter",1',source)
        self.assertIn('componentWithoutJitter?"unjittered":"uncalibrated"',source)


if __name__=='__main__':unittest.main()
