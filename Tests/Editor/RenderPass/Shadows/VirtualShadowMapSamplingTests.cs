using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapSamplingTests
    {
        // Tiny pages make every edge/corner testable; production uses the same
        // functions with 128-texel pages. Physical slots are deliberately shuffled.
        private sealed class Fixture : IDisposable
        {
            internal readonly ComputeShader Shader;
            internal readonly uint[] TableData = new uint[12];
            internal readonly uint4[] MetadataData = new uint4[12];
            internal readonly uint[] OwnerData = new uint[16];
            internal readonly uint[] StaticData = new uint[256], DynamicData = new uint[256];
            internal readonly VirtualShadowMapProjection[] ProjectionData = new VirtualShadowMapProjection[3];
            internal readonly GraphicsBuffer Table = new(GraphicsBuffer.Target.Structured, 12, 4);
            internal readonly GraphicsBuffer Metadata = new(GraphicsBuffer.Target.Structured, 12, 16);
            internal readonly GraphicsBuffer Owners = new(GraphicsBuffer.Target.Structured, 16, 4);
            internal readonly GraphicsBuffer Counters = new(GraphicsBuffer.Target.Structured, 4, 4);
            private readonly GraphicsBuffer m_Projections = new(GraphicsBuffer.Target.Structured, 3, 160);
            // Integer pools support Load/Store, not filtered Sample. Texture2D's
            // constructor validates Sample usage on Unity 6.7; use the same UAV
            // resource type as production and upload through a tiny test kernel.
            private readonly RenderTexture m_Static = CreatePool();
            private readonly RenderTexture m_Dynamic = CreatePool();
            private readonly GraphicsBuffer m_StaticUpload = new(GraphicsBuffer.Target.Structured, 256, 4);
            private readonly GraphicsBuffer m_DynamicUpload = new(GraphicsBuffer.Target.Structured, 256, 4);
            private readonly ComputeShader m_UploadShader;
            private readonly BlueNoiseResources m_BlueNoise = PipelineResourceManager.Get<BlueNoiseResources>();

            private void BindBlueNoise(int kernel)
            {
                Shader.SetTexture(kernel, "_SobolScramblingTile1SPP", m_BlueNoise.ScramblingTile1SPP);
                Shader.SetTexture(kernel, "_SobolRankingTile1SPP", m_BlueNoise.RankingTile1SPP);
                Shader.SetTexture(kernel, "_SobolOwenScrambledSequence", m_BlueNoise.OwenScrambledSequence);
            }

            private static RenderTexture CreatePool()
            {
                var texture = new RenderTexture(new RenderTextureDescriptor(16, 16)
                {
                    graphicsFormat = GraphicsFormat.R32_UInt,
                    depthStencilFormat = GraphicsFormat.None,
                    enableRandomWrite = true,
                    msaaSamples = 1,
                });
                Assert.That(texture.Create(), Is.True);
                return texture;
            }

            internal Fixture(bool allocator = false)
            {
                string path = allocator ? "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"
                    : "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute";
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                Assert.That(source, Is.Not.Null, path);
                Shader = Object.Instantiate(source);
                m_UploadShader = allocator ? Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute")) : Shader;
                Shader.SetInt("_VSMPrototypeEnabled", 1);
                Shader.SetVector("_VSMReceiverParameters", Vector4.zero);
                Shader.SetVector("_VSMReceiverQuality", Vector4.zero);
                Shader.SetVector("_VSMSMRTParameters", Vector4.zero);
                Shader.SetMatrix("_VSMReceiverViewProjection", Matrix4x4.identity);
                Shader.SetInt("_CSMOutputWidth", 8); Shader.SetInt("_CSMOutputHeight", 8);
                Shader.SetInt("_VSMPrototypeRequestEnabled", 1);
                Shader.SetInt("_VSMProjectionCount", 3);
                Shader.SetInt("_VSMPrototypeVirtualResolution", 8);
                Shader.SetInt("_VSMPrototypePageSize", 4);
                Shader.SetInt("_VSMPrototypePagesPerAxis", 2);
                Shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                Shader.SetInt("_VSMPrototypePageTableEntryCount", 12);
                Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 16);
                Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
                Shader.SetInt("_CSMFrameIndex", 7);
                Shader.SetInts("_SamplingPixel", 0, 0);
                for (int i = 0; i < 3; i++)
                {
                    Matrix4x4 matrix = Matrix4x4.identity;
                    matrix.m00 = matrix.m11 = 0.1f / (1 << i);
                    matrix.m03 = matrix.m13 = matrix.m23 = 0.5f;
                    ProjectionData[i] = new VirtualShadowMapProjection
                    {
                        WorldToShadow = matrix,
                        SelectionSphere = new Vector4(0, 0, 0, -5 * (1 << i)),
                        Parameters = new Vector4(1 << i, 0, 0.1f, 100),
                    };
                }
            }

            internal void Map(int page, int slot, float staticDepth = 0, float dynamicDepth = 0)
            {
                TableData[page] = (uint)slot + 1;
                MetadataData[page] = new uint4(10, (uint)slot + 1, 7, 8);
                OwnerData[slot] = (uint)page + 1;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                {
                    int pixel = (slot / 4 * 4 + y) * 16 + slot % 4 * 4 + x;
                    StaticData[pixel] = math.asuint(staticDepth);
                    DynamicData[pixel] = math.asuint(dynamicDepth);
                }
            }

            internal void Upload()
            {
                Table.SetData(TableData); Metadata.SetData(MetadataData); Owners.SetData(OwnerData);
                Counters.SetData(new uint[4]); m_Projections.SetData(ProjectionData);
                m_StaticUpload.SetData(StaticData); m_DynamicUpload.SetData(DynamicData);
                int upload = m_UploadShader.FindKernel("UploadTestPools");
                m_UploadShader.SetBuffer(upload, "_TestStaticData", m_StaticUpload);
                m_UploadShader.SetBuffer(upload, "_TestDynamicData", m_DynamicUpload);
                m_UploadShader.SetTexture(upload, "_TestStaticPool", m_Static);
                m_UploadShader.SetTexture(upload, "_TestDynamicPool", m_Dynamic);
                m_UploadShader.Dispatch(upload, 2, 2, 1);
            }

            internal float2[] Run(string kernelName, float4[] inputs, int2[] offsets = null, float4[] normals = null,
                Texture2D depth = null)
            {
                int kernel = Shader.FindKernel(kernelName);
                BindBlueNoise(kernel);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
                using var offset = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 8);
                using var normal = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 8);
                input.SetData(inputs); offset.SetData(offsets ?? new int2[inputs.Length]);
                normal.SetData(normals ?? new float4[inputs.Length]);
                Shader.SetInt("_SamplingCount", inputs.Length);
                Shader.SetBuffer(kernel, "_SamplingInputs", input);
                bool inspectOnly = kernelName == "InspectBias" || kernelName == "InspectTransition"
                    || kernelName == "InspectScreenNormal" || kernelName == "InspectVSMStochasticSample"
                    || kernelName == "InspectVSMStochasticTexelOffset" || kernelName == "InspectSMRTSamples";
                if (depth != null)
                {
                    Shader.SetTexture(kernel, "_DepthTexture", depth);
                    Shader.SetInt("_CSMOutputWidth", depth.width);
                    Shader.SetInt("_CSMOutputHeight", depth.height);
                }
                if (!inspectOnly) Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", Metadata);
                if (kernelName != "MarkFootprints")
                {
                    Shader.SetBuffer(kernel, "_SamplingResults", output);
                    if (!inspectOnly)
                    {
                        Shader.SetBuffer(kernel, "_VSMPrototypePageTable", Table);
                        Shader.SetTexture(kernel, "_VSMPrototypeStaticPhysicalPage", m_Static);
                        Shader.SetTexture(kernel, "_VSMPrototypeDynamicPhysicalPage", m_Dynamic);
                    }
                    if (kernelName == "SampleTaps") Shader.SetBuffer(kernel, "_SamplingOffsets", offset);
                    else if (kernelName != "InspectTransition" && kernelName != "InspectVSMStochasticSample"
                        && kernelName != "InspectVSMStochasticTexelOffset")
                    {
                        Shader.SetBuffer(kernel, "_SamplingNormals", normal);
                        if (kernelName != "FilterFootprints" && kernelName != "InspectScreenNormal")
                            Shader.SetBuffer(kernel, "_VSMProjections", m_Projections);
                    }
                }
                Shader.Dispatch(kernel, (inputs.Length + 63) / 64, 1, 1);
                var result = new float2[inputs.Length];
                if (kernelName != "MarkFootprints") output.GetData(result);
                Metadata.GetData(MetadataData);
                return result;
            }

            internal void MarkScreen(Texture depth, Texture normal, int frame, bool resetAge)
            {
                int clear = Shader.FindKernel(resetAge ? "VSMPrototypeResetReceiverFeedback" : "VSMPrototypeClearReceiverRequests");
                Shader.SetBuffer(clear, "_VSMPrototypePageMetadata", Metadata);
                Shader.Dispatch(clear, 1, 1, 1);
                int mark = Shader.FindKernel("VSMMarkReceiverPages");
                Shader.SetInt("_CSMFrameIndex", frame);
                Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                Shader.SetMatrix("_CSMInvViewProjMatrix", Matrix4x4.identity);
                Shader.SetBuffer(mark, "_VSMProjections", m_Projections);
                Shader.SetBuffer(mark, "_VSMPrototypePageMetadata", Metadata);
                Shader.SetTexture(mark, "_DepthTexture", depth);
                Shader.SetTexture(mark, "_GBuffer1", normal);
                // No physical pool or page-table binding: cold start must work.
                Shader.Dispatch(mark, 1, 1, 1);
                Metadata.GetData(MetadataData);
            }

            internal void Allocate()
            {
                int kernel = Shader.FindKernel("VSMPrototypeAllocatePages");
                Shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", Table);
                Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", Metadata);
                Shader.SetBuffer(kernel, "_VSMPrototypePhysicalPageOwners", Owners);
                Shader.SetBuffer(kernel, "_VSMPrototypeAllocatorCounters", Counters);
                Shader.Dispatch(kernel, 1, 1, 1);
                Table.GetData(TableData); Metadata.GetData(MetadataData); Owners.GetData(OwnerData);
            }

            internal float4 RunDiagnostic(float4 receiver, int mode, bool footprint = false,
                Matrix4x4? viewProjection = null, Vector3? receiverNormal = null, int screenSize = 8)
            {
                int kernel = Shader.FindKernel(footprint ? "InspectReceiverFootprint" : "InspectReceiverDiagnostics");
                BindBlueNoise(kernel);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var normal = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                Vector3 n = receiverNormal ?? Vector3.forward;
                input.SetData(new[] { receiver }); normal.SetData(new[] { new float4(n.x, n.y, n.z, 0) });
                Shader.SetInt("_SamplingCount", 1);
                Shader.SetInt("_VSMReceiverDebugMode", mode);
                Shader.SetInt("_CSMOutputWidth", screenSize); Shader.SetInt("_CSMOutputHeight", screenSize);
                Shader.SetMatrix("_VSMReceiverViewProjection", viewProjection ?? Matrix4x4.identity);
                Shader.SetBuffer(kernel, "_SamplingInputs", input);
                Shader.SetBuffer(kernel, "_SamplingNormals", normal);
                Shader.SetBuffer(kernel, "_DiagnosticResults", output);
                Shader.SetBuffer(kernel, "_VSMProjections", m_Projections);
                if (!footprint)
                {
                    Shader.SetBuffer(kernel, "_VSMPrototypePageTable", Table);
                    Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", Metadata);
                    Shader.SetTexture(kernel, "_VSMPrototypeStaticPhysicalPage", m_Static);
                    Shader.SetTexture(kernel, "_VSMPrototypeDynamicPhysicalPage", m_Dynamic);
                }
                Shader.Dispatch(kernel, 1, 1, 1);
                var result = new float4[1]; output.GetData(result);
                Metadata.GetData(MetadataData);
                return result[0];
            }

            internal void RunScreenDiagnostic(Texture depth, Texture normal, Texture shadow,
                RenderTexture output, RenderTexture data, int mode)
            {
                int kernel = Shader.FindKernel("VSMReceiverDebug");
                BindBlueNoise(kernel);
                Shader.SetInt("_VSMReceiverDebugMode", mode);
                Shader.SetInt("_CSMOutputWidth", depth.width); Shader.SetInt("_CSMOutputHeight", depth.height);
                Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse());
                Shader.SetMatrix("_VSMReceiverViewProjection", ScreenInverse().inverse);
                Shader.SetBuffer(kernel, "_VSMProjections", m_Projections);
                Shader.SetBuffer(kernel, "_VSMPrototypePageTable", Table);
                Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", Metadata);
                Shader.SetTexture(kernel, "_VSMPrototypeStaticPhysicalPage", m_Static);
                Shader.SetTexture(kernel, "_VSMPrototypeDynamicPhysicalPage", m_Dynamic);
                Shader.SetTexture(kernel, "_DepthTexture", depth);
                Shader.SetTexture(kernel, "_GBuffer1", normal);
                Shader.SetTexture(kernel, "_VSMReceiverDebugShadow", shadow);
                Shader.SetTexture(kernel, "_VSMReceiverDebugOutput", output);
                Shader.SetTexture(kernel, "_VSMReceiverDebugData", data);
                Shader.Dispatch(kernel, (depth.width + 7) / 8, (depth.height + 7) / 8, 1);
            }

            public void Dispose()
            {
                Table.Dispose(); Metadata.Dispose(); Owners.Dispose(); Counters.Dispose(); m_Projections.Dispose();
                m_StaticUpload.Dispose(); m_DynamicUpload.Dispose();
                m_Static.Release(); m_Dynamic.Release();
                if (m_UploadShader != Shader) Object.DestroyImmediate(m_UploadShader);
                Object.DestroyImmediate(m_Static); Object.DestroyImmediate(m_Dynamic); Object.DestroyImmediate(Shader);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CurrentReceiverMarking_ColdStartAllocatesSameFrameAndClearsDepartedDemand(bool density, bool smrt)
        {
            using var f = new Fixture(allocator: true);
            var depth = new Texture2D(8, 8, TextureFormat.RFloat, false, true);
            var normal = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            try
            {
                var depths = new float[64]; var normals = new Color[64];
                for (int i = 0; i < 64; i++) { depths[i] = .5f; normals[i] = new Color(.5f, .5f, 0, 0); }
                depth.SetPixelData(depths, 0); depth.Apply(); normal.SetPixels(normals); normal.Apply();
                f.Shader.SetVector("_VSMReceiverQuality", new Vector4(density ? 1 : 0, 1, .025f, 1));
                f.Shader.SetVector("_VSMSMRTParameters", smrt ? new Vector4(4, 8, 10, .05f) : Vector4.zero);
                f.Upload();
                f.MarkScreen(depth, normal, 0, true);
                int requested = 0;
                var roles = new uint[f.MetadataData.Length];
                for (int i = 0; i < roles.Length; i++) roles[i] = f.MetadataData[i].x & 3841u;
                for (int i = 0; i < f.MetadataData.Length; i++)
                    if ((f.MetadataData[i].x & 1) != 0) { requested++; Assert.That(f.MetadataData[i].z, Is.Zero); }
                Assert.That(requested, Is.GreaterThan(0));
                f.Allocate();
                var counters = new uint[4]; f.Counters.GetData(counters);
                Assert.That(counters[1], Is.EqualTo(requested));
                Assert.That(counters[2], Is.EqualTo(requested));
                Assert.That(counters[3], Is.Zero);
                // Allocation consumes roles; the next marker must still request every receiver.
                f.MarkScreen(depth, normal, 1, false);
                for (int i = 0; i < roles.Length; i++) Assert.That(f.MetadataData[i].x & 3841u, Is.EqualTo(roles[i]));
                f.Allocate();
                f.Counters.GetData(counters);
                Assert.That(counters[1], Is.EqualTo(requested));
                Assert.That(counters[2], Is.Zero);
                for (int i = 0; i < depths.Length; i++) depths[i] = SystemInfo.usesReversedZBuffer ? 0 : 1;
                depth.SetPixelData(depths, 0); depth.Apply();
                f.MarkScreen(depth, normal, 2, false); f.Allocate();
                f.Counters.GetData(counters);
                Assert.That(counters[1], Is.Zero, "Sky must not retain last frame's demand.");
                for (int i = 0; i < f.MetadataData.Length; i++)
                    if (f.TableData[i] != 0) Assert.That(f.MetadataData[i].z, Is.EqualTo(1), "LRU age survives ordinary request clearing.");
            }
            finally { Object.DestroyImmediate(depth); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void SMRT_BND1PhasesAdvanceAcross256FramesAndKeepRayStrataAndReceiverSupport()
        {
            using var f = new Fixture();
            var inputs = new float4[256 * 4];
            var rays = new float4[inputs.Length];
            for (int count = 4; count <= 8; count++)
            {
                for (int frame = 0; frame < 256; frame++) for (int mode = 0; mode < 4; mode++)
                {
                    int i = frame * 4 + mode;
                    inputs[i] = new float4(19, 37, frame, mode);
                    rays[i] = new float4(frame % count, count, 0, 0);
                }
                var samples = f.Run("InspectSMRTSamples", inputs, normals: rays);
                var visited = new bool[4, 256];
                for (int frame = 0; frame < 256; frame++)
                {
                    int i = frame * 4;
                    for (int dim = 0; dim < 4; dim++)
                    {
                        float phase = samples[i + dim / 2][dim % 2];
                        Assert.That(phase, Is.InRange(0f, .99999994f));
                        int bin = Mathf.FloorToInt(phase * 256);
                        Assert.That(visited[dim, bin], Is.False, "Temporal sequence must not use the static 1SPP mask");
                        visited[dim, bin] = true;
                    }
                    int ray = frame % count;
                    float radiusSquared = math.lengthsq(samples[i + 2]);
                    Assert.That(radiusSquared, Is.InRange((float)ray / count - 1e-6f, (float)(ray + 1) / count + 1e-6f));
                    Assert.That(samples[i + 3].x, Is.InRange(-1f, 1f));
                    Assert.That(samples[i + 3].y, Is.InRange(-1f, 1f));
                }
                for (int i = 0; i < inputs.Length; i++) inputs[i].z += 256;
                var repeated = f.Run("InspectSMRTSamples", inputs, normals: rays);
                CollectionAssert.AreEqual(samples, repeated, "Full temporal sequence repeats at 256 frames");
            }
        }

        [Test]
        public void SMRT_TraversesThinCellsWithoutBridgingEmptyDepthOrLockingCentralShadow()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 8, 10, .5f));
            // A one-texel rod at x=4, t=2. It is an independent geometric slab:
            // a ray hits exactly when its x at t=2 lies in [4,5), at any y.
            for (int y = 0; y < 8; y++) SetSMRTDepth(f, 4, y, .3f);
            f.Upload();
            var inputs = new float4[400]; var rays = new float4[400]; var expected = new bool[400];
            for (int i = 0; i < inputs.Length; i++)
            {
                float x = 2.051f + (i % 20) * .18f, slope = -.6f + (i / 20) * .06f;
                inputs[i] = new float4(x / 8, .45f, .2f, 0);
                rays[i] = new float4(slope, 0, 3, 0);
                float hitX = x + slope * 2;
                expected[i] = hitX >= 4 && hitX < 5;
            }
            float2[] result = f.Run("TraceSMRTRays", inputs, normals: rays);
            for (int i = 0; i < result.Length; i++)
            {
                Assert.That(result[i].x, Is.EqualTo(1), "Complete ray " + i);
                Assert.That(result[i].y, Is.EqualTo(expected[i] ? 0 : 1), "Analytic rod " + i);
            }
        }

        private static void SetSMRTDepth(Fixture f, int x, int y, float depth)
        {
            int slot = (int)f.TableData[y / 4 * 2 + x / 4] - 1;
            f.StaticData[(slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4] = math.asuint(depth);
        }

        [Test]
        public void SMRT_PageReuseTraversesShuffledPhysicalPagesInBothDirections()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            SetSMRTDepth(f, 2, 1, .4f); SetSMRTDepth(f, 5, 1, .4f); f.Upload();
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 8, 5, 1));
            var origins = new[] { new float4(1.5f / 8, 1.5f / 8, .2f, 0), new float4(6.5f / 8, 1.5f / 8, .2f, 0) };
            var rays = new[] { new float4(1, 0, 5, .5f), new float4(-1, 0, 5, .5f) };
            var result = f.Run("TraceSMRTRays", origins, normals: rays);
            Assert.That(result[0], Is.EqualTo(new float2(1, 0)));
            Assert.That(result[1], Is.EqualTo(new float2(1, 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SMRT_PageReuseRejectsInvalidNextPage(bool reverse)
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 8, 5, 1));
            int nextPage = reverse ? 0 : 1;
            var origin = new[] { new float4((reverse ? 6.5f : 1.5f) / 8, 1.5f / 8, .2f, 0) };
            var ray = new[] { new float4(reverse ? -1 : 1, 0, 5, .5f) };
            for (int fault = 0; fault < 3; fault++)
            {
                f.Map(nextPage, 11 - nextPage);
                if (fault == 0) f.TableData[nextPage] = 0;
                else if (fault == 1) f.MetadataData[nextPage].x |= 4;
                else f.MetadataData[nextPage].y++;
                f.Upload();
                Assert.That(f.Run("TraceSMRTRays", origin, normals: ray)[0].x, Is.Zero);
            }
        }

        [Test]
        public void SMRT_ParallelTailPreservesFarOccludersAndExhaustionIsUnavailable()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 1, .5f));
            SetSMRTDepth(f, 4, 3, .8f); f.Upload(); // t=12, well beyond the bend at t=1.
            var inputs = new[] { new float4(3.75f / 8, 3.5f / 8, .2f, 0) };
            Assert.That(f.Run("TraceSMRTRays", inputs, normals: new[] { new float4(.5f, 0, 1, 0) })[0],
                Is.EqualTo(new float2(1, 0)));
            Assert.That(f.Run("TraceSMRTRays", inputs, normals: new[] { new float4(-10, 0, 10, 0) })[0].x,
                Is.Zero, "Out-of-map/budget failure cannot become a clear ray");
        }

        [Test]
        public void SMRT_ContinuationPreservesWorldRayAcrossScrolledAndRescaledClipmaps()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            for (int level = 0; level < 3; level++)
            {
                var p = f.ProjectionData[level];
                p.WorldToShadow = Matrix4x4.identity;
                p.WorldToShadow.m00 = p.WorldToShadow.m11 = 1f / (8 << level);
                p.WorldToShadow.m03 = level == 1 ? .375f : .5f;
                p.WorldToShadow.m13 = .5f;
                p.WorldToShadow.m22 = level == 1 ? .1f : .05f;
                p.WorldToShadow.m23 = level == 1 ? .4f : .2f;
                f.ProjectionData[level] = p;
            }
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 6, .5f));
            // Receiver x=-2, dx/dt=.5. The true ray reaches x=.5 at t=5;
            // the former fine-level tail stopped diverging at t=2.827, x<0.
            for (int y = 0; y < 8; y++) for (int x = 3; x < 8; x++)
            {
                int slot = (int)f.TableData[4 + y / 4 * 2 + x / 4] - 1;
                f.StaticData[(slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4] = math.asuint(.9f);
            }
            f.Upload();
            var receiver = new[] { new float4(.25f, .5f, .2f, 0) };
            var ray = new[] { new float4(.5f, 0, 0, 0) };
            Assert.That(f.Run("TraceSMRTClipmaps", receiver, normals: ray)[0], Is.EqualTo(new float2(1, 0)));
            // Without parents, the full disk no longer fits this fine map.
            // The filter must reject the union, rather than depend on ray phase.
            f.Shader.SetInt("_VSMProjectionCount", 1);
            Assert.That(f.Run("FilterSMRTFootprints", receiver)[0].x, Is.Zero);
        }

        [Test]
        public void SMRT_ContinuationDoesNotRestartBehindItsSegmentAndRequestsPrimaryDependencies()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page, page >= 4 ? .55f : 0);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 6, .5f));
            f.Upload();
            // Parent surface t=1 lies behind the first segment end (t=2.827).
            var receiver = new[] { new float4(.5f, .5f, .5f, 0) };
            Assert.That(f.Run("TraceSMRTClipmaps", receiver, normals: new[] { new float4(.2f, 0, 0, 0) })[0],
                Is.EqualTo(new float2(1, 1)));
            f.Run("ResolveReceivers", new[] { new float4(0, 0, 0, 0) });
            for (int page = 4; page < 12; page++)
                Assert.That(f.MetadataData[page].x & 512u, Is.Not.Zero, "Continuation depth is a primary dependency");
        }

        [Test]
        public void SMRT_FootprintValidityDoesNotDependOnRandomPhase()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 1, .5f));
            var input = new[] { new float4(.48f, .48f, .2f, 0) };
            for (int frame = 0; frame < 16; frame++)
            {
                f.Shader.SetInt("_CSMFrameIndex", frame);
                f.MetadataData[3].x = 10; f.Upload();
                Assert.That(f.Run("FilterSMRTFootprints", input)[0], Is.EqualTo(new float2(1, 1)), "Empty resident pages");
                f.MetadataData[3].x |= 4; f.Upload();
                Assert.That(f.Run("FilterSMRTFootprints", input)[0].x, Is.Zero, "Dirty union invalidates all phases");
            }
        }

        [Test]
        public void SMRT_ResolveEntryUsesSoftOutputWithoutPCFOverwritingItsOutParameter()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            for (int y = 0; y < 8; y++) for (int x = 4; x < 8; x++) SetSMRTDepth(f, x, y, .205f);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            var receiver = new[] { new float4(.1f, .1f, -.3f, 0) };
            float pcf = f.Run("ResolveReceivers", receiver)[0].x;
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 1, .5f));
            Assert.That(pcf, Is.InRange(.1f, .9f));
            float soft = f.Run("FilterSMRTFootprints", new[] { new float4(.51f, .51f, .2f, 0) })[0].y;
            Assert.That(soft, Is.Not.EqualTo(pcf));
            Assert.That(f.Run("ResolveReceivers", receiver)[0].x, Is.EqualTo(soft));
        }

        [Test]
        public void SMRT_MissingFineSupportRetriesParentThenCompletePCFWithoutLosingOcclusion()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page, page >= 4 && page < 8 ? .8f : 0);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 4, 1, .5f));
            var receiver = new[] { new float4(0, 0, 0, 0) };
            f.MetadataData[3].x |= 4; f.Upload();
            Assert.That(f.Run("ResolveReceivers", receiver)[0].x, Is.Zero);
            var requested = new uint[12];
            for (int page = 0; page < 12; page++) requested[page] = f.MetadataData[page].x & 3841u;
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page, page >= 4 && page < 8 ? .8f : 0);
            f.Upload(); f.Run("ResolveReceivers", receiver);
            for (int page = 0; page < 12; page++)
                Assert.That(f.MetadataData[page].x & 3841u, Is.EqualTo(requested[page]));
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page, .8f);
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 8, 100, .5f)); f.Upload();
            Assert.That(f.Run("ResolveReceivers", receiver)[0].x, Is.Zero);
        }

        [Test]
        public void SMRT_BoundedGapFillDoesNotExtendAcrossEmptyDepth()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            f.Shader.SetVector("_VSMSMRTParameters", new Vector4(4, 8, 10, .5f));
            SetSMRTDepth(f, 3, 3, .3f); SetSMRTDepth(f, 4, 3, .45f); f.Upload();
            var receiver = new[] { new float4(3.5f / 8, 3.5f / 8, .2f, 0) };
            var ray = new[] { new float4(.5f, 0, 4, 0) };
            Assert.That(f.Run("TraceSMRTRays", receiver, normals: ray)[0].y, Is.Zero);
            SetSMRTDepth(f, 4, 3, 0); f.Upload();
            Assert.That(f.Run("TraceSMRTRays", receiver, normals: ray)[0], Is.EqualTo(new float2(1, 1)));
        }

        [Test]
        public void SMRT_SlopedUnoccludedReceiverStaysLitWithOriginIntegrationAndOddRayCounts()
        {
            using var f = new Fixture();
            for (int page = 0; page < 12; page++) f.Map(page, 11 - page);
            var projection = f.ProjectionData[0];
            projection.Parameters.x = .01f; projection.WorldToShadow.m22 = .05f;
            f.ProjectionData[0] = projection;
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            var input = new float4[128]; var bias = new float4[128];
            foreach (float gx in new[] { -.002f, 0, .002f }) foreach (float gy in new[] { -.002f, .002f })
            {
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    SetSMRTDepth(f, x, y, .2f + (x - 3.5f) * gx + (y - 3.5f) * gy);
                for (int i = 0; i < input.Length; i++)
                {
                    float x = 3.6f + i % 16 * .05f, y = 3.6f + i / 16 * .1f;
                    input[i] = new float4(x / 8, y / 8, .2f + (x - 4) * gx + (y - 4) * gy, 0);
                    bias[i] = new float4(gx, gy, .0005f, 0);
                }
                f.Upload();
                for (int rays = 4; rays <= 8; rays++)
                {
                    f.Shader.SetVector("_VSMSMRTParameters", new Vector4(rays, 8, 2, Mathf.Tan(.25f * Mathf.Deg2Rad)));
                    var output = f.Run("FilterSMRTFootprints", input, normals: bias);
                    foreach (float2 sample in output) Assert.That(sample, Is.EqualTo(new float2(1, 1)));
                }
            }
        }

        [SetUp]
        public void RequireSupportedDevice()
            => Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void ReceiverDebugKernel_WritesRawDataAndSkyWithoutFeedback(int mode)
        {
            using var f = new Fixture(allocator: true);
            var depth = new Texture2D(8, 8, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var normal = new Texture2D(8, 8, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
            var output = new RenderTexture(new RenderTextureDescriptor(8, 8)
            {
                graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm, depthStencilFormat = GraphicsFormat.None,
                enableRandomWrite = true, msaaSamples = 1,
            });
            var data = new RenderTexture(new RenderTextureDescriptor(8, 8)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat, depthStencilFormat = GraphicsFormat.None,
                enableRandomWrite = true, msaaSamples = 1,
            });
            try
            {
                Assert.That(output.Create() && data.Create(), Is.True);
                var depths = new float[64]; var normals = new float4[64];
                for (int i = 0; i < 64; i++) { depths[i] = 0.25f; normals[i] = new float4(0.5f, 0.5f, 0, 0); }
                depths[0] = SystemInfo.usesReversedZBuffer ? 0 : 1;
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                normal.SetPixelData(normals, 0); normal.Apply(false, false);
                for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
                f.Upload();
                var before = (uint4[])f.MetadataData.Clone();
                // Reuse the known 0.25 depth as the source-shadow value to test
                // comparison packing independently of the recomputed hard shadow.
                f.RunScreenDiagnostic(depth, normal, depth, output, data, mode);
                var rawReadback = AsyncGPUReadback.Request(data);
                rawReadback.WaitForCompletion();
                Assert.That(rawReadback.hasError, Is.False);
                var raw = rawReadback.GetData<float4>();
                Assert.That(raw[0], Is.EqualTo(new float4(-2)));
                float4 expected = mode switch
                {
                    3 => new float4(4, 4, 1, 1),
                    4 => new float4(1, 1, 1, 0),
                    5 => new float4(0, 0, 0.25f, 0.25f),
                    6 => new float4(-1),
                    _ => new float4(0, 0, -1, 0),
                };
                Assert.That(math.distance(raw[36], expected), Is.LessThan(1e-5f));
                var colorReadback = AsyncGPUReadback.Request(output);
                colorReadback.WaitForCompletion();
                Assert.That(colorReadback.hasError, Is.False);
                Assert.That(colorReadback.GetData<Color32>()[0], Is.EqualTo(new Color32(0, 0, 0, 255)));
                f.Metadata.GetData(f.MetadataData);
                Assert.That(f.MetadataData, Is.EqualTo(before));
            }
            finally
            {
                output.Release(); data.Release();
                Object.DestroyImmediate(output); Object.DestroyImmediate(data);
                Object.DestroyImmediate(depth); Object.DestroyImmediate(normal);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReceiverDiagnostics_MatchResolveAndDoNotWriteFeedback(bool pcf)
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, 0));
            var receiver = new float4(0.625f, 0.625f, 0, 0);
            var before = (uint4[])f.MetadataData.Clone();
            float4 levels = f.RunDiagnostic(receiver, 0);
            Assert.That(levels.xyz, Is.EqualTo(new float3(0, 0, -1)));
            float4 work = f.RunDiagnostic(receiver, 4);
            Assert.That(work, Is.EqualTo(new float4(pcf ? 9 : 1, pcf ? 9 : 1, 1, 0)));
            float4 status = f.RunDiagnostic(receiver, 5);
            Assert.That(status.x, Is.Zero);
            Assert.That(f.MetadataData, Is.EqualTo(before), "Diagnostic replay must not request pages or update timestamps.");
            float2[] reference = f.Run("ResolveReceivers", new[] { receiver }, normals: new[] { new float4(0, 0, 1, 0) });
            Assert.That(status.y, Is.EqualTo(reference[0].x));
        }

        [Test]
        public void ReceiverDiagnostics_ReportFallbackDirtyAndTransitionWork()
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
            for (int i = 0; i < 4; i++) f.MetadataData[i].x |= 4u;
            f.Upload();
            var receiver = new float4(0.625f, 0.625f, 0, 0);
            Assert.That(f.RunDiagnostic(receiver, 0).xyz, Is.EqualTo(new float3(0, 1, -1)));
            Assert.That(f.RunDiagnostic(receiver, 4), Is.EqualTo(new float4(2, 1, 2, 0)));
            Assert.That(f.RunDiagnostic(receiver, 5).x, Is.EqualTo(2));
            for (int i = 0; i < 4; i++) f.MetadataData[i].x &= ~4u;
            f.Upload();
            receiver.x = 2.25f;
            float4 levels = f.RunDiagnostic(receiver, 0);
            Assert.That(levels.xyz, Is.EqualTo(new float3(0, 0, 1)));
            Assert.That(levels.w, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(f.RunDiagnostic(receiver, 4), Is.EqualTo(new float4(2, 2, 2, 1)));
        }

        [Test]
        public void ReceiverDiagnostics_DistinguishEmptyResidentAndAllMissing()
        {
            using var f = new Fixture();
            f.Upload();
            var receiver = new float4(0.625f, 0.625f, 0, 0);
            Assert.That(f.RunDiagnostic(receiver, 0).xyz, Is.EqualTo(new float3(0, -1, -1)));
            Assert.That(f.RunDiagnostic(receiver, 5).xy, Is.EqualTo(new float2(1, 1)));
            for (int i = 0; i < 4; i++) f.Map(i, i);
            f.Upload();
            Assert.That(f.RunDiagnostic(receiver, 0).xyz, Is.EqualTo(new float3(0, 0, -1)));
            Assert.That(f.RunDiagnostic(receiver, 5).xy, Is.EqualTo(new float2(0, 1)));
        }

        [Test]
        public void ReceiverFootprint_ScalesWithVirtualTexels()
        {
            using var f = new Fixture();
            f.Upload();
            Assert.That(f.RunDiagnostic(new float4(0, 0, 0, 0), 0, true).xy, Is.EqualTo(new float2(4, 4)));
            Assert.That(f.RunDiagnostic(new float4(0, 0, 0, 1), 0, true).xy, Is.EqualTo(new float2(8, 8)));
            Assert.That(f.RunDiagnostic(new float4(0, 0, 0, -1), 0, true).xy, Is.EqualTo(new float2(-1, -1)));
        }

        [Test]
        public void DensityPolicy_RespondsToOutputResolutionZoomAndVirtualTexelDensity()
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 8, 0));
            Assert.That(f.RunDiagnostic(float4.zero, 6).xyz, Is.EqualTo(new float3(1, 0, 1)));
            Assert.That(f.RunDiagnostic(float4.zero, 6, screenSize: 16).xyz, Is.EqualTo(new float3(0, 0, 0)));
            Assert.That(f.RunDiagnostic(float4.zero, 6,
                viewProjection: Matrix4x4.Scale(new Vector3(2, 2, 1))).xyz, Is.EqualTo(new float3(0, 0, 0)));
            for (int i = 0; i < 3; i++) f.ProjectionData[i].Parameters.x *= 0.5f;
            f.Upload();
            Assert.That(f.RunDiagnostic(float4.zero, 6).xyz, Is.EqualTo(new float3(2, 0, 2)));
        }

        [Test]
        public void DensityPolicy_PerspectiveDistanceFovAndGeometricSlopeAffectDemand()
        {
            using var f = new Fixture();
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 0));
            Matrix4x4 near = Matrix4x4.Perspective(60, 1, 0.1f, 100) * Matrix4x4.Translate(new Vector3(0, 0, -8));
            Matrix4x4 far = Matrix4x4.Perspective(60, 1, 0.1f, 100) * Matrix4x4.Translate(new Vector3(0, 0, -16));
            float nearLOD = f.RunDiagnostic(float4.zero, 6, viewProjection: near).x;
            Assert.That(f.RunDiagnostic(float4.zero, 6, viewProjection: far).x, Is.EqualTo(nearLOD + 1).Within(1e-5));
            Matrix4x4 zoom = Matrix4x4.Perspective(30, 1, 0.1f, 100) * Matrix4x4.Translate(new Vector3(0, 0, -8));
            Assert.That(f.RunDiagnostic(float4.zero, 6, viewProjection: zoom).x, Is.LessThan(nearLOD));
            Matrix4x4 angled = Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0));
            float flatLOD = f.RunDiagnostic(float4.zero, 6, viewProjection: angled).x;
            Assert.That(f.RunDiagnostic(float4.zero, 6, viewProjection: angled,
                receiverNormal: new Vector3(-0.8f, 0, 0.6f)).x, Is.LessThan(flatLOD));
            Assert.That(f.RunDiagnostic(float4.zero, 6, receiverNormal: Vector3.right).w, Is.EqualTo(-1));
        }

        [Test]
        public void DensityPolicy_UsesAvailableFineCoverageWithoutResizingTheProjection()
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
            f.Upload();
            var receiver = new float4(3, 0, 0, 0);
            Assert.That(f.RunDiagnostic(receiver, 0).x, Is.EqualTo(1), "Legacy half-radius selection.");
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 0));
            Assert.That(f.RunDiagnostic(receiver, 0).xy, Is.EqualTo(new float2(0, 0)));
            Assert.That(f.RunDiagnostic(new float4(6, 0, 0, 0), 6).yz, Is.EqualTo(new float2(1, 1)));
            Assert.That(f.RunDiagnostic(new float4(21, 0, 0, 0), 0).xy, Is.EqualTo(new float2(-1, -1)));
            Assert.That(f.RunDiagnostic(new float4(0, 0, 101, 0), 5).y, Is.EqualTo(1));
        }

        [Test]
        public void DensityPolicy_DisablingRestoresLegacyChoiceAndUnavailableQualityView()
        {
            using var f = new Fixture();
            f.Upload();
            var receiver = new float4(3, 0, 0, 0);
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 0));
            Assert.That(f.RunDiagnostic(receiver, 0).x, Is.Zero);
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(false, 1, 0));
            Assert.That(f.RunDiagnostic(receiver, 0).x, Is.EqualTo(1));
            Assert.That(f.RunDiagnostic(receiver, 6), Is.EqualTo(new float4(-1)));
        }

        [Test]
        public void DensityPolicy_PcfCoverageGuardAndNormalOffsetConstrainFineRequests()
        {
            using var f = new Fixture();
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 0));
            var receiver = new float4(4.5f, 0, 0, 0);
            Assert.That(f.RunDiagnostic(receiver, 6).y, Is.Zero);
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            Assert.That(f.RunDiagnostic(receiver, 6).yz, Is.EqualTo(new float2(1, 1)));
            f.Shader.SetVector("_VSMReceiverParameters", Vector4.zero);
            for (int i = 0; i < 3; i++) f.ProjectionData[i].Parameters.y = 1;
            f.Upload();
            // A grazing X normal offsets the fine receiver by one world texel,
            // outside level zero; level one still covers the biased receiver.
            Assert.That(f.RunDiagnostic(receiver, 6, receiverNormal: Vector3.right).yz, Is.EqualTo(new float2(1, 1)));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(.05f)]
        public void DensityPolicy_TransitionsAtLodBoundaryWithoutBlendingFallbackTwice(float coverage)
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, i < 4 ? 0.8f : 0);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 4 * Mathf.Pow(2, 0.9f), 0, coverage));
            float4 levels = f.RunDiagnostic(float4.zero, 0);
            Assert.That(levels.xyz, Is.EqualTo(new float3(0, 0, 1)));
            Assert.That(levels.w, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(f.RunDiagnostic(float4.zero, 5).y, Is.EqualTo(0.5f).Within(1e-5));
            for (int i = 0; i < 4; i++) f.MetadataData[i].x |= 4;
            f.Upload();
            Assert.That(f.RunDiagnostic(float4.zero, 0).xyz, Is.EqualTo(new float3(0, 1, -1)));
            Assert.That(f.RunDiagnostic(float4.zero, 5).y, Is.EqualTo(1));
        }

        [TestCase(.2f, .5f)]
        [TestCase(.05f, 0)]
        [TestCase(0, 0)]
        public void DensityPolicy_CoverageTransitionDoesNotChangePreferredLevel(float coverage, float blend)
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, i < 4 ? .8f : 0);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 4, 0, coverage));
            float4 levels = f.RunDiagnostic(new float4(4.5f, 0, 0, 0), 0);
            Assert.That(levels.xy, Is.EqualTo(new float2(0, 0)));
            Assert.That(levels.w, Is.EqualTo(blend).Within(1e-5));
            Assert.That(f.RunDiagnostic(new float4(4.5f, 0, 0, 0), 5).y, Is.EqualTo(blend).Within(1e-5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DensityPolicy_FeedbackAndSamplingStartAtSameLevelAndKeepCoarseCoverage(bool pcf)
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, 0.8f);
            for (int i = 4; i < 8; i++) f.MetadataData[i].x |= 4;
            f.Upload();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, 0));
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 8, 0));
            Assert.That(f.RunDiagnostic(float4.zero, 0).xyz, Is.EqualTo(new float3(1, 2, -1)));
            float2[] result = f.Run("ResolveReceivers", new[] { float4.zero }, normals: new[] { new float4(0, 0, 1, 0) });
            Assert.That(result[0].x, Is.Zero);
            for (int i = 0; i < 4; i++) Assert.That(f.MetadataData[i].x & 1u, Is.Zero);
            Assert.That(Array.Exists(f.MetadataData, x => (x.x & 256u) != 0), Is.True);
            bool requestedPreferred = false;
            for (int i = 4; i < 8; i++) requestedPreferred |= (f.MetadataData[i].x & 1u) != 0;
            Assert.That(requestedPreferred, Is.True);
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Taps_CrossAllEdgesAndCornersWithoutPhysicalAtlasAdjacency(int center)
        {
            using var f = new Fixture();
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page], page % 2 == 0 ? 0.8f : 0.2f,
                page >= 2 ? 0.9f : 0);
            f.Upload();
            var inputs = new float4[9]; var offsets = new int2[9];
            for (int y = -1, i = 0; y <= 1; y++) for (int x = -1; x <= 1; x++, i++)
            { inputs[i] = new float4((center + 0.5f) / 8, (center + 0.5f) / 8, 0.5f, 0); offsets[i] = new int2(x, y); }
            float2[] result = f.Run("SampleTaps", inputs, offsets);
            for (int i = 0; i < 9; i++)
            {
                int page = (center + offsets[i].y) / 4 * 2 + (center + offsets[i].x) / 4;
                Assert.That(result[i].x, Is.EqualTo(1));
                Assert.That(result[i].y, Is.EqualTo(page == 1 ? 1 : 0));
            }
        }

        [Test]
        public void Taps_RejectOutOfMapAndDirtyPagesButAcceptCompletedEmptyPages()
        {
            using var f = new Fixture();
            f.Map(0, 9); f.Map(1, 2); f.MetadataData[1].x |= 4;
            f.Upload();
            var input = new[] { new float4(0, 0.1f, 0.5f, 0), new float4(1, 0.1f, 0.5f, 0),
                new float4(0.1f, 0.1f, 0.5f, 0), new float4(0.6f, 0.1f, 0.5f, 0),
                new float4(0.1f, 0.6f, 0.5f, 0) };
            var offsets = new[] { new int2(-1, 0), int2.zero, int2.zero, int2.zero, int2.zero };
            float2[] result = f.Run("SampleTaps", input, offsets);
            for (int i = 0; i < result.Length; i++)
                Assert.That(result[i], Is.EqualTo(new float2(i == 2 ? 1 : 0, 1)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CurrentFrame_CoalescedRequestsPreservePerPageRolesAndAge(int pattern)
        {
            using var f = new Fixture();
            const int count = 193;
            int frame = pattern == 0 ? 0 : 17;
            var inputs = new float4[count]; var halos = new float4[count];
            var expected = new uint4[12];
            for (int page = 0; page < 12; page++)
            {
                expected[page] = new uint4(10u, (uint)(page + 1), (uint)(page % 2 == 0 ? 0 : 19), 64u);
                f.MetadataData[page] = expected[page];
            }
            for (int i = 0; i < count; i++)
            {
                int lane = i % 64;
                int level = pattern == 2 ? (i / 7) % 3 : (i / 64) % 3;
                uint role = lane % 3 == 0 ? 512u : lane % 3 == 1 ? 1024u : 2048u;
                float x = lane < 32 ? .125f : .625f, y = i % 5 == 0 ? .625f : .125f;
                int halo = pattern == 1 && lane == 0 ? 7 : pattern == 2 ? lane % 5 : 0;
                // Inactive first lanes, partial waves, mixed LODs, map boundaries,
                // and long halos must preserve each page's exact role union.
                bool active = lane % 11 != 0 || pattern == 1;
                if (pattern == 2 && lane % 13 == 0) x = lane % 2 == 0 ? 1f : -.01f;
                inputs[i] = new float4(x, y, level, role); halos[i] = new float4(halo, active ? 1 : 0, 0, 0);
                if (!active || x < 0 || x >= 1) continue;
                int tx = Mathf.FloorToInt(x * 8), ty = Mathf.FloorToInt(y * 8);
                for (int py = Mathf.Max(0, ty - halo) / 4; py <= Mathf.Min(7, ty + halo) / 4; py++)
                for (int px = Mathf.Max(0, tx - halo) / 4; px <= Mathf.Min(7, tx + halo) / 4; px++)
                {
                    int page = level * 4 + py * 2 + px;
                    expected[page].x |= 1u | role | (level == 2 ? 256u : 0u);
                    expected[page].z = Math.Max(expected[page].z, (uint)frame);
                }
            }
            f.Upload();
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            using var haloInput = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            input.SetData(inputs); haloInput.SetData(halos);
            int kernel = f.Shader.FindKernel("MarkCoalescedFootprints");
            f.Shader.SetInt("_SamplingCount", count); f.Shader.SetInt("_CSMFrameIndex", frame);
            f.Shader.SetBuffer(kernel, "_SamplingInputs", input);
            f.Shader.SetBuffer(kernel, "_SamplingNormals", haloInput);
            f.Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", f.Metadata);
            f.Shader.Dispatch(kernel, (count + 63) / 64, 1, 1);
            f.Metadata.GetData(f.MetadataData);
            Assert.That(f.MetadataData, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingFine_ReprojectsWorldPositionDepthAndLevelBias(bool normalBias)
        {
            using var f = new Fixture();
            var projection = f.ProjectionData[2];
            projection.WorldToShadow.m03 = normalBias ? 0.45f : 0.25f;
            projection.WorldToShadow.m13 = 0.25f;
            projection.WorldToShadow.m23 = 0.25f;
            projection.Parameters.y = normalBias ? 1 : 0;
            f.ProjectionData[2] = projection;
            f.Map(normalBias ? 9 : 8, 7, 0.4f);
            f.Upload();
            float2[] result = f.Run("ResolveReceivers", new[] { float4.zero },
                normals: new[] { normalBias ? new float4(1, 0, 0, 0) : float4.zero });
            Assert.That(result[0].x, Is.Zero);
            // Fine demand survives fallback, and the coarsest footprint is prioritized.
            Assert.That(f.MetadataData[3].x & 1, Is.EqualTo(1));
            Assert.That(f.MetadataData[normalBias ? 9 : 8].x & 257, Is.EqualTo(257));
        }

        [TestCase(false, 0f)]
        [TestCase(true, 0.5f)]
        public void Transition_BlendsOnlyAvailableSamples(bool coarseAvailable, float expected)
        {
            using var f = new Fixture();
            f.Map(3, 9, 0.8f);
            if (coarseAvailable) f.Map(7, 2); // A completed empty coarse page is legitimately lit.
            f.Upload();
            float2[] result = f.Run("ResolveReceivers", new[] { new float4(2.25f, 0, 0, 0) });
            Assert.That(result[0].x, Is.EqualTo(expected).Within(0.0001));
        }

        [TestCase(false, 1f)]
        [TestCase(true, 0f)]
        public void EmptyFine_IsValidButDirtyFineMustFallBack(bool dirty, float expected)
        {
            using var f = new Fixture();
            f.Map(3, 9); f.Map(7, 2, 0.8f);
            if (dirty) f.MetadataData[3].x |= 4;
            f.Upload();
            Assert.That(f.Run("ResolveReceivers", new[] { float4.zero })[0].x, Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void MissingAll_UsesLitTerminalAndRequestsWholeChainEvenDuringBootstrap(int enabled)
        {
            using var f = new Fixture();
            f.Shader.SetInt("_VSMPrototypeEnabled", enabled);
            f.Upload();
            Assert.That(f.Run("ResolveReceivers", new[] { float4.zero })[0].x, Is.EqualTo(1));
            for (int page = 0; page < 12; page++)
            {
                Assert.That(f.MetadataData[page].x, Is.EqualTo(page >= 8 ? 257u : page < 4 ? 513u : 2049u));
                Assert.That(f.MetadataData[page].z, Is.EqualTo(7));
            }
        }

        [Test]
        public void Footprint_ClipsAtMapBoundaryInsteadOfWrappingRequests()
        {
            using var f = new Fixture();
            f.Upload();
            f.Run("MarkFootprints", new[] { new float4(0, 0, 0, 2), new float4(1, 0, 0, 0) });
            for (int page = 0; page < 12; page++)
                Assert.That(f.MetadataData[page].x, Is.EqualTo(page == 8 ? 257u : 0u));
        }

        [Test]
        public void Feedback_SharedPageRetainsPrimaryAndTransitionDemand()
        {
            using var f = new Fixture();
            f.Upload();
            f.Run("MarkFootprints", new[] { new float4(.5f, .5f, 512, 0), new float4(.5f, .5f, 1024, 0) });
            for (int page = 0; page < 4; page++)
                Assert.That(f.MetadataData[page].x, Is.EqualTo(1537u));
        }

        [Test]
        public void Allocator_CoarseDemandDisplacesRequestedDetailWithoutLosingOverflowAccounting()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2);
            f.Map(0, 0); f.Map(1, 1);
            for (int frame = 7; frame < 10; frame++)
            {
                f.MetadataData[0].x |= 1; f.MetadataData[0].z = (uint)frame;
                f.MetadataData[1].x |= 1; f.MetadataData[1].z = (uint)frame;
                f.MetadataData[11].x |= 257; f.MetadataData[11].z = (uint)frame;
                f.Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                f.Upload(); f.Allocate();
                Assert.That(f.TableData[11], Is.EqualTo(1));
                Assert.That(f.TableData[0], Is.Zero);
                Assert.That(f.TableData[1], Is.EqualTo(2));
                var counters = new uint[4]; f.Counters.GetData(counters);
                Assert.That(counters, Is.EqualTo(new uint[] { 2, 3, frame == 7 ? 1u : 0u, 1 }));
                Assert.That(f.MetadataData[0].w & 129, Is.EqualTo(129));
                Assert.That(f.MetadataData[11].x & 257, Is.Zero);
            }
        }

        [Test]
        public void Allocator_IntermediateCoverageRecoversFromFineSaturationAndStaysResident()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4);
            for (int page = 0; page < 4; page++) f.Map(page, page);
            for (int frame = 7; frame < 10; frame++)
            {
                for (int page = 0; page < 12; page++)
                {
                    if (page >= 6 && page != 11) continue;
                    f.MetadataData[page].x |= page == 11 ? 257u : 1u;
                    f.MetadataData[page].z = (uint)frame;
                }
                f.Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                f.Upload(); f.Allocate();
                Assert.That(f.TableData[11], Is.Not.Zero);
                Assert.That(f.TableData[4], Is.Not.Zero);
                Assert.That(f.TableData[5], Is.Not.Zero);
                Assert.That(f.TableData[3], Is.EqualTo(4));
                for (int page = 0; page < 3; page++)
                {
                    Assert.That(f.TableData[page], Is.Zero);
                    Assert.That(f.MetadataData[page].w & 129u, Is.EqualTo(129u));
                }
                var counters = new uint[4]; f.Counters.GetData(counters);
                Assert.That(counters, Is.EqualTo(new uint[] { 4, 7, frame == 7 ? 3u : 0u, 3 }));
            }
        }

        [Test]
        public void Allocator_ColdPoolFillsIntermediateCoverageBeforeFineDetail()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4);
            for (int page = 0; page < 6; page++) f.MetadataData[page] = new uint4(1, 0, 7, 0);
            f.MetadataData[11] = new uint4(257, 0, 7, 0);
            f.Upload(); f.Allocate();
            Assert.That(f.TableData[11], Is.EqualTo(1));
            Assert.That(f.TableData[4], Is.EqualTo(2));
            Assert.That(f.TableData[5], Is.EqualTo(3));
            Assert.That(f.TableData[0], Is.EqualTo(4));
            var counters = new uint[4]; f.Counters.GetData(counters);
            Assert.That(counters, Is.EqualTo(new uint[] { 4, 7, 4, 3 }));
        }

        [Test]
        public void Allocator_CoarseOversubscriptionRemainsBoundedAndReportsEveryMiss()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2);
            f.MetadataData[0] = new uint4(1, 0, 7, 0);
            for (int page = 8; page < 12; page++) f.MetadataData[page] = new uint4(257, 0, 7, 0);
            f.Upload(); f.Allocate();
            Assert.That(f.TableData[8], Is.EqualTo(1));
            Assert.That(f.TableData[9], Is.EqualTo(2));
            Assert.That(f.TableData[0] | f.TableData[10] | f.TableData[11], Is.Zero);
            var counters = new uint[4]; f.Counters.GetData(counters);
            Assert.That(counters, Is.EqualTo(new uint[] { 2, 5, 2, 3 }));
        }

        [Test]
        public void FeedbackReset_DropsOldPriorityWithoutDiscardingCompletedDepth()
        {
            using var f = new Fixture(allocator: true);
            f.Map(11, 3); f.MetadataData[11].x |= 3841;
            f.Upload();
            int kernel = f.Shader.FindKernel("VSMPrototypeResetReceiverFeedback");
            f.Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", f.Metadata);
            f.Shader.Dispatch(kernel, 1, 1, 1);
            f.Metadata.GetData(f.MetadataData);
            Assert.That(f.MetadataData[11].x, Is.EqualTo(10));
            Assert.That(f.MetadataData[11].y, Is.EqualTo(4));
            Assert.That(f.MetadataData[11].z, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(7)]
        public void Allocator_ColdPoolPrioritizesRolesAboveClipmapLevel(int frame)
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4);
            f.Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
            f.MetadataData[0] = new uint4(513, 0, (uint)frame, 0);
            f.MetadataData[1] = new uint4(1025, 0, (uint)frame, 0);
            f.MetadataData[2] = new uint4(1537, 0, (uint)frame, 0); // Shared primary/transition: primary wins.
            f.MetadataData[4] = new uint4(1, 0, (uint)frame, 0);
            f.MetadataData[5] = new uint4(3073, 0, (uint)frame, 0);
            f.MetadataData[11] = new uint4(3841, 0, (uint)frame, 0); // Terminal always wins.
            f.Upload(); f.Allocate();
            Assert.That(f.TableData[11], Is.EqualTo(1));
            Assert.That(f.TableData[0], Is.EqualTo(3));
            Assert.That(f.TableData[2], Is.EqualTo(4));
            Assert.That(f.TableData[5], Is.EqualTo(2));
            Assert.That(f.TableData[1] | f.TableData[4], Is.Zero);
            var counters = new uint[4]; f.Counters.GetData(counters);
            Assert.That(counters, Is.EqualTo(new uint[] { 4, 6, 4, 2 }));
            foreach (uint4 metadata in f.MetadataData) Assert.That(metadata.x & 3841u, Is.Zero);
            Assert.That(f.MetadataData[2].w & 1537u, Is.EqualTo(1537u));
        }

        [Test]
        public void Allocator_PrimaryReclaimsFallbackBeforeTransitionAndRemainsStable()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4);
            f.Map(4, 0); f.Map(5, 1); f.Map(6, 2); f.Map(11, 3);
            int[] pages = { 0, 4, 5, 6, 11 };
            uint[] roles = { 513, 513, 1025, 1, 257 };
            for (int frame = 7; frame < 10; frame++)
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    f.MetadataData[pages[i]].x |= roles[i];
                    f.MetadataData[pages[i]].z = (uint)frame;
                }
                f.Shader.SetInt("_VSMPrototypeFeedbackFrameIndex", frame);
                f.Upload(); f.Allocate();
                Assert.That(f.TableData[0], Is.EqualTo(3));
                Assert.That(f.TableData[4], Is.EqualTo(1));
                Assert.That(f.TableData[5], Is.EqualTo(2));
                Assert.That(f.TableData[11], Is.EqualTo(4));
                Assert.That(f.TableData[6], Is.Zero);
                var counters = new uint[4]; f.Counters.GetData(counters);
                Assert.That(counters, Is.EqualTo(new uint[] { 4, 5, frame == 7 ? 1u : 0u, 1 }));
                Assert.That(f.MetadataData[6].w & 129u, Is.EqualTo(129u));
                for (int slot = 0; slot < 4; slot++)
                    Assert.That(f.TableData[f.OwnerData[slot] - 1], Is.EqualTo(slot + 1));
            }
        }

        [Test]
        public void Allocator_StaleRoleBitsDoNotProtectAResident()
        {
            using var f = new Fixture(allocator: true);
            f.Shader.SetInt("_VSMPrototypePhysicalPageCapacity", 1);
            f.Map(0, 0); f.MetadataData[0].x |= 3841; f.MetadataData[0].z = 6;
            f.MetadataData[1] = new uint4(513, 0, 7, 0);
            f.Upload(); f.Allocate();
            Assert.That(f.TableData[0], Is.Zero);
            Assert.That(f.TableData[1], Is.EqualTo(1));
            Assert.That(f.MetadataData[0].x & 3841u, Is.Zero);
            Assert.That(f.MetadataData[0].w & 3841u, Is.Zero);
        }

        [TestCase(64)]
        [TestCase(8192)]
        [TestCase(262144)]
        public void Allocator_RequestBitsetPreservesOrderAcrossWordsAndMaximumLayout(int count)
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            try
            {
                int levels = count == 64 ? 2 : 16, perLevel = count / levels;
                int[] indices = { 0, 31, 32, count == 64 ? 33 : 2047, count == 64 ? 34 : 2048, count - 1 };
                var pages = new int[indices.Length];
                var data = new uint4[count]; var mappings = new uint[count];
                for (int i = 0; i < indices.Length; i++)
                {
                    pages[i] = (levels - 1 - indices[i] / perLevel) * perLevel + indices[i] % perLevel;
                    data[pages[i]] = new uint4(i == 0 ? 257u : 513u, 0, 7, 0);
                }
                table.SetData(mappings); metadata.SetData(data); owners.SetData(new uint[4]); counters.SetData(new uint[4]);
                int kernel = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", levels);
                shader.SetInt("_VSMPrototypePageTableEntryCount", count);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
                shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(kernel, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(kernel, "_VSMPrototypeAllocatorCounters", counters);
                shader.Dispatch(kernel, 1, 1, 1);
                table.GetData(mappings); metadata.GetData(data);
                var counts = new uint[4]; counters.GetData(counts);
                Assert.That(counts, Is.EqualTo(new uint[] { 4, 6, 4, 2 }));
                for (int i = 0; i < pages.Length; i++)
                {
                    Assert.That(mappings[pages[i]], Is.EqualTo(i < 4 ? (uint)i + 1 : 0u));
                    Assert.That(data[pages[i]].x & 3841u, Is.Zero);
                    Assert.That(data[pages[i]].w & 128u, Is.EqualTo(i < 4 ? 0u : 128u));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReceiverRoles_FollowIntendedBlendIndependentlyOfResidency(bool transition)
        {
            using var f = new Fixture();
            var receiver = new float4(transition ? 2.25f : 0.625f, 0.625f, 0, 0);
            var expected = new uint[12];
            for (int resident = 0; resident < 2; resident++)
            {
                Array.Clear(f.MetadataData, 0, f.MetadataData.Length);
                if (resident != 0) for (int page = 0; page < 12; page++) f.Map(page, page);
                f.Upload(); f.Run("ResolveReceivers", new[] { receiver });
                bool primary = false, blend = false;
                for (int page = 0; page < 12; page++)
                {
                    uint request = f.MetadataData[page].x & 3841u;
                    if (resident == 0) expected[page] = request;
                    else Assert.That(request, Is.EqualTo(expected[page]));
                    if ((request & 1u) == 0) continue;
                    Assert.That(request, Is.EqualTo(page < 4 ? 513u : page < 8 ? (transition ? 3073u : 2049u) : 257u));
                    primary |= (request & 512u) != 0;
                    blend |= (request & 1024u) != 0;
                }
                Assert.That(primary, Is.True);
                Assert.That(blend, Is.EqualTo(transition));
            }
        }

        [Test]
        public void StochasticSamples_RoundingCannotEscapeTheOneTexelFeedbackHalo()
        {
            using var f = new Fixture();
            var inputs = new[]
            {
                new float4(0.49999994f, 0.49999994f, 1, 0),
                new float4(0.49999994f, 0.49999994f, 0, 1),
                new float4(-0.5f, -0.5f, -1, 0),
                new float4(-0.5f, -0.5f, 0, -1),
                new float4(-0.2f, 0.2f, 0.6f, -0.8f),
            };
            Assert.That(f.Run("InspectVSMStochasticTexelOffset", inputs), Is.EqualTo(new[]
            {
                new float2(1, 0), new float2(0, 1), new float2(-1, 0), new float2(0, -1), new float2(0, -1),
            }));
        }

        [Test]
        public void StochasticSamples_CoverNineEqualAreaStrataAndVaryDeterministicallyByFrameAndPixel()
        {
            using var f = new Fixture();
            const int frameCount = 128;
            var inputs = new float4[frameCount * 9];
            for (int frame = 0; frame < frameCount; frame++) for (int sample = 0; sample < 9; sample++)
                inputs[frame * 9 + sample] = new float4(17, 29, sample, frame);
            float2[] samples = f.Run("InspectVSMStochasticSample", inputs);
            Assert.That(f.Run("InspectVSMStochasticSample", inputs), Is.EqualTo(samples));
            float2 mean = 0;
            float meanSquareRadius = 0;
            bool frameChanged = false;
            for (int i = 0; i < samples.Length; i++)
            {
                int sample = i % 9;
                float squareRadius = math.lengthsq(samples[i]);
                float angle = math.atan2(samples[i].y, samples[i].x);
                if (angle < 0) angle += 2 * math.PI;
                Assert.That(squareRadius, Is.InRange(sample / 3 / 3f - 1e-6f, (sample / 3 + 1) / 3f + 1e-6f));
                Assert.That(angle, Is.InRange(sample % 3 * 2 * math.PI / 3 - 1e-6f,
                    (sample % 3 + 1) * 2 * math.PI / 3 + 1e-6f));
                mean += samples[i]; meanSquareRadius += squareRadius;
                if (i >= 9 && math.lengthsq(samples[i] - samples[i - 9]) > 1e-6f) frameChanged = true;
            }
            Assert.That(frameChanged, Is.True);
            Assert.That(math.length(mean / samples.Length), Is.LessThan(0.04f));
            Assert.That(meanSquareRadius / samples.Length, Is.EqualTo(0.5f).Within(0.02f));
            inputs[0].x += 1;
            Assert.That(math.lengthsq(f.Run("InspectVSMStochasticSample", new[] { inputs[0] })[0] - samples[0]),
                Is.GreaterThan(1e-6f));
        }

        [Test]
        public void StochasticFilter_NineComparisonMeanConvergesToAnalyticUnitDiskCoverage()
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 1));
            f.Shader.SetInts("_SamplingPixel", 17, 29);
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page], page % 2 == 0 ? 0.8f : 0.2f);
            f.Upload();
            // The page boundary is a straight occluder edge 0.25 texels from
            // the receiver. Its disk coverage has an independent exact integral.
            const float distance = 0.25f;
            double expected = (Math.Acos(distance) - distance * Math.Sqrt(1 - distance * distance)) / Math.PI;
            var inputs = new[] { new float4((4 - distance) / 8, 4.2f / 8, 0.5f, 0) };
            double sum = 0, individualError = 0, blockSum = 0, blockError = 0;
            const int frameCount = 512, blockSize = 64;
            for (int frame = 0; frame < frameCount; frame++)
            {
                f.Shader.SetInt("_CSMFrameIndex", frame);
                float2 value = f.Run("FilterFootprints", inputs)[0];
                Assert.That(value.x, Is.EqualTo(1));
                Assert.That(value.y * 9, Is.EqualTo(Mathf.Round(value.y * 9)).Within(1e-5f));
                sum += value.y; blockSum += value.y;
                individualError += (value.y - expected) * (value.y - expected);
                if ((frame + 1) % blockSize == 0)
                {
                    double error = blockSum / blockSize - expected;
                    blockError += error * error; blockSum = 0;
                }
            }
            Assert.That(sum / frameCount, Is.EqualTo(expected).Within(0.025));
            Assert.That(blockError / (frameCount / blockSize), Is.LessThan(individualError / frameCount * 0.15));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StochasticFilter_IncompletePotentialFootprintUsesTheSameFallbackAcrossFrames(bool dirty)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 1));
            for (int page = 0; page < 3; page++) f.Map(page, 8 + page, 0.8f);
            if (dirty) { f.Map(3, 11, 0.8f); f.MetadataData[3].x |= 4; }
            for (int page = 4; page < 8; page++) f.Map(page, page - 4);
            f.Upload();
            // Only a small disk corner can reach fine page 3. Residency must
            // be checked even in frames whose nine samples miss that corner.
            var receiver = new float4(-0.8125f, -0.8125f, 0, 0);
            for (int frame = 0; frame < 64; frame++)
            {
                f.Shader.SetInt("_CSMFrameIndex", frame);
                Assert.That(f.Run("ResolveReceivers", new[] { receiver })[0].x, Is.EqualTo(1));
                float4 diagnostic = f.RunDiagnostic(receiver, 0);
                Assert.That(diagnostic.x, Is.Zero);
                Assert.That(diagnostic.y, Is.EqualTo(1));
            }
            for (int page = 0; page < 4; page++) Assert.That(f.MetadataData[page].x & 1, Is.EqualTo(1));
            // The experimental option does not enable PCF by itself.
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(0, 0, 0, 1));
            Assert.That(f.Run("ResolveReceivers", new[] { receiver })[0].x, Is.Zero);
        }

        [TestCase(3.5f, 0.1875f)]
        [TestCase(3.25f, 0.109375f)]
        public void PCF_NormalizesAreaWeightsAcrossShuffledPages(float center, float expected)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++)
                f.Map(page, slots[page], page % 2 == 0 ? 0.8f : 0.2f, page >= 2 ? 0.9f : 0);
            f.Upload();
            float2 value = f.Run("FilterFootprints", new[] { new float4(center / 8, center / 8, 0.5f, 0) })[0];
            Assert.That(value.x, Is.EqualTo(1));
            Assert.That(value.y, Is.EqualTo(expected).Within(0.00001));
        }

        private static float4[] AreaPhaseInputs(bool insidePageCorner = false)
        {
            var inputs = new float4[17 * 17];
            for (int y = 0; y < 17; y++) for (int x = 0; x < 17; x++)
            {
                float2 texel = insidePageCorner ? 3.125f + new float2(x, y) * (0.75f / 16)
                    : 3 + new float2(x, y) * (1f / 16);
                inputs[y * 17 + x] = new float4(texel / 8, 0.5f, 0);
            }
            return inputs;
        }

        private static void SetAreaTestDepth(Fixture fixture, int x, int y, float depth)
        {
            int slot = (int)fixture.TableData[y / 4 * 2 + x / 4] - 1;
            int pixel = (slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4;
            fixture.StaticData[pixel] = math.asuint(depth);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void PCF_AreaWeightsPreserveConstantsAndRejectNyquistAcrossSubTexelPhases(int pattern)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                bool lit = pattern == 1 || (pattern >= 2 && ((pattern == 2 ? x : pattern == 3 ? y : x + y) & 1) != 0);
                SetAreaTestDepth(f, x, y, lit ? 0.2f : 0.8f);
            }
            f.Upload();
            // A two-texel-wide box integrates one full period of an alternating
            // signal at every phase. This reference does not reproduce tap weights.
            float expected = pattern < 2 ? pattern : 0.5f;
            foreach (float2 value in f.Run("FilterFootprints", AreaPhaseInputs()))
            {
                Assert.That(value.x, Is.EqualTo(1));
                Assert.That(value.y, Is.EqualTo(expected).Within(1e-6f));
            }
        }

        [Test]
        public void HardSampling_RemainsPointSampledWhenStochasticOptionIsSelected()
        {
            using var f = new Fixture();
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                SetAreaTestDepth(f, x, y, (x & 1) != 0 ? 0.2f : 0.8f);
            f.Upload();
            float4[] inputs = AreaPhaseInputs();
            f.Shader.SetVector("_VSMReceiverParameters", Vector4.zero);
            float2[] hard = f.Run("FilterFootprints", inputs);
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(0, 0, 0, 1));
            Assert.That(f.Run("FilterFootprints", inputs), Is.EqualTo(hard));
            for (int i = 0; i < inputs.Length; i++)
                Assert.That(hard[i], Is.EqualTo(new float2(1, ((int)math.floor(inputs[i].x * 8) & 1) != 0 ? 1 : 0)));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void Sampling_AcrossSubTexelPhasesSeparatesCoplanarReceiversAndBlockers(int mode, bool blocker)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(mode == 0 ? 0 : 1, 0, 0, mode == 2 ? 1 : 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                SetAreaTestDepth(f, x, y, 0.5f + 0.05f * (x - 2.75f) + 0.03f * (y - 2.75f) + (blocker ? 0.04f : 0));
            f.Upload();
            float4[] inputs = AreaPhaseInputs();
            var corrections = new float4[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                float2 texel = inputs[i].xy * 8;
                inputs[i].z = 0.5f + 0.05f * (texel.x - 3.25f) + 0.03f * (texel.y - 3.25f);
                corrections[i] = new float4(0.05f, 0.03f, 1e-5f, 0);
            }
            foreach (float2 value in f.Run("FilterFootprints", inputs, normals: corrections))
                Assert.That(value, Is.EqualTo(new float2(1, blocker ? 0 : 1)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PCF_AreaFilteringDoesNotRenormalizeAroundMissingPagesAcrossPhases(bool dirty)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 3; page++) f.Map(page, slots[page], 0.8f);
            if (dirty) { f.Map(3, slots[3], 0.8f); f.MetadataData[3].x |= 4; }
            for (int page = 4; page < 8; page++) f.Map(page, page - 4);
            f.Upload();
            float4[] inputs = AreaPhaseInputs(true);
            foreach (float2 value in f.Run("FilterFootprints", inputs)) Assert.That(value.x, Is.Zero);
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new float4((inputs[i].xy * 8 - 4) / 0.8f, 0, 0);
            foreach (float2 value in f.Run("ResolveReceivers", inputs)) Assert.That(value.x, Is.EqualTo(1));
        }

        [Test]
        public void PCF_IsContinuousAcrossTheVirtualPageBoundary()
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page], page % 2 == 0 ? 0.8f : 0.2f);
            f.Upload();
            var inputs = new[] { new float4(0.5f - 1e-5f, 0.3f, 0.5f, 0),
                new float4(0.5f, 0.3f, 0.5f, 0), new float4(0.5f + 1e-5f, 0.3f, 0.5f, 0) };
            float2[] result = f.Run("FilterFootprints", inputs);
            foreach (float2 value in result)
            {
                Assert.That(value.x, Is.EqualTo(1));
                Assert.That(value.y, Is.EqualTo(0.5f).Within(0.0002));
            }
        }

        [TestCase(false, 0f)]
        [TestCase(true, 1f)]
        public void PCF_MissingNeighborDegradesTheWholeKernel(bool pcf, float expected)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, 0));
            f.Map(3, 9, 0.8f); // Fine center exists, neighboring fine pages do not.
            for (int page = 4; page < 8; page++) f.Map(page, page - 4);
            f.Upload();
            Assert.That(f.Run("ResolveReceivers", new[] { float4.zero })[0].x, Is.EqualTo(expected));
            for (int page = 0; page < 4; page++) Assert.That(f.MetadataData[page].x & 1, Is.EqualTo(1));
        }

        [Test]
        public void PCF_IncompleteTransitionFootprintDoesNotBrightenThePrimary()
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 0, 0, 0));
            for (int page = 0; page < 4; page++) f.Map(page, 8 + page, 0.8f);
            f.Map(7, 4);
            f.Upload();
            Assert.That(f.Run("ResolveReceivers", new[] { new float4(2.25f, 0, 0, 0) })[0].x, Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Sampling_ReceiverPlaneCorrectionRemovesCoplanarTapSelfShadow(bool pcf, bool stochastic)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, stochastic ? 1 : 0));
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int slot = slots[y / 4 * 2 + x / 4];
                int pixel = (slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4;
                f.StaticData[pixel] = math.asuint(0.5f + 0.05f * (x - 2.75f) + 0.03f * (y - 2.75f));
            }
            f.Upload();
            var input = new[] { new float4(3.25f / 8, 3.25f / 8, 0.5f, 0) };
            float2 uncorrected = f.Run("FilterFootprints", input)[0];
            float2 corrected = f.Run("FilterFootprints", input,
                normals: new[] { new float4(0.05f, 0.03f, 1e-5f, 0) })[0];
            Assert.That(uncorrected.x, Is.EqualTo(1));
            Assert.That(uncorrected.y, Is.LessThan(1));
            Assert.That(corrected, Is.EqualTo(new float2(1, 1)));
        }

        private static Matrix4x4 ScreenInverse(float extent = 1)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = extent;
            matrix.m11 = SystemInfo.graphicsUVStartsAtTop ? -extent : extent;
            return matrix;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReceiverNormal_UsesDepthPlaneAtScreenEdgesAndRejectsDiscontinuousNeighbors(bool discontinuity)
        {
            using var f = new Fixture();
            var depth = new Texture2D(9, 9, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var depths = new float[81];
                for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++)
                    depths[y * 9 + x] = 0.5f + 0.2f * ((x + 0.5f) / 9 * 2 - 1)
                        + 0.1f * ((y + 0.5f) / 9 * 2 - 1);
                if (discontinuity) { depths[4 * 9 + 5] = 0.95f; depths[5 * 9 + 4] = 0; }
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse());
                f.Upload();
                int2[] pixels = { new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(4, 4) };
                var inputs = new float4[10]; var normals = new float4[10];
                for (int i = 0; i < 10; i++)
                {
                    inputs[i] = new float4(pixels[i / 2].x, pixels[i / 2].y, i % 2, 0);
                    normals[i] = new float4(i % 3 == 0 ? 0.8f : -0.8f, 0, 0.6f, 0);
                }
                float3 expected = math.normalize(new float3(-0.2f, -0.1f, 1));
                float2[] results = f.Run("InspectScreenNormal", inputs, normals: normals, depth: depth);
                for (int i = 0; i < 10; i++)
                {
                    float2 value = i % 2 == 0 ? expected.xy : new float2(expected.z, 0);
                    Assert.That(results[i].x, Is.EqualTo(value.x).Within(1e-5));
                    Assert.That(results[i].y, Is.EqualTo(value.y).Within(1e-5));
                }
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(-1, 0)]
        [TestCase(1, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 1)]
        public void ReceiverNormal_RejectsCloserDepthAcrossAPlaneBoundary(int directionX, int directionY)
        {
            using var f = new Fixture();
            var depth = new Texture2D(9, 9, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var depths = new float[81];
                for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++)
                    depths[y * 9 + x] = 0.5f + 0.2f * ((x + 0.5f) / 9 * 2 - 1)
                        + 0.1f * ((y + 0.5f) / 9 * 2 - 1);
                // The other face is closer to the center depth than either
                // correct derivative, but does not extrapolate onto its plane.
                depths[(4 + directionY) * 9 + 4 + directionX] = 0.49f;
                depths[(4 + 2 * directionY) * 9 + 4 + 2 * directionX] = 0.49f;
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse()); f.Upload();
                var inputs = new[] { new float4(4, 4, 0, 0), new float4(4, 4, 1, 0) };
                var normals = new[] { new float4(0, 0, 1, 0), new float4(0, 0, 1, 0) };
                float2[] result = f.Run("InspectScreenNormal", inputs, normals: normals, depth: depth);
                float3 expected = math.normalize(new float3(-0.2f, -0.1f, 1));
                Assert.That(result[0].x, Is.EqualTo(expected.x).Within(1e-5));
                Assert.That(result[0].y, Is.EqualTo(expected.y).Within(1e-5));
                Assert.That(result[1].x, Is.EqualTo(expected.z).Within(1e-5));
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(-1, 0)]
        [TestCase(1, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 1)]
        public void ReceiverNormal_IncompleteEdgeStencilDoesNotForceADifferentFace(int directionX, int directionY)
        {
            using var f = new Fixture();
            var depth = new Texture2D(9, 9, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var depths = new float[81];
                for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++)
                    depths[y * 9 + x] = 0.5f + 0.2f * ((x + 0.5f) / 9 * 2 - 1)
                        + 0.1f * ((y + 0.5f) / 9 * 2 - 1);
                int pixelX = 4 + 3 * directionX;
                int pixelY = 4 + 3 * directionY;
                // The correct face has only one neighbor before the image edge;
                // the opposite pair is complete but belongs to another face.
                depths[(pixelY - directionY) * 9 + pixelX - directionX] = 0.9f;
                depths[(pixelY - 2 * directionY) * 9 + pixelX - 2 * directionX] = 0.9f;
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse()); f.Upload();
                var inputs = new[] { new float4(pixelX, pixelY, 0, 0), new float4(pixelX, pixelY, 1, 0) };
                var normals = new[] { new float4(0, 0, 1, 0), new float4(0, 0, 1, 0) };
                float2[] result = f.Run("InspectScreenNormal", inputs, normals: normals, depth: depth);
                float3 expected = math.normalize(new float3(-0.2f, -0.1f, 1));
                Assert.That(result[0].x, Is.EqualTo(expected.x).Within(1e-5));
                Assert.That(result[0].y, Is.EqualTo(expected.y).Within(1e-5));
                Assert.That(result[1].x, Is.EqualTo(expected.z).Within(1e-5));
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(2)]
        [TestCase(3)]
        public void ReceiverNormal_UsesFirstNeighborsWhenSecondNeighborsAreUnavailable(int size)
        {
            using var f = new Fixture();
            var depth = new Texture2D(size, size, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var depths = new float[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    depths[y * size + x] = 0.5f + 0.2f * ((x + 0.5f) / size * 2 - 1)
                        + 0.1f * ((y + 0.5f) / size * 2 - 1);
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse()); f.Upload();
                float2 result = f.Run("InspectScreenNormal", new[] { new float4(size / 2, size / 2, 0, 0) },
                    normals: new[] { new float4(0, 0, 1, 0) }, depth: depth)[0];
                float3 expected = math.normalize(new float3(-0.2f, -0.1f, 1));
                Assert.That(result.x, Is.EqualTo(expected.x).Within(1e-5));
                Assert.That(result.y, Is.EqualTo(expected.y).Within(1e-5));
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(1)]
        [TestCase(3)]
        public void ReceiverNormal_UsesFallbackWhenNoDepthPlaneCanBeReconstructed(int size)
        {
            using var f = new Fixture();
            var depth = new Texture2D(size, size, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var depths = new float[size * size]; depths[size * size / 2] = 0.5f;
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", ScreenInverse()); f.Upload();
                float2 value = f.Run("InspectScreenNormal", new[] { new float4(size / 2, size / 2, 0, 0) },
                    normals: new[] { new float4(0.6f, 0, 0.8f, 0) }, depth: depth)[0];
                Assert.That(value, Is.EqualTo(new float2(0.6f, 0)));
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sampling_NormalMappedSlopeIsLitButASeparateOccluderStillShadows(bool pcf)
        {
            using var f = new Fixture();
            var depth = new Texture2D(5, 5, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                // True surface z = 3*x + 0.5*y, but the normal map points down
                // the light depth axis: the old shading-normal bias sees no slope.
                var position = new Vector3(-0.225f, -0.225f, -0.7875f);
                Matrix4x4 inverse = ScreenInverse(0.02f);
                inverse.m03 = position.x; inverse.m13 = position.y; inverse.m23 = position.z - 0.5f;
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", inverse);
                f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0.1f, 0, 0));
                var projection = f.ProjectionData[0];
                projection.WorldToShadow.m00 = projection.WorldToShadow.m11 = 0.5f;
                projection.WorldToShadow.m22 = 0.1f;
                projection.Parameters = new Vector4(0.25f, 0, 0, 100);
                f.ProjectionData[0] = projection;
                var depths = new float[25];
                for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
                    depths[y * 5 + x] = 0.5f + 0.02f * (3 * ((x + 0.5f) / 5 * 2 - 1)
                        + 0.5f * ((y + 0.5f) / 5 * 2 - 1));
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                int[] slots = { 9, 2, 14, 6 };
                for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int slot = slots[y / 4 * 2 + x / 4];
                    int pixel = (slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4;
                    f.StaticData[pixel] = math.asuint(0.5f + 0.1f * (3 * ((x + 0.5f) / 4 - 1)
                        + 0.5f * ((y + 0.5f) / 4 - 1)));
                }
                f.Upload();
                var normals = new[] { new float4(0, 0, 1, 0) };
                float oldShadow = f.Run("ResolveReceivers", new[] { new float4(position.x, position.y, position.z, 0) },
                    normals: normals)[0].x;
                Assert.That(oldShadow, Is.LessThan(1), "The shading-normal path must reproduce self-shadowing.");
                var input = new[] { new float4(2, 2, 0, 0) };
                Assert.That(f.Run("ResolveScreenReceivers", input, normals: normals, depth: depth)[0].x,
                    Is.EqualTo(1).Within(1e-5));
                for (int i = 0; i < f.StaticData.Length; i++)
                    if (f.StaticData[i] != 0) f.StaticData[i] = math.asuint(math.asfloat(f.StaticData[i]) + 0.15f);
                f.Upload();
                Assert.That(f.Run("ResolveScreenReceivers", input, normals: normals, depth: depth)[0].x, Is.Zero);
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sampling_PlaneBoundaryNormalPreservesNearbyOccluder(bool pcf)
        {
            using var f = new Fixture();
            var depth = new Texture2D(5, 5, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            try
            {
                var position = new Vector3(-0.025f, -0.025f, -0.0875f);
                Matrix4x4 inverse = ScreenInverse(0.02f);
                inverse.m03 = position.x; inverse.m13 = position.y; inverse.m23 = position.z - 0.5f;
                f.Shader.SetMatrix("_CSMInvViewProjMatrix", inverse);
                f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0.1f, 0, 0));
                var projection = f.ProjectionData[0];
                projection.WorldToShadow.m00 = projection.WorldToShadow.m11 = 0.5f;
                projection.WorldToShadow.m22 = 0.1f;
                projection.Parameters = new Vector4(0.25f, 0, 0, 100);
                f.ProjectionData[0] = projection;
                var depths = new float[25];
                for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
                    depths[y * 5 + x] = x > 2
                        ? 0.494f + 0.01f * ((y + 0.5f) / 5 * 2 - 1)
                        : 0.5f + 0.02f * (3 * ((x + 0.5f) / 5 * 2 - 1)
                            + 0.5f * ((y + 0.5f) / 5 * 2 - 1));
                depth.SetPixelData(depths, 0); depth.Apply(false, false);
                int[] slots = { 9, 2, 14, 6 };
                for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int slot = slots[y / 4 * 2 + x / 4];
                    int pixel = (slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4;
                    // A parallel blocker is only 0.1 world units in front.
                    f.StaticData[pixel] = math.asuint(0.51f + 0.1f * (3 * ((x + 0.5f) / 4 - 1)
                        + 0.5f * ((y + 0.5f) / 4 - 1)));
                }
                f.Upload();
                var normals = new[] { new float4(0, 0, 1, 0) };
                Assert.That(f.Run("ResolveScreenReceivers", new[] { new float4(2, 2, 0, 0) },
                    normals: normals, depth: depth)[0].x, Is.Zero);
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(false, 0.0078125f, false)]
        [TestCase(true, 0.0078125f, false)]
        [TestCase(true, 0.0078125f, true)]
        [TestCase(false, 0.015625f, false)]
        [TestCase(true, 0.015625f, false)]
        [TestCase(true, 0.015625f, true)]
        [TestCase(false, 0.03125f, false)]
        [TestCase(true, 0.03125f, false)]
        [TestCase(true, 0.03125f, true)]
        public void Sampling_DefaultBiasKeepsCoplanarReceiverLitAndNearbyBlockerShadowed(bool pcf, float texelSize, bool stochastic)
        {
            using var f = new Fixture();
            f.Shader.SetInt("_VSMProjectionCount", 1);
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0,
                VividAdditionalLightData.DefaultShadowDepthBias, VividAdditionalLightData.DefaultShadowSlopeBias, stochastic ? 1 : 0));
            var projection = f.ProjectionData[0];
            projection.WorldToShadow.m00 = projection.WorldToShadow.m11 = 1 / (8 * texelSize);
            projection.WorldToShadow.m22 = 0.1f;
            projection.Parameters = new Vector4(texelSize, VividAdditionalLightData.DefaultShadowNormalBias, 0, 100);
            f.ProjectionData[0] = projection;
            int[] slots = { 9, 2, 14, 6 };
            for (int page = 0; page < 4; page++) f.Map(page, slots[page]);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int slot = slots[y / 4 * 2 + x / 4];
                int pixel = (slot / 4 * 4 + y % 4) * 16 + slot % 4 * 4 + x % 4;
                // The receiver lies on z = x, sampled at virtual texel centers.
                f.StaticData[pixel] = math.asuint(0.5f + 0.1f * (x - 3.5f) * texelSize);
            }
            f.Upload();
            var inputs = new[] { float4.zero };
            var normals = new[] { new float4(-1, 0, 1, 0) };
            Assert.That(f.Run("ResolveReceivers", inputs, normals: normals)[0].x, Is.EqualTo(1).Within(1e-5));

            // Default depth and normal biases already advance by two texels.
            // Adding a full slope bias again advances by 4.5 texels in total,
            // incorrectly passing this blocker three texels toward the light.
            for (int i = 0; i < f.StaticData.Length; i++)
                if (f.StaticData[i] != 0)
                    f.StaticData[i] = math.asuint(math.asfloat(f.StaticData[i]) + 0.1f * 3 * texelSize);
            f.Upload();
            Assert.That(f.Run("ResolveReceivers", inputs, normals: normals)[0].x, Is.Zero);
        }

        [TestCase(3f, 0f)]
        [TestCase(4f, 0f)]
        [TestCase(4.5f, 1.25f)]
        [TestCase(6f, 4f)]
        [TestCase(1000f, 4f)]
        public void Bias_OnlyUncorrectedGrazingSlopeAddsComparisonBias(float slope, float residualBias)
        {
            using var f = new Fixture();
            const float texelSize = 0.015625f, depthScale = 0.1f;
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1,
                VividAdditionalLightData.DefaultShadowDepthBias, VividAdditionalLightData.DefaultShadowSlopeBias, 0));
            f.ProjectionData[0].WorldToShadow.m22 = depthScale;
            f.ProjectionData[0].Parameters.x = texelSize;
            f.ProjectionData[0].Parameters.y = VividAdditionalLightData.DefaultShadowNormalBias;
            f.Upload();
            var inputs = new[] { float4.zero, new float4(0, 0, 1, 0) };
            var normals = new[] { new float4(-slope, 0, 1, 0), new float4(-slope, 0, 1, 0) };
            float2[] result = f.Run("InspectBias", inputs, normals: normals);
            Assert.That(result[0].x, Is.EqualTo((VividAdditionalLightData.DefaultShadowDepthBias + residualBias)
                * depthScale * texelSize).Within(1e-6));
            Assert.That(result[1].x, Is.EqualTo(Mathf.Min(slope, 4) * depthScale * texelSize).Within(1e-6));
            Assert.That(result[1].y, Is.Zero);
        }

        [TestCase(0.01f, 1f)]
        [TestCase(0.1f, 1f)]
        [TestCase(0.01f, 0.5f)]
        public void Bias_ScalesWithTexelsAndDepthRangeAndBoundsGrazingSlopes(float depthScale, float texelSize)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(1, 2, 3, 0));
            f.ProjectionData[0].WorldToShadow.m22 = depthScale;
            f.ProjectionData[0].Parameters.x = texelSize;
            f.ProjectionData[0].Parameters.y = 2;
            f.Upload();
            var inputs = new[] { float4.zero, float4.zero, float4.zero, new float4(0, 0, 1, 0) };
            var normals = new[] { new float4(0.6f, 0, 0.8f, 0), new float4(1, 0, 0, 0),
                new float4(0, 0, 1, 0), new float4(0.6f, 0, 0.8f, 0) };
            float2[] result = f.Run("InspectBias", inputs, normals: normals);
            Assert.That(result[0].x, Is.EqualTo(2 * depthScale * texelSize).Within(1e-6));
            Assert.That(result[0].y, Is.EqualTo(1.2f * texelSize).Within(1e-6));
            Assert.That(result[1].x, Is.EqualTo(6 * depthScale * texelSize).Within(1e-6));
            Assert.That(result[1].y, Is.EqualTo(2 * texelSize).Within(1e-6));
            Assert.That(result[2].y, Is.Zero);
            Assert.That(result[3].x, Is.EqualTo(-0.75f * depthScale * texelSize).Within(1e-6));
        }

        [Test]
        public void Transition_UsesSmoothEndpointsAndCanBeDisabled()
        {
            using var f = new Fixture();
            f.Upload();
            var inputs = new float4[6];
            for (int i = 0; i < 5; i++) inputs[i] = new float4(0.4f + i * 0.025f, 0.1f, 0, 0);
            inputs[5] = new float4(0.49f, 0, 0, 0);
            float[] expected = { 0, 0.15625f, 0.5f, 0.84375f, 1, 0 };
            float2[] result = f.Run("InspectTransition", inputs);
            for (int i = 0; i < result.Length; i++) Assert.That(result[i].x, Is.EqualTo(expected[i]).Within(1e-5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Transition_HasNoStepWhenTheSelectedLevelChanges(bool pcf)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, 0));
            for (int page = 0; page < 4; page++) f.Map(page, 8 + page, 0.8f);
            for (int page = 4; page < 8; page++) f.Map(page, page - 4);
            f.Upload();
            var inputs = new[] { new float4(2.5f - 1e-4f, 0, 0, 0),
                new float4(2.5f, 0, 0, 0), new float4(2.5f + 1e-4f, 0, 0, 0) };
            foreach (float2 value in f.Run("ResolveReceivers", inputs))
                Assert.That(value.x, Is.EqualTo(1).Within(1e-5));
        }
    }
}
