using System;
using NUnit.Framework;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SdfRadiosityBudgetTests
    {
        [TestCase(37, 0, 16, 128, 37)]
        [TestCase(37, 16, 16, 128, 21)]
        [TestCase(37, 32, 16, 0, 5)]
        [TestCase(37, 37, 16, 128, 0)]
        [TestCase(37, 40, 16, 0, 0)]
        [TestCase(128, 0, 16, 128, 128)]
        [TestCase(129, 0, 16, 128, 16)]
        [TestCase(4096, 4090, 64, 128, 6)]
        [TestCase(4096, 0, 0, 0, 1)]
        [TestCase(0, 0, 16, 128, 0)]
        public void UpdateBudget_RespectsSceneLimitAndRemainingCards(
            int cards, int cursor, int budget, int fullUpdateLimit, int expected)
        {
            Assert.That(SdfDiffuseGatherPass.GetRadiosityUpdateCount(cards, cursor, budget, fullUpdateLimit),
                Is.EqualTo(expected));
        }

        [Test]
        public void UpdateBudget_ChangingBudgetContinuesAnUnfinishedSweep()
        {
            int cursor = SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, 0, 16, 0);
            cursor += SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, cursor, 8, 0);
            cursor += SdfDiffuseGatherPass.GetRadiosityUpdateCount(37, cursor, 8, 128);
            Assert.That(cursor, Is.EqualTo(37));
        }

        [TestCase(4096, 0, 16, 128, 4, 64)]
        [TestCase(4096, 4090, 16, 128, 4, 6)]
        [TestCase(37, 0, 16, 128, 4, 37)]
        [TestCase(37, 16, 16, 0, 2, 21)]
        [TestCase(4096, 0, int.MaxValue, 0, int.MaxValue, 4096)]
        public void UpdateBudget_BoostIsBoundedAndKeepsTheCursor(
            int cards, int cursor, int budget, int fullUpdateLimit, int boost, int expected)
        {
            Assert.That(SdfDiffuseGatherPass.GetRadiosityUpdateCount(cards, cursor, budget, fullUpdateLimit, boost),
                Is.EqualTo(expected));
        }

        [TestCase(1.004f, true)]
        [TestCase(0.996f, true)]
        [TestCase(1.006f, false)]
        [TestCase(0.994f, false)]
        [TestCase(0f, false)]
        public void LightingTolerance_UsesRelativeEnergy(float scale, bool expected)
        {
            var direction = new UnityEngine.Vector4(0, 1, 0, -1);
            var original = new UnityEngine.Vector4(3600, 1800, 900, 1);
            var changed = new UnityEngine.Vector4(original.x * scale, original.y * scale, original.z * scale, 1);
            Assert.That(SdfDiffuseGatherPass.LightingWithinTolerance(direction, original, direction, changed, .005f),
                Is.EqualTo(expected));
        }

        [Test]
        public void LightingTolerance_RejectsShadowChangesAndInvalidEnergy()
        {
            var direction = new UnityEngine.Vector4(0, 1, 0, -1);
            var color = new UnityEngine.Vector4(3600, 1800, 900, 1);
            var changed = color;
            changed.w = 0;
            Assert.That(SdfDiffuseGatherPass.LightingWithinTolerance(direction, color, direction, changed, .005f), Is.False);
            changed = color;
            changed.x = float.NaN;
            Assert.That(SdfDiffuseGatherPass.LightingWithinTolerance(direction, color, direction, changed, .005f), Is.False);
        }

        [Test]
        public void LightingTolerance_AccumulatedDriftExitsTheAcceptedBaseline()
        {
            var direction = new UnityEngine.Vector4(0, 1, 0, -1);
            var accepted = new UnityEngine.Vector4(1000, 1000, 1000, 1);
            for (int step = 1; step <= 8; step++)
            {
                var current = new UnityEngine.Vector4(1000 + step, 1000 + step, 1000 + step, 1);
                bool matches = SdfDiffuseGatherPass.LightingWithinTolerance(direction, accepted, direction, current, .005f);
                if (step >= 6)
                    Assert.That(matches, Is.False, "The comparison baseline must not advance for tolerated changes.");
            }
        }

        [Test]
        public void SmallTransforms_KeepTheBasisAndBoundTranslation()
        {
            var identity = UnityEngine.Matrix4x4.identity;
            Assert.That(VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.IsSmallRadiosityTransformChange(
                identity, UnityEngine.Matrix4x4.Translate(new UnityEngine.Vector3(.02f, 0, 0))), Is.True);
            Assert.That(VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.IsSmallRadiosityTransformChange(
                identity, UnityEngine.Matrix4x4.Translate(new UnityEngine.Vector3(.1f, 0, 0))), Is.False);
            Assert.That(VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.IsSmallRadiosityTransformChange(
                identity, UnityEngine.Matrix4x4.Rotate(UnityEngine.Quaternion.Euler(0, .1f, 0))), Is.False);
        }

        [TestCase(1800f, 3600f, true, 2f)]
        [TestCase(3600f, 1800f, true, .5f)]
        [TestCase(3600f, 0f, true, 0f)]
        [TestCase(0f, 3600f, false, 0f)]
        [TestCase(100f, 1000f, false, 0f)]
        [TestCase(10000f, 20000f, false, 0f)]
        public void LinearRelight_RequiresRecoverableUnsaturatedEnergy(float before, float after, bool expected, float ratio)
        {
            var a = new UnityEngine.Vector4(before, before, before, 1);
            var b = new UnityEngine.Vector4(after, after, after, 1);
            bool valid = SdfDiffuseGatherPass.TryGetRadiosityLightScale(a, b, 8, out var scale);
            Assert.That(valid, Is.EqualTo(expected));
            if (valid)
                Assert.That(scale.x, Is.EqualTo(ratio));
        }

        [Test]
        public void LinearRelight_RejectsShadowChangesAndMissingSpectralChannels()
        {
            var a = new UnityEngine.Vector4(1000, 0, 500, 1);
            var b = new UnityEngine.Vector4(2000, 0, 1000, 1);
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightScale(a, b, 8, out var scale), Is.True);
            Assert.That(scale.y, Is.EqualTo(1f));
            b.y = 1;
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightScale(a, b, 8, out _), Is.False);
            b.y = 0;
            b.w = 0;
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightScale(a, b, 8, out _), Is.False);
            b.w = 1;
            b.x = float.NaN;
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightScale(a, b, 8, out _), Is.False);
        }

        [Test]
        public void LinearRelight_StableCallsDoNotAllocate()
        {
            var previous = new UnityEngine.Vector4(1800, 1800, 1800, 1);
            var current = new UnityEngine.Vector4(3600, 3600, 3600, 1);
            bool valid = true;
            for (int i = 0; i < 256; i++)
                valid &= SdfDiffuseGatherPass.TryGetRadiosityLightScale(previous, current, 8, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
                valid &= SdfDiffuseGatherPass.TryGetRadiosityLightScale(previous, current, 8, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(valid, Is.True);
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void MixedRelight_RequiresTheSameScaleForEveryContributingSource()
        {
            var black = new UnityEngine.Vector4(0, 0, 0, 1);
            var sun = new UnityEngine.Vector4(1000, 1000, 1000, 1);
            var brightSun = new UnityEngine.Vector4(2000, 2000, 2000, 1);
            var emission = new UnityEngine.Vector4(120, 40, 12, 0);
            var brightEmission = emission * 2;
            Assert.That(SdfDiffuseGatherPass.TryGetRadiositySourceScale(black, black,
                emission, brightEmission, 8, out var scale), Is.True);
            Assert.That(scale.x, Is.EqualTo(2f));
            Assert.That(SdfDiffuseGatherPass.TryGetRadiositySourceScale(sun, brightSun,
                emission, brightEmission, 8, out _), Is.True);
            Assert.That(SdfDiffuseGatherPass.TryGetRadiositySourceScale(sun, sun,
                emission, brightEmission, 8, out _), Is.False, "A constant sun must not scale with a changing emitter.");
        }

        [Test]
        public void EmissionRelight_IndependentEmitterChangesCannotScaleTheWholeScene()
        {
            var scale = UnityEngine.Vector4.one;
            int channels = 0;
            var emitter = new UnityEngine.Vector4(10, 5, 0, 0);
            Assert.That(VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.TryMergeEmissionScale(
                emitter, emitter * 2, ref scale, ref channels), Is.True);
            Assert.That(VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.TryMergeEmissionScale(
                emitter, emitter, ref scale, ref channels), Is.False);
        }

        [Test]
        public void EmissionRelight_ToleranceMustNotDriftAcrossManyEmitters()
        {
            var scale = UnityEngine.Vector4.one;
            int channels = 0;
            var emitter = new UnityEngine.Vector4(1, 0, 0, 0);
            bool allAccepted = true;
            for (int i = 0; i < 100; i++)
                allAccepted &= VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.TryMergeEmissionScale(
                    emitter, emitter * (2f + .00001f * i), ref scale, ref channels);
            Assert.That(allAccepted, Is.False);
            Assert.That(scale.x, Is.EqualTo(2f));
        }

        [Test]
        public void EmissionRelight_PreservesOnlyTheRepresentedTransportInputs()
        {
            var original = new VividRP.Runtime.GPUDriven.Meshlets.VividMeshSdfGpuScene.DiffuseSurface
            {
                AlbedoAndValid = UnityEngine.Vector4.one,
                CardMinAndFirst = new UnityEngine.Vector4(0, 0, 0, 1),
                CardExtent = UnityEngine.Vector4.one,
            };
            var changed = original;
            changed.Emission = new UnityEngine.Vector4(1, 1, 1, original.Emission.w);
            Assert.That(original.HasSameTransport(changed), Is.True);
            changed.Emission.w = (float)UnityEngine.Rendering.CullMode.Back;
            Assert.That(original.HasSameTransport(changed), Is.False, "Material sidedness changes invalidate cached transport.");
            changed = original;
            changed.AlbedoAndValid.x = .5f;
            Assert.That(original.HasSameTransport(changed), Is.False);
            changed = original;
            changed.CardExtent.x = 2f;
            Assert.That(original.HasSameTransport(changed), Is.False);
        }

        [Test]
        public void SkyRelight_RequiresACommonScaleAndAnUnsaturatedPeakBound()
        {
            var black = new UnityEngine.Vector4(0, 0, 0, 1);
            var sky = UnityEngine.Vector4.one;
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightingScale(black, black, black, black,
                sky, sky * 2, 120f, 8, out var scale), Is.True);
            Assert.That(scale.x, Is.EqualTo(2f));
            var sun = new UnityEngine.Vector4(900, 900, 900, 1);
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightingScale(sun, sun, black, black,
                sky, sky * 2, 120f, 8, out _), Is.False);
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightingScale(black, black, black, black,
                sky, sky * 2, 10000f, 8, out _), Is.False);
            Assert.That(SdfDiffuseGatherPass.TryGetRadiosityLightingScale(black, black, black, black,
                sky, sky * 2, float.NaN, 8, out _), Is.False);
        }

        [TestCase(37, 128, 64, 8, 8, 8)]
        [TestCase(129, 128, 64, 8, 8, 1)]
        [TestCase(37, 0, 64, 8, 8, 1)]
        [TestCase(37, 128, 4096, 8, 8, 1)]
        [TestCase(37, 128, 64, 3, 8, 3)]
        [TestCase(37, 128, 64, 8, 2, 2)]
        [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, 8, 8, 1)]
        public void RelightSweeps_BoundSmallSceneWork(int cards, int limit, int samples, int remaining, int requested, int expected)
        {
            Assert.That(SdfDiffuseGatherPass.GetRadiosityRelightSweepCount(cards, limit, samples, remaining, requested), Is.EqualTo(expected));
        }

        [Test]
        public void UpdateBudget_StableCallsDoNotAllocate()
        {
            int total = 0;
            for (int i = 0; i < 256; i++)
                total += SdfDiffuseGatherPass.GetRadiosityUpdateCount(4096, i, 16, 128, 4);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
                total += SdfDiffuseGatherPass.GetRadiosityUpdateCount(4096, i, 16, 128, 4);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);
        }
    }
}
