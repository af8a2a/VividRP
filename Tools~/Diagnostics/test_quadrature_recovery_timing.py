"""Check recovery discontinuity metrics and fail-closed GPU timing evidence."""
from pathlib import Path
import unittest

import numpy as np

from AnalyzeScreenProbeQuadratureRecovery import reentry_steps
from AnalyzeScreenProbeQuadratureTiming import qualify_samples
from BuildScreenProbeQuadratureRecovery import build as recovery_build
from BuildScreenProbeQuadratureTiming import build as timing_build
from BuildScreenProbeQuadraturePixHold import build as hold_build
from ReplayScreenProbeQuadraturePix import select_pair


class QuadratureRecoveryTimingTests(unittest.TestCase):
    def test_reentry_removes_common_lighting_change(self):
        a=np.ones((256,2,3,4));a[...,:3]*=(2+np.arange(256)*.1)[:,None,None,None]
        eligible=np.zeros(256);eligible[185:]=1
        result=reentry_steps(a,a.copy(),eligible,128)
        self.assertEqual(result['firstReentryIndex'],185)
        self.assertEqual(result['maximumRoiMeanStepPercent'],0)
        self.assertEqual(result['maximumP99GainStepPercent'],0)

    def test_reentry_detects_a_real_gain_step(self):
        a=np.ones((256,2,3,4));a[...,:3]*=2;b=a.copy();b[185:,...,:3]+=.004
        eligible=np.zeros(256);eligible[185:]=1
        result=reentry_steps(a,b,eligible,128)
        self.assertAlmostEqual(result['maximumRoiMeanStepPercent'],.2)
        self.assertAlmostEqual(result['maximumP99GainStepPercent'],.2)
        with self.assertRaisesRegex(AssertionError,'did not reenter'):reentry_steps(a,b,np.zeros(256),128)

    def test_gpu_samples_are_not_substituted_by_valid_handle(self):
        p=dict(markers=['pair'],censorFirstGpuSamples=1,censorLastGpuSamples=1,minimumRetainedGpuSamples=2)
        row=dict(marker='pair',valid=True,wrappedAround=False,count=0,samples=[])
        c=dict(complete=True,errors=[],timingSamples=[row])
        self.assertTrue(qualify_samples(c,p)[0])
        row.update(count=4,samples=[dict(nanoseconds=1000,count=1) for _ in range(4)])
        self.assertFalse(qualify_samples(c,p)[0])
        for value,count in ((0,1),(-1,1),(10**18,1),(1000,2)):
            row['samples'][1]=dict(nanoseconds=value,count=count)
            self.assertTrue(qualify_samples(c,p)[0])

    def test_recovery_builder_keeps_motion_then_stops_and_caps_payload(self):
        source,_=recovery_build(Path('C:/recovery'))
        self.assertIn('frame<warmup+128?Mathf.Sin',source)
        self.assertIn('count = 256',source)
        self.assertIn('160L*1024*1024',source)
        self.assertIn('privateAaPairs!=(count+4)*15',source)
        self.assertIn('ReadRecoveryEligibility(context,mode.output)',source)
        self.assertEqual(source.count('void Log('),1)

    def test_timing_does_not_read_back_the_retained_gpu_interval(self):
        source=timing_build(Path('C:/timing'))
        self.assertEqual(source.count('void Log('),1)
        self.assertIn('f>2 && f!=warmup-1 && f!=warmup+count-1',source)
        self.assertIn('if(frame==warmup+64)StartTiming()',source)
        self.assertIn('cmd.BeginSample(marker);cmd.DispatchCompute',source)
        self.assertIn('atlas.width!=840',source)

    def test_pix_holder_releases_the_cli_queue_and_restores_owned_resources(self):
        source=hold_build(Path('C:/hold'))
        self.assertIn('async System.Threading.Tasks.Task<object> RunOwned()',source)
        self.assertEqual(source.count('EditorApplication.delayCall+=async'),1)
        self.assertIn('await RunOwned()',source)
        self.assertIn('hold-terminal.json',source)
        self.assertIn('CleanupTiming();CleanupQuadrature();CleanupComponents()',source)
        self.assertIn('System.IO.File.Exists(output+"/done.txt")',source)
        self.assertNotIn('timingRecorders[i]=ProfilerRecorder.StartNew',source)

    def test_pix_selection_uses_marker_ancestry_and_queue_local_identity(self):
        rows=[]
        for i,label in enumerate(('Baseline','Stratified')):
            parent=10+i*10
            rows.extend([dict(queueId=1,eventIndex=parent,parentIndex=0,name='GI Quadrature '+label+' Resolve',gpuWork=False,queueType=0),
                         dict(queueId=1,eventIndex=parent+1,parentIndex=parent,name='Compute.Dispatch',gpuWork=False,queueType=0),
                         dict(queueId=1,eventIndex=parent+2,parentIndex=parent+1,name='Dispatch',gpuWork=True,queueType=0)])
        selected=select_pair(rows);self.assertEqual(selected['baseline']['eventIndex'],12)
        with self.assertRaisesRegex(AssertionError,'ambiguous'):select_pair(rows+[rows[-1]])


if __name__=='__main__':unittest.main()
