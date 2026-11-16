"""Receiver-origin changes must retain thresholds, bindings and strict A/A guards."""
from pathlib import Path
import copy
import hashlib
import unittest

import numpy as np

from AnalyzeScreenProbeOriginReplay import counters
from AnalyzeScreenProbeNativeOrigins import qualify_case_pair
from BuildScreenProbePixelOracleCapture import build
from ScreenProbeOriginReplay import tracing_source


class OriginReplayTests(unittest.TestCase):
    def test_only_two_emission_origins_change(self):
        original=(Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute').read_text(encoding='utf-8-sig')
        changed=tracing_source()
        changed=changed.replace('\nfloat _SdfOriginLimit;','').replace('min(_SdfDiffuseRayBias, _SdfOriginLimit)','_SdfDiffuseRayBias')
        prefix='Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'
        changed=changed.replace(prefix,'')
        self.assertEqual(changed,original)
        self.assertIn('position + storedNormal * _SdfDiffuseRayBias',tracing_source())

    def test_replay_restores_all_native_outputs_and_origin_in_finally(self):
        source,shaders=build(Path('C:/origin'),covered_wavelet=True,energy_moments=True,origin_limit=.04)
        self.assertIn('finally {\n            for(int i=0;i<last;++i)raw.SetComputeTextureParam',source)
        self.assertIn('cmd.SetComputeFloatParam(experimentalTrace,"_SdfOriginLimit",originNativeLimit)',source)
        self.assertIn('ReplayOriginTrace(context);ReplayComponents(context,texture)',source)
        self.assertIn('Unpaired origin replay readbacks',source)
        self.assertIn('gi.recordRayDiagnostics.Override(true)',source)
        self.assertIn('EditorUtility.IsPersistent(experimentalOriginalTrace)',source)
        self.assertIn('Origin tracing owner or persistent provenance changed',source)
        self.assertIn('originCounters?.Dispose();CleanupEnergyMoments()',source)
        self.assertEqual(len(shaders),11)
        with self.assertRaisesRegex(AssertionError,'static energy-domain'):
            build(Path('C:/invalid-origin'),origin_limit=.01)

    def test_counter_hash_coverage_and_black_back_semantics(self):
        v=np.zeros(32,dtype='<u4');v[0]=v[1]=v[2]=16;v[5]=3
        row=dict(counters=v.tolist(),sha256=hashlib.sha256(v.tobytes()).hexdigest())
        counters(row)
        bad=v.copy();bad[6]=1
        row=dict(counters=bad.tolist(),sha256=hashlib.sha256(bad.tobytes()).hexdigest())
        with self.assertRaisesRegex(AssertionError,'nonblack'):counters(row)

    def test_unwritten_direction_cells_are_preserved_while_written_cells_are_poisoned(self):
        body=Path(__file__).with_name('ScreenProbeOriginReplay.compute.txt').read_text()
        self.assertIn('written?float4(-32768.0',body)
        self.assertIn(':_OriginNativeDirections.Load',body)
        self.assertIn('RWStructuredBuffer<uint4> _OriginReceiverOutput',body)

    def test_adaptive_offset_remains_bounded_and_requires_positive_outside_sample(self):
        body=tracing_source(True)
        self.assertIn('all(receiver.xy==instance.entityId)',body)
        self.assertIn('bias=min(_SdfDiffuseRayBias,max(bias,safe))',body)
        self.assertIn('VividMeshSdfSample(field,grid)>0.075*field.originAndVoxelSize.w',body)
        self.assertIn('return position+normal*_SdfDiffuseRayBias',body)
        self.assertNotIn('VividMeshSdfTryAdvanceReceiver',body)
        for scale in (.2,1.,4.2):
            voxel=.045;requested=.01;maximum=.04
            safe=.125*voxel*scale;offset=min(maximum,max(requested,safe))
            self.assertLessEqual(offset,maximum);self.assertGreaterEqual(offset,requested)
            self.assertGreater(offset/scale,.075*voxel)

    def test_field_bound_and_cap_do_not_claim_arbitrary_field_completion(self):
        body=tracing_source(True,2048)
        self.assertIn('ceil(length(float3(field.dimensions-1u))/(0.075*0.7))+1.0',body)
        self.assertIn('_SdfOriginLimit < 0.04 ?',body)
        for dimensions in ((32,32,32),(8,4,8)):
            distance=np.linalg.norm(np.array(dimensions)-1)
            budget=min(2048,max(192,int(np.ceil(distance/(.075*.7)))+1))
            self.assertGreater((budget-1)*.075*.7,distance)
        # The finite cap is a cost bound, not a completion proof for every field.
        distance=np.linalg.norm(np.array((64,64,64))-1)
        self.assertLess(2047*.075*.7,distance)

    def test_budget_control_removes_origin_condition_from_both_marchers(self):
        body=tracing_source(True,2048,True)
        self.assertNotIn('_SdfOriginLimit < 0.04 ?',body)
        self.assertEqual(body.count('ceil(length(float3(field.dimensions-1u))/(0.075*0.7))+1.0'),2)
        with self.assertRaises(AssertionError):tracing_source(True,192,True)

    def test_physical_comparison_rejects_different_budget_program_or_geometry(self):
        protocol=dict(nativeOriginLimitMeters=.04,sequence=1733,warmup=1,measuredFrames=1,spacing=16,rays=8,
            originTraceSteps=2048,originAdaptiveCandidate=True,originStepPolicy='field-diagonal-all-origins',
            files={'candidate-tracing.compute.txt':'trace','targets.json':'targets'})
        programs={key:[{'hash':'compiled'}] for key in ('experimentalTracePrograms','privatePrograms','originReplayPrograms','quadraturePrograms','oraclePrograms')}
        programs['records']=[dict(frame=1,vp=[1],state=dict(inverseVp=[1]))]
        rows={(1,tag):dict(sha256='input',width=16,height=8,format='float4')
            for tag in ('input-depth','input-gbuffer0','input-gbuffer1','geometry','pixel-geometry')}
        a=[programs,protocol,rows];b=copy.deepcopy(a);b[1]['nativeOriginLimitMeters']=.01
        self.assertEqual(qualify_case_pair([a,b]),5)
        for edit in ('policy','program','geometry','matrix'):
            c=copy.deepcopy(b)
            if edit=='policy':c[1]['originStepPolicy']='field-diagonal-capped'
            if edit=='program':c[0]['privatePrograms'][0]['hash']='other'
            if edit=='geometry':c[2][1,'geometry']['sha256']='other'
            if edit=='matrix':c[0]['records'][0]['state']['inverseVp']=[2]
            with self.assertRaises(AssertionError,msg=edit):qualify_case_pair([a,c])


if __name__=='__main__':unittest.main()
