"""Independent geometric and radiometric invariants for the Cornell oracle."""
import copy
import json
import unittest

import numpy as np

from CornellLambertReference import FIXTURE, Scene, camera_points, cosine_sample, estimate


class ReferenceTests(unittest.TestCase):
    def setUp(self):
        self.fixture = json.loads(FIXTURE.read_text(encoding="utf-8-sig"))

    def test_box_inside_and_rotated_hits(self):
        scene = Scene(self.fixture)
        floor = scene.items.index(next(o for o in scene.items if o["name"] == "Floor"))
        d, index, normal = scene.intersect(np.array([[0., -.1, 2.]]), np.array([[0., 1., 0.]]))
        self.assertEqual(index[0], floor)
        self.assertAlmostEqual(d[0], .1)
        np.testing.assert_array_equal(normal[0], [0, -1, 0])
        # Start along the local +X axis of the rotated short block.
        i = next(i for i, o in enumerate(scene.items) if o["name"] == "Short White Block")
        axis = scene.rotations[i][:, 0]
        d, index, normal = scene.intersect((scene.centers[i]+axis*.6)[None], -axis[None])
        self.assertEqual(index[0], i)
        self.assertAlmostEqual(d[0], .6-scene.half[i, 0])
        np.testing.assert_allclose(normal[0], axis, atol=1e-12)

    def test_rectangle_view_factor_and_black_back(self):
        # Isolate the emitter, moving solid boxes away; the geometry is still traced.
        f = copy.deepcopy(self.fixture)
        for o in f["geometry"]:
            if not any(o["emissionLinear"]):
                o["position"][0] += 1000
        scene = Scene(f)
        h = 3.
        point = scene.centers[scene.light]-np.array([0, h, 0])
        rng = np.random.default_rng(671)
        values = scene.direct(np.broadcast_to(point, (65536, 3)), np.tile([0., 1., 0.], (65536, 1)), rng.random((65536, 2)))[:, 0]
        a, b = scene.half[scene.light, [0, 2]]/h
        exact = scene.emission[scene.light, 0]*2/np.pi*(a/np.sqrt(1+a*a)*np.arctan(b/np.sqrt(1+a*a))
            + b/np.sqrt(1+b*b)*np.arctan(a/np.sqrt(1+b*b)))
        self.assertLess(abs(values.mean()-exact), 5*values.std(ddof=1)/np.sqrt(len(values)))
        above = scene.centers[scene.light]+[0, .035, 0]
        black = scene.direct(np.tile(above, (64, 1)), np.tile([0., -1., 0.], (64, 1)), rng.random((64, 2)))
        np.testing.assert_array_equal(black, np.zeros_like(black))

    def test_cosine_distribution_and_light_linearity(self):
        rng = np.random.default_rng(8)
        normals = np.tile([0., 1., 0.], (65536, 1))
        directions = cosine_sample(normals, rng.random((len(normals), 2)))
        np.testing.assert_allclose(np.linalg.norm(directions, axis=1), 1, atol=1e-12)
        self.assertTrue(np.all(directions[:, 1] >= 0))
        self.assertAlmostEqual(directions[:, 1].mean(), 2/3, delta=.003)
        scene = Scene(self.fixture)
        point = camera_points(scene, self.fixture)[3]
        before = estimate(scene, point["position"], point["normal"], 1024, 2, 8)
        scene.emission *= 4
        after = estimate(scene, point["position"], point["normal"], 1024, 2, 8)
        np.testing.assert_allclose(after["meanRgb"], np.array(before["meanRgb"])*4, atol=1e-12)
        np.testing.assert_allclose(after["meanRgbByOrder"], np.array(before["meanRgbByOrder"])*4, atol=1e-12)

    def test_black_scene_does_not_double_count_bsdf_light_hits(self):
        scene = Scene(self.fixture)
        scene.albedo[:] = 0
        point = camera_points(scene, self.fixture)[3]
        direct = estimate(scene, point["position"], point["normal"], 4096, 0, 711)
        bounced = estimate(scene, point["position"], point["normal"], 4096, 3, 711)
        np.testing.assert_array_equal(direct["meanRgb"], bounced["meanRgb"])
        np.testing.assert_array_equal(np.array(bounced["meanRgbByOrder"])[1:], np.zeros((3, 3)))


if __name__ == "__main__":
    unittest.main()
