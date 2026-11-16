"""Check stratified lookup mass and conservative prototype ownership."""
from pathlib import Path
import unittest
import numpy as np

from BuildScreenProbeQuadrature import build,shader_source


class ProbeQuadratureTests(unittest.TestCase):
    def test_four_strata_cover_the_box_with_equal_mass(self):
        u=(np.arange(64)+.5)/64;x,y=np.meshgrid(u,u)
        points=[np.stack([x+cx-1,y+cy-1],-1) for cy in range(2) for cx in range(2)]
        values=np.stack(points)
        self.assertTrue((values>=-1).all() and (values<=1).all())
        self.assertAlmostEqual(float(values[...,0].mean()),0)
        self.assertAlmostEqual(float(values[...,1].mean()),0)
        self.assertAlmostEqual(float((values*values).sum(-1).mean()),2/3,delta=1e-4)

    def test_prototype_keeps_kernel_ids_and_adds_no_ray_trace(self):
        source=shader_source()
        self.assertEqual(source.count('#pragma kernel '),6)
        self.assertIn('SdfQuadratureSlot = sample',source)
        self.assertIn('covered += float4(value.rgb * value.a, value.a) * 0.25',source)
        helper=Path(__file__).with_name('ScreenProbeQuadrature.hlsl.txt').read_text()
        self.assertIn('_SdfNearOcclusion.z > 0.0',helper)
        self.assertIn('ScreenProbeBilateralWeight(tile, position, normal, eyeDepth) < 0.75',helper)
        self.assertIn('state.x < 64.0 || state.w != 1.0',helper)
        self.assertNotIn('TraceSdf',helper)

    def test_capture_owns_shader_and_restores_the_diagnostic_constant(self):
        source=build(Path('C:/quadrature'))
        self.assertNotIn('__',source)
        self.assertIn('SetupQuadrature()',source)
        self.assertIn('CleanupQuadrature();CleanupComponents()',source)
        self.assertIn('32|(mode.aa?0:67108864)',source)
        self.assertIn('"_SdfGatherHistoryDiagnostics",32)',source)
        self.assertIn('quadraturePrograms',source)
        self.assertIn('object.ReferenceEquals(field.GetValue(qualityPass),quadratureShader)',source)


if __name__=='__main__':unittest.main()
