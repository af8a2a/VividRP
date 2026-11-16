"""Guard actual-pixel receiver capture and independent Lambert oracle inputs."""
from pathlib import Path
import json
import unittest

import numpy as np

from BuildScreenProbePixelOracleCapture import build,targets,REGIONS,GEOMETRY_SHADER
from CornellLambertReference import FIXTURE,Scene
from CornellPixelOracle import match_receivers
from AnalyzeCornellPixelOracle import comparison


class PixelOracleTests(unittest.TestCase):
    def test_off_probe_pixels_cover_all_declared_regions(self):
        points=targets();self.assertEqual(len(points),2336)
        self.assertEqual(len({(q['x'],q['y']) for q in points}),len(points))
        self.assertEqual({q['region'] for q in points},set(REGIONS))
        for q in points:
            x,y,w,h=REGIONS[q['region']]
            self.assertTrue(x<=q['x']<x+w and y<=q['y']<y+h)
            self.assertFalse(q['x']%16==8 and q['y']%16==8)

    def test_geometry_only_shader_uses_actual_gbuffer_without_transport(self):
        self.assertIn('LoadReceiver(pixel,position,normal)',GEOMETRY_SHADER)
        self.assertIn('float4(normal,1)',GEOMETRY_SHADER)
        self.assertNotIn('TraceSdf',GEOMETRY_SHADER)
        self.assertNotIn('Radiosity',GEOMETRY_SHADER)

    def test_capture_keeps_original_tracing_and_early_guards_allocations(self):
        source,_=build(Path('C:/oracle'))
        self.assertLess(source.index('Paused PlayMode required before allocating'),source.index('ScriptableObject.CreateInstance<VolumeProfile>'))
        self.assertLess(source.index('var privateOwned='),source.index('void SetupOraclePixels('))
        self.assertIn('oraclePixelMap?.Dispose();CleanupPrivateReplay()',source)
        self.assertIn('ReadDiffuseSurface',source)
        self.assertIn('oracleFixturePath=path,fixtureSha256=Hash',source)
        self.assertIn('oraclePrograms.AddRange',source)
        self.assertIn('384L*1024*1024',source)

    def receiver(self):
        fixture=json.loads(FIXTURE.read_text(encoding='utf-8-sig'));scene=Scene(fixture,'black')
        camera=fixture['camera'];point=targets()[0];k=np.tan(np.radians(camera['verticalFov'])/2)
        direction=np.array([[((point['x']+.5)/1920*2-1)*k*camera['aspect'],((point['y']+.5)/1080*2-1)*k,1.]])
        direction/=np.linalg.norm(direction,axis=1)[:,None];origin=np.array([camera['position']])
        distance,_,normal=scene.intersect(origin,direction)
        geometry=np.column_stack((origin+distance[:,None]*direction,[0.]));normals=np.column_stack((normal,[1.]))
        return scene,camera,[point],geometry,normals

    def test_boundary_projection_preserves_actual_tangential_location(self):
        scene,camera,points,g,n=self.receiver();g[0,:3]+=5e-6*n[0,:3];g[0,0]+=1e-6
        actual,_,_,q=match_receivers(scene,camera,points,g,n)
        self.assertEqual(actual[0,0],g[0,0]);self.assertAlmostEqual(actual[0,1],3.)
        self.assertAlmostEqual(q['maximumSurfaceCorrectionMeters'],5e-6)

    def test_projection_rejects_wrong_surface_normal_or_pixel(self):
        scene,camera,points,g,n=self.receiver()
        bad=g.copy();bad[0,:3]+=21e-6*n[0,:3]
        with self.assertRaisesRegex(AssertionError,'off the analytic surface'):match_receivers(scene,camera,points,bad,n)
        bad_normal=n.copy();bad_normal[0,:3]*=-1
        with self.assertRaisesRegex(AssertionError,'normals disagree'):match_receivers(scene,camera,points,g,bad_normal)
        bad=g.copy();bad[0,0]+=.1
        with self.assertRaisesRegex(AssertionError,'pixel footprint'):match_receivers(scene,camera,points,bad,n)

    def test_reference_noise_cancels_from_paired_mse_difference(self):
        a=np.array([[1.,3.],[1.,3.]]);b=np.array([[2.,2.],[2.,2.]])
        q=comparison(a,b,[1.5,2.5],[.04,.09]);m=q['meanMapMse']
        self.assertAlmostEqual(m['delta'],0.)
        self.assertAlmostEqual(m['referenceNoiseBias'],.065)
        self.assertAlmostEqual(m['deltaReferenceIidStandardError'],np.sqrt(.13))
        self.assertEqual(q['addedAbsoluteMeanErrorUpperBoundPercentOfReference'],0.)
        same=comparison(a,a,[1.5,2.5],[.04,.09])
        self.assertEqual(same['meanMapMse']['deltaReferenceIidStandardError'],0.)
        self.assertEqual(same['meanMapMse']['delta'],0.)

    def test_added_mean_error_bound_and_rejection_of_invalid_reference(self):
        a=np.array([[1.,3.],[2.,4.]]);b=a+.01
        q=comparison(a,b,[2.,3.],[1e-5,1e-5])
        self.assertLessEqual(q['addedAbsoluteMeanErrorPercentOfReference'],q['addedAbsoluteMeanErrorUpperBoundPercentOfReference'])
        with self.assertRaises(AssertionError):comparison(a,b,[2.,3.],[-.01,.1])


if __name__=='__main__':unittest.main()
