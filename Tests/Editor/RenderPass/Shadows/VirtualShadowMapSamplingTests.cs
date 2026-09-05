using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
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
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
                using var offset = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 8);
                using var normal = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 8);
                input.SetData(inputs); offset.SetData(offsets ?? new int2[inputs.Length]);
                normal.SetData(normals ?? new float4[inputs.Length]);
                Shader.SetInt("_SamplingCount", inputs.Length);
                Shader.SetBuffer(kernel, "_SamplingInputs", input);
                bool inspectOnly = kernelName == "InspectBias" || kernelName == "InspectTransition"
                    || kernelName == "InspectScreenNormal";
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
                    else if (kernelName != "InspectTransition")
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

        [Test]
        public void DensityPolicy_TransitionsAtLodBoundaryWithoutBlendingFallbackTwice()
        {
            using var f = new Fixture();
            for (int i = 0; i < 12; i++) f.Map(i, i, i < 4 ? 0.8f : 0);
            f.Upload();
            f.Shader.SetVector("_VSMReceiverQuality", VirtualShadowMapReceiverQuality.BuildParameters(true, 4 * Mathf.Pow(2, 0.9f), 0));
            float4 levels = f.RunDiagnostic(float4.zero, 0);
            Assert.That(levels.xyz, Is.EqualTo(new float3(0, 0, 1)));
            Assert.That(levels.w, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(f.RunDiagnostic(float4.zero, 5).y, Is.EqualTo(0.5f).Within(1e-5));
            for (int i = 0; i < 4; i++) f.MetadataData[i].x |= 4;
            f.Upload();
            Assert.That(f.RunDiagnostic(float4.zero, 0).xyz, Is.EqualTo(new float3(0, 1, -1)));
            Assert.That(f.RunDiagnostic(float4.zero, 5).y, Is.EqualTo(1));
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
                Assert.That(f.MetadataData[page].x, Is.EqualTo(page >= 8 ? 257u : 1u));
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
            f.Map(11, 3); f.MetadataData[11].x |= 257;
            f.Upload();
            int kernel = f.Shader.FindKernel("VSMPrototypeResetReceiverFeedback");
            f.Shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", f.Metadata);
            f.Shader.Dispatch(kernel, 1, 1, 1);
            f.Metadata.GetData(f.MetadataData);
            Assert.That(f.MetadataData[11].x, Is.EqualTo(10));
            Assert.That(f.MetadataData[11].y, Is.EqualTo(4));
            Assert.That(f.MetadataData[11].z, Is.Zero);
        }

        [TestCase(3.5f, 0.16f)]
        [TestCase(3.25f, 0.09876543f)]
        public void PCF_NormalizesTentWeightsAcrossShuffledPages(float center, float expected)
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

        [TestCase(false)]
        [TestCase(true)]
        public void Sampling_ReceiverPlaneCorrectionRemovesCoplanarTapSelfShadow(bool pcf)
        {
            using var f = new Fixture();
            f.Shader.SetVector("_VSMReceiverParameters", new Vector4(pcf ? 1 : 0, 0, 0, 0));
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
            Assert.That(result[0].x, Is.EqualTo(4.25f * depthScale * texelSize).Within(1e-6));
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
