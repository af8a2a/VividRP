using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapLocalLightPhase1Tests
    {
        private const string ProductionShader = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const uint LightClusterOffsetBits = 26u;
        private const uint LightClusterOffsetMask = (1u << 26) - 1u;
        private const uint LightClusterCountMask = 63u;
        private const uint PageRequestedBit = 1u << 0;
        private const uint PagePrimaryRequestedBit = 1u << 2;
        private const uint PageCoarseRequestedBit = 1u << 3;

        private static uint PackCluster(uint offset, uint count)
            => (offset & LightClusterOffsetMask) | ((count & LightClusterCountMask) << (int)LightClusterOffsetBits);

        private static uint UnpackOffset(uint packed) => packed & LightClusterOffsetMask;
        private static uint UnpackCount(uint packed) => (packed >> (int)LightClusterOffsetBits) & LightClusterCountMask;

        [Test]
        public void CollectLocalLights_FiltersClassifiesMapsAndAllocatesZeroInSteadyState()
        {
            using var grid = new VirtualShadowMapLocalLightGrid();
            var registry = new VirtualShadowMapRegistry();
            var clipmaps = new VirtualShadowMapClipmapLayout();
            clipmaps.Update(Vector3.zero, Quaternion.Euler(45f, 30f, 0f),
                new Bounds(Vector3.zero, Vector3.one * 50f), 100f, 512, 6, 1f, 1ul, 10ul);

            var lightData = new VividLightData();
            lightData.punctualLights = new VividLightData.PunctualLightData[6];
            lightData.punctualLightShadowMeta = new VividLightData.PunctualLightShadowMeta[6];
            lightData.punctualLightCount = 6;

            // 0: Non-shadow point light (filtered out)
            lightData.punctualLights[0] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(0f, 2f, 5f), range = 10f, lightType = 0u, shadowStrength = 1f,
                directionWS = Vector3.forward, upWS = Vector3.up,
            };
            lightData.punctualLightShadowMeta[0] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(100ul), flags = VividLightRenderDataFlags.None,
            };

            // 1: Shadow point light close to camera -> Full VSM (6 faces)
            lightData.punctualLights[1] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(0f, 2f, 5f), range = 12f, lightType = 0u, shadowStrength = 1f,
                directionWS = Vector3.forward, upWS = Vector3.up,
            };
            lightData.punctualLightShadowMeta[1] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(101ul), shapeRadius = 0.25f, flags = VividLightRenderDataFlags.CastShadows,
            };

            // 2: Shadow spot light with zero shadowStrength (filtered out)
            lightData.punctualLights[2] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(1f, 2f, 5f), range = 12f, lightType = 1u, shadowStrength = 0f,
                directionWS = Vector3.down, upWS = Vector3.forward,
            };
            lightData.punctualLightShadowMeta[2] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(102ul), spotAngle = 60f, flags = VividLightRenderDataFlags.CastShadows,
            };

            // 3: Shadow spot light close to camera -> Full VSM (1 face)
            lightData.punctualLights[3] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(-2f, 4f, 6f), range = 15f, lightType = 1u, shadowStrength = 1f,
                directionWS = Vector3.down, upWS = Vector3.forward,
            };
            lightData.punctualLightShadowMeta[3] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(103ul), spotAngle = 50f, flags = VividLightRenderDataFlags.CastShadows,
            };

            // 4: Distant shadow spot light -> Single-Page VSM
            lightData.punctualLights[4] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(0f, 0f, 500f), range = 5f, lightType = 1u, shadowStrength = 1f,
                directionWS = Vector3.down, upWS = Vector3.forward,
            };
            lightData.punctualLightShadowMeta[4] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(104ul), spotAngle = 45f, flags = VividLightRenderDataFlags.CastShadows,
            };

            // 5: Projector box (lightType == 2, filtered out)
            lightData.punctualLights[5] = new VividLightData.PunctualLightData
            {
                positionWS = new Vector3(0f, 1f, 3f), range = 10f, lightType = 2u, shadowStrength = 1f,
                directionWS = Vector3.forward, upWS = Vector3.up,
            };
            lightData.punctualLightShadowMeta[5] = new VividLightData.PunctualLightShadowMeta
            {
                lightEntityId = EntityId.FromULong(105ul), flags = VividLightRenderDataFlags.CastShadows,
            };

            int collected = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
            Assert.That(collected, Is.EqualTo(3));
            Assert.That(grid.CollectedDescriptors[0].LightType, Is.EqualTo(VividVSMLightType.Point));
            Assert.That(grid.CollectedDescriptors[0].IsSinglePage, Is.False);
            Assert.That(grid.CollectedDescriptors[0].PunctualLightIndex, Is.EqualTo(1u));
            Assert.That(grid.CollectedDescriptors[1].LightType, Is.EqualTo(VividVSMLightType.Spot));
            Assert.That(grid.CollectedDescriptors[1].IsSinglePage, Is.False);
            Assert.That(grid.CollectedDescriptors[1].PunctualLightIndex, Is.EqualTo(3u));
            Assert.That(grid.CollectedDescriptors[2].LightType, Is.EqualTo(VividVSMLightType.Spot));
            Assert.That(grid.CollectedDescriptors[2].IsSinglePage, Is.True);
            Assert.That(grid.CollectedDescriptors[2].PunctualLightIndex, Is.EqualTo(4u));

            registry.PrepareFrame(1ul, clipmaps, grid.CollectedDescriptors, collected);
            grid.PopulatePunctualVSMInfo(registry);
            int dirCount = registry.DirectionalCount;
            Assert.That(grid.PunctualVSMInfoUpload[0].x, Is.EqualTo(-1));
            Assert.That(grid.PunctualVSMInfoUpload[1].x, Is.EqualTo(dirCount)); // Point Face0
            Assert.That(grid.PunctualVSMInfoUpload[1].y & (int)VividVSMProjectionFlags.SinglePage, Is.Zero);
            Assert.That(grid.PunctualVSMInfoUpload[2].x, Is.EqualTo(-1));
            Assert.That(grid.PunctualVSMInfoUpload[3].x, Is.EqualTo(dirCount + 6)); // Full Spot
            Assert.That(grid.PunctualVSMInfoUpload[3].y & (int)VividVSMProjectionFlags.SinglePage, Is.Zero);
            Assert.That(grid.PunctualVSMInfoUpload[4].x, Is.EqualTo(dirCount + 7)); // Single-Page Spot
            Assert.That(grid.PunctualVSMInfoUpload[4].y & (int)VividVSMProjectionFlags.SinglePage, Is.Not.Zero);
            Assert.That(grid.PunctualVSMInfoUpload[5].x, Is.EqualTo(-1));
            registry.CommitRecordedFrame();

            // Zero-GC steady-state verification.
            for (int w = 0; w < 16; w++)
            {
                int n = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
                registry.PrepareFrame(1ul, clipmaps, grid.CollectedDescriptors, n);
                grid.PopulatePunctualVSMInfo(registry);
                registry.CommitRecordedFrame();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                int n = grid.CollectLocalLights(lightData, Vector3.zero, 60f, 1080, 4096, 0f, 128f, true);
                registry.PrepareFrame(1ul, clipmaps, grid.CollectedDescriptors, n);
                grid.PopulatePunctualVSMInfo(registry);
                registry.CommitRecordedFrame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void GPU_PruneLightGridCS_FiltersAndOrdersFullBeforeSinglePage()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // 5 punctual lights:
            // 0: no shadow (-1)
            // 1: single-page VSM
            // 2: full VSM
            // 3: no shadow (-1)
            // 4: full VSM
            var infoData = new[]
            {
                new int2(-1, 0),
                new int2(10, (int)VividVSMProjectionFlags.SinglePage),
                new int2(6, 0),
                new int2(-1, 0),
                new int2(7, 0),
            };
            // Cluster 0: [0, 1, 2, 3, 4] at offset 0 -> pruned: [2, 4, 1] (count 3, offset 0)
            // Cluster 1: [0, 3] at offset 5 -> pruned: [] (count 0, offset 5)
            var offsets = new[] { PackCluster(0u, 5u), PackCluster(5u, 2u) };
            var indices = new uint[] { 0u, 1u, 2u, 3u, 4u, 0u, 3u };

            using var layeredOffset = new GraphicsBuffer(GraphicsBuffer.Target.Structured, offsets.Length, sizeof(uint));
            using var layeredList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Length, sizeof(uint));
            using var vsmInfo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, infoData.Length, sizeof(int) * 2);
            using var prunedOffset = new GraphicsBuffer(GraphicsBuffer.Target.Structured, offsets.Length, sizeof(uint));
            using var prunedList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Length, sizeof(uint));
            try
            {
                layeredOffset.SetData(offsets);
                layeredList.SetData(indices);
                vsmInfo.SetData(infoData);
                prunedList.SetData(new uint[indices.Length]);

                int kernel = shader.FindKernel("VSMPruneLightGridCS");
                shader.SetInt("_VSMClusterCount", offsets.Length);
                shader.SetInt("_VSMPunctualLightCount", infoData.Length);
                shader.SetBuffer(kernel, "_VSMLayeredOffset", layeredOffset);
                shader.SetBuffer(kernel, "_VSMLayeredLightList", layeredList);
                shader.SetBuffer(kernel, "_VSMPunctualLightVSMInfo", vsmInfo);
                shader.SetBuffer(kernel, "_VSMPrunedLightOffsetCountRW", prunedOffset);
                shader.SetBuffer(kernel, "_VSMPrunedLightIndicesRW", prunedList);
                shader.Dispatch(kernel, 1, 1, 1);

                var outOffsets = new uint[offsets.Length];
                var outIndices = new uint[indices.Length];
                prunedOffset.GetData(outOffsets);
                prunedList.GetData(outIndices);

                Assert.That(UnpackOffset(outOffsets[0]), Is.EqualTo(0u));
                Assert.That(UnpackCount(outOffsets[0]), Is.EqualTo(3u));
                Assert.That(outIndices[0], Is.EqualTo(2u));
                Assert.That(outIndices[1], Is.EqualTo(4u));
                Assert.That(outIndices[2], Is.EqualTo(1u)); // Single-Page strictly at tail

                Assert.That(UnpackOffset(outOffsets[1]), Is.EqualTo(5u));
                Assert.That(UnpackCount(outOffsets[1]), Is.EqualTo(0u));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void GPU_MarkCoarsePagesUnified_MarksDirectionalLocalAndSinglePage()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // 3 projections:
            // 0: Directional coarse clipmap (4x4 = 16 entries, offset 0)
            // 1: Full Local Spot (4x4 mip0 -> 16+4+1 = 21 entries, 3 mips, offset 16; coarsest mip2 at 16 + 20 = 36)
            // 2: Single-Page Local Spot (1 entry, offset 37)
            const int totalEntries = 38;
            var projs = new[]
            {
                new VirtualShadowMapProjection
                {
                    WorldToShadow = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f)),
                    SelectionSphere = new Vector4(0f, 0f, 0f, -10f),
                    Addressing = new uint4(0u, 0u, 4u, 1u),
                    Meta = new uint4((uint)VividVSMLightType.Directional, (uint)VividVSMProjectionFlags.IsCoarseClipLevel, 0u, 1u),
                },
                new VirtualShadowMapProjection
                {
                    Addressing = new uint4(16u, 0u, 4u, 3u),
                    Meta = new uint4((uint)VividVSMLightType.Spot, (uint)VividVSMProjectionFlags.UseReceiverMask, 0u, 0u),
                },
                new VirtualShadowMapProjection
                {
                    Addressing = new uint4(37u, 0u, 1u, 1u),
                    Meta = new uint4((uint)VividVSMLightType.Spot, (uint)VividVSMProjectionFlags.SinglePage, 0u, 0u),
                },
            };

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, projs.Length, VirtualShadowMapProjection.Stride);
            using var reqFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var reqMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 2);
            try
            {
                projBuf.SetData(projs);
                reqFlags.SetData(new uint[totalEntries]);
                reqMasks.SetData(new uint2[totalEntries]);

                int kernel = shader.FindKernel("VSMMarkCoarsePagesUnified");
                shader.SetInt("_VSMProjectionCount", projs.Length);
                shader.SetInt("_VSMPrototypeRequestEnabled", 1);
                shader.SetInt("_VSMReceiverMaskEnabled", 1);
                shader.SetInt("_VSMPrototypePageTableEntryCount", totalEntries);
                shader.SetInt("_VSMMarkCoarsePagesLocal", 1);
                shader.SetBuffer(kernel, "_VSMProjections", projBuf);
                shader.SetBuffer(kernel, "_VSMPageRequestFlags", reqFlags);
                shader.SetBuffer(kernel, "_VSMPageReceiverMasks", reqMasks);
                shader.Dispatch(kernel, 1, 1, 1);

                var flags = new uint[totalEntries];
                var masks = new uint2[totalEntries];
                reqFlags.GetData(flags);
                reqMasks.GetData(masks);

                uint expectedFlag = PageRequestedBit | PageCoarseRequestedBit;
                // Directional center (1..2, 1..2)
                Assert.That(flags[1 * 4 + 1], Is.EqualTo(expectedFlag));
                Assert.That(flags[2 * 4 + 2], Is.EqualTo(expectedFlag));
                Assert.That(flags[0], Is.Zero);

                // Local Full VSM coarsest mip2 (index 36)
                Assert.That(flags[36], Is.EqualTo(expectedFlag));
                Assert.That(masks[36], Is.EqualTo(new uint2(0xFFFFFFFFu, 0xFFFFFFFFu)));
                Assert.That(flags[16], Is.Zero);

                // Single-Page VSM (index 37)
                Assert.That(flags[37], Is.EqualTo(expectedFlag));
                Assert.That(masks[37], Is.EqualTo(new uint2(0xFFFFFFFFu, 0xFFFFFFFFu)));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void GPU_MarkLocalReceiverPages_MarksSpotPointFaceAndStopsOnSinglePageTail()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // Camera at origin looking +Z, perspective 60 deg, 16x16 viewport.
            const int width = 16;
            const int height = 16;
            // Unity camera view matrix has -Z forward; use standard worldToCameraMatrix convention.
            Matrix4x4 worldToCam = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, -1f)).inverse;
            Matrix4x4 camProj = Matrix4x4.Perspective(60f, 1f, 0.1f, 100f);
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camProj, false);
            Matrix4x4 viewProj = gpuProj * worldToCam;
            Matrix4x4 invViewProj = viewProj.inverse;

            // Surface point right in front of camera at (0, 0, 5), facing -Z toward camera and lights.
            Vector3 receiverPos = new Vector3(0f, 0f, 5f);
            Vector4 clip = viewProj * new Vector4(receiverPos.x, receiverPos.y, receiverPos.z, 1f);
            float deviceDepth = clip.z / clip.w;

            // Build Registry with 1 Full Spot light (lightIdx 0) + 1 Single-Page Spot light (lightIdx 1).
            var registry = new VirtualShadowMapRegistry();
            var descriptors = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 500ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 0f, 1f),
                    Rotation = Quaternion.identity, // Looks +Z toward receiver at z=5
                    Range = 20f,
                    SpotAngle = 60f,
                    MaxResolution = 1024, // 8x8 mip0 -> 85 entries
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 501ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 0f, 1f),
                    Rotation = Quaternion.identity,
                    Range = 20f,
                    SpotAngle = 60f,
                    MaxResolution = 1024,
                    IsSinglePage = true,
                    UseReceiverMask = false,
                    PunctualLightIndex = 1u,
                },
            };
            registry.PrepareFrame(1ul, null, descriptors, descriptors.Length);
            int totalEntries = registry.TotalPageTableEntries;
            Assert.That(registry.ProjectionCount, Is.EqualTo(2));

            // Punctual light GPU data matching HLSL VSMPunctualLightData (128 bytes).
            float outerCos = Mathf.Cos(30f * Mathf.Deg2Rad);
            float innerCos = Mathf.Cos(20f * Mathf.Deg2Rad);
            float angleScale = 1f / Mathf.Max(innerCos - outerCos, 1e-4f);
            float angleOffset = -outerCos * angleScale;
            var punctuals = new[]
            {
                new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(0f, 0f, 1f), range = 20f, lightType = 1u,
                    directionWS = Vector3.forward, angleScale = angleScale, angleOffset = angleOffset,
                    shadowStrength = 1f,
                },
                new VividLightData.PunctualLightData
                {
                    positionWS = new Vector3(0f, 0f, 1f), range = 20f, lightType = 1u,
                    directionWS = Vector3.forward, angleScale = angleScale, angleOffset = angleOffset,
                    shadowStrength = 1f,
                },
            };
            var vsmInfos = new[]
            {
                new int2(0, (int)registry.Projections[0].Meta.y),
                new int2(1, (int)registry.Projections[1].Meta.y),
            };

            // Single cluster covering entire 16x16 screen, containing [0 (Full), 1 (SinglePage)].
            var clusterOffsets = new[] { PackCluster(0u, 2u) };
            var clusterIndices = new[] { 0u, 1u };
            var logBases = new[] { 1.1f };

            var depthTex = new RenderTexture(width, height, 0, GraphicsFormat.R32_SFloat) { enableRandomWrite = true };
            var gbuffer1Tex = new RenderTexture(width, height, 0, GraphicsFormat.R16G16B16A16_SFloat) { enableRandomWrite = true };
            depthTex.Create();
            gbuffer1Tex.Create();
            // Fill depth with receiver depth via temporary Texture2D blit.
            var depthSeed = new Texture2D(width, height, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var depthColors = new Color[width * height];
            for (int i = 0; i < depthColors.Length; i++) depthColors[i] = new Color(deviceDepth, 0f, 0f, 0f);
            depthSeed.SetPixels(depthColors);
            depthSeed.Apply(false, false);
            Graphics.Blit(depthSeed, depthTex);

            using var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, registry.ProjectionCount, VirtualShadowMapProjection.Stride);
            using var reqFlags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var reqMasks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint) * 2);
            using var punctualBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, punctuals.Length, 128);
            using var infoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vsmInfos.Length, sizeof(int) * 2);
            using var prunedOffset = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            using var prunedList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            using var logBaseBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));
            try
            {
                projBuf.SetData(registry.Projections, 0, 0, registry.ProjectionCount);
                reqFlags.SetData(new uint[totalEntries]);
                reqMasks.SetData(new uint2[totalEntries]);
                punctualBuf.SetData(punctuals);
                infoBuf.SetData(vsmInfos);
                prunedOffset.SetData(clusterOffsets);
                prunedList.SetData(clusterIndices);
                logBaseBuf.SetData(logBases);

                float radiusScreen = Mathf.Min(1f / (width * Mathf.Abs(camProj.m00)), 1f / (height * Mathf.Abs(camProj.m11)));
                int kernel = shader.FindKernel("VSMMarkLocalReceiverPages");
                shader.SetInt("_VSMPrototypeRequestEnabled", 1);
                shader.SetInt("_VSMReceiverMaskEnabled", 1);
                shader.SetInt("_VSMPrototypePageSize", VirtualShadowMapPrototypeRuntime.PageSize);
                shader.SetInt("_VSMPrototypePageTableEntryCount", totalEntries);
                shader.SetInt("_VSMProjectionCount", registry.ProjectionCount);
                shader.SetInt("_VSMClusterCount", 1);
                shader.SetInt("_VSMPunctualLightCount", punctuals.Length);
                shader.SetInt("_CSMOutputWidth", width);
                shader.SetInt("_CSMOutputHeight", height);
                shader.SetInt("_VSMCullBackfacingPixels", 0);
                shader.SetMatrix("_CSMInvViewProjMatrix", invViewProj);
                shader.SetMatrix("_VSMCameraViewMatrix", worldToCam);
                shader.SetVector("_VSMCameraViewParams", new Vector4(radiusScreen, 1f, 0f, 0f));
                shader.SetVector("_VSMClusterParams", new Vector4(16f, 1f, 1f, 1f));
                shader.SetVector("_VSMClusterDepthParams", new Vector4(0.1f, 100f, 1.1f, 0f));
                shader.SetBuffer(kernel, "_VSMProjections", projBuf);
                shader.SetBuffer(kernel, "_VSMPageRequestFlags", reqFlags);
                shader.SetBuffer(kernel, "_VSMPageReceiverMasks", reqMasks);
                shader.SetBuffer(kernel, "_VSMPunctualLights", punctualBuf);
                shader.SetBuffer(kernel, "_VSMPunctualLightVSMInfo", infoBuf);
                shader.SetBuffer(kernel, "_VSMPrunedLightOffsetCount", prunedOffset);
                shader.SetBuffer(kernel, "_VSMPrunedLightIndices", prunedList);
                shader.SetBuffer(kernel, "_VSMLogBaseBuffer", logBaseBuf);
                shader.SetTexture(kernel, "_DepthTexture", depthTex);
                shader.SetTexture(kernel, "_GBuffer1", gbuffer1Tex);
                shader.Dispatch(kernel, 1, 1, 1);

                var outFlags = new uint[totalEntries];
                var outMasks = new uint2[totalEntries];
                reqFlags.GetData(outFlags);
                reqMasks.GetData(outMasks);

                // Full Spot (entries 0..84) must have at least one primary requested page with a non-zero 8x8 receiver mask.
                bool anyFullMarked = false;
                bool anyMaskSet = false;
                for (int i = 0; i < 85; i++)
                {
                    if ((outFlags[i] & (PageRequestedBit | PagePrimaryRequestedBit)) == (PageRequestedBit | PagePrimaryRequestedBit))
                        anyFullMarked = true;
                    if (outMasks[i].x != 0u || outMasks[i].y != 0u)
                        anyMaskSet = true;
                }
                Assert.That(anyFullMarked, Is.True);
                Assert.That(anyMaskSet, Is.True);

                // Single-Page Spot (entry 85) MUST remain unmarked because VSMMarkLocalReceiverPages breaks on Single-Page tail.
                Assert.That(outFlags[85], Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(depthSeed);
                depthTex.Release();
                gbuffer1Tex.Release();
                Object.DestroyImmediate(depthTex);
                Object.DestroyImmediate(gbuffer1Tex);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
