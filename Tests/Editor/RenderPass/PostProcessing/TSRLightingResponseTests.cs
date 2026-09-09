using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class TSRLightingResponseTests
    {
        [SetUp]
        public void RequireComputeDevice()
            => Assume.That(SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback, Is.True);

        [TestCase(false)]
        [TestCase(true)]
        public void LocalClipping_StableNoisyReceiverRestoresAtMostHalfTheClippedHistory(bool waveOps)
        {
            if (waveOps) Assume.That(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, Is.True);
            using var fixture = new Fixture(waveOps);
            var input = LocalClippingInput();
            Snapshot clipped = fixture.Run(input);
            input.LumaInstability = 1;
            Snapshot relaxed = fixture.Run(input);
            float displacement = input.History.r - clipped.AcceptedHistory.r;
            Assert.That(displacement, Is.GreaterThan(0.02f));
            Assert.That(relaxed.AcceptedHistory.r - clipped.AcceptedHistory.r, Is.GreaterThan(0.005f));
            Assert.That(relaxed.AcceptedHistory.r - clipped.AcceptedHistory.r, Is.LessThanOrEqualTo(displacement * 0.5f + 0.001f));
            Assert.That(relaxed.PendingState, Is.Zero);
            Assert.That(relaxed.SampleCount, Is.EqualTo(16));
        }

        [TestCase(0)] // Receiver motion.
        [TestCase(1)] // Invalid history depth despite a stationary motion vector.
        [TestCase(2)] // Immature history.
        [TestCase(3)] // Pending lighting confirmation from the preceding frame.
        [TestCase(4)] // A silhouette inside the reconstruction footprint.
        [TestCase(5)] // Discontinuous receiver.
        public void LocalClipping_UnreliableHistoryRetainsFullClipping(int reason)
        {
            using var fixture = new Fixture();
            var input = LocalClippingInput();
            if (reason == 0) input.MotionPixels = 0.5f;
            if (reason == 1) input.HistoryDepth = 0.56f;
            if (reason == 2) input.HistorySamples = 2;
            if (reason == 3) input.PreviousState = 6;
            if (reason == 4) input.NearbyDepthFeatureOffset = new Vector2Int(3, 3);
            if (reason == 5) input.DepthError = 0.03f;
            Snapshot clipped = fixture.Run(input);
            input.LumaInstability = 1;
            Snapshot guarded = fixture.Run(input);
            Assert.That(ColorError(guarded.AcceptedHistory, clipped.AcceptedHistory), Is.LessThan(0.001f));
        }

        [TestCase(0.6f, 0.55f)]
        [TestCase(0.2f, 0.8f)]
        [TestCase(0.8f, 0.2f)]
        public void LocalClipping_UniformLightingStepDoesNotRestoreOutdatedColor(float history, float current)
        {
            using var fixture = new Fixture();
            Snapshot result = fixture.Run(new Input { History = Gray(history), Current = Gray(current),
                NeighborhoodLow = current, NeighborhoodHigh = current, LumaInstability = 1 });
            Assert.That(ColorError(result.AcceptedHistory, Gray(current)), Is.LessThan(0.001f));
        }

        private static Input LocalClippingInput() => new Input
        {
            Current = Gray(0.55f), History = Gray(0.6f), NeighborhoodLow = 0.48f, NeighborhoodHigh = 0.52f
        };

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void SustainedLightingOrChromaChange_ConfirmsAfterThreeRecursiveFrames(int direction)
        {
            using var fixture = new Fixture();
            Confirm(fixture, DirectionInput(direction), direction);
        }

        [Test]
        public void CombinedChromaDifference_DoesNotRequireEitherComponentToExceedTheThreshold()
        {
            using var fixture = new Fixture();
            // Delta Co=.28 and Cg=.25 jointly exceed the .35 chroma threshold.
            Confirm(fixture, new Input { Current = new Color(0.63f, 0.85f, 0.07f), History = Gray(0.6f),
                ChromaPattern = true, NeighborhoodColorA = new Color(0.73f, 0.95f, 0.17f), NeighborhoodColorB = Gray(0.3f) }, 3);
        }

        [TestCase(1f, 0.56f, 1)]
        [TestCase(0.56f, 1f, 2)]
        public void RecursiveLightingChange_ConfirmsAndExpiresOutdatedResurrection(float current, float previous, int direction)
        {
            using var fixture = new Fixture();
            // Static confirmation uses the .28 entry threshold and .14 continuation
            // threshold, even at instability 1. Feed actual GPU history updates;
            // the confirmed change must expire the incompatible resurrection cache.
            Confirm(fixture, new Input { Current = Gray(current), History = Gray(previous), Resurrection = Gray(previous),
                ResurrectionFrames = 6, LumaInstability = 1, NeighborhoodLow = 0.3f, NeighborhoodHigh = 1 }, direction);
        }

        [TestCase(1f, 0.56f, 0.67f, 0.695872f)]
        [TestCase(0.56f, 1f, 0.89f, 0.864128f)]
        public void SoftLightingConfirmation_CapsHistoryContributionAndRecoversSampleCount(float current, float previous, float directExpected, float recursiveExpected)
        {
            using var fixture = new Fixture();
            int direction = current > previous ? 1 : 2;
            var input = new Input { Current = Gray(current), History = Gray(previous), PreviousState = direction * 4 + 2,
                Resurrection = Gray(previous), ResurrectionFrames = 6, NeighborhoodLow = 0.3f, NeighborhoodHigh = 1 };
            Snapshot direct = AssertStep(fixture, input, 0, true);
            Assert.That(ColorError(direct.AcceptedHistory, input.History), Is.LessThan(0.003f), "Keep clipped history for the soft blend.");
            Assert.That(ColorError(direct.Updated, Gray(directExpected)), Is.LessThan(0.003f));
            Feed(input, direct); input.Current = input.History;
            Assert.That(AssertStep(fixture, input, 0, false).SampleCount, Is.EqualTo(5));
            input = new Input { Current = Gray(current), History = Gray(previous), Resurrection = Gray(previous),
                ResurrectionFrames = 6, NeighborhoodLow = 0.3f, NeighborhoodHigh = 1 };
            Snapshot recursive = null;
            for (int frame = 1; frame <= 3; frame++)
            {
                recursive = AssertStep(fixture, input, frame < 3 ? direction * 4 + frame : 0, frame == 3);
                Feed(input, recursive);
            }
            Assert.That(ColorError(recursive.Updated, Gray(recursiveExpected)), Is.LessThan(0.003f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AlternatingHighAmplitudeSamples_RetainHistoryWithoutConfirmingAFalseLightingChange(bool waveOps)
        {
            if (waveOps) Assume.That(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, Is.True);
            using var fixture = new Fixture(waveOps);
            var input = new Input { History = Gray(0.5f) };
            for (int frame = 0; frame < 16; frame++)
            {
                input.Current = Gray(frame % 2 == 0 ? 0.9f : 0.1f);
                Snapshot result = AssertStep(fixture, input, frame % 2 == 0 ? 5 : 9, false);
                Assert.That(result.Updated.r, Is.InRange(0.45f, 0.55f));
                Feed(input, result);
            }
        }

        [Test]
        public void AnInterruptedColorDifference_StartsANewThreeFrameConfirmation()
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1);
            Feed(input, AssertStep(fixture, input, 5, false));
            input.Current = input.History;
            Feed(input, AssertStep(fixture, input, 0, false));
            input.Current = Gray(0.8f);
            Feed(input, AssertStep(fixture, input, 5, false));
            Feed(input, AssertStep(fixture, input, 6, false));
            AssertStep(fixture, input, 0, true);
        }

        [Test]
        public void AReversedDifference_DiscardsTheOldDirectionCount()
        {
            using var fixture = new Fixture();
            var input = new Input { Current = Gray(0.8f), History = Gray(0.5f) };
            Feed(input, AssertStep(fixture, input, 5, false));
            Feed(input, AssertStep(fixture, input, 6, false));
            input.Current = Gray(0.1f);
            Feed(input, AssertStep(fixture, input, 9, false));
            Feed(input, AssertStep(fixture, input, 10, false));
            AssertStep(fixture, input, 0, true);
        }

        [TestCase(0.26f, 0, 0, false)]
        [TestCase(0.29f, 0, 5, false)]
        [TestCase(0.26f, 5, 6, false)]
        [TestCase(0.26f, 6, 0, true)]
        [TestCase(0.13f, 5, 0, false)]
        [TestCase(0.26f, 9, 0, false)]
        [TestCase(0.15f, 0, 0, false)]
        [TestCase(0.15f, 5, 6, false)]
        [TestCase(0.15f, 6, 0, true)]
        public void ContinuationMargin_RequiresTheExistingDirectionAndStillHasAnExit(float difference, int previousState, int nextState, bool confirmed)
        {
            using var fixture = new Fixture();
            AssertStep(fixture, new Input { Current = Gray(1), History = Gray(1 - difference),
                PreviousState = previousState, NeighborhoodHigh = 1 }, nextState, confirmed);
        }

        [TestCase(0f, 0, 5)]
        [TestCase(2f, 6, 0)]
        public void LumaInstability_RelaxesOnlyTheMovingColorThreshold(float motionPixels, int previousState, int nextState)
        {
            using var fixture = new Fixture();
            AssertStep(fixture, new Input { Current = Gray(1), History = Gray(0.7f),
                LumaInstability = 1, MotionPixels = motionPixels, PreviousState = previousState, NeighborhoodHigh = 1 }, nextState, false);
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(9)]
        [TestCase(31)]
        public void LegacyInvalidOrOtherDirectionState_DoesNotPrematurelyConfirm(int previousState)
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1); input.PreviousState = previousState;
            AssertStep(fixture, input, 5, false);
        }

        [Test]
        public void MovingReceivers_KeepImmediateRejectionAndClearPendingState()
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1); input.MotionPixels = 2; input.PreviousState = 6;
            AssertStep(fixture, input, 0, true);
            AssertStep(fixture, new Input { Current = Gray(0.5f), History = Gray(0.5f), MotionPixels = 2, PreviousState = 6 }, 0, false);
        }

        [Test]
        public void AStationaryDepthEdge_ClearsPendingAndRetainsHistoryAcrossStrongColorDifferences()
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1); input.DepthError = 0.02f; input.PreviousState = 6;
            for (int frame = 1; frame <= 3; frame++)
                Feed(input, AssertStep(fixture, input, 0, false));
        }

        [Test]
        public void AMovingDepthEdge_KeepsImmediateColorRejection()
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1); input.DepthError = 0.02f; input.PreviousState = 6; input.MotionPixels = 2;
            AssertStep(fixture, input, 0, true);
        }

        [TestCase(-3, 0, false)]
        [TestCase(3, 0, false)]
        [TestCase(0, -3, false)]
        [TestCase(0, 3, false)]
        [TestCase(-3, -3, false)]
        [TestCase(3, 3, false)]
        [TestCase(-3, 0, true)]
        [TestCase(3, 0, true)]
        [TestCase(0, -3, true)]
        [TestCase(0, 3, true)]
        [TestCase(-3, -3, true)]
        [TestCase(3, 3, true)]
        public void NearbyGeometryWithAContinuousCenter_PreservesHistoryWithoutLightingConfirmation(int featureX, int featureY, bool waveOps)
        {
            if (waveOps) Assume.That(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, Is.True);
            using var fixture = new Fixture(waveOps);
            Input input = DirectionInput(1); input.PreviousState = 6;
            input.NearbyDepthFeatureOffset = new Vector2Int(featureX, featureY);
            for (int frame = 1; frame <= 3; frame++)
                Feed(input, AssertStep(fixture, input, 0, false));
        }

        [Test]
        public void SmallSamplingNoise_ClearsPendingWithoutDiscardingHistory()
        {
            using var fixture = new Fixture();
            for (int frame = 0; frame < 16; frame++)
            {
                var input = new Input { Current = Gray(0.5f + ((frame * 7 % 17) - 8) * 0.003125f),
                    History = Gray(0.5f), PreviousState = 6 };
                Snapshot result = AssertStep(fixture, input, 0, false);
                Assert.That(result.SampleCount, Is.EqualTo(16).Within(0.003f));
                Assert.That(Mathf.Abs(result.Updated.r - 0.5f), Is.LessThan(0.006f));
            }
        }

        [Test]
        public void PendingState_SurvivesAnExpiredResurrectionColorCache()
        {
            using var fixture = new Fixture();
            Input input = DirectionInput(1); input.PreviousState = 5; input.ResurrectionFrames = 0;
            AssertStep(fixture, input, 6, false);
            input.PreviousState = 6;
            AssertStep(fixture, input, 0, true);
            Snapshot stored = fixture.Run(new Input { Current = Gray(0.5f), History = Gray(0.5f),
                ForceUpdateState = true, ForcedUpdateState = 5, ResurrectionFrames = 0 });
            Assert.That(stored.PendingState, Is.EqualTo(5));
            Assert.That(stored.ResurrectionFrames, Is.Zero);
        }

        [Test]
        public void InvalidPrimary_ClearsPendingAndCanRecoverCompatibleResurrection()
        {
            using var fixture = new Fixture();
            Snapshot result = fixture.Run(new Input { Current = Gray(1), History = Gray(0.1f), HistorySamples = 0,
                PreviousState = 6, Resurrection = Gray(0.9f), ResurrectionFrames = 6, NeighborhoodHigh = 1 });
            Assert.That(result.Accepted, Is.Zero);
            Assert.That(result.AcceptedAlpha, Is.Zero);
            Assert.That(result.PendingState, Is.Zero);
            Assert.That(result.Updated.r, Is.EqualTo(0.965f).Within(0.003f));
            Assert.That(result.ResurrectionFrames, Is.EqualTo(5));
        }

        private static void Confirm(Fixture fixture, Input input, int direction)
        {
            for (int frame = 1; frame <= 3; frame++)
                Feed(input, AssertStep(fixture, input, frame < 3 ? direction * 4 + frame : 0, frame == 3));
        }
        private static Snapshot AssertStep(Fixture fixture, Input input, int state, bool confirmed)
        {
            Snapshot result = fixture.Run(input);
            bool hardRejected = confirmed && input.MotionPixels > 1;
            Assert.That(result.PendingState, Is.EqualTo(state));
            Assert.That(result.Accepted, Is.EqualTo(hardRejected ? 0 : 1));
            if (confirmed)
            {
                Assert.That(result.AcceptedAlpha, Is.EqualTo(hardRejected ? -1 : -2));
                Assert.That(result.ResurrectionFrames, Is.Zero);
                Assert.That(ColorError(result.ResurrectionColor, Color.clear), Is.LessThan(0.003f));
                if (hardRejected) Assert.That(ColorError(result.Updated, input.Current), Is.LessThan(0.003f));
                else
                {
                    Assert.That(result.SampleCount, Is.EqualTo(4));
                    Assert.That(ColorError(result.Updated, input.Current),
                        Is.LessThanOrEqualTo(ColorError(result.AcceptedHistory, input.Current) * 0.75f + 0.003f));
                }
            }
            else Assert.That(result.AcceptedAlpha, Is.GreaterThanOrEqualTo(0));
            return result;
        }
        private static float ColorError(Color a, Color b)
            => Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Abs(a.b - b.b));

        private static void Feed(Input input, Snapshot result)
        {
            input.History = result.Updated; input.HistorySamples = result.SampleCount;
            input.Resurrection = result.ResurrectionColor; input.ResurrectionFrames = result.ResurrectionFrames;
            input.PreviousState = result.PendingState;
        }
        private static Input DirectionInput(int direction)
        {
            if (direction <= 2) return new Input { Current = Gray(direction == 1 ? 0.8f : 0.2f), History = Gray(direction == 1 ? 0.2f : 0.8f) };
            Color a = direction <= 4 ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.1f, 0.9f, 0.1f);
            Color b = direction <= 4 ? new Color(0.1f, 0.1f, 0.9f) : new Color(0.9f, 0.1f, 0.9f);
            return new Input { Current = direction % 2 == 1 ? a : b, History = direction % 2 == 1 ? b : a,
                ChromaPattern = true, NeighborhoodColorA = a, NeighborhoodColorB = b };
        }

        [TestCase(8, 0.25f, true)]
        [TestCase(8, 0.75f, true)]
        [TestCase(13, 0.25f, true)]
        [TestCase(13, 0.75f, true)]
        [TestCase(13, -0.75f, true)]
        [TestCase(13, 0.75f, false)]
        public void PendingReprojection_UsesPointStateAcrossScaleAndViewportEdges(int outputSize, float shiftPixels, bool hasHistory)
        {
            ReprojectionResult result = InspectReprojectedState(outputSize, shiftPixels, hasHistory, false);
            Assert.That(result.statesMatch, Is.True);
            Assert.That(result.metadataMatch, Is.True);
            Assert.That(result.paddingUntouched, Is.True);
            if (hasHistory) Assert.That(result.filteredColorPixels, Is.GreaterThan(0), "Only alpha should switch to point sampling.");
        }

        [TestCase(8, 0f, 0.75f, 0f)]
        [TestCase(13, 0f, 0.75f, 0.25f)]
        [TestCase(8, 0.75f, -0.5f, 0f)]
        [TestCase(8, -0.75f, 0.5f, 0f)]
        public void PendingReprojection_IgnoresJitterWhileColorUsesIt(int outputSize, float shiftPixels, float jitterX, float jitterY)
        {
            ReprojectionResult result = InspectReprojectedState(outputSize, shiftPixels, true, false, new Vector2(jitterX, jitterY));
            Assert.That(result.statesMatch && result.metadataMatch && result.paddingUntouched, Is.True);
            Assert.That(result.filteredColorPixels, Is.GreaterThan(0));
            if (shiftPixels == 0) Assert.That(result.colorOnlyInvalidPixels, Is.GreaterThan(0));
            else Assert.That(result.stateOnlyInvalidPixels, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EightJitterPhases_DoNotAccumulateStationaryPendingStateDrift(bool waveOps)
        {
            if (waveOps) Assume.That(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, Is.True);
            foreach (ReprojectionResult result in InspectEightJitterPhases(waveOps))
            {
                Assert.That(result.statesMatch && result.metadataMatch && result.paddingUntouched, Is.True, "Phase " + result.phase);
                Assert.That(result.filteredColorPixels, Is.GreaterThan(0));
            }
        }

        private sealed class ReprojectionResult
        {
            public int outputSize, paddedSize, statePixels, invalidPixels, filteredColorPixels;
            public float shiftPixels;
            public Vector2 jitterPixels;
            public int phase, stateOnlyInvalidPixels, colorOnlyInvalidPixels;
            [NonSerialized] public float[] stateGrid;
            public bool hasHistory, waveOps, statesMatch = true, metadataMatch = true, paddingUntouched = true;
        }

        private static ReprojectionResult[] InspectEightJitterPhases(bool waveOps)
        {
            // Eight Halton phases, closed back to the last phase. Feed actual GPU
            // alpha back into the next dispatch; RGB uses a fixed reconstruction pattern.
            var phases = new[] { new Vector2(0.5f, 1f / 3), new Vector2(0.25f, 2f / 3),
                new Vector2(0.75f, 1f / 9), new Vector2(0.125f, 4f / 9),
                new Vector2(0.625f, 7f / 9), new Vector2(0.375f, 2f / 9),
                new Vector2(0.875f, 5f / 9), new Vector2(0.0625f, 8f / 9) };
            var results = new ReprojectionResult[phases.Length];
            float[] previousStates = null;
            for (int frame = 0; frame < phases.Length; frame++)
            {
                Vector2 delta = phases[(frame + phases.Length - 1) % phases.Length] - phases[frame];
                ReprojectionResult result = InspectReprojectedState(8, 0, true, waveOps, delta, previousStates, frame + 1);
                // Interior states must retain their original pixel throughout the loop;
                // color-UV rejection at the viewport border may clear border states.
                for (int y = 2; y < 6; y++) for (int x = 2; x < 6; x++)
                    result.statesMatch &= result.stateGrid[y * 8 + x] == ((x + y) % 2 != 0 ? 26 : 5);
                results[frame] = result; previousStates = result.stateGrid;
            }
            return results;
        }

        private static ReprojectionResult InspectReprojectedState(int outputSize, float shiftPixels, bool hasHistory, bool waveOps,
            Vector2 jitterPixels = default, float[] previousStates = null, int phase = 0)
        {
            const int renderSize = 8;
            int paddedSize = (outputSize + 7) / 8 * 8;
            var owned = new System.Collections.Generic.List<Object>();
            var result = new ReprojectionResult { outputSize = outputSize, paddedSize = paddedSize,
                shiftPixels = shiftPixels, hasHistory = hasHistory, waveOps = waveOps, jitterPixels = jitterPixels, phase = phase,
                stateGrid = new float[outputSize * outputSize] };
            try
            {
                Texture2D InputTexture(int size, int channels, float value)
                {
                    GraphicsFormat format = channels == 1 ? GraphicsFormat.R32_SFloat : channels == 2
                        ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32G32B32A32_SFloat;
                    var texture = new Texture2D(size, size, format, TextureCreationFlags.None)
                        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    owned.Add(texture);
                    var values = new float[size * size * channels];
                    for (int i = 0; i < values.Length; i++) values[i] = value;
                    texture.SetPixelData(values, 0); texture.Apply(false, false); return texture;
                }
                RenderTexture OutputTexture(int channels)
                {
                    var texture = new RenderTexture(new RenderTextureDescriptor(paddedSize, paddedSize)
                    { graphicsFormat = channels == 2 ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32G32B32A32_SFloat,
                        depthStencilFormat = GraphicsFormat.None, enableRandomWrite = true, msaaSamples = 1 });
                    if (!texture.Create()) throw new InvalidOperationException("Cannot create state reprojection diagnostic target.");
                    owned.Add(texture); return texture;
                }
                var motion = InputTexture(renderSize, 2, 0);
                var motionValues = new float[renderSize * renderSize * 2];
                for (int i = 0; i < renderSize * renderSize; i++) motionValues[i * 2] = -shiftPixels / outputSize;
                motion.SetPixelData(motionValues, 0); motion.Apply(false, false);
                var boundary = InputTexture(renderSize, 1, 1);
                var zero = InputTexture(renderSize, 1, 0);
                var sourceColor = InputTexture(outputSize, 4, 0);
                var colorValues = new float[outputSize * outputSize * 4];
                for (int y = 0; y < outputSize; y++) for (int x = 0; x < outputSize; x++)
                {
                    int pixel = y * outputSize + x;
                    bool odd = (x + y) % 2 != 0;
                    for (int c = 0; c < 3; c++) colorValues[pixel * 4 + c] = odd ? 0.8f : 0.2f;
                    colorValues[pixel * 4 + 3] = previousStates != null ? previousStates[pixel] : (odd ? 26 : 5);
                }
                sourceColor.SetPixelData(colorValues, 0); sourceColor.Apply(false, false);
                var historyMeta = InputTexture(outputSize, 2, 0);
                var metadata = new float[outputSize * outputSize * 2];
                for (int i = 0; i < outputSize * outputSize; i++) { metadata[i * 2] = 16; metadata[i * 2 + 1] = 0.5f; }
                historyMeta.SetPixelData(metadata, 0); historyMeta.Apply(false, false);
                var resurrectionMeta = InputTexture(outputSize, 2, 0);
                var historyOut = OutputTexture(4); var historyMetaOut = OutputTexture(2);
                var resurrectionOut = OutputTexture(4); var resurrectionMetaOut = OutputTexture(2);
                using (var clear = new CommandBuffer())
                {
                    foreach (var target in new[] { historyOut, historyMetaOut, resurrectionOut, resurrectionMetaOut })
                    { clear.SetRenderTarget(target); clear.ClearRenderTarget(false, true, new Color(-17, -17, -17, -17)); }
                    Graphics.ExecuteCommandBuffer(clear);
                }
                var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/TSR/TSRReprojectHistory.compute"));
                owned.Add(shader);
                if (waveOps) shader.EnableKeyword("VIVID_TSR_WAVE_OPS"); else shader.DisableKeyword("VIVID_TSR_WAVE_OPS");
                int kernel = shader.FindKernel("CS");
                shader.SetVector("_RenderSize", new Vector4(renderSize, renderSize, 1f / renderSize, 1f / renderSize));
                shader.SetVector("_OutputSize", new Vector4(outputSize, outputSize, 1f / outputSize, 1f / outputSize));
                shader.SetVector("_PreviousOutputSize", new Vector4(outputSize, outputSize, 1f / outputSize, 1f / outputSize));
                shader.SetVector("_Jitter", new Vector4(0, 0, 2 * jitterPixels.x / outputSize,
                    (SystemInfo.graphicsUVStartsAtTop ? -2 : 2) * jitterPixels.y / outputSize));
                shader.SetVector("_TSRParams", new Vector4(hasHistory ? 1 : 0, 16, 0, 0));
                shader.SetTexture(kernel, "_DilatedMotion", motion); shader.SetTexture(kernel, "_ReprojectionBoundary", boundary);
                shader.SetTexture(kernel, "_ThinGeometryCoverage", zero); shader.SetTexture(kernel, "_LumaInstability", zero);
                shader.SetTexture(kernel, "_HistoryColor", sourceColor); shader.SetTexture(kernel, "_HistoryMeta", historyMeta);
                shader.SetTexture(kernel, "_ResurrectionColor", sourceColor); shader.SetTexture(kernel, "_ResurrectionMeta", resurrectionMeta);
                shader.SetTexture(kernel, "_ReprojectedHistoryColor", historyOut); shader.SetTexture(kernel, "_ReprojectedHistoryMeta", historyMetaOut);
                shader.SetTexture(kernel, "_ReprojectedResurrectionColor", resurrectionOut);
                shader.SetTexture(kernel, "_ReprojectedResurrectionMeta", resurrectionMetaOut);
                shader.Dispatch(kernel, paddedSize / 8, paddedSize / 8, 1);
                float[] Read(RenderTexture texture)
                {
                    var request = AsyncGPUReadback.Request(texture, 0); request.WaitForCompletion();
                    if (request.hasError) throw new InvalidOperationException("State reprojection readback failed.");
                    return request.GetData<float>().ToArray();
                }
                float[] colors = Read(resurrectionOut), mainMetadata = Read(historyMetaOut), cacheMetadata = Read(resurrectionMetaOut);
                for (int y = 0; y < paddedSize; y++) for (int x = 0; x < paddedSize; x++)
                {
                    int pixel = y * paddedSize + x;
                    if (x >= outputSize || y >= outputSize)
                    {
                        for (int c = 0; c < 4; c++) result.paddingUntouched &= colors[pixel * 4 + c] == -17;
                        for (int c = 0; c < 2; c++) result.paddingUntouched &= mainMetadata[pixel * 2 + c] == -17 && cacheMetadata[pixel * 2 + c] == -17;
                        continue;
                    }
                    float statePixelX = x + 0.5f + shiftPixels;
                    float historyPixelX = statePixelX + jitterPixels.x;
                    float historyPixelY = y + 0.5f + jitterPixels.y;
                    bool validColor = hasHistory && historyPixelX >= 0 && historyPixelX <= outputSize
                        && historyPixelY >= 0 && historyPixelY <= outputSize;
                    bool validState = hasHistory && statePixelX >= 0 && statePixelX <= outputSize;
                    int sourceX = Mathf.Clamp(Mathf.FloorToInt(statePixelX), 0, outputSize - 1);
                    float expectedState = validColor && validState ? colorValues[(y * outputSize + sourceX) * 4 + 3] : 0;
                    result.stateGrid[y * outputSize + x] = colors[pixel * 4 + 3];
                    result.statesMatch &= colors[pixel * 4 + 3] == expectedState;
                    result.metadataMatch &= Mathf.Abs(mainMetadata[pixel * 2] - (validColor ? 4 : 0)) < 0.003f
                        && cacheMetadata[pixel * 2] == 0 && cacheMetadata[pixel * 2 + 1] == 0;
                    if (!validState && validColor) result.stateOnlyInvalidPixels++;
                    if (validState && !validColor) result.colorOnlyInvalidPixels++;
                    if (validColor)
                    {
                        result.statePixels++;
                        float pointColor = (sourceX + y) % 2 != 0 ? 0.8f : 0.2f;
                        if (Mathf.Abs(colors[pixel * 4] - pointColor) > 0.003f) result.filteredColorPixels++;
                    }
                    else result.invalidPixels++;
                }
            }
            finally
            {
                foreach (Object resource in owned)
                { if (resource is RenderTexture texture) texture.Release(); Object.DestroyImmediate(resource); }
            }
            return result;
        }

        private static Color Gray(float value) => new(value, value, value, 1);

        private sealed class Input
        {
            internal Color Current = Gray(0.8f), History = Gray(0.2f), Resurrection = Color.clear;
            internal float PreviousState;
            internal bool ForceUpdateState;
            internal float ForcedUpdateState;
            internal Color NeighborhoodColorA = new Color(0.9f, 0.1f, 0.1f), NeighborhoodColorB = new Color(0.1f, 0.1f, 0.9f);
            internal Vector2Int NearbyDepthFeatureOffset;
            internal float DepthError, MotionPixels, LumaInstability, HistorySamples = 16, HistoryDepth = 0.5f, ResurrectionFrames;
            internal float NeighborhoodLow = 0.1f, NeighborhoodHigh = 0.9f;
            internal bool ChromaPattern;
        }

        private sealed class Snapshot
        {
            internal Color Updated, ResurrectionColor, AcceptedHistory;
            internal float Accepted, AcceptedAlpha, SampleCount, ResurrectionFrames, PendingState;
        }

        private sealed class Fixture : IDisposable
        {
            internal const int Size = 8, PixelCount = Size * Size;
            private const int Center = 4 * Size + 4;
            private readonly ComputeShader reject, update;
            private readonly Texture2D color, history, resurrection, depth, depthError, zero, instability, motion, historyMeta, resurrectionMeta;
            private readonly RenderTexture acceptedColor, rejection, updatedColor, updatedMeta, updatedResurrectionColor, updatedResurrectionMeta;

            internal Fixture(bool waveOps = false)
            {
                reject = Load("TSRRejectShading.compute", waveOps);
                update = Load("TSRUpdateHistory.compute", waveOps);
                color = CreateInput(GraphicsFormat.R32G32B32A32_SFloat);
                history = CreateInput(GraphicsFormat.R32G32B32A32_SFloat);
                resurrection = CreateInput(GraphicsFormat.R32G32B32A32_SFloat);
                depth = CreateInput(GraphicsFormat.R32_SFloat); depthError = CreateInput(GraphicsFormat.R32_SFloat); zero = CreateInput(GraphicsFormat.R32_SFloat);
                instability = CreateInput(GraphicsFormat.R32_SFloat); motion = CreateInput(GraphicsFormat.R32G32_SFloat);
                historyMeta = CreateInput(GraphicsFormat.R32G32_SFloat); resurrectionMeta = CreateInput(GraphicsFormat.R32G32_SFloat);
                acceptedColor = CreateOutput(GraphicsFormat.R32G32B32A32_SFloat);
                rejection = CreateOutput(GraphicsFormat.R32_SFloat); updatedColor = CreateOutput(GraphicsFormat.R32G32B32A32_SFloat);
                updatedMeta = CreateOutput(GraphicsFormat.R32G32_SFloat);
                updatedResurrectionColor = CreateOutput(GraphicsFormat.R32G32B32A32_SFloat);
                updatedResurrectionMeta = CreateOutput(GraphicsFormat.R32G32_SFloat);
                SetConstant(depth, 1, 0.5f); SetConstant(zero, 1, 0);
            }

            private static ComputeShader Load(string name, bool waveOps)
            {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/TSR/" + name);
                if (source == null) throw new InvalidOperationException("Missing TSR compute: " + name);
                var shader = Object.Instantiate(source);
                if (waveOps) shader.EnableKeyword("VIVID_TSR_WAVE_OPS");
                else shader.DisableKeyword("VIVID_TSR_WAVE_OPS");
                shader.SetVector("_RenderSize", new Vector4(Size, Size, 1f / Size, 1f / Size));
                shader.SetVector("_OutputSize", new Vector4(Size, Size, 1f / Size, 1f / Size));
                return shader;
            }

            private static Texture2D CreateInput(GraphicsFormat format)
                => new(Size, Size, format, TextureCreationFlags.None) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            private static RenderTexture CreateOutput(GraphicsFormat format)
            {
                var texture = new RenderTexture(new RenderTextureDescriptor(Size, Size)
                { graphicsFormat = format, depthStencilFormat = GraphicsFormat.None, enableRandomWrite = true, msaaSamples = 1 });
                if (!texture.Create()) throw new InvalidOperationException("Could not create isolated TSR target.");
                return texture;
            }

            private static void SetConstant(Texture2D texture, int channels, float x, float y = 0)
            {
                var values = new float[PixelCount * channels];
                for (int i = 0; i < PixelCount; i++)
                { values[i * channels] = x; if (channels > 1) values[i * channels + 1] = y; }
                texture.SetPixelData(values, 0); texture.Apply(false, false);
            }

            private static void SetColor(float[] values, int pixel, Color value)
            {
                values[pixel * 4] = value.r; values[pixel * 4 + 1] = value.g;
                values[pixel * 4 + 2] = value.b; values[pixel * 4 + 3] = 1;
            }

            private void SetDepthInputs(Input input)
            {
                var depths = new float[PixelCount];
                var errors = new float[PixelCount];
                for (int pixel = 0; pixel < PixelCount; pixel++)
                { depths[pixel] = 0.5f; errors[pixel] = input.DepthError; }
                if (input.NearbyDepthFeatureOffset != Vector2Int.zero)
                {
                    int featureX = Size / 2 + input.NearbyDepthFeatureOffset.x;
                    int featureY = Size / 2 + input.NearbyDepthFeatureOffset.y;
                    depths[featureY * Size + featureX] = 0.52f;
                    // A .52 sample surrounded by .5 produces a full 3x3 .02 error
                    // patch in TSRDilateVelocity. The receiver stays at depth .5,
                    // with zero center error; patches at the viewport edge truncate.
                    for (int y = Mathf.Max(0, featureY - 1); y <= Mathf.Min(Size - 1, featureY + 1); y++)
                        for (int x = Mathf.Max(0, featureX - 1); x <= Mathf.Min(Size - 1, featureX + 1); x++)
                            errors[y * Size + x] = 0.02f;
                }
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                depthError.SetPixelData(errors, 0); depthError.Apply(false, false);
            }

            internal Snapshot Run(Input input)
            {
                var colors = new float[PixelCount * 4];
                var histories = new float[PixelCount * 4];
                var resurrections = new float[PixelCount * 4];
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    int pixel = y * Size + x;
                    Color value = input.ChromaPattern
                        ? ((x + y) % 2 == 0 ? input.NeighborhoodColorA : input.NeighborhoodColorB)
                        : Gray((x + y) % 2 == 0 ? input.NeighborhoodHigh : input.NeighborhoodLow);
                    if (pixel == Center) value = input.Current;
                    SetColor(colors, pixel, value);
                    SetColor(histories, pixel, input.History);
                    SetColor(resurrections, pixel, input.Resurrection);
                    resurrections[pixel * 4 + 3] = input.PreviousState;
                    if (input.ForceUpdateState) histories[pixel * 4 + 3] = input.ForcedUpdateState;
                }
                color.SetPixelData(colors, 0); color.Apply(false, false);
                history.SetPixelData(histories, 0); history.Apply(false, false);
                resurrection.SetPixelData(resurrections, 0); resurrection.Apply(false, false);
                SetDepthInputs(input);
                SetConstant(instability, 1, input.LumaInstability);
                SetConstant(motion, 2, input.MotionPixels / Size);
                SetConstant(historyMeta, 2, input.HistorySamples, input.HistoryDepth);
                SetConstant(resurrectionMeta, 2, input.ResurrectionFrames, 0.5f);
                int kernel = reject.FindKernel("CS");
                reject.SetVector("_TSRRejectionParams", new Vector4(0.003f, 16, 0.28f, 0.35f));
                reject.SetTexture(kernel, "_InputColor", color); reject.SetTexture(kernel, "_InputDepth", depth);
                reject.SetTexture(kernel, "_DilatedDepth", depth); reject.SetTexture(kernel, "_DilatedMotion", motion);
                reject.SetTexture(kernel, "_DepthError", depthError); reject.SetTexture(kernel, "_ReprojectionBoundary", zero);
                reject.SetTexture(kernel, "_LumaInstability", instability); reject.SetTexture(kernel, "_ReprojectedHistoryColor", history);
                reject.SetTexture(kernel, "_ReprojectedHistoryMeta", historyMeta);
                reject.SetTexture(kernel, "_ReprojectedResurrectionColor", resurrection);
                reject.SetTexture(kernel, "_AcceptedHistoryColor", acceptedColor); reject.SetTexture(kernel, "_RejectionMask", rejection);
                reject.Dispatch(kernel, 1, 1, 1);
                kernel = update.FindKernel("CS");
                update.SetVector("_TSRParams", new Vector4(input.HistorySamples > 0 ? 1 : 0, 16, 0, 0));
                update.SetTexture(kernel, "_CurrentFrameColor", color); update.SetTexture(kernel, "_DilatedMotion", motion);
                update.SetTexture(kernel, "_DilatedDepth", depth); update.SetTexture(kernel, "_ReprojectionBoundary", zero);
                update.SetTexture(kernel, "_ThinGeometryCoverage", zero); update.SetTexture(kernel, "_LumaInstability", instability);
                update.SetTexture(kernel, "_AcceptedHistoryColor", input.ForceUpdateState ? (Texture)history : acceptedColor);
                update.SetTexture(kernel, "_ReprojectedHistoryMeta", historyMeta);
                update.SetTexture(kernel, "_ReprojectedResurrectionColor", resurrection);
                update.SetTexture(kernel, "_ReprojectedResurrectionMeta", resurrectionMeta); update.SetTexture(kernel, "_RejectionMask", input.ForceUpdateState ? (Texture)zero : rejection);
                update.SetTexture(kernel, "_UpdatedHistoryColor", updatedColor); update.SetTexture(kernel, "_UpdatedHistoryMeta", updatedMeta);
                update.SetTexture(kernel, "_UpdatedResurrectionColor", updatedResurrectionColor);
                update.SetTexture(kernel, "_UpdatedResurrectionMeta", updatedResurrectionMeta);
                update.Dispatch(kernel, 1, 1, 1);
                float[] accepted = Read(acceptedColor), masks = Read(rejection), updated = Read(updatedColor);
                float[] meta = Read(updatedMeta), resurrectionData = Read(updatedResurrectionMeta);
                float[] resurrectionColors = Read(updatedResurrectionColor);
                return new Snapshot
                {
                    Accepted = input.ForceUpdateState ? 0 : masks[Center],
                    AcceptedAlpha = input.ForceUpdateState ? input.ForcedUpdateState : accepted[Center * 4 + 3], SampleCount = meta[Center * 2],
                    AcceptedHistory = new Color(accepted[Center * 4], accepted[Center * 4 + 1], accepted[Center * 4 + 2], accepted[Center * 4 + 3]),
                    ResurrectionFrames = resurrectionData[Center * 2], PendingState = resurrectionColors[Center * 4 + 3],
                    ResurrectionColor = new Color(resurrectionColors[Center * 4], resurrectionColors[Center * 4 + 1], resurrectionColors[Center * 4 + 2], resurrectionColors[Center * 4 + 3]),
                    Updated = new Color(updated[Center * 4], updated[Center * 4 + 1], updated[Center * 4 + 2], updated[Center * 4 + 3])
                };
            }

            private static float[] Read(RenderTexture texture)
            {
                var request = AsyncGPUReadback.Request(texture, 0); request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException("Isolated TSR readback failed.");
                return request.GetData<float>().ToArray();
            }

            public void Dispose()
            {
                foreach (Texture2D texture in new[] { color, history, resurrection, depth, depthError, zero, instability, motion, historyMeta, resurrectionMeta })
                    Object.DestroyImmediate(texture);
                foreach (RenderTexture texture in new[] { acceptedColor, rejection, updatedColor, updatedMeta, updatedResurrectionColor, updatedResurrectionMeta })
                { texture.Release(); Object.DestroyImmediate(texture); }
                Object.DestroyImmediate(reject); Object.DestroyImmediate(update);
            }
        }
    }
}
