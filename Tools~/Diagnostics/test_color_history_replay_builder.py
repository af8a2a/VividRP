"""Verify private replay ownership, frame-zero initialization and immutable inputs."""
import unittest
from pathlib import Path

from BuildScreenProbeColorHistoryReplay import build
from BuildScreenProbeColorHistoryMotion import build as motion_build


class ColorHistoryReplayBuilderTests(unittest.TestCase):
    def test_none_mode_owns_all_history_channels_and_starts_at_zero(self):
        source,shaders=build(Path("C:/replay"),"none",1733,128)
        self.assertNotIn("__",source)
        self.assertIn('phase = 1; frame = -1',source)
        self.assertIn('Run("shared",1733)',source)
        self.assertIn('privateColor[path,previous]',source)
        self.assertIn('privateMoments[path,previous]',source)
        self.assertIn('privateGeometry[path,previous]',source)
        self.assertIn('Unpaired private AA readbacks',source)
        self.assertEqual(set(shaders),{"temporal.compute.txt","wavelet.compute.txt"})
        self.assertNotIn('SetValue(referencePass,referenceShader)',source)

    def test_feedback_is_limited_to_private_candidate_targets(self):
        source,shaders=build(Path("C:/replay"),"conservative-moments",9481,256)
        self.assertIn('warmup = 1024, count = 256',source)
        self.assertIn('privateFiltered,privateColor[1,slot]',source)
        self.assertIn('privateAdjustedMoments,privateMoments[1,frame&1]',source)
        self.assertNotIn('CopyTexture(privateFiltered,PrivateResolve',source)
        self.assertIn('#define VIVID_HISTORY_FEEDBACK_MOMENTS 1',shaders['feedback.compute.txt'])

    def test_unsupported_window_is_rejected(self):
        with self.assertRaises(AssertionError):build(Path("C:/replay"),"none",1733,10)

    def test_stable_clamp_candidate_is_isolated_from_native_temporal_program(self):
        source,shaders=build(Path("C:/replay"),"stable-unclamped",1733,128)
        self.assertIn('privateTemporalCandidate=PrivateShader',source)
        self.assertNotIn('feedback.compute.txt',shaders)
        self.assertNotIn('if (fastUpdate == 0.0)',shaders['temporal.compute.txt'])
        self.assertIn('if (fastUpdate == 0.0)\n        return history;',shaders['temporal-candidate.compute.txt'])
        self.assertIn('path==1 && privateTemporalCandidate!=null',source)

    def test_motion_preserves_aa_and_records_reprojection_inputs(self):
        source,shaders=motion_build(Path("C:/replay"),1733)
        self.assertNotIn("__",source)
        self.assertIn('privateRois=new[]{("edge",880,414,160,128)}',source)
        self.assertIn('Quaternion.Euler(0,-7f*t,0)',source)
        self.assertIn('"resolved","source",false,true',source)
        self.assertIn('frame>=warmup && item.Item1!="moments"',source)
        self.assertIn('camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2)',source)
        self.assertIn('CleanupCamera").Invoke(owner',source)
        self.assertIn('Private native-baseline mismatch',source)
        self.assertIn('temporal-candidate.compute.txt',shaders)


if __name__=="__main__":unittest.main()
