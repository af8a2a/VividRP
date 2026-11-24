using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SdfSurfaceCacheSampledLightingTests
    {
        private const string CardLightingShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfSceneDirectLighting.compute";
        private const string ReGIRBuildShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Lighting/ReGIRGridBuild.compute";

        private const int CardSize = 32;
        private const int PresampledTileSize = 1024;
        private const int PresampledTileCount = 128;

        [StructLayout(LayoutKind.Sequential)]
        private struct TestMeshSdfField
        {
            public Vector4 originAndVoxelSize;
            public uint dimX, dimY, dimZ, wordOffset;
            public uint signMode, pageOffset;
            public float shellThickness;
            public uint mostlyTwoSided;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestMeshSdfInstance
        {
            public Vector4 worldToLocal0, worldToLocal1, worldToLocal2;
            public uint fieldIndex, layerMask, entityId0, entityId1;
            public float distanceScale;
            public uint motionFrame, pad0, pad1;
            public Vector4 previousLocalToWorld0, previousLocalToWorld1, previousLocalToWorld2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestMeshSdfTraceData
        {
            public Vector4 worldToLocal0, worldToLocal1, worldToLocal2;
            public uint fieldIndex, layerMask, entityId0, entityId1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestMeshSdfNode
        {
            public Vector3 boundsMin;
            public uint firstLeaf;
            public Vector3 boundsMax;
            public uint escapeIndex;
            public uint leafCount, layerMask, pad0, pad1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestDiffuseSurface
        {
            public Vector4 albedoAndValid;
            public Vector4 emission;
            public Vector4 cardMinAndFirst;
            public Vector4 cardExtent;
        }

        [Test]
        public void ComputeLocalLightHash_IsDeterministic_Sensitive_AndZeroAlloc()
        {
            Assert.That(SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(null, 0), Is.EqualTo(0UL));

            var lights = CreateMixedTestLights();
            ulong h0 = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length);
            ulong h1 = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length);
            Assert.That(h0, Is.Not.EqualTo(0UL));
            Assert.That(h1, Is.EqualTo(h0));

            // Sensitivity to position, color, shadowStrength, and count.
            lights[0].positionWS.x += 0.25f;
            ulong hPos = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length);
            Assert.That(hPos, Is.Not.EqualTo(h0));

            lights[0].color.y += 1.0f;
            ulong hColor = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length);
            Assert.That(hColor, Is.Not.EqualTo(hPos));

            lights[0].shadowStrength = 0.5f;
            ulong hShadow = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length);
            Assert.That(hShadow, Is.Not.EqualTo(hColor));

            ulong hFewer = SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length - 1);
            Assert.That(hFewer, Is.Not.EqualTo(hShadow));

            // Warm-up then assert 0 managed allocations.
            ulong sink = 0UL;
            for (int i = 0; i < 16; i++)
                sink = unchecked(sink + SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                sink = unchecked(sink + SdfSurfaceCacheSampledLighting.ComputeLocalLightHash(lights, lights.Length));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(sink, Is.Not.EqualTo(0UL));
            Assert.That(allocated, Is.EqualTo(0L));
        }

        [Test]
        public void PrepareLightState_ZeroLocalLights_StaysInactiveAndZeroAlloc()
        {
            using var helper = new SdfSurfaceCacheSampledLighting();
            var frameData = new ContextContainer();
            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.reGIRLightCount = 0;

            // Warm-up.
            for (int i = 0; i < 8; i++)
                helper.PrepareLightState(frameData);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
                helper.PrepareLightState(frameData);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(helper.HasLocalLights, Is.False);
            Assert.That(helper.LocalLightCount, Is.EqualTo(0));
            Assert.That(helper.NeedsReGIRBuild, Is.False);
            Assert.That(helper.LightStateHash, Is.EqualTo(0UL));
            Assert.That(allocated, Is.EqualTo(0L));
        }

        [Test]
        public void UpdateSdfCardSampledDirectLighting_ReGIRRIS_MatchesEvaluateAllGoldenReference()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders are not supported on this device.");

            var cardShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CardLightingShaderPath);
            var regirShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ReGIRBuildShaderPath);
            Assert.That(cardShader, Is.Not.Null, CardLightingShaderPath);
            Assert.That(regirShader, Is.Not.Null, ReGIRBuildShaderPath);
            Assert.That(cardShader.HasKernel("UpdateSdfCardSampledDirectLighting"), Is.True);

            var lights = CreateMixedTestLights();

            using var harness = new SyntheticCardHarness(cardShader, regirShader, withOccluder: false);

            // 1. Golden reference: EvaluateAllLights = 1 (exact punctual + 4x stratified area lights, averaged over 4 frames for area lights).
            Vector3 goldenMean = Vector3.zero;
            const int goldenFrames = 4;
            for (uint f = 0; f < goldenFrames; f++)
            {
                Vector3 frameMean = harness.DispatchAndReadMean(
                    lights,
                    onionCenterWS: Vector3.zero,
                    evaluateAll: true,
                    useReGIR: false,
                    frameIndex: f + 1u);
                goldenMean += frameMean;
            }
            goldenMean /= goldenFrames;

            Assert.That(goldenMean.x, Is.GreaterThan(0.1f), $"Expected non-trivial red golden radiance, got {goldenMean}");
            Assert.That(goldenMean.y, Is.GreaterThan(0.1f), $"Expected non-trivial green golden radiance, got {goldenMean}");
            Assert.That(goldenMean.z, Is.GreaterThan(0.1f), $"Expected non-trivial blue golden radiance, got {goldenMean}");

            // 2. Onion ReGIR + 4x4 single-pass multi-iteration RIS inside Onion coverage.
            Vector3 risMean = Vector3.zero;
            const int risFrames = 8;
            for (uint f = 0; f < risFrames; f++)
            {
                Vector3 frameMean = harness.DispatchAndReadMean(
                    lights,
                    onionCenterWS: Vector3.zero,
                    evaluateAll: false,
                    useReGIR: true,
                    frameIndex: f + 100u);
                risMean += frameMean;
            }
            risMean /= risFrames;

            AssertRelativeParity(risMean, goldenMean, 0.06f, "Onion ReGIR 4x4 RIS vs EvaluateAll golden reference");

            // 3. Outside-Onion PowerRIS fallback (onion center 500m away -> every texel has cellIndex == -1).
            Vector3 fallbackMean = Vector3.zero;
            for (uint f = 0; f < risFrames; f++)
            {
                Vector3 frameMean = harness.DispatchAndReadMean(
                    lights,
                    onionCenterWS: new Vector3(500.0f, 500.0f, 500.0f),
                    evaluateAll: false,
                    useReGIR: true,
                    frameIndex: f + 200u);
                fallbackMean += frameMean;
            }
            fallbackMean /= risFrames;

            AssertRelativeParity(fallbackMean, goldenMean, 0.08f, "Outside-Onion PowerRIS fallback vs EvaluateAll golden reference");
        }

        [Test]
        public void UpdateSdfCardSampledDirectLighting_ShadowOcclusion_BlocksLocalLight()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders are not supported on this device.");

            var cardShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CardLightingShaderPath);
            var regirShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ReGIRBuildShaderPath);
            Assert.That(cardShader, Is.Not.Null);
            Assert.That(regirShader, Is.Not.Null);

            using var harness = new SyntheticCardHarness(cardShader, regirShader, withOccluder: true);

            // Single bright point light directly above the occluder box at y=2 (occluder covers x,z in [-0.6, 0.6], y in [1.5, 2.5]).
            var unshadowed = new[]
            {
                MakePointLight(new Vector3(0.0f, 4.0f, 0.0f), new Vector3(40.0f, 30.0f, 20.0f), 12.0f, shadowStrength: 0.0f)
            };
            var shadowed = new[]
            {
                MakePointLight(new Vector3(0.0f, 4.0f, 0.0f), new Vector3(40.0f, 30.0f, 20.0f), 12.0f, shadowStrength: 1.0f)
            };

            Color[] litPixels = harness.DispatchAndReadPixels(unshadowed, Vector3.zero, evaluateAll: true, useReGIR: false, frameIndex: 1u);
            Color[] shadowPixels = harness.DispatchAndReadPixels(shadowed, Vector3.zero, evaluateAll: true, useReGIR: false, frameIndex: 1u);

            // Center texel (16, 16) is directly beneath the occluder at (0, 2, 0).
            Color centerUnshadowed = litPixels[16 * CardSize + 16];
            Color centerShadowed = shadowPixels[16 * CardSize + 16];
            // Corner texel (1, 1) at (-1.8125, 0, -1.8125) has an unobstructed line of sight to (0, 4, 0).
            Color cornerUnshadowed = litPixels[1 * CardSize + 1];
            Color cornerShadowed = shadowPixels[1 * CardSize + 1];

            Assert.That(centerUnshadowed.r, Is.GreaterThan(0.15f), $"Center unshadowed should be bright, got {centerUnshadowed}");
            Assert.That(centerShadowed.r, Is.LessThan(1e-3f), $"Center shadowed should be occluded by MeshSDF box, got {centerShadowed}");
            Assert.That(cornerShadowed.r, Is.EqualTo(cornerUnshadowed.r).Within(1e-3f),
                $"Corner should remain lit: unshadowed={cornerUnshadowed}, shadowed={cornerShadowed}");
        }

        private static void AssertRelativeParity(Vector3 actual, Vector3 expected, float maxRelError, string label)
        {
            float errR = Mathf.Abs(actual.x - expected.x) / Mathf.Max(expected.x, 1e-4f);
            float errG = Mathf.Abs(actual.y - expected.y) / Mathf.Max(expected.y, 1e-4f);
            float errB = Mathf.Abs(actual.z - expected.z) / Mathf.Max(expected.z, 1e-4f);
            Assert.That(errR, Is.LessThanOrEqualTo(maxRelError), $"{label} R mismatch: actual={actual}, expected={expected}, relErr={errR:P2}");
            Assert.That(errG, Is.LessThanOrEqualTo(maxRelError), $"{label} G mismatch: actual={actual}, expected={expected}, relErr={errG:P2}");
            Assert.That(errB, Is.LessThanOrEqualTo(maxRelError), $"{label} B mismatch: actual={actual}, expected={expected}, relErr={errB:P2}");
        }

        private static VividReGIRLightData[] CreateMixedTestLights()
        {
            return new[]
            {
                // 4 Point lights
                MakePointLight(new Vector3(-1.5f, 2.2f, -1.2f), new Vector3(18.0f, 5.0f, 3.0f), 10.0f, 0.0f),
                MakePointLight(new Vector3(1.6f, 2.6f, -1.0f), new Vector3(4.0f, 16.0f, 6.0f), 10.0f, 0.0f),
                MakePointLight(new Vector3(-1.2f, 3.0f, 1.8f), new Vector3(5.0f, 7.0f, 22.0f), 12.0f, 0.0f),
                MakePointLight(new Vector3(1.5f, 1.8f, 1.4f), new Vector3(14.0f, 12.0f, 4.0f), 9.0f, 0.0f),
                // 4 Spot lights aimed downward at the card
                MakeSpotLight(new Vector3(-0.8f, 3.2f, 0.0f), Vector3.down, new Vector3(24.0f, 10.0f, 6.0f), 12.0f, 60.0f, 30.0f),
                MakeSpotLight(new Vector3(0.9f, 3.5f, 0.2f), new Vector3(-0.15f, -1.0f, 0.0f).normalized, new Vector3(6.0f, 24.0f, 12.0f), 12.0f, 55.0f, 25.0f),
                MakeSpotLight(new Vector3(0.0f, 2.8f, -1.1f), new Vector3(0.0f, -1.0f, 0.25f).normalized, new Vector3(8.0f, 8.0f, 26.0f), 11.0f, 65.0f, 35.0f),
                MakeSpotLight(new Vector3(0.2f, 3.0f, 1.1f), new Vector3(0.0f, -1.0f, -0.25f).normalized, new Vector3(18.0f, 18.0f, 14.0f), 11.0f, 60.0f, 30.0f),
                // 2 Rectangle area lights facing down
                MakeRectLight(new Vector3(-0.6f, 2.5f, 0.5f), new Vector2(1.4f, 0.9f), new Vector3(12.0f, 4.0f, 10.0f), 12.0f),
                MakeRectLight(new Vector3(0.7f, 2.9f, -0.4f), new Vector2(1.1f, 1.2f), new Vector3(5.0f, 14.0f, 14.0f), 12.0f),
                // 2 Tube lights along X and Z
                MakeTubeLight(new Vector3(0.0f, 2.4f, -0.6f), Vector3.right, 1.8f, new Vector3(10.0f, 6.0f, 3.0f), 10.0f),
                MakeTubeLight(new Vector3(0.4f, 2.7f, 0.7f), Vector3.forward, 1.6f, new Vector3(3.0f, 9.0f, 12.0f), 10.0f),
            };
        }

        private static VividReGIRLightData MakePointLight(Vector3 pos, Vector3 color, float range, float shadowStrength)
        {
            float power = (0.2126f * color.x + 0.7152f * color.y + 0.0722f * color.z) * 4.0f * Mathf.PI;
            return new VividReGIRLightData
            {
                positionWS = pos,
                range = range,
                color = color,
                lightType = VividReGIRLightData.TypePoint,
                directionWS = Vector3.down,
                rightWS = Vector3.right,
                upWS = Vector3.forward,
                shapeRadius = 0.05f,
                power = power,
                renderingLayerMask = 1u,
                shadowStrength = shadowStrength,
                rangeAttenuationScale = 1.0f / (range * range),
                rangeAttenuationBias = 1.0f,
                indirectMultiplier = 1.0f,
            };
        }

        private static VividReGIRLightData MakeSpotLight(
            Vector3 pos, Vector3 dir, Vector3 color, float range, float outerAngleDeg, float innerAngleDeg)
        {
            float cosOuter = Mathf.Cos(outerAngleDeg * 0.5f * Mathf.Deg2Rad);
            float cosInner = Mathf.Cos(innerAngleDeg * 0.5f * Mathf.Deg2Rad);
            float invRange = 1.0f / Mathf.Max(cosInner - cosOuter, 1e-4f);
            float power = (0.2126f * color.x + 0.7152f * color.y + 0.0722f * color.z) * 2.0f * Mathf.PI * (1.0f - cosOuter);
            return new VividReGIRLightData
            {
                positionWS = pos,
                range = range,
                color = color,
                lightType = VividReGIRLightData.TypeSpot,
                directionWS = dir.normalized,
                angleScale = invRange,
                angleOffset = -cosOuter * invRange,
                rightWS = Vector3.right,
                upWS = Vector3.forward,
                shapeRadius = 0.05f,
                power = power,
                renderingLayerMask = 1u,
                shadowStrength = 0.0f,
                rangeAttenuationScale = 1.0f / (range * range),
                rangeAttenuationBias = 1.0f,
                indirectMultiplier = 1.0f,
            };
        }

        private static VividReGIRLightData MakeRectLight(Vector3 pos, Vector2 size, Vector3 color, float range)
        {
            float area = size.x * size.y;
            float power = (0.2126f * color.x + 0.7152f * color.y + 0.0722f * color.z) * Mathf.PI * area;
            return new VividReGIRLightData
            {
                positionWS = pos,
                range = range,
                color = color,
                lightType = VividReGIRLightData.TypeRectangle,
                directionWS = Vector3.down,
                rightWS = Vector3.right,
                upWS = Vector3.forward,
                areaSize = size,
                power = power,
                renderingLayerMask = 1u,
                cosBarnDoorAngle = 0.0f,
                barnDoorLength = 0.0f,
                shadowStrength = 0.0f,
                rangeAttenuationScale = 1.0f / (range * range),
                rangeAttenuationBias = 1.0f,
                indirectMultiplier = 1.0f,
            };
        }

        private static VividReGIRLightData MakeTubeLight(Vector3 pos, Vector3 right, float length, Vector3 color, float range)
        {
            float power = (0.2126f * color.x + 0.7152f * color.y + 0.0722f * color.z) * Mathf.PI * Mathf.PI * length;
            return new VividReGIRLightData
            {
                positionWS = pos,
                range = range,
                color = color,
                lightType = VividReGIRLightData.TypeTube,
                directionWS = Vector3.down,
                rightWS = right.normalized,
                upWS = Vector3.up,
                areaSize = new Vector2(length, 0.0f),
                power = power,
                renderingLayerMask = 1u,
                shadowStrength = 0.0f,
                rangeAttenuationScale = 1.0f / (range * range),
                rangeAttenuationBias = 1.0f,
                indirectMultiplier = 1.0f,
            };
        }

        private sealed class SyntheticCardHarness : IDisposable
        {
            private readonly ComputeShader m_CardShader;
            private readonly ComputeShader m_ReGIRShader;
            private readonly int m_SampledKernel;
            private readonly int m_PreparePdfKernel;
            private readonly int m_ReducePdfKernel;
            private readonly int m_PresampleKernel;
            private readonly int m_BuildKernel;

            private readonly GraphicsBuffer m_Fields;
            private readonly GraphicsBuffer m_Instances;
            private readonly GraphicsBuffer m_TraceInstances;
            private readonly GraphicsBuffer m_Nodes;
            private readonly GraphicsBuffer m_Leaves;
            private readonly GraphicsBuffer m_Voxels;
            private readonly GraphicsBuffer m_Surfaces;
            private readonly GraphicsBuffer m_LocalCardTable;
            private readonly GraphicsBuffer m_RadiosityWork;
            private readonly GraphicsBuffer m_FinalOrder;

            private readonly Texture2DArray m_AlbedoDepth;
            private readonly Texture2DArray m_NormalCoverage;
            private readonly Texture3D m_DummyBrickAtlas;
            private readonly RenderTexture m_Output;

            public SyntheticCardHarness(ComputeShader cardShader, ComputeShader regirShader, bool withOccluder)
            {
                m_CardShader = cardShader;
                m_ReGIRShader = regirShader;
                m_SampledKernel = cardShader.FindKernel("UpdateSdfCardSampledDirectLighting");
                m_PreparePdfKernel = regirShader.FindKernel("ReGIRPrepareLightPdf");
                m_ReducePdfKernel = regirShader.FindKernel("ReGIRReduceLightPdf");
                m_PresampleKernel = regirShader.FindKernel("ReGIRPresampleLocalLights");
                m_BuildKernel = regirShader.FindKernel("ReGIRGridBuild");

                // 4x4x4 dense MeshSDF field: positive (+2.0) for card receiver, negative (-1.0) solid occluder box when requested.
                const int dim = 4;
                int voxelCount = dim * dim * dim;
                var voxelWords = new uint[voxelCount]; // 2 fields * (64/2 = 32 uints) = 64 uints
                ushort posHalf = Mathf.FloatToHalf(2.0f);
                ushort surfaceHalf = Mathf.FloatToHalf(0.0f);
                uint posPair = (uint)posHalf | ((uint)posHalf << 16);
                uint surfacePair = (uint)surfaceHalf | ((uint)surfaceHalf << 16);
                for (int i = 0; i < 32; i++)
                {
                    voxelWords[i] = posPair;
                    voxelWords[32 + i] = surfacePair;
                }
                m_Voxels = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelWords.Length, sizeof(uint));
                m_Voxels.SetData(voxelWords);

                var fields = new[]
                {
                    // Field 0: empty positive SDF for card host instance
                    new TestMeshSdfField
                    {
                        originAndVoxelSize = new Vector4(-2.0f, -1.0f, -2.0f, 1.0f),
                        dimX = dim, dimY = dim, dimZ = dim, wordOffset = 0u,
                        signMode = 0u, pageOffset = 0xffffffffu, shellThickness = 0.0f, mostlyTwoSided = 0u
                    },
                    // Field 1: solid occluder box spanning [-0.6, 0.6] x [1.5, 2.5] x [-0.6, 0.6]
                    new TestMeshSdfField
                    {
                        originAndVoxelSize = new Vector4(-0.6f, 1.5f, -0.6f, 0.4f),
                        dimX = dim, dimY = dim, dimZ = dim, wordOffset = 32u,
                        signMode = 0u, pageOffset = 0xffffffffu, shellThickness = 0.0f, mostlyTwoSided = 0u
                    },
                };
                m_Fields = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fields.Length, Marshal.SizeOf<TestMeshSdfField>());
                m_Fields.SetData(fields);

                var identity = Matrix4x4.identity;
                var instances = new[]
                {
                    MakeInstance(identity, fieldIndex: 0u, layerMask: 1u),
                    MakeInstance(identity, fieldIndex: 1u, layerMask: withOccluder ? 1u : 0u),
                };
                m_Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instances.Length, Marshal.SizeOf<TestMeshSdfInstance>());
                m_Instances.SetData(instances);

                var traceInstances = new[]
                {
                    MakeTraceData(identity, fieldIndex: 0u, layerMask: 1u),
                    MakeTraceData(identity, fieldIndex: 1u, layerMask: withOccluder ? 1u : 0u),
                };
                m_TraceInstances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, traceInstances.Length, Marshal.SizeOf<TestMeshSdfTraceData>());
                m_TraceInstances.SetData(traceInstances);

                var nodes = new[]
                {
                    new TestMeshSdfNode
                    {
                        boundsMin = withOccluder ? new Vector3(-0.6f, 1.5f, -0.6f) : new Vector3(100.0f, 100.0f, 100.0f),
                        firstLeaf = 0u,
                        boundsMax = withOccluder ? new Vector3(0.6f, 2.5f, 0.6f) : new Vector3(101.0f, 101.0f, 101.0f),
                        escapeIndex = 1u,
                        leafCount = withOccluder ? 1u : 0u,
                        layerMask = withOccluder ? 1u : 0u,
                    }
                };
                m_Nodes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodes.Length, Marshal.SizeOf<TestMeshSdfNode>());
                m_Nodes.SetData(nodes);

                m_Leaves = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
                m_Leaves.SetData(new[] { 1u });

                // Single horizontal card (slot = 2 -> axis = 1 (Y), slice = 2) on y = 0, x,z in [-2, 2].
                var surfaces = new[]
                {
                    new TestDiffuseSurface
                    {
                        albedoAndValid = new Vector4(0.8f, 0.8f, 0.8f, 1.0f),
                        emission = Vector4.zero,
                        cardMinAndFirst = new Vector4(-2.0f, 0.0f, -2.0f, 1.0f), // first = 0
                        cardExtent = new Vector4(4.0f, 1.0f, 4.0f, 1.0f),
                    }
                };
                m_Surfaces = new GraphicsBuffer(GraphicsBuffer.Target.Structured, surfaces.Length, Marshal.SizeOf<TestDiffuseSurface>());
                m_Surfaces.SetData(surfaces);

                m_LocalCardTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float) * 4);
                m_LocalCardTable.SetData(new[] { Vector4.zero });

                // _SdfRadiosityWork: [0] = workOffset (= 2), [1] = firstCard (= 0), [2] = instanceIndex (0), [3] = slot (2).
                m_RadiosityWork = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(uint));
                m_RadiosityWork.SetData(new uint[] { 2u, 0u, 0u, 2u });

                m_FinalOrder = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
                m_FinalOrder.SetData(new uint[] { 0u, 0u });

                m_AlbedoDepth = new Texture2DArray(CardSize, CardSize, 6, TextureFormat.RGBAHalf, false, true);
                m_NormalCoverage = new Texture2DArray(CardSize, CardSize, 6, TextureFormat.RGBAHalf, false, true);
                var albedoPixels = new Color[CardSize * CardSize];
                var normalPixels = new Color[CardSize * CardSize];
                for (int i = 0; i < albedoPixels.Length; i++)
                {
                    albedoPixels[i] = new Color(0.8f, 0.8f, 0.8f, 0.0f); // depth = 0 -> y = 0
                    normalPixels[i] = new Color(0.5f, 1.0f, 0.5f, 1.0f); // normalLS = (0, +1, 0), coverage = 1
                }
                for (int slice = 0; slice < 6; slice++)
                {
                    m_AlbedoDepth.SetPixels(albedoPixels, slice);
                    m_NormalCoverage.SetPixels(normalPixels, slice);
                }
                m_AlbedoDepth.Apply(false, false);
                m_NormalCoverage.Apply(false, false);

                m_DummyBrickAtlas = new Texture3D(1, 1, 1, TextureFormat.RHalf, false);
                m_DummyBrickAtlas.SetPixels(new[] { Color.white });
                m_DummyBrickAtlas.Apply(false, false);

                m_Output = new RenderTexture(CardSize, CardSize, 0, GraphicsFormat.R16G16B16A16_SFloat)
                {
                    enableRandomWrite = true,
                };
                m_Output.Create();
            }

            public Vector3 DispatchAndReadMean(
                VividReGIRLightData[] lights,
                Vector3 onionCenterWS,
                bool evaluateAll,
                bool useReGIR,
                uint frameIndex)
            {
                Color[] pixels = DispatchAndReadPixels(lights, onionCenterWS, evaluateAll, useReGIR, frameIndex);
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    Assert.That(float.IsFinite(c.r) && c.r >= 0.0f, Is.True, $"Invalid R at pixel {i}: {c}");
                    Assert.That(float.IsFinite(c.g) && c.g >= 0.0f, Is.True, $"Invalid G at pixel {i}: {c}");
                    Assert.That(float.IsFinite(c.b) && c.b >= 0.0f, Is.True, $"Invalid B at pixel {i}: {c}");
                    sum += new Vector3(c.r, c.g, c.b);
                }
                return sum / pixels.Length;
            }

            public Color[] DispatchAndReadPixels(
                VividReGIRLightData[] lights,
                Vector3 onionCenterWS,
                bool evaluateAll,
                bool useReGIR,
                uint frameIndex)
            {
                var parameters = SdfSurfaceCacheSampledLighting.CreateOnionParameters(
                    onionCenterWS,
                    lights.Length,
                    frameIndex,
                    out int pdfSize,
                    out int pdfTexels,
                    out int pdfMips);
                int slotCount = Mathf.Max(1, (int)Math.Min(parameters.slotCount, int.MaxValue));
                int presampledCount = useReGIR ? PresampledTileSize * PresampledTileCount : 1;

                using var lightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(lights.Length, 1), VividReGIRLightData.Stride);
                using var paramBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividReGIRParameters.Stride);
                using var reservoirBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, useReGIR ? slotCount : 1, VividReGIRReservoir.Stride);
                using var presampledBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, presampledCount, ReGIRGridBuildPass.PresampledLightEntryStride);
                lightBuffer.SetData(lights);
                paramBuffer.SetData(new[] { parameters });

                RenderTexture pdfTexture = null;
                try
                {
                    var cmd = new CommandBuffer { name = "SdfSurfaceCacheSampledLightingTest" };
                    if (useReGIR)
                    {
                        var desc = new RenderTextureDescriptor(pdfSize, pdfSize, GraphicsFormat.R32_SFloat, 0)
                        {
                            msaaSamples = 1,
                            enableRandomWrite = true,
                            useMipMap = true,
                            autoGenerateMips = false,
                            mipCount = pdfMips,
                        };
                        pdfTexture = new RenderTexture(desc)
                        {
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp,
                        };
                        pdfTexture.Create();

                        cmd.SetComputeBufferParam(m_ReGIRShader, m_PreparePdfKernel, "_ReGIRLights", lightBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_PreparePdfKernel, "_ReGIRParameters", paramBuffer);
                        cmd.SetComputeTextureParam(m_ReGIRShader, m_PreparePdfKernel, "_ReGIRLightPdfMipOutput", pdfTexture, 0);
                        cmd.DispatchCompute(m_ReGIRShader, m_PreparePdfKernel, CoreUtils.DivRoundUp(pdfTexels, 256), 1, 1);

                        for (int mip = 1; mip < pdfMips; mip++)
                        {
                            int mipSize = Mathf.Max(1, pdfSize >> mip);
                            cmd.SetComputeBufferParam(m_ReGIRShader, m_ReducePdfKernel, "_ReGIRParameters", paramBuffer);
                            cmd.SetComputeTextureParam(m_ReGIRShader, m_ReducePdfKernel, "_ReGIRLightPdfMipInput", pdfTexture, mip - 1);
                            cmd.SetComputeTextureParam(m_ReGIRShader, m_ReducePdfKernel, "_ReGIRLightPdfMipOutput", pdfTexture, mip);
                            cmd.SetComputeIntParam(m_ReGIRShader, "_ReGIRLightPdfSourceMip", mip - 1);
                            int g = CoreUtils.DivRoundUp(mipSize, 8);
                            cmd.DispatchCompute(m_ReGIRShader, m_ReducePdfKernel, g, g, 1);
                        }

                        cmd.SetComputeBufferParam(m_ReGIRShader, m_PresampleKernel, "_ReGIRLights", lightBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_PresampleKernel, "_ReGIRParameters", paramBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_PresampleKernel, "_ReGIRPresampledLights", presampledBuffer);
                        cmd.SetComputeTextureParam(m_ReGIRShader, m_PresampleKernel, "_ReGIRLightPdfTexture", pdfTexture);
                        cmd.DispatchCompute(m_ReGIRShader, m_PresampleKernel, CoreUtils.DivRoundUp(presampledCount, 256), 1, 1);

                        cmd.SetComputeBufferParam(m_ReGIRShader, m_BuildKernel, "_ReGIRLights", lightBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_BuildKernel, "_ReGIRParameters", paramBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_BuildKernel, "_ReGIRReservoirs", reservoirBuffer);
                        cmd.SetComputeBufferParam(m_ReGIRShader, m_BuildKernel, "_ReGIRPresampledLights", presampledBuffer);
                        cmd.SetComputeTextureParam(m_ReGIRShader, m_BuildKernel, "_ReGIRLightPdfTexture", pdfTexture);
                        cmd.DispatchCompute(m_ReGIRShader, m_BuildKernel, CoreUtils.DivRoundUp(slotCount, 256), 1, 1);
                    }

                    // Bind MeshSDF scene + card + GlobalSDF fallbacks + sampled lighting inputs.
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfFields", m_Fields);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfInstances", m_Instances);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfTraceInstances", m_TraceInstances);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfNodes", m_Nodes);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfLeafInstances", m_Leaves);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_MeshSdfVoxels", m_Voxels);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_MeshSdfBrickAtlas", m_DummyBrickAtlas);
                    cmd.SetComputeVectorParam(m_CardShader, "_MeshSdfBrickAtlasSize", Vector4.zero);
                    cmd.SetComputeIntParam(m_CardShader, "_MeshSdfNodeCount", 1);

                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_GlobalSdfNear", m_DummyBrickAtlas);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_GlobalSdfMiddle", m_DummyBrickAtlas);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_GlobalSdfFar", m_DummyBrickAtlas);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_GlobalSdfMip", m_DummyBrickAtlas);
                    cmd.SetComputeIntParam(m_CardShader, "_GlobalSdfUseMip", 0);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_GlobalSdfPages", m_Leaves);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_GlobalSdfThinBricks", m_Leaves);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_GlobalSdfObjectPageWork", m_Leaves);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_GlobalSdfObjectPages", m_Leaves);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_GlobalSdfObjectGrid", m_Leaves);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfUseGlobalClipmap", 0);
                    cmd.SetComputeFloatParam(m_CardShader, "_SdfMeshDetailDistance", 0.0f);

                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_SdfDiffuseSurfaces", m_Surfaces);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_SdfLocalCardTable", m_LocalCardTable);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_SdfRadiosityWork", m_RadiosityWork);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_SdfFinalLightingOrder", m_FinalOrder);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_SdfSurfaceAlbedoDepth", m_AlbedoDepth);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_SdfSurfaceNormalCoverage", m_NormalCoverage);
                    cmd.SetComputeTextureParam(m_CardShader, m_SampledKernel, "_SdfCardLightingOutput", m_Output);

                    cmd.SetComputeIntParam(m_CardShader, "_SdfCardLightingFirstCard", 0);
                    cmd.SetComputeVectorParam(m_CardShader, "_SdfFinalLightingLayout", new Vector4(CardSize, CardSize, 1.0f, 1.0f));
                    cmd.SetComputeFloatParam(m_CardShader, "_SdfDiffuseRayBias", 0.04f);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfDiffuseLayerMask", 1);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfDiffuseLightLayerMask", 0);
                    cmd.SetComputeVectorParam(m_CardShader, "_SdfDiffuseLightPositionRange", Vector4.zero);
                    cmd.SetComputeVectorParam(m_CardShader, "_SdfDiffuseLightColorShadow", Vector4.zero);

                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_ReGIRLights", lightBuffer);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_ReGIRParameters", paramBuffer);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_ReGIRReservoirs", reservoirBuffer);
                    cmd.SetComputeBufferParam(m_CardShader, m_SampledKernel, "_ReGIRPresampledLights", presampledBuffer);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfSampledLightingLocalIterations", SdfSurfaceCacheSampledLighting.DefaultLocalIterations);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfSampledLightingLocalCandidates", SdfSurfaceCacheSampledLighting.DefaultLocalCandidates);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfSampledLightingEvaluateAll", evaluateAll ? 1 : 0);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfSampledLightingUseReGIR", useReGIR ? 1 : 0);
                    cmd.SetComputeIntParam(m_CardShader, "_SdfSampledLightingFrame", (int)frameIndex);

                    cmd.DispatchCompute(m_CardShader, m_SampledKernel, CardSize / 8, CardSize / 8, 1);
                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Release();

                    var readback = new Texture2D(CardSize, CardSize, TextureFormat.RGBAFloat, false, true);
                    var prev = RenderTexture.active;
                    RenderTexture.active = m_Output;
                    readback.ReadPixels(new Rect(0, 0, CardSize, CardSize), 0, 0, false);
                    readback.Apply(false, false);
                    RenderTexture.active = prev;
                    Color[] pixels = readback.GetPixels();
                    UnityEngine.Object.DestroyImmediate(readback);
                    return pixels;
                }
                finally
                {
                    if (pdfTexture != null)
                    {
                        pdfTexture.Release();
                        UnityEngine.Object.DestroyImmediate(pdfTexture);
                    }
                }
            }

            public void Dispose()
            {
                m_Fields?.Dispose();
                m_Instances?.Dispose();
                m_TraceInstances?.Dispose();
                m_Nodes?.Dispose();
                m_Leaves?.Dispose();
                m_Voxels?.Dispose();
                m_Surfaces?.Dispose();
                m_LocalCardTable?.Dispose();
                m_RadiosityWork?.Dispose();
                m_FinalOrder?.Dispose();
                if (m_AlbedoDepth != null) UnityEngine.Object.DestroyImmediate(m_AlbedoDepth);
                if (m_NormalCoverage != null) UnityEngine.Object.DestroyImmediate(m_NormalCoverage);
                if (m_DummyBrickAtlas != null) UnityEngine.Object.DestroyImmediate(m_DummyBrickAtlas);
                if (m_Output != null)
                {
                    m_Output.Release();
                    UnityEngine.Object.DestroyImmediate(m_Output);
                }
            }

            private static TestMeshSdfTraceData MakeTraceData(Matrix4x4 worldToLocal, uint fieldIndex, uint layerMask)
            {
                return new TestMeshSdfTraceData
                {
                    worldToLocal0 = worldToLocal.GetRow(0),
                    worldToLocal1 = worldToLocal.GetRow(1),
                    worldToLocal2 = worldToLocal.GetRow(2),
                    fieldIndex = fieldIndex,
                    layerMask = layerMask,
                };
            }

            private static TestMeshSdfInstance MakeInstance(Matrix4x4 worldToLocal, uint fieldIndex, uint layerMask)
            {
                return new TestMeshSdfInstance
                {
                    worldToLocal0 = worldToLocal.GetRow(0),
                    worldToLocal1 = worldToLocal.GetRow(1),
                    worldToLocal2 = worldToLocal.GetRow(2),
                    fieldIndex = fieldIndex,
                    layerMask = layerMask,
                    distanceScale = 1.0f,
                    previousLocalToWorld0 = worldToLocal.GetRow(0),
                    previousLocalToWorld1 = worldToLocal.GetRow(1),
                    previousLocalToWorld2 = worldToLocal.GetRow(2),
                };
            }
        }
    }
}
