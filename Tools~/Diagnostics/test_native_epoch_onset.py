"""Preserve the original short control while supporting current native precision."""
from pathlib import Path
import unittest

from BuildScreenProbeDirectionalBindingExperiment import build
from BuildScreenProbeNativeQuadratureHistory import build as native_history_build


class NativeEpochOnsetTests(unittest.TestCase):
    template=Path(__file__).with_name('ScreenProbeAnchorGpuTimingCapture.cs').read_text(encoding='utf-8-sig')

    def test_current_precision_changes_only_declared_age_and_arm_count(self):
        old=build(self.template,Path('C:/onset'))
        modern=build(self.template,Path('C:/onset'),512,2)
        self.assertEqual(old.replace('age=128;','age=512;').replace('Enumerable.Repeat((false,false),8)',
            'Enumerable.Repeat((false,false),2)').replace('gi.screenProbeAnchorMaxAge.Override(128);',
            'gi.screenProbeAnchorMaxAge.Override(512);'),modern)
        self.assertIn('age=512;',modern)
        self.assertIn('if (readyFrames>=8) resetPending=true;',modern)
        self.assertIn('GetField("ProbeAnchorMaxAge",inst).SetValue(state,-1)',modern)

    def test_first_divergence_retains_both_arms_and_the_preceding_frame(self):
        source=build(self.template,Path('C:/onset'),512,2)
        self.assertIn('new[]{Math.Max(0,f-1),f}.Distinct()',source)
        self.assertIn('SaveBundle("00-r0-t0",selected,baselinePayloads)',source)
        self.assertIn('SaveBundle(arm,selected,currentPayloads)',source)
        self.assertIn('const long retainedPayloadBudget=3L*1024*1024*1024',source)
        self.assertIn('savedPayloadBudget = 192L * 1024 * 1024',source)
        self.assertIn('AsyncGPUReadback.WaitAllRequests()',source)

    def test_unsupported_protocols_do_not_generate_a_script(self):
        for age,arms in ((0,2),(512,1),(32,8),(128,0)):
            with self.assertRaises(ValueError):build(self.template,Path('C:/onset'),age,arms)

    def test_native_candidate_owns_positive_history_with_independent_control(self):
        source,shaders=native_history_build(Path('C:/native-candidate'))
        self.assertIn('SetValue(null, 67108864)',source)
        self.assertIn('atlas.width!=840',source)
        self.assertIn('EditorRecordProbeAnchorDiagnostics"].SetValue(null,false)',source)
        self.assertIn('path==0?componentAa:PrivateResolve("m_RawResolved")',source)
        self.assertIn('path==1,frame==0 ||',source)
        self.assertIn('path==1,frame>=warmup || frame==0',source)
        self.assertIn('NormalizeQuadrature(context);ReadComponentAa(context,quadratureNormalized,"copied")',source)
        self.assertIn('32|(mode.aa?0:67108864|134217728)',source)
        self.assertIn('134217728u) != 0u ? -covered.a : covered.a',shaders['quadrature-gather.compute.txt'])

    def test_native_motion_control_resolves_before_candidate_pixel_history(self):
        source,_=native_history_build(Path('C:/native-candidate'),9481,True)
        self.assertIn('ReadPrivate(context,componentAa,"resolved","source",false,true)',source)
        self.assertIn('privateAaPairs!=(count+4)*10',source)
        self.assertIn('camera.transform.SetPositionAndRotation(originalPose.Item1,originalPose.Item2)',source)


if __name__=='__main__':unittest.main()
