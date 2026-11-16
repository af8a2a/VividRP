"""Check matched wavelet contrasts, immutable input witnesses and failure handling."""
from copy import deepcopy
from pathlib import Path
import unittest

import numpy as np

from BuildScreenProbePixelOracleCapture import build
from ScreenProbeWaveletFactorial import VARIANTS,qualify_inputs,row_normalized_flux,round_zero


class WaveletFactorialTests(unittest.TestCase):
    def test_row_normalization_uses_same_flux_and_matches_weighted_average(self):
        rng=np.random.default_rng(7031);energy=rng.random((6,3));weights=rng.random((6,6))*.1;np.fill_diagonal(weights,0)
        flux=np.sum(weights[...,None]*(energy[None,:,:]-energy[:,None,:]),axis=1)
        self_weight=36/256;total=self_weight+weights.sum(1)
        a=energy+flux/total[:,None]
        b=(self_weight*energy+weights@energy)/total[:,None]
        np.testing.assert_allclose(a,b,rtol=1e-14)
        np.testing.assert_allclose((energy+flux).sum(0),energy.sum(0)+flux.sum(0))
        self.assertTrue((a>=0).all())

    def test_variants_share_prepare_and_only_change_declared_output_operations(self):
        source,shaders=build(Path('C:/factorial'),covered_wavelet=True,energy_moments=True,conservative_flux=True,wavelet_factorial=True)
        fixed=shaders['flux-wavelet.compute.txt'];row=shaders['symmetric-row-rne.compute.txt']
        before=lambda s:s[s.index('void PrepareScreenProbeFluxCS'):s.index('void FilterScreenProbeFluxPixel')]
        self.assertEqual(before(fixed),before(row));self.assertEqual(before(row),before(shaders['symmetric-row-rtz.compute.txt']))
        self.assertEqual(row_normalized_flux(fixed),row)
        self.assertEqual(round_zero(fixed),shaders['flux-rtz.compute.txt'])
        self.assertEqual(shaders['energy-row-rne.compute.txt'],shaders['covered-wavelet.compute.txt'].replace('_SdfDiffuseOutput[pixel] = center;','_SdfDiffuseOutput[pixel] = SdfRoundToNearestHalf(center);'))
        self.assertIn('if(frame>=warmup)foreach(string variant in factorialVariants)',source)
        self.assertIn('path==1 && waveletVariant==null',source)
        self.assertIn('var shader=overrideShader??fluxShader',source)
        self.assertIn('512L*1024*1024',source)
        self.assertIn('if(pair)PairPrivate',source)
        with self.assertRaisesRegex(AssertionError,'static conservative'):
            build(Path('C:/bad-factorial'),wavelet_factorial=True)

    def witness(self):
        p=dict(waveletFactorial=True,waveletFactorialVariants=list(VARIANTS),warmup=2,measuredFrames=3)
        rows={(f,role,side):dict(sha256='same',width=1920,height=1080,format='R16G16B16A16_SFloat')
            for f in range(2,5) for role in ('witness-input-full','witness-moments-full') for side in ('candidate',*VARIANTS)}
        guides=[dict(frame=f,side=side,sha256='same',width=1920,height=1080,format='R32G32B32A32_SFloat')
            for f in (2,4) for side in ('candidate','symmetric-row-rne','symmetric-row-rtz','flux-rtz')]
        return dict(factorialGuideHashes=guides),p,rows

    def test_inputs_and_initial_guides_are_checked_independently(self):
        c,p,rows=self.witness();self.assertEqual(qualify_inputs(c,p,rows)['fullInputPairs'],30)
        bad=deepcopy(rows);bad[3,'witness-moments-full','flux-rtz']['sha256']='other'
        with self.assertRaisesRegex(AssertionError,'inputs differ'):qualify_inputs(c,p,bad)
        bad=deepcopy(c);bad['factorialGuideHashes'][0]['sha256']='other'
        with self.assertRaisesRegex(AssertionError,'guides differ'):qualify_inputs(bad,p,rows)
        bad=deepcopy(c);bad['factorialGuideHashes'].append(bad['factorialGuideHashes'][0])
        with self.assertRaisesRegex(AssertionError,'duplicate'):qualify_inputs(bad,p,rows)


if __name__=='__main__':unittest.main()
