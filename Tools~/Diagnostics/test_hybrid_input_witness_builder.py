"""Keep controlled witness/feedback protocols explicit and output creation immutable."""
import tempfile
import unittest
from pathlib import Path

import numpy as np

from BuildScreenProbeHybridInputWitness import witness
from BuildScreenProbeHistoryFeedback import build


class HybridInputWitnessBuilderTests(unittest.TestCase):
    def test_two_geometries_share_the_same_reference_sequence_and_window(self):
        with tempfile.TemporaryDirectory(prefix="vivid-hybrid-witness-") as directory:
            root=Path(directory);g=np.zeros((136,120,4),dtype="<f4")
            first=root/"a.bin";second=root/"b.bin";first.write_bytes(g.tobytes())
            g[59,48,0]=np.nextafter(np.float32(0),np.float32(1));second.write_bytes(g.tobytes())
            source,_,a,b=witness(root/"output",first,second)
            self.assertNotEqual(a,b)
            self.assertNotIn("__",source)
            self.assertIn('Run("geometry-0",25387)',source)
            self.assertIn('Run("geometry-1",25387)',source)
            self.assertIn('warmup = 832, count = 64',source)
            self.assertIn('ReadCrop(context,Resolve("m_ScreenRays")',source)
            self.assertIn('previousInverseVpRecomputed',source)

    def test_feedback_modifies_only_native_color_history_and_keeps_gates_separate(self):
        source,shader=build(Path("C:/pilot"),1733)
        self.assertNotIn("__",source)
        for arm in ("baseline-a","baseline-b","feedback"):self.assertIn('Run("'+arm+'",1733)',source)
        self.assertIn('iteration!=1',source)
        self.assertIn('history.rgb=EncodeScreenProbeCoveredHistory(lighting.rgb,lighting.a)',shader)
        self.assertIn('_SdfFeedbackPixelHistory[id.xy]=history',shader)
        self.assertNotIn('history.a=',shader)
        self.assertNotIn('SetValue(referencePass,referenceShader)',source)

    def test_conservative_feedback_keeps_distinct_inputs_and_symmetric_normalization(self):
        source,shader=build(Path("C:/conservative"),1733,"conservative")
        self.assertNotIn("__",source)
        self.assertIn('bool conservativeFeedback=true',source)
        self.assertIn('cmd.CopyTexture(feedbackFiltered,history)',source)
        self.assertIn('ReadFeedbackEnergy(context,history,"before")',source)
        self.assertIn('weight/=max(ownWeight,_SdfFeedbackWeights.Load',shader)
        self.assertIn('sample.rgb-center.rgb',shader)
        self.assertIn('float sum=1.0',shader)
        self.assertNotIn('history.a=',shader)

    def test_moment_alignment_changes_only_the_mean_after_current_spatial_filtering(self):
        source,shader=build(Path("C:/moments"),1733,"conservative-moments")
        self.assertNotIn("__",source)
        self.assertIn('bool alignFeedbackMoments=true',source)
        self.assertIn('iteration==3 && alignFeedbackMoments',source)
        self.assertIn('#define VIVID_HISTORY_FEEDBACK_MOMENTS 1',shader)
        self.assertIn('moments.x+newLog-oldLog',shader)
        self.assertNotIn('moments.y=',shader)
        self.assertNotIn('moments.z=',shader)
        self.assertNotIn('moments.w=',shader)


if __name__=="__main__":unittest.main()
