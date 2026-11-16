"""First-hit comparison must distinguish unknown Screen identities from misses."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import numpy as np

from CornellLambertReference import Scene
from ScreenProbeFirstHitOracle import ArchivedRays,STATE_SOURCE,STATES,map_entities,project_receivers,decode_entities,statistics,decompose_delta,require_same_direction_words


def fixture():
    base=dict(rotation=[0,0,0],baseColorLinear=[.5,.5,.5],emissionLinear=[0,0,0],cullMode=2)
    floor=dict(base,name='Floor',position=[0,-.1,0],scale=[2,.2,2],meshBounds=[1,1,1])
    light=dict(base,name='Light',position=[0,2,0],scale=[1,1,1],meshBounds=[1,0,1],emissionLinear=[200,200,200])
    return Scene(dict(geometry=[floor,light]),'black')


class FirstHitOracleTests(unittest.TestCase):
    def test_shader_states_distinguish_miss_missing_surface_and_backface(self):
        self.assertEqual([STATES[n] for n in ('RayMissingSurface','RayMiss','RayBackface')],[3,4,5])

    def test_direction_equality_checks_signed_zero_bits(self):
        a=np.array([[0.,0.,1.]],dtype='<f4');b=a.copy();require_same_direction_words(a,b)
        b[0,0]=-0.
        self.assertTrue(np.array_equal(a,b))
        with self.assertRaises(AssertionError):require_same_direction_words(a,b)

    def test_entity_words_preserve_all_64_bits_and_reject_unknown(self):
        m={(0xffffffff,0xfedcba98):(3,7)}
        r=decode_entities(np.array([[65535,65535,0xba98,0xfedc],[0,0,0,0]],float),m)
        np.testing.assert_array_equal(r,[3,-1])
        with self.assertRaisesRegex(AssertionError,'Unknown archived'):decode_entities(np.array([[1,0,0,0]],float),m)

    def test_identity_join_requires_complete_material_and_transform_match(self):
        s=fixture();instances=[]
        for i,o in enumerate(s.items):
            linear=np.diag(1/np.array(o['scale']))
            instances.append(dict(index=i,entity=[i+1,0],worldToLocal=np.column_stack((linear,-linear@s.centers[i])).tolist(),
                albedo=[.5,.5,.5,1],emission=[*s.emission[i],2],layerMask=8))
        self.assertEqual(map_entities(s,dict(instances=instances)),{(1,0):(0,0),(2,0):(1,1)})
        bad=copy.deepcopy(instances);bad[0]['albedo'][0]=.6
        with self.assertRaises(AssertionError):map_entities(s,dict(instances=bad))
        bad=copy.deepcopy(instances);bad[0]['emission'][3]=0
        with self.assertRaisesRegex(AssertionError,'material side'):map_entities(s,dict(instances=bad))
        with self.assertRaisesRegex(AssertionError,'uniquely'):map_entities(s,dict(instances=instances[:1]))

    def test_projection_uses_face_normal_and_rejects_off_surface_receiver(self):
        s=fixture();g=np.array([[[.25,.000003,.4,128+256*255]]]);r=np.array([[[1,0,0,1]]],np.uint32)
        valid,hit,p,n,q=project_receivers(s,g,r,{(1,0):(0,0)})
        np.testing.assert_allclose(p,[[.25,0,.4]],atol=1e-14);np.testing.assert_array_equal(n,[[0,1,0]])
        self.assertLess(q['maximumSurfaceProjectionMeters'],.000004)
        g[0,0,1]=.00003
        with self.assertRaisesRegex(AssertionError,'off its actual'):project_receivers(s,g,r,{(1,0):(0,0)})
        r[0,0,2]=9
        with self.assertRaisesRegex(AssertionError,'instance order'):project_receivers(s,g,r,{(1,0):(0,0)})

    def test_unknown_screen_hit_is_not_a_false_miss_or_wrong_emission(self):
        s=fixture();mask=np.ones(5,bool);truth=np.array([1,1,-1,0,0]);actual=np.array([1,1,-1,-1,-1])
        state=np.array([2|2048,2,4,4,1],np.uint32);rgb=np.array([[0,0,0],[200,200,200],[0,0,0],[0,0,0],[1,1,1]],float)
        d=np.array([[0,-1,0],[0,1,0],[0,0,1],[0,0,1],[0,0,1]],float)
        q=statistics(mask,truth,actual,state,rgb,d,s)
        self.assertEqual((q['correctMisses'],q['falseMisses'],q['unknownNonMissIdentities']),(1,1,1))
        self.assertEqual((q['correctEmitterBackWithNonblackReturn'],q['correctEmitterFrontBelowEmission']),(0,0))
        rgb[0]=10
        self.assertEqual(statistics(mask,truth,actual,state,rgb,d,s)['correctEmitterBackWithNonblackReturn'],1)

    def test_exact_opaque_emitter_back_is_a_black_hit_not_a_miss(self):
        s=fixture();origins=np.array([[0,2.1,0],[0,1.9,0]],float);d=np.array([[0,-1,0],[0,1,0]],float)
        distance,hit,normal=s.intersect(origins,d,face_forward=False)
        np.testing.assert_array_equal(hit,[s.light,s.light]);np.testing.assert_allclose(distance,[.1,.1])
        np.testing.assert_array_equal(s.reflection_visible(d,normal),[False,True])

    def test_energy_components_are_disjoint_and_use_the_whole_group_denominator(self):
        mask=np.array([True,True,True,False]);actual=[np.array([0,1,1,2]),np.array([1,1,1,2])]
        states=[np.array([2,1,2,2]),np.array([2,2,2,2])]
        colors=[np.zeros((4,3)),np.array([[1,1,1],[2,2,2],[-1,-1,-1],[100,100,100]],float)]
        result=decompose_delta(mask,actual,states,colors,np.array([2,1,.5,1]))
        self.assertEqual([q['rays'] for q in result.values()],[1,1,1])
        self.assertAlmostEqual(sum(q['weightedDeltaYPerAllGroupRays'] for q in result.values()),3.5/3)

    def test_archive_reader_preserves_files_and_checks_payload_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);payload=np.array([1,2,3,4],dtype='<f4').tobytes();sha=hashlib.sha256(payload).hexdigest()
            row=dict(frame=0,tag='origin-geometry-full',side='origin-native',path=str(root/'capture/0000.bin'),
                sha256=sha,width=1,height=1,format='R32G32B32A32_SFloat')
            files={'capture.json':dict(complete=True,errors=[],records=[row]),'protocol.json':dict(originStepPolicy='field-diagonal-all-origins'),
                'source-verification.json':dict(unchanged=True,changedSources=[]),
                'source-before-capture.json':dict(files=[dict(path='Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl',sha256=hashlib.sha256(STATE_SOURCE.read_bytes()).hexdigest())])}
            with zipfile.ZipFile(root/'frames.zip','x') as archive:archive.writestr('capture/0000.bin',payload)
            archive_bytes=(root/'frames.zip').read_bytes()
            journal=dict(complete=True,roundTripVerified=True,archive='frames.zip',archiveSha256=hashlib.sha256(archive_bytes).hexdigest(),
                files=[dict(path='capture/0000.bin',bytes=len(payload),sha256=sha)])
            files['raw-retention.json']=journal
            for name,value in files.items():(root/name).write_text(json.dumps(value))
            original={p.name:p.read_bytes() for p in root.iterdir()}
            qualification=dict(countersByLimit={str(k):[0]*32 for k in (.04,.01)})
            with patch('ScreenProbeFirstHitOracle.qualify_origin',return_value=qualification):reader=ArchivedRays(root)
            try:np.testing.assert_array_equal(reader.read(0,'origin-geometry-full','origin-native'),[[[1,2,3,4]]])
            finally:reader.close()
            self.assertEqual({p.name:p.read_bytes() for p in root.iterdir()},original)
            journal['files'][0]['sha256']='0'*64;(root/'raw-retention.json').write_text(json.dumps(journal))
            with patch('ScreenProbeFirstHitOracle.qualify_origin',return_value=qualification):reader=ArchivedRays(root)
            try:
                with self.assertRaises(AssertionError):reader.read(0,'origin-geometry-full','origin-native')
            finally:reader.close()
            self.assertFalse((root/'capture').exists())

    def test_archive_reader_refuses_current_decoder_against_changed_capture_abi(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            files={'capture.json':dict(complete=True,errors=[],records=[]),'protocol.json':dict(originStepPolicy='field-diagonal-all-origins'),
                'source-verification.json':dict(unchanged=True,changedSources=[]),
                'source-before-capture.json':dict(files=[dict(path='Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl',sha256='0'*64)])}
            for name,value in files.items():(root/name).write_text(json.dumps(value))
            qualification=dict(countersByLimit={str(k):[0]*32 for k in (.04,.01)})
            with patch('ScreenProbeFirstHitOracle.qualify_origin',return_value=qualification):
                with self.assertRaisesRegex(AssertionError,'ray-state ABI'):ArchivedRays(root)


if __name__=='__main__':unittest.main()
