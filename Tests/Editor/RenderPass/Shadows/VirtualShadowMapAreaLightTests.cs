using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.VirtualShadowMap;

namespace VividRP.Tests.Editor.RenderPass.Shadows
{
    public sealed class VirtualShadowMapAreaLightTests
    {
        private const string PageManagementShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const string ResolveShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const int ClusterPackingOffsetBits = 26;
        private const uint ClusterPackingOffsetMask = (1u << ClusterPackingOffsetBits) - 1u;
        private const uint PageTableValidBit = 0x80000000u;

        private static ComputeShader LoadShader(string primaryPath, string fallbackPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(primaryPath);
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(fallbackPath);
            return shader;
        }

        private static uint PackCluster(uint offset, uint count) => (count << ClusterPackingOffsetBits) | (offset & ClusterPackingOffsetMask);
        private static uint UnpackOffset(uint packed) => packed & ClusterPackingOffsetMask;
        private static uint UnpackCount(uint packed) => packed >> ClusterPackingOffsetBits;

        private static float Unpack4Bit(uint4 maskBits, int slot)
        {
            uint word = (slot >> 3) switch
            {
                0 => maskBits.x,
                1 => maskBits.y,
                2 => maskBits.z,
                _ => maskBits.w,
            };
            uint nibble = (word >> ((slot & 7) * 4)) & 0xFu;
            return nibble / 15.0f;
        }

        private static uint EncodeNativePageTable(uint physX, uint physY, uint lodOffset = 0u)
        {
            return PageTableValidBit | (lodOffset << 20) | (physY << 10) | physX;
        }

        [Test]
        public void CPU_CollectAndRegister_AreaLights_EffectiveRangeCacheAndZeroGC()
        {
            var lightData = new VividLightData();
            lightData.Reset();

            // 1 shadow-casting Point light (farther away)
            lightData.punctualLights = new[]
            {
                new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(0f, 2f, 20f),
                    range = 8f,
                    lightType = 0u,
                    directionWS = Vector3.down,
                    upWS = Vector3.forward,
                    shadowStrength = 1.0f,
                },
            };
            lightData.punctualLightShadowMeta = new[]
            {
                new VividLightData.PunctualLightShadowMeta
                {
                    lightEntityId = EntityId.FromULong(100ul),
                    spotAngle = 90f,
                    shapeRadius = 0.1f,
                    flags = VividLightRenderDataFlags.CastShadows,
                },
            };
            lightData.punctualLightCount = 1;

            // 3 Area lights: [0] shadow-casting Rect (4x3, range 10 -> Reff 12.5), [1] shadow-casting Tube (width 6, range 8 -> Reff 11.0), [2] non-shadow Rect.
            lightData.areaLights = new[]
            {
                new VividLightData.AreaLightData
                {
                    positionWS = new Vector3(0f, 3f, 5f),
                    range = 10f,
                    forwardWS = Vector3.down,
                    width = 4f,
                    rightWS = Vector3.right,
                    height = 3f,
                    upWS = Vector3.forward,
                    lightType = 1u, // Rectangle
                    cosBarnDoorAngle = 0f,
                    barnDoorLength = 1000f,
                },
                new VividLightData.AreaLightData
                {
                    positionWS = new Vector3(2f, 2f, 6f),
                    range = 8f,
                    forwardWS = Vector3.forward,
                    width = 6f,
                    rightWS = Vector3.right,
                    height = 0f,
                    upWS = Vector3.up,
                    lightType = 0u, // Tube
                },
                new VividLightData.AreaLightData
                {
                    positionWS = new Vector3(-2f, 2f, 4f),
                    range = 10f,
                    forwardWS = Vector3.down,
                    width = 2f,
                    rightWS = Vector3.right,
                    height = 2f,
                    upWS = Vector3.forward,
                    lightType = 1u, // Rectangle (no shadow)
                },
            };
            lightData.areaLightShadowMeta = new[]
            {
                new VividLightData.AreaLightShadowMeta
                {
                    lightEntityId = EntityId.FromULong(201ul),
                    shadowStrength = 0.75f,
                    flags = VividLightRenderDataFlags.CastShadows,
                },
                new VividLightData.AreaLightShadowMeta
                {
                    lightEntityId = EntityId.FromULong(202ul),
                    shadowStrength = 1.0f,
                    flags = VividLightRenderDataFlags.CastShadows,
                },
                new VividLightData.AreaLightShadowMeta
                {
                    lightEntityId = EntityId.FromULong(203ul),
                    shadowStrength = 1.0f,
                    flags = VividLightRenderDataFlags.None,
                },
            };
            lightData.areaLightCount = 3;

            using var localGrid = new VirtualShadowMapLocalLightGrid();
            var registry = new VirtualShadowMapRegistry();

            int collected = localGrid.CollectLocalLights(
                lightData,
                Vector3.zero,
                60f,
                1080,
                localMaxResolution: 1024,
                localLodBias: 0f,
                distantThresholdPx: 64f,
                useReceiverMask: true);

            Assert.That(collected, Is.EqualTo(3));
            Assert.That(localGrid.AreaLightCount, Is.EqualTo(3));

            // Closest/largest screen diameter should be Rect (Reff 12.5 at dist ~5.83m) first.
            ref readonly VividVSMLocalLightDescriptor rectDesc = ref localGrid.CollectedDescriptors[0];
            Assert.That(rectDesc.IsAreaLight, Is.True);
            Assert.That(rectDesc.LightType, Is.EqualTo(VividVSMLightType.Rect));
            Assert.That(rectDesc.Range, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(rectDesc.SourceRadius, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(rectDesc.PunctualLightIndex, Is.EqualTo(0u));

            ref readonly VividVSMLocalLightDescriptor tubeDesc = ref localGrid.CollectedDescriptors[1];
            Assert.That(tubeDesc.IsAreaLight, Is.True);
            Assert.That(tubeDesc.LightType, Is.EqualTo(VividVSMLightType.Tube));
            Assert.That(tubeDesc.Range, Is.EqualTo(11.0f).Within(1e-4f));
            Assert.That(tubeDesc.SourceRadius, Is.EqualTo(3.0f).Within(1e-4f));
            Assert.That(tubeDesc.PunctualLightIndex, Is.EqualTo(1u));

            registry.PrepareFrame(1ul, null, localGrid.CollectedDescriptors, collected);
            localGrid.PopulatePunctualVSMInfo(registry);
            registry.CommitRecordedFrame();

            // 3 cubemap lights x 6 faces = 18 projections.
            Assert.That(registry.ProjectionCount, Is.EqualTo(18));
            Assert.That(localGrid.AreaVSMInfoUpload[0].x, Is.EqualTo(0));
            Assert.That(math.asfloat(localGrid.AreaVSMInfoUpload[0].z), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(localGrid.AreaVSMInfoUpload[1].x, Is.EqualTo(6));
            Assert.That(math.asfloat(localGrid.AreaVSMInfoUpload[1].z), Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(localGrid.AreaVSMInfoUpload[2].x, Is.EqualTo(-1)); // Non-shadow AreaLight
            Assert.That(localGrid.PunctualVSMInfoUpload[0].x, Is.EqualTo(12));

            // Rotating a Rect AreaLight in place must keep world-aligned cubemap depth cache valid (NextMapFlags.Valid).
            lightData.areaLights[0].forwardWS = Vector3.left;
            lightData.areaLights[0].rightWS = Vector3.forward;
            lightData.areaLights[0].upWS = Vector3.up;
            collected = localGrid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 1024, 0f, 64f, true);
            registry.PrepareFrame(1ul, null, localGrid.CollectedDescriptors, collected);
            for (int f = 0; f < 6; f++)
            {
                Assert.That(registry.NextMap[f].Flags, Is.EqualTo((uint)VividVSMNextMapFlags.Valid),
                    $"In-place Rect rotation must keep cubemap face {f} cache valid.");
            }
            registry.CommitRecordedFrame();

            // Translating the Rect AreaLight must invalidate all 6 cubemap faces (NextMapFlags.InvalidateAll).
            lightData.areaLights[0].positionWS += new Vector3(0.5f, 0f, 0f);
            collected = localGrid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 1024, 0f, 64f, true);
            registry.PrepareFrame(1ul, null, localGrid.CollectedDescriptors, collected);
            for (int f = 0; f < 6; f++)
            {
                Assert.That(registry.NextMap[f].Flags, Is.EqualTo((uint)VividVSMNextMapFlags.InvalidateAll),
                    $"Translating Rect AreaLight must invalidate cubemap face {f} cache.");
            }
            registry.CommitRecordedFrame();

            // Steady-state zero-GC check (16 warm-up frames to settle tiered JIT, then 128 measured frames).
            for (int w = 0; w < 16; w++)
            {
                int c = localGrid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 1024, 0f, 64f, true);
                registry.PrepareFrame(1ul, null, localGrid.CollectedDescriptors, c);
                localGrid.PopulatePunctualVSMInfo(registry);
                registry.CommitRecordedFrame();
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                int c = localGrid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 1024, 0f, 64f, true);
                registry.PrepareFrame(1ul, null, localGrid.CollectedDescriptors, c);
                localGrid.PopulatePunctualVSMInfo(registry);
                registry.CommitRecordedFrame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void GPU_PruneLightGridCS_PrunesAreaCategoryWithFullBeforeSinglePage()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = LoadShader(
                PageManagementShaderPath,
                "Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            var shader = UnityEngine.Object.Instantiate(source);

            const int clusterCount = 2;
            const int singlePageFlag = (int)VividVSMProjectionFlags.SinglePage;

            // Punctual: [0]=no shadow, [1]=full VSM 0
            var punctualInfo = new[] { new int2(-1, 0), new int2(0, 0) };
            // Area: [0]=no shadow, [1]=SinglePage VSM 12, [2]=Full VSM 6
            var areaInfo = new[]
            {
                new int4(-1, 0, 0, 0),
                new int4(12, singlePageFlag, math.asint(1.0f), 0),
                new int4(6, 0, math.asint(0.8f), 0),
            };

            // Planar offsets: [0..1] = Punctual clusters 0..1, [2..3] = Area clusters 0..1.
            var layeredOffsets = new[]
            {
                PackCluster(0u, 2u), // Punctual cluster 0: [0, 1] -> [1]
                PackCluster(2u, 0u), // Punctual cluster 1: []
                PackCluster(2u, 3u), // Area cluster 0: [0, 1 (SP), 2 (Full)] -> [2 (Full), 1 (SP)]
                PackCluster(5u, 2u), // Area cluster 1: [0, 2 (Full)] -> [2 (Full)]
            };
            var layeredIndices = new uint[] { 0u, 1u, 0u, 1u, 2u, 0u, 2u };

            using var layeredOffsetBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layeredOffsets.Length, sizeof(uint));
            using var layeredListBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layeredIndices.Length, sizeof(uint));
            using var pInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, punctualInfo.Length, sizeof(int) * 2);
            using var aInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, areaInfo.Length, sizeof(int) * 4);
            using var prunedOffsetBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, clusterCount * 2, sizeof(uint));
            using var prunedListBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layeredIndices.Length, sizeof(uint));
            try
            {
                layeredOffsetBuf.SetData(layeredOffsets);
                layeredListBuf.SetData(layeredIndices);
                pInfoBuf.SetData(punctualInfo);
                aInfoBuf.SetData(areaInfo);
                prunedListBuf.SetData(new uint[layeredIndices.Length]);

                int kernel = shader.FindKernel("VSMPruneLightGridCS");
                shader.SetInt("_VSMClusterCount", clusterCount);
                shader.SetInt("_VSMPunctualLightCount", punctualInfo.Length);
                shader.SetInt("_VSMAreaLightCount", areaInfo.Length);
                shader.SetInt("_VSMPruneAreaCategoryEnabled", 1);
                shader.SetBuffer(kernel, "_VSMLayeredOffset", layeredOffsetBuf);
                shader.SetBuffer(kernel, "_VSMLayeredLightList", layeredListBuf);
                shader.SetBuffer(kernel, "_VSMPunctualLightVSMInfo", pInfoBuf);
                shader.SetBuffer(kernel, "_VSMAreaLightVSMInfo", aInfoBuf);
                shader.SetBuffer(kernel, "_VSMPrunedLightOffsetCountRW", prunedOffsetBuf);
                shader.SetBuffer(kernel, "_VSMPrunedLightIndicesRW", prunedListBuf);
                shader.Dispatch(kernel, 1, 1, 1);

                var outOffsets = new uint[clusterCount * 2];
                var outIndices = new uint[layeredIndices.Length];
                prunedOffsetBuf.GetData(outOffsets);
                prunedListBuf.GetData(outIndices);

                // Category 0 (Punctual)
                Assert.That(UnpackOffset(outOffsets[0]), Is.EqualTo(0u));
                Assert.That(UnpackCount(outOffsets[0]), Is.EqualTo(1u));
                Assert.That(outIndices[0], Is.EqualTo(1u));
                Assert.That(UnpackCount(outOffsets[1]), Is.EqualTo(0u));

                // Category 1 (Area): Full (2) strictly before SinglePage (1)
                Assert.That(UnpackOffset(outOffsets[2]), Is.EqualTo(2u));
                Assert.That(UnpackCount(outOffsets[2]), Is.EqualTo(2u));
                Assert.That(outIndices[2], Is.EqualTo(2u));
                Assert.That(outIndices[3], Is.EqualTo(1u));

                Assert.That(UnpackOffset(outOffsets[3]), Is.EqualTo(5u));
                Assert.That(UnpackCount(outOffsets[3]), Is.EqualTo(1u));
                Assert.That(outIndices[5], Is.EqualTo(2u));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void GPU_ResolveLocalShadows_RectAreaLightHardShadowStrengthAndAnisotropicSMRT()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform()
                || !VirtualShadowMapLocalShadowResolver.IsMaskFormatSupported())
            {
                Assert.Ignore("VSM local shadow mask requires Reverse-Z, compute shaders, and R32G32B32A32_UInt UAV support.");
            }

            ComputeShader shader = LoadShader(
                ResolveShaderPath,
                "Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(VirtualShadowMapLocalShadowResolver.ResolveLocalKernelName);
            Assert.That(kernel, Is.GreaterThanOrEqualTo(0));

            const int width = 16;
            const int height = 16;
            const int pageSize = 8;

            var camView = Matrix4x4.LookAt(new Vector3(0, 2, -6), new Vector3(0, 0, 2), Vector3.up).inverse;
            var worldToCam = Matrix4x4.Scale(new Vector3(1, 1, -1)) * camView;
            var camProj = Matrix4x4.Perspective(60f, 1f, 0.1f, 50f);
            var vp = GL.GetGPUProjectionMatrix(camProj, true) * worldToCam;
            var invVp = vp.inverse;

            // Rectangle AreaLight at (0, 4, 0) pointing straight down (-Y), width=2m, height=1m, range=10m.
            float effectiveRange = VirtualShadowMapRegistry.CalculateAreaEffectiveRange(10f, 2f, 1f, false);
            var descriptors = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 301ul,
                    LightType = VividVSMLightType.Rect,
                    PositionWS = new Vector3(0f, 4f, 0f),
                    Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                    Range = effectiveRange,
                    SpotAngle = 90f,
                    SourceRadius = 0.5f * Mathf.Sqrt(5f),
                    NormalBias = 0f,
                    ResolutionLodBias = 0f,
                    TexelDitherScale = 1f,
                    MaxResolution = 256,
                    MinMipLevel = 0u,
                    IsSinglePage = false,
                    UseReceiverMask = false,
                    IsAreaLight = true,
                    PunctualLightIndex = 0u,
                }
            };

            var registry = new VirtualShadowMapRegistry();
            registry.PrepareFrame(1ul, null, descriptors, descriptors.Length);
            Assert.That(registry.ProjectionCount, Is.EqualTo(6));

            // Receiver on floor at (0, 0, 0) (front hemisphere, -Y face index 3).
            Vector3 receiverWS = new Vector3(0f, 0f, 0f);
            Vector4 clipPos = vp * new Vector4(receiverWS.x, receiverWS.y, receiverWS.z, 1f);
            float receiverDeviceDepth = clipPos.z / clipPos.w;

            // Occluder halfway between light (0,4,0) and receiver (0,0,0) at (0,2,0) on -Y face (index 3).
            ref readonly VirtualShadowMapProjection negYProj = ref registry.Projections[3];
            Vector4 occShadow = negYProj.WorldToShadow * new Vector4(0f, 2.0f, 0f, 1f);
            float occDepth = occShadow.z / occShadow.w;

            var depthTex = new Texture2D(width, height, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var depthPixels = new float[width * height];
            for (int i = 0; i < depthPixels.Length; i++) depthPixels[i] = receiverDeviceDepth;
            depthTex.SetPixelData(depthPixels, 0);
            depthTex.Apply(false, true);

            var gbuf1Tex = new Texture2D(width, height, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None);
            var gbuf1Pixels = new Color[width * height];
            for (int i = 0; i < gbuf1Pixels.Length; i++) gbuf1Pixels[i] = new Color(0.5f, 1.0f, 0.0f, 1.0f); // normal = +Y
            gbuf1Tex.SetPixels(gbuf1Pixels);
            gbuf1Tex.Apply(false, true);

            var poolRt = new RenderTexture(new RenderTextureDescriptor(16, 16)
            {
                graphicsFormat = GraphicsFormat.R32_UInt,
                depthStencilFormat = GraphicsFormat.None,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                msaaSamples = 1,
            });
            poolRt.filterMode = FilterMode.Point;
            poolRt.Create();
            var dummyPoolRt = new RenderTexture(poolRt.descriptor);
            dummyPoolRt.Create();

            var maskRt = new RenderTexture(width, height, 0, GraphicsFormat.R32G32B32A32_UInt)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            maskRt.Create();

            // Page (0,0) = occluder depth; Page (1,0) = empty (0.0 depth, fully lit).
            var uploadShader = LoadShader(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute",
                "Packages/VividRP/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute");
            Assert.That(uploadShader, Is.Not.Null);
            int uploadKernel = uploadShader.FindKernel("UploadTestPools");
            var fillData = new uint[16 * 16 * 2];
            uint occBits = math.asuint(occDepth);
            for (int y = 0; y < pageSize; y++)
                for (int x = 0; x < pageSize; x++)
                    fillData[y * 16 + x] = occBits;
            var fillBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fillData.Length, sizeof(uint));
            fillBuf.SetData(fillData);
            {
                var uploadCmd = new CommandBuffer();
                uploadCmd.SetComputeBufferParam(uploadShader, uploadKernel, "_TestStaticData", fillBuf);
                uploadCmd.SetComputeBufferParam(uploadShader, uploadKernel, "_TestDynamicData", fillBuf);
                uploadCmd.SetComputeTextureParam(uploadShader, uploadKernel, "_TestStaticPool", poolRt);
                uploadCmd.SetComputeTextureParam(uploadShader, uploadKernel, "_TestDynamicPool", dummyPoolRt);
                uploadCmd.DispatchCompute(uploadShader, uploadKernel, 2, 2, 2);
                Graphics.ExecuteCommandBuffer(uploadCmd);
                uploadCmd.Dispose();
            }

            var pageTableOccluded = new uint[registry.TotalPageTableEntries];
            var pageTableLit = new uint[registry.TotalPageTableEntries];
            for (int i = 0; i < pageTableOccluded.Length; i++)
            {
                pageTableOccluded[i] = EncodeNativePageTable(0u, 0u);
                pageTableLit[i] = EncodeNativePageTable(1u, 0u);
            }

            var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, registry.ProjectionCount, VirtualShadowMapProjection.Stride);
            var pageTableBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageTableOccluded.Length, sizeof(uint));
            var prunedOffsetsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            var prunedIndicesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var pInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int) * 2);
            var punctualBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.PunctualLightData.Stride);
            var aInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int) * 4);
            var areaBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.AreaLightData.Stride);
            var logBaseBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));

            try
            {
                var projUpload = new VirtualShadowMapProjection[registry.ProjectionCount];
                Array.Copy(registry.Projections, projUpload, registry.ProjectionCount);
                projBuf.SetData(projUpload);
                pageTableBuf.SetData(pageTableOccluded);
                // Cluster 0: Category 0 (Punctual) count=0, Category 1 (Area) offset=0 count=1 -> AreaLight 0 packed into slot 0.
                prunedOffsetsBuf.SetData(new[] { PackCluster(0u, 0u), PackCluster(0u, 1u) });
                prunedIndicesBuf.SetData(new uint[] { 0u });
                pInfoBuf.SetData(new[] { new int2(-1, 0) });
                punctualBuf.SetData(new[] { default(VividLightData.PunctualLightData) });
                // shadowStrength = 0.8 -> fully occluded should pack near lerp(1.0, 0.0, 0.8) = 0.2.
                aInfoBuf.SetData(new[] { new int4(0, 0, math.asint(0.8f), 0) });
                areaBuf.SetData(new[]
                {
                    new VividLightData.AreaLightData
                    {
                        positionWS = new Vector3(0f, 4f, 0f),
                        range = 10f,
                        color = Vector3.one,
                        lightType = 1u, // Rectangle
                        forwardWS = Vector3.down,
                        width = 2f,
                        rightWS = Vector3.right,
                        height = 1f,
                        upWS = Vector3.forward,
                        cosBarnDoorAngle = 0f,
                        barnDoorLength = 1000f,
                    }
                });
                logBaseBuf.SetData(new float[] { 1.05f });

                void DispatchResolve(Vector4 smrtParams, Vector4 smrtSettings)
                {
                    var cmd = new CommandBuffer();
                    cmd.SetComputeTextureParam(shader, kernel, "_DepthTexture", depthTex);
                    cmd.SetComputeTextureParam(shader, kernel, "_GBuffer1", gbuf1Tex);
                    cmd.SetComputeTextureParam(shader, kernel, "_VSMPhysicalPagePool", poolRt);
                    cmd.SetComputeTextureParam(shader, kernel, VirtualShadowMapLocalShadowResolver.LocalShadowMaskBitsRWId, maskRt);
                    cmd.SetComputeBufferParam(shader, kernel, "_VSMPrototypePageTable", pageTableBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapProjectionSet.BufferId, projBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightOffsetCountId, prunedOffsetsBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightIndicesId, prunedIndicesBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightVSMInfoId, pInfoBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightsId, punctualBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.AreaLightVSMInfoId, aInfoBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.AreaLightsId, areaBuf);
                    cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.LogBaseBufferId, logBaseBuf);

                    cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, registry.ProjectionCount);
                    cmd.SetComputeIntParam(shader, "_VSMPrototypePageTableEntryCount", pageTableOccluded.Length);
                    cmd.SetComputeIntParam(shader, "_VSMPrototypePageSize", pageSize);
                    cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.ClusterCountId, 1);
                    cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.PunctualLightCountId, 0);
                    cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.AreaLightCountId, 1);
                    cmd.SetComputeIntParam(shader, "_CSMOutputWidth", width);
                    cmd.SetComputeIntParam(shader, "_CSMOutputHeight", height);
                    cmd.SetComputeIntParam(shader, "_CSMFrameIndex", 1);
                    cmd.SetComputeMatrixParam(shader, "_CSMInvViewProjMatrix", invVp);
                    cmd.SetComputeMatrixParam(shader, VirtualShadowMapLocalLightGrid.CameraViewMatrixId, worldToCam);
                    cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.CameraViewParamsId, new Vector4(0.01f, 1f, 0f, 0f));
                    cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterParamsId, new Vector4(64f, 1f, 1f, 1f));
                    cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterDepthParamsId, new Vector4(0.1f, 50f, 1.05f, 0f));
                    cmd.SetComputeVectorParam(shader, "_VSMReceiverOffsetParameters", Vector4.zero);
                    cmd.SetComputeVectorParam(shader, "_VSMReceiverViewForward", -worldToCam.GetRow(2));
                    cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTParametersId, smrtParams);
                    cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTSettingsId, smrtSettings);
                    cmd.DispatchCompute(shader, kernel, 2, 2, 1);
                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Dispose();
                }

                uint4[] ReadbackMask()
                {
                    int rbKernel = uploadShader.FindKernel("ReadbackUInt4Texture");
                    using var rbBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, width * height, sizeof(uint) * 4);
                    var rbCmd = new CommandBuffer();
                    rbCmd.SetComputeTextureParam(uploadShader, rbKernel, "_TestReadbackUInt4Tex", maskRt);
                    rbCmd.SetComputeBufferParam(uploadShader, rbKernel, "_TestReadbackUInt4Out", rbBuf);
                    rbCmd.SetComputeIntParams(uploadShader, "_TestReadbackUInt4Size", width, height);
                    rbCmd.DispatchCompute(uploadShader, rbKernel, CoreUtils.DivRoundUp(width, 8), CoreUtils.DivRoundUp(height, 8), 1);
                    Graphics.ExecuteCommandBuffer(rbCmd);
                    rbCmd.Dispose();
                    var result = new uint4[width * height];
                    rbBuf.GetData(result);
                    return result;
                }

                // 1. Hard shadow occluded with shadowStrength=0.8 -> expected ~0.20 (nibble 3/15 = 0.2).
                DispatchResolve(new Vector4(1f, 0f, 50f, 33.3f), Vector4.zero);
                uint4[] maskData1 = ReadbackMask();
                float occludedShadow = Unpack4Bit(maskData1[8 * width + 8], 0);
                Assert.That(occludedShadow, Is.InRange(0.10f, 0.30f),
                    "Occluded Rect AreaLight with shadowStrength=0.8 must unpack near 0.2.");

                // 2. Anisotropic SMRT (8 rays x 4 samples) against unoccluded pool -> expected >= 0.9.
                pageTableBuf.SetData(pageTableLit);
                aInfoBuf.SetData(new[] { new int4(0, 0, math.asint(1.0f), 0) });
                DispatchResolve(new Vector4(8f, 4f, 50f, 33.3f), new Vector4(5f, 1f, 2f, 0f));
                uint4[] maskData2 = ReadbackMask();
                float litSmrtShadow = Unpack4Bit(maskData2[8 * width + 8], 0);
                Assert.That(litSmrtShadow, Is.GreaterThanOrEqualTo(0.9f),
                    "Unoccluded Rect AreaLight anisotropic SMRT must unpack near 1.0.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depthTex);
                UnityEngine.Object.DestroyImmediate(gbuf1Tex);
                fillBuf.Dispose();
                dummyPoolRt.Release();
                poolRt.Release();
                maskRt.Release();
                projBuf.Dispose();
                pageTableBuf.Dispose();
                prunedOffsetsBuf.Dispose();
                prunedIndicesBuf.Dispose();
                pInfoBuf.Dispose();
                punctualBuf.Dispose();
                aInfoBuf.Dispose();
                areaBuf.Dispose();
                logBaseBuf.Dispose();
            }
        }
    }
}
