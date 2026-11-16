"""A shared-random-number bias sensitivity must retain geometry and material energy."""
import unittest

import numpy as np

from CornellDirectOriginBias import paired_direct


class OriginBiasTests(unittest.TestCase):
    def test_query_offset_preserves_input_normal_and_paired_zero(self):
        class DistanceLight:
            def direct(self,positions,normals,uv):
                return (1/(3-positions[:,1])**2)[:,None]*np.broadcast_to([1.,2.,3.],positions.shape)
        n=np.array([0.,2.,0.]);r=paired_direct(DistanceLight(),[0,0,0],n,16384,1427)
        np.testing.assert_array_equal(n,[0,2,0])
        np.testing.assert_array_equal(r[0]['pairedDeltaRgb'],[0,0,0])
        for q in r:
            expected=(1/(3-q['offsetMeters'])**2-1/9)*np.array([1,2,3])
            np.testing.assert_allclose(q['pairedDeltaRgb'],expected,rtol=1e-11,atol=1e-15)


if __name__=='__main__':unittest.main()
