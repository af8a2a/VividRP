"""Check epoch isolation and distinguish initialization decay from new drift."""
import unittest
from pathlib import Path

import numpy as np

from AnalyzeAnchorHistoryInitialization import recurrence
from BuildScreenProbeHistoryEpoch import build
from AnalyzeScreenProbeHistoryEpoch import signal_comparison


class AnchorHistoryEpochTests(unittest.TestCase):
    def test_existing_difference_must_follow_the_fixed_age_recurrence(self):
        before=np.ones((128,1,3,4));after=before.copy()
        after[...,:3]+=.0005*(511/512)**np.arange(128).reshape(-1,1,1,1)
        self.assertTrue(recurrence(before,after,512)['emaTailExplainsDifference'])
        after[64:,...,:3]+=.0001
        self.assertFalse(recurrence(before,after,512)['emaTailExplainsDifference'])

    def test_coverage_mismatch_is_not_classified_as_initial_color_history(self):
        before=np.ones((4,1,1,4));after=before.copy();after[1,...,3]=.5
        with self.assertRaisesRegex(AssertionError,'Coverage'):recurrence(before,after,512)

    def test_epoch_waits_for_source_and_uses_persistent_trace(self):
        source=build(Path('C:/epoch'),False)
        self.assertNotIn('__',source)
        self.assertIn('phase = 2;active=false;return;',source)
        self.assertIn('ResetGatherHistoryEpoch();phase=1;frame=0;active=true;',source)
        self.assertNotIn('SetupExperimentalTracing(qualityPass)',source)
        self.assertIn('Original persistent trace required.',source)
        self.assertIn('Frozen source invalidated after epoch reset.',source)
        self.assertIn('m_HasHistory',source)
        self.assertIn('m_AnchorStatisticsHistoryValid',source)
        self.assertLess(source.index('object qualityPass=null;'),source.index('void CaptureEpochFirstFrame('))

    def test_retained_frame_gaps_use_the_actual_elapsed_frames(self):
        frames=np.array([0,32,64,96,127]);before=np.ones((5,1,1,4));after=before.copy()
        after[...,:3]+=.0005*(511/512)**frames.reshape(-1,1,1,1)
        v=recurrence(before,after,512,frames)
        self.assertTrue(v['emaTailExplainsDifference'])
        self.assertAlmostEqual(v['expectedNormRatio'],(511/512)**127)

    def test_mean_and_temporal_variance_are_reported_separately(self):
        a=np.array([1.,3.,1.,3.]).reshape(4,1,1);b=np.array([2.,3.,2.,3.]).reshape(4,1,1)
        v=signal_comparison(a,b)
        self.assertAlmostEqual(v['meanChangePercent'],25)
        self.assertAlmostEqual(v['varianceChangePercent'],-75)

    def test_epoch_only_invalidates_gather_metadata_between_camera_frames(self):
        source=build(Path('C:/epoch'),True,9481,1024)
        helper=source[source.index('void ResetGatherHistoryEpoch()'):source.index('void CaptureEpochFirstFrame(')]
        self.assertIn('IsFrameActive',helper)
        self.assertIn('sequence.SetValue(texture,-1L)',helper)
        self.assertNotIn('Dispose',helper)
        self.assertNotIn('Radiosity',helper)
        self.assertIn('warmup = 4096, count = 1024',source)
        self.assertIn('Run("capture",9481)',source)

    def test_pixel_stage_mode_uses_the_identical_final_roi_reader_and_budget(self):
        source=build(Path('C:/epoch'),True,pixel_stages=True)
        self.assertIn('ReadQuality(context,texture,index==0?"resolved":"temporal","pixel")',source)
        self.assertIn('const long payloadBudget=192L*1024*1024',source)
        self.assertIn('qualityPixelRegions',source)
        self.assertIn('if(role=="final")AccumulateQualityPixels',source)


if __name__=='__main__':unittest.main()
