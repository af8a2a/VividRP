"""Numerical evidence gates: reject zero GPU counters and changed metadata."""
import copy
import unittest
from ScreenProbeTimingQualification import MARKERS, qualify


def fixture():
    protocol = dict(preflight=[128, 128], measuredSequence=[128, 512, 512, 128] * 2,
                    censorFirstGpuSamples=1, censorLastGpuSamples=1, minimumRetainedGpuSamples=2)
    capture = dict(complete=True, errors=[], isPlaying=True, graphicsApi="Direct3D12",
                   warmup=4, lastFrame=10, records=[], timings=[])
    for i, age in enumerate(protocol["preflight"] + protocol["measuredSequence"]):
        arm = f"{i:02d}-{'float512' if age > 128 else 'half128'}"
        capture["records"].append(dict(arm=arm, completed=True, targetCount=10, otherCount=0))
        tags = [(f, "rays") for f in range(3)] if i < 2 else []
        tags += [(f, tag) for f in (3, 9) for tag in ("geometry", "raw", "radiosity")]
        for frame, tag in tags:
            capture["records"].append(dict(arm=arm, frame=frame, tag=tag, width=1, height=1,
                                            format="R16G16B16A16_SFloat", sha256="unchanged"))
        for name in ("m_Probes", "m_PreviousProbes", "m_PreparedAnchor", "m_ReconstructedAnchor"):
            capture["records"].append(dict(arm=arm, resource=name,
                format="R32G32B32A32_SFloat" if age > 128 else "R16G16B16A16_SFloat"))
        for marker in MARKERS:
            capture["timings"].append(dict(arm=arm, marker=marker, valid=True, wrappedAround=False,
                count=4, samples=[dict(nanoseconds=1000, count=1) for _ in range(4)]))
    return capture, protocol, dict(unchanged=True)


class QualificationTests(unittest.TestCase):
    def test_missing_or_zero_gpu_is_not_zero_cost(self):
        baseline = fixture()
        self.assertTrue(qualify(*baseline)["qualified"])
        for samples in ([], [dict(nanoseconds=0, count=1) for _ in range(4)]):
            capture, protocol, source = copy.deepcopy(baseline)
            capture["timings"][0].update(samples=samples, count=len(samples))
            self.assertFalse(qualify(capture, protocol, source)["performanceConclusionAvailable"])

    def test_same_raw_rgb_does_not_hide_changed_ray_metadata(self):
        capture, protocol, source = fixture()
        row = next(r for r in capture["records"] if r.get("tag") == "rays"
                   and r["arm"] == "01-half128" and r["frame"] == 2)
        row["sha256"] = "distance-and-normal-changed"
        result = qualify(capture, protocol, source)
        self.assertFalse(result["qualified"])
        self.assertEqual((result["firstDifference"]["frame"], result["firstDifference"]["tag"]), (2, "rays"))

    def test_missing_dense_frame_cannot_pass_on_endpoints(self):
        capture, protocol, source = fixture()
        capture["records"] = [r for r in capture["records"] if not
            (r.get("tag") == "rays" and r["arm"] == "01-half128" and r["frame"] == 1)]
        self.assertFalse(qualify(capture, protocol, source)["qualified"])


if __name__ == "__main__":
    unittest.main()
