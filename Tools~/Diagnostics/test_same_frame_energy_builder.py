"""Protect exact original-shader AA and private-output ownership for references."""
import unittest
from pathlib import Path
import numpy as np

from BuildScreenProbeSameFrameEnergy import build
from AnalyzeScreenProbeSameFrameEnergy import compare,reason_fractions,black_boundary_margin


class SameFrameEnergyBuilderTests(unittest.TestCase):
    def test_reference_retraces_screen_rays_and_restores_original_bindings(self):
        source,shaders=build(Path("C:/energy"),1733,7919)
        self.assertNotIn("__",source)
        self.assertIn('energySets=16,energySeed=7919',source)
        self.assertIn('EditorUtility.IsPersistent(shader)',source)
        self.assertIn('Original trace energy AA mismatch',source)
        self.assertIn('SetComputeIntParam(shader,"_SdfDirectionalGuideEnabled",0)',source)
        self.assertIn('SetComputeIntParam(shader,"_SdfDirectionalGuideEnabled",nativeGuide)',source)
        self.assertIn('SetComputeIntParam(shader,"_SdfDiffuseFrame",nativeFrame)',source)
        self.assertIn('SetComputeTextureParam(shader,kernel,"_SdfScreenRays",PrivateResolve',source)
        self.assertIn('cmd.DispatchCompute(shader,screenKernel,15,17,1);\n                cmd.DispatchCompute(shader,kernel,15,17,1);',source)
        self.assertNotIn('SetValue(privatePass',source)
        self.assertIn('v.rgb*v.a',shaders['energy-accumulator.compute.txt'])
        self.assertLess(source.index('var energyRegions='),source.index('privateTemporalHook='))
        self.assertLess(source.index('var privateOwned='),source.index('RenderTexture EnergyTexture('))

    def test_budget_and_sample_count_are_explicit(self):
        source,_=build(Path("C:/energy"),9481,25387,1,64)
        self.assertIn('energySets=1,energySeed=25387',source)
        self.assertIn('warmup = 1024, count = 64',source)
        with self.assertRaises(AssertionError):build(Path("C:/energy"),9481,25387,0)

    def test_reason_mode_owns_full_diagnostics_and_restores_native_flag(self):
        source,shaders=build(Path("C:/energy"),1733,7919,16,64,True)
        self.assertIn('energyRecordReasons=true',source)
        self.assertIn('new RenderTextureDescriptor(960,136)',source)
        self.assertIn('PrivateField("m_RecordRayDiagnostics")?1:0',source)
        self.assertTrue(shaders['energy-accumulator.compute.txt'].startswith('#define ENERGY_RECORD_REASONS 1'))

    def test_energy_drift_and_residual_are_reported_separately(self):
        reference=np.full((64,2),10.0)
        baseline=reference+np.array([1.,-1.])
        candidate=reference+np.array([2.,-2.])
        result=compare(reference,baseline,candidate)
        self.assertEqual(result['meanChangePercent'],0)
        self.assertEqual(result['meanMapMseChangePercent'],300)

    def test_ray_reason_audit_rejects_missing_rays_and_coverage_mismatch(self):
        a=np.array([[[1.,1.,1.,1.]]]);b=np.array([[[1.,2.,1.,8.]]])
        report=reason_fractions(a,b,np.array([[.25]]),1)
        self.assertEqual(sum(report.values()),1)
        self.assertEqual(report['accepted'],.25)
        with self.assertRaises(AssertionError):reason_fractions(a,b,np.array([[.5]]),1)
        b[...,3]=7
        with self.assertRaises(AssertionError):reason_fractions(a,b,np.array([[.25]]),1)

    def test_black_boundary_proof_requires_range_beyond_every_corner_and_bias(self):
        point=np.array([[1.,-1.,2.,0.]])
        low=np.array([-2.,-3.,-4.]);high=np.array([5.,6.,7.])
        corners=np.array([[x,y,z] for x in (low[0],high[0]) for y in (low[1],high[1]) for z in (low[2],high[2])])
        far=np.linalg.norm(corners-point[0,:3],axis=1).max()
        self.assertLess(black_boundary_margin(point,low,high,far,.04)[0],0)
        self.assertGreater(black_boundary_margin(point,low,high,far+.1,.04)[0],0)


if __name__=="__main__":unittest.main()
