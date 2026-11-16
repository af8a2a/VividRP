"""Check conservative pair exchanges and native GPU-witness failure handling."""
from copy import deepcopy
from pathlib import Path
import unittest
from unittest.mock import patch

import numpy as np

from AnalyzeScreenProbeFlux import qualify_mass
from BuildScreenProbePixelOracleCapture import build,snapshot_capture


def exchange(energy,weights):
    return energy+np.sum(weights[...,None]*(energy[None,:,:]-energy[:,None,:]),axis=1)


class FluxTests(unittest.TestCase):
    def test_symmetric_flux_preserves_mass_nonnegativity_and_constants(self):
        rng=np.random.default_rng(7213);energy=rng.random((27,3))*20
        raw=rng.random((27,27));weights=(raw+raw.T)/(2*27);np.fill_diagonal(weights,0)
        output=exchange(energy,weights)
        np.testing.assert_allclose(output.sum(0),energy.sum(0),rtol=1e-14)
        self.assertTrue((output>=energy.min(0)).all() and (output<=energy.max(0)).all())
        fixed=np.broadcast_to([3.,.5,11.],energy.shape)
        np.testing.assert_array_equal(exchange(fixed,weights),fixed)

    def test_boundary_guard_must_be_applied_at_both_endpoints(self):
        energy=np.array([[0.,0.,0.],[1.,2.,3.],[5.,7.,9.]])
        raw=np.full((3,3),.2);allowed=np.array([1,0,1])
        symmetric=raw*allowed[:,None]*allowed[None,:]
        np.testing.assert_allclose(exchange(energy,symmetric).sum(0),energy.sum(0))
        np.testing.assert_array_equal(exchange(energy,symmetric)[1],energy[1])
        self.assertFalse(np.allclose(exchange(energy,raw*allowed[:,None]).sum(0),energy.sum(0)))

    def test_native_consumes_flux_before_next_step_and_replay_stays_independent(self):
        source,shaders=build(Path('C:/flux'),covered_wavelet=True,energy_moments=True,conservative_flux=True)
        hook=source[source.index('    Action<ComputePassContext,int,Texture,Texture,Texture,Texture> privateWaveletHook='):]
        self.assertLess(hook.index('RecordFlux(context,index'),hook.index('if(index!=3)return'))
        self.assertIn('if(path==1)RecordFlux(context,iteration',source)
        self.assertIn('else cmd.DispatchCompute(waveletShader',source)
        self.assertIn('fluxPrograms,fluxMass,privateAaPairs',source)
        self.assertIn('frame==warmup || frame==warmup+count-1',source)
        shader=shaders['flux-wavelet.compute.txt']
        self.assertIn('tapSummary.y<=0.0',shader)
        self.assertIn('kernel[x+2]*kernel[y+2]/256.0',shader)
        self.assertIn('SdfRoundToNearestHalf(center)',shader)
        self.assertIn('SdfAtrousLogLuminance(center.rgb * abs(center.a))',shader)
        with self.assertRaisesRegex(AssertionError,'Flux requires'):
            build(Path('C:/invalid-flux'),conservative_flux=True)

    def test_mass_checker_rejects_nonconservation_coverage_and_missing_pairs(self):
        protocol=dict(conservativeFluxWavelet=True,performanceQualified=False,warmup=10,measuredFrames=2,
            fluxMassFrames=[10,11],gates=dict(fluxMassRoundingBound=True,fluxCoverageByteExact=True,fluxFp32RelativeTolerance=2e-6))
        rows=[dict(frame=f,iteration=i,side=s,format='R16G16B16A16_SFloat',width=1920,height=1080,
            sum=[1.,2.,3.],halfUlp=[1e-4]*3,alphaSha256='abc',saturated=0,nonFinite=0,negative=0)
            for f in (10,11) for i in range(4) for s in ('input','output')]
        capture=dict(fluxMass=rows)
        self.assertTrue(qualify_mass(capture,protocol)['allPassed'])
        bad=deepcopy(capture);bad['fluxMass'][7]['sum'][0]+=.01
        self.assertFalse(qualify_mass(bad,protocol)['allPassed'])
        bad=deepcopy(capture);bad['fluxMass'][1]['alphaSha256']='changed'
        with self.assertRaisesRegex(AssertionError,'coverage'):qualify_mass(bad,protocol)
        bad=deepcopy(capture);bad['fluxMass'].pop()
        with self.assertRaisesRegex(AssertionError,'Incomplete'):qualify_mass(bad,protocol)

    def test_pre_capture_snapshot_pins_every_executable_and_parameter_file(self):
        output=Path('C:/before-flux')
        shaders={'flux-wavelet.compute.txt':'test','energy-temporal.compute.txt':'test','covered-wavelet.compute.txt':'test'}
        with patch('VerifyGiSourceSnapshot.create') as create:
            snapshot_capture(output,shaders)
        snapshot,workspace,includes,inputs,scripts=create.call_args.args
        self.assertEqual(snapshot,output/'source-before-capture.json')
        self.assertEqual(set(inputs),{output/name for name in ('protocol.json','targets.json',*shaders)})
        self.assertEqual(scripts,[output/'capture-script.cs'])
        self.assertIn('Tools~/Diagnostics/ScreenProbeConservativeFlux.cs.txt',includes)
        self.assertEqual(workspace,Path(__file__).resolve().parents[2])


if __name__=='__main__':unittest.main()
