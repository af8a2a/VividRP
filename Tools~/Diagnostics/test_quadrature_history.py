"""Check private-history input ownership and energy gates for quadrature replay."""
from pathlib import Path
import unittest

import numpy as np

from BuildScreenProbeQuadratureHistory import build
from AnalyzeScreenProbeQuadratureHistory import energy_and_noise


class QuadratureHistoryTests(unittest.TestCase):
    def test_candidate_receives_positive_coverage_and_owns_its_histories(self):
        source, shaders=build(Path('C:/quadrature-history'))
        self.assertNotIn('__',source)
        self.assertEqual(source.count('ReplayComponents(context,texture)'),1)
        self.assertLess(source.index('ReplayComponents(context,texture);NormalizeQuadrature(context);'), source.index('if(index==1)ReplayPrivateTemporal(context);'))
        self.assertIn('path==0?PrivateResolve("m_RawResolved"):quadratureNormalized',source)
        self.assertIn('float4(value.rgb,abs(value.a))',shaders['normalize-alpha.compute.txt'])
        self.assertIn('privateColor[path,previous]',source)
        self.assertIn('privateMoments[path,previous]',source)
        self.assertIn('privateGeometry[path,previous]',source)
        self.assertIn('CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();',source)
        self.assertEqual(source.count('"EditorAtrousDispatchCapture", "EditorGatherFrameOverride"'),1)
        self.assertIn('componentAaPairs!=count+4',source)
        self.assertIn('privateAaPairs!=(count+4)*25',source)
        self.assertIn('State("HasScreenRays")==useScreenRays',source)
        self.assertIn('State("ScreenCardLighting")==screenCardLighting',source)
        self.assertIn('State("GlobalClipmapUsed")==useGlobalClipmap',source)

    def test_energy_consumes_coverage_and_rejects_signed_history(self):
        baseline=np.ones((64,2,2,4))
        baseline[...,:3]*=(1+.1*np.sin(np.arange(64)))[:,None,None,None]
        candidate=baseline.copy()
        candidate[...,:3]*=2;candidate[...,3]*=.5
        result=energy_and_noise(baseline,candidate)
        self.assertEqual(result['rgbMeanChangePercent'],[0,0,0])
        candidate[...,3]*=-1
        with self.assertRaisesRegex(AssertionError,'Signed eligibility'):
            energy_and_noise(baseline,candidate)

    def test_motion_keeps_baseline_checks_and_restores_camera(self):
        source,_=build(Path('C:/quadrature-history'),9481,True)
        self.assertIn('Run("capture",9481)',source)
        self.assertIn('Mathf.Sin(2f*Mathf.PI*(frame-warmup)/128f)',source)
        self.assertIn('privateAaPairs!=(count+4)*10',source)
        self.assertIn('camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2);',source)
        self.assertIn('ReadPrivate(context,quadratureNormalized,"resolved","candidate",false,true)',source)
        self.assertIn('path==0,frame==0 || (frame>=warmup && item.Item1!="moments")',source)


if __name__=='__main__':unittest.main()
