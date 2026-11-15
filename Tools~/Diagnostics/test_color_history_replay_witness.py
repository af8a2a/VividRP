"""Forensic output must preserve a single differing word, including signed zero."""
import tempfile
import unittest
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeColorHistoryReplay import failure_details,publication_equal


class ColorHistoryReplayWitnessTests(unittest.TestCase):
    def report(self,first,second,format):
        with tempfile.TemporaryDirectory(prefix="vivid-private-witness-") as directory:
            root=Path(directory);rows=[]
            for name,values in (("native",first),("baseline",second)):
                path=root/(name+".bin");path.write_bytes(values.tobytes())
                rows.append(dict(failureKey="7/history/full",frame=7,side=name,path=str(path),format=format,width=2,height=2))
            return failure_details(dict(records=rows))[0]

    def test_signed_zero_remains_a_failure_with_raw_half_word(self):
        first=np.zeros((2,2,4),dtype="<f2");second=first.copy();second[1,1,0]=-0.0
        result=self.report(first,second,"R16G16B16A16_SFloat")
        self.assertEqual(result["firstLocalPixel"],[1,1])
        self.assertEqual(result["maxAbsoluteDifference"],0)
        self.assertEqual(result["words"],[ [0,0,0,0],[32768,0,0,0] ])
        self.assertEqual(result["byteDifferences"],1)

    def test_packed_geometry_is_not_decoded_as_color(self):
        first=np.zeros((2,2,1),dtype="<u4");second=first.copy();second[0,1,0]=0xdeadbeef
        result=self.report(first,second,"R32_UInt")
        self.assertEqual(result["firstLocalPixel"],[1,0])
        self.assertEqual(result["words"],[[0],[0xdeadbeef]])

    def test_publication_requires_protected_channels_and_nonempty_hash(self):
        self.assertTrue(publication_equal(dict(metadataSha256="a"*64),dict(metadataSha256="a"*64),"moments",True))
        self.assertFalse(publication_equal(dict(metadataSha256="a"*64),dict(metadataSha256="b"*64),"moments",True))
        with self.assertRaises(AssertionError):publication_equal(dict(metadataSha256=None),dict(metadataSha256=None),"moments",True)

    def test_legacy_moment_witness_allows_mean_change_but_rejects_variance_change(self):
        with tempfile.TemporaryDirectory(prefix="vivid-publication-witness-") as directory:
            root=Path(directory);a=np.array([[[1,2,3,.5]]],dtype="<f2");b=a.copy();b[...,0]=4
            first=root/"before.bin";second=root/"after.bin";first.write_bytes(a.tobytes());second.write_bytes(b.tobytes())
            record=lambda p:dict(path=str(p),width=1,height=1,format="R16G16B16A16_SFloat")
            self.assertTrue(publication_equal(record(first),record(second),"moments",False))
            b[...,1]=np.nextafter(b[...,1],np.float16(np.inf));second.write_bytes(b.tobytes())
            self.assertFalse(publication_equal(record(first),record(second),"moments",False))


if __name__=="__main__":unittest.main()
