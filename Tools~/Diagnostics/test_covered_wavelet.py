"""Check consumed-energy reconstruction independently of conditional RGB."""
from pathlib import Path
import unittest

import numpy as np

from BuildScreenProbePixelOracleCapture import build
from ScreenProbeCoveredWavelet import covered_source
from AnalyzeCornellPixelOracle import qualify_wavelet_inputs


class CoveredWaveletTests(unittest.TestCase):
    def test_constant_consumed_energy_survives_varying_coverage(self):
        coverage=np.array([.25,.5,.75,1.]);weights=np.array([1.,4.,6.,4.])
        energy=np.array([2.,3.,4.]);radiance=energy[None,:]/coverage[:,None]
        original=(weights[:,None]*radiance).sum(0)/weights.sum()
        filtered=(weights[:,None]*radiance*coverage[:,None]).sum(0)/weights.sum()
        for center in coverage:
            encoded=np.minimum(filtered/center,65000.)
            np.testing.assert_allclose(encoded*center,energy,atol=1e-14)
        self.assertGreater(float(abs(original*coverage[0]-energy).max()),1.)

    def test_complete_coverage_preserves_weighted_color_and_hdr_limit(self):
        color=np.array([[1.,2.,3.],[4.,5.,6.]]);weights=np.array([.3,.7]);coverage=np.ones(2)
        expected=weights@color;actual=(weights[:,None]*color*coverage[:,None]).sum(0)
        np.testing.assert_array_equal(actual,expected)
        np.testing.assert_array_equal(np.minimum(np.array([2.,3.,4.])/.00001,65000.),np.full(3,65000.))

    def test_only_color_accumulation_changes_and_owned_candidate_is_replayed(self):
        source,shaders=build(Path('C:/covered-wavelet'),1733,True)
        self.assertIn('path==1?privateCoveredWavelet:privateWavelet',source)
        self.assertIn('PrivateShader("covered-wavelet.compute.txt"',source)
        self.assertIn('privateBindFrame(cmd,waveletShader)',source)
        old=(Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl').read_text(encoding='utf-8-sig')
        new=covered_source(old)
        self.assertIn(new,shaders['quadrature-gather.compute.txt'])
        self.assertIn(new,shaders['covered-wavelet.compute.txt'])
        self.assertIn(old,shaders['wavelet.compute.txt'])
        # Keep all guide, variance, geometry, alpha, domain and rejection logic.
        for line in old.splitlines():
            if any(s in line for s in ('float3 sum = center.rgb','sum += weight * lighting.rgb','center.rgb = sum / totalWeight')):continue
            self.assertIn(line,new)
        self.assertNotIn('center.a =',new[:new.index('#if defined(VIVID_SDF_ATROUS_FINAL)\n    center.a')])

    def test_symmetric_confidence_and_same_input_witness_preserve_native_pairs(self):
        coverage=np.array([.001,.1,.5,.9,1.]);a,b=np.meshgrid(coverage,coverage)
        weight=np.minimum(a,b)/np.maximum(a,b)
        np.testing.assert_array_equal(weight,weight.T)
        self.assertTrue(((weight>0)&(weight<=1)).all())
        source,shaders=build(Path('C:/symmetric-wavelet'),1733,True,True,True)
        self.assertIn('max(max(abs(lighting.a), abs(center.a)), 1e-4)',shaders['covered-wavelet.compute.txt'])
        self.assertIn('ReplayPrivateWavelets(context,1,true)',source)
        self.assertIn('ReadPrivate(context,privateLighting[1],"witness-input",waveletSide,false,false,true)',source)
        self.assertIn('ReadPrivate(context,privateMoments[1,frame&1],"witness-moments",waveletSide,false,false,true)',source)
        self.assertIn('waveletSide,path==1 && !conditionalWitness',source)
        self.assertIn('if(pair)PairPrivate(f+"/"+role+"/full"',source)

    def test_counterfactual_requires_both_full_inputs_and_descriptors_to_match(self):
        rows={(frame,role,side):dict(sha256='exact',width=1920,height=1080,format='R16G16B16A16_SFloat')
            for frame in range(2) for role in ('witness-input-full','witness-moments-full')
            for side in ('candidate','quadrature-reference')}
        self.assertEqual(qualify_wavelet_inputs(rows,range(2)),4)
        key=(1,'witness-moments-full','quadrature-reference')
        for field,value in (('sha256','different'),('width',1919),('format','R32G32B32A32_SFloat')):
            old=rows[key][field];rows[key][field]=value
            with self.assertRaisesRegex(AssertionError,'inputs differ'):qualify_wavelet_inputs(rows,range(2))
            rows[key][field]=old


if __name__=='__main__':unittest.main()
