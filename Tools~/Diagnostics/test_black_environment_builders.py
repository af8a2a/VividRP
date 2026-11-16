"""Protect no-sky isolation, history cleanup and exact-input diagnostic contracts."""
import unittest
from pathlib import Path

from BuildScreenProbeBlackEnvironmentMicro import build as micro
from BuildScreenProbeBlackEnvironmentQuality import build as quality
from BuildSdfBlackEnvironmentCandidate import build_source


class BlackEnvironmentBuilderTests(unittest.TestCase):
    def test_micro_owns_outputs_and_restores_native_diagnostic_bits(self):
        source,shaders=micro(Path('C:/micro'))
        self.assertNotIn('__',source)
        self.assertIn('string privateMethod="none"',source)
        self.assertNotIn('temporal-candidate.compute.txt',shaders)
        self.assertIn('("corrected",nativeDiagnostics|33554432)',source)
        self.assertIn('activeSkyType!=SkyType.None',source)
        self.assertIn('SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",nativeDiagnostics)',source)

    def test_quality_sets_explicit_mode_and_discards_experimental_histories(self):
        baseline=quality(Path('C:/quality'),False,False)
        candidate=quality(Path('C:/quality'),True,False)
        self.assertNotIn('__',candidate)
        self.assertIn('warmup = 1024, count = 128',candidate)
        self.assertIn('phase = 1; frame = -1',candidate)
        self.assertIn('SetValue(null, 0)',baseline)
        self.assertIn('SetValue(null, 33554432)',candidate)
        self.assertIn('screenProbeSamplingGuide.Override(false)',candidate)
        self.assertIn('CleanupCamera").Invoke(histories',candidate)
        self.assertIn('ReadQuality(context,qualityDepth,"input-depth","hash")',candidate)

    def test_quality_can_validate_the_default_guided_path_without_increasing_ray_budget(self):
        source=quality(Path('C:/quality'),True,True,9481)
        self.assertIn('screenProbeSamplingGuide.Override(true)',source)
        self.assertIn('Run("capture",9481)',source)
        self.assertIn('spacing = 16, rays = 8',source)

    def test_long_window_keeps_exact_statistics_and_bounds_binary_growth(self):
        source=quality(Path('C:/quality'),True,True,1733,1024)
        self.assertIn('warmup = 4096, count = 1024',source)
        self.assertIn('blocks=16',source)
        self.assertIn('count<=128 || f<warmup+64',source)
        self.assertIn('PublishQualityStatistics()',source)
        self.assertIn('Out-of-order final statistics',source)

    def test_trace_candidate_is_generated_without_changing_the_production_source(self):
        path=Path(__file__).resolve().parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute'
        original=path.read_bytes();candidate=build_source()
        self.assertEqual(original,path.read_bytes())
        self.assertNotIn(b'SdfKnownBlackEnvironmentMiss',original)
        self.assertEqual(candidate.count('bool SdfKnownBlackEnvironmentMiss('),1)
        self.assertEqual(candidate.count('#pragma kernel '),9)
        self.assertIn('else if (missState == RayMiss && !useSky',candidate)

    def test_transient_shader_ownership_and_compilation_are_shared_by_both_captures(self):
        sources=(micro(Path('C:/micro'))[0],quality(Path('C:/quality'),True,False))
        for source in sources:
            self.assertIn('candidate-tracing.compute.txt',source)
            self.assertIn('experimentalTracePrograms',source)
            self.assertIn('CompileComputeShaderVariant',source)
            self.assertIn('object.ReferenceEquals(field.GetValue(experimentalTraceOwner),experimentalTrace)',source)
            self.assertIn('CleanupExperimentalTracing();',source)
        self.assertIn('SetupExperimentalTracing(privatePass)',sources[0])
        self.assertIn('SetupExperimentalTracing(qualityPass)',sources[1])
        self.assertIn('Owned experimental trace shader required.',sources[0])


if __name__=='__main__':unittest.main()
