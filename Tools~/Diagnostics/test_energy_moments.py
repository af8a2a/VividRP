"""Check the consumed-energy moment contract and owned native installation."""
from pathlib import Path
import unittest

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_cold_energy_moments
from BuildScreenProbePixelOracleCapture import build
from CornellLambertReference import Scene
from ScreenProbeEnergyMotionReference import boundary_receiver


class EnergyMomentTests(unittest.TestCase):
    def test_coverage_rgb_covariance_is_included_in_observation_variance(self):
        coverage=np.array([.25,.5,.75,1.]);rgb=6./coverage
        self.assertGreater(float(np.var(np.log2(1+rgb))),.1)
        self.assertEqual(float(np.var(np.log2(1+rgb*coverage))),0.)

    def test_cold_moment_check_rejects_conditional_domain_and_metadata_changes(self):
        temporal=np.array([[[6.,6.,6.,.5]]])
        baseline=np.array([[[np.float16(np.log2(7)),0.,1.,.5]]],float)
        candidate=np.array([[[np.float16(np.log2(4)),0.,1.,.5]]],float)
        qualify_cold_energy_moments(temporal,baseline,candidate)
        with self.assertRaisesRegex(AssertionError,'domain mismatch'):qualify_cold_energy_moments(temporal,baseline,baseline)
        bad=candidate.copy();bad[...,2]=2
        with self.assertRaisesRegex(AssertionError,'age/coverage'):qualify_cold_energy_moments(temporal,baseline,bad)

    def test_relighting_log_jacobian_matches_linear_energy_scaling(self):
        energy=3.7;scale=4.;x=np.log2(1+energy);step=1e-5
        transform=lambda q:np.log2(1+scale*(2**q-1))
        derivative=(transform(x+step)-transform(x-step))/(2*step)
        self.assertAlmostEqual(derivative,scale*(1+energy)/(1+scale*energy),places=8)

    def test_native_and_private_share_new_temporal_but_control_stays_original(self):
        source,shaders=build(Path('C:/energy'),covered_wavelet=True,energy_moments=True)
        self.assertIn('privateTemporalCandidate=PrivateShader("energy-temporal.compute.txt"',source)
        self.assertIn('field.SetValue(privatePass,privateTemporalCandidate);energyTemporalInstalled=true',source)
        self.assertIn('CleanupEnergyMoments();oraclePixelMap?.Dispose();CleanupPrivateReplay()',source)
        self.assertIn('path==1 && privateTemporalCandidate!=null?privateTemporalCandidate:privateTemporal',source)
        self.assertIn('history.momentCoverage = momentWeight / max(history.footprint, 1e-5)',shaders['energy-temporal.compute.txt'])
        self.assertIn('ScreenProbeLogLuminance(result.rgb * result.a)',shaders['energy-temporal.compute.txt'])
        self.assertNotIn('ScreenProbeLogLuminance(result.rgb * result.a)',shaders['temporal.compute.txt'])
        self.assertIn('SdfAtrousLogLuminance(center.rgb * abs(center.a))',shaders['covered-wavelet.compute.txt'])
        self.assertNotIn('SdfAtrousLogLuminance(center.rgb * abs(center.a))',shaders['wavelet.compute.txt'])
        with self.assertRaisesRegex(AssertionError,'moment units'):
            build(Path('C:/energy'),covered_wavelet=True,wavelet_witness=True,energy_moments=True)

    def test_motion_keeps_fixed_trajectory_and_restores_native_before_disposal(self):
        source,_=build(Path('C:/energy-motion'),covered_wavelet=True,energy_moments=True,motion=True)
        self.assertIn('Mathf.Sin(2f*Mathf.PI*(frame-warmup)/128f)',source)
        self.assertIn('privateAaPairs!=(count+4)*10',source)
        self.assertIn('ReadPrivate(context,componentAa,"resolved","source",false,true)',source)
        self.assertIn('CleanupEnergyMoments();oraclePixelMap?.Dispose();CleanupPrivateReplay()',source)
        self.assertIn('camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2)',source)

    def test_moving_receiver_projection_preserves_tangents_and_rejects_wrong_face(self):
        fixture=dict(geometry=[dict(name='Floor',position=[0,-.5,0],rotation=[0,0,0],scale=[4,1,4],meshBounds=[1,1,1],
            baseColorLinear=[.5,.5,.5],emissionLinear=[0,0,0]),dict(name='Light',position=[0,2,0],rotation=[0,0,0],
            scale=[1,1,1],meshBounds=[1,0,1],baseColorLinear=[0,0,0],emissionLinear=[1,1,1])])
        scene=Scene(fixture,'black')
        position,_,hit,error=boundary_receiver(scene,[.2,-6e-6,.3],[0,1,0])
        np.testing.assert_allclose(position,[.2,0,.3],atol=1e-15);self.assertEqual(hit,0);self.assertAlmostEqual(error,6e-6)
        with self.assertRaisesRegex(AssertionError,'correction exceeds'):boundary_receiver(scene,[.2,-21e-6,.3],[0,1,0])
        with self.assertRaisesRegex(AssertionError,'analytic face'):boundary_receiver(scene,[.2,0,.3],[0,-1,0])


if __name__=='__main__':unittest.main()
