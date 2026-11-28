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
    public sealed class VirtualShadowMapLocalShadowPhase3Tests
    {
        private const string ResolveShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const int ClusterPackingOffsetBits = 24;
        private const uint PageTableValidBit = 0x80000000u;

        private static ComputeShader LoadResolveShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ResolveShaderPath);
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute");
            return shader;
        }

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
        public void LocalShadowResolver_SMRTParametersAndZeroGCSteadyState()
        {
            using var resolver = new VirtualShadowMapLocalShadowResolver();
            // Warm up mask allocation
            resolver.EnsureMaskTexture(64, 64);
            Assert.That(resolver.LocalShadowMaskBits, Is.Not.Null);
            Assert.That(resolver.Width, Is.EqualTo(64));
            Assert.That(resolver.Height, Is.EqualTo(64));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 16; i++)
                resolver.EnsureMaskTexture(64, 64);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), "EnsureMaskTexture steady-state must allocate 0 managed bytes.");

            Vector4 defaultParams = VirtualShadowMapLocalShadowResolver.BuildLocalSMRTParameters(null);
            Assert.That(defaultParams.x, Is.EqualTo(1.0f));
            Assert.That(defaultParams.y, Is.EqualTo(0.0f));
            Assert.That(defaultParams.z, Is.EqualTo(VirtualShadowMapLocalShadowResolver.DefaultMaxSlopeBiasLocal));
            Assert.That(defaultParams.w, Is.GreaterThan(30.0f));
        }

        [Test]
        public void GPU_ResolveLocalShadows_SpotAndPointHardShadow()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform()
                || !VirtualShadowMapLocalShadowResolver.IsMaskFormatSupported())
            {
                Assert.Ignore("VSM local shadow mask requires Reverse-Z, compute shaders, and R32G32B32A32_UInt UAV support.");
            }

            ComputeShader shader = LoadResolveShader();
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(VirtualShadowMapLocalShadowResolver.ResolveLocalKernelName);
            Assert.That(kernel, Is.GreaterThanOrEqualTo(0));

            const int width = 16;
            const int height = 16;
            const int pageSize = 8;

            // Camera at (0, 2, -6) looking at +Z towards a flat ground plane at y = 0.
            var camView = Matrix4x4.LookAt(new Vector3(0, 2, -6), new Vector3(0, 0, 2), Vector3.up).inverse;
            // Unity camera worldToCameraMatrix negates Z.
            var worldToCam = Matrix4x4.Scale(new Vector3(1, 1, -1)) * camView;
            var camProj = Matrix4x4.Perspective(60f, 1f, 0.1f, 50f);
            var gpuCamProj = GL.GetGPUProjectionMatrix(camProj, true);
            var vp = gpuCamProj * worldToCam;
            var invVp = vp.inverse;

            // 1 Spot light above (0, 5, 0) pointing straight down, 1 Point light at (3, 4, 0).
            var descriptors = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 101ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(0f, 5f, 0f),
                    Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                    Range = 12f,
                    SpotAngle = 90f,
                    SourceRadius = 0f,
                    NormalBias = 0f,
                    ResolutionLodBias = 0f,
                    TexelDitherScale = 1f,
                    MaxResolution = 256,
                    MinMipLevel = 0u,
                    IsSinglePage = false,
                    UseReceiverMask = false,
                    PunctualLightIndex = 0u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 102ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = new Vector3(3f, 4f, 0f),
                    Rotation = Quaternion.identity,
                    Range = 12f,
                    SpotAngle = 90f,
                    SourceRadius = 0f,
                    NormalBias = 0f,
                    ResolutionLodBias = 0f,
                    TexelDitherScale = 1f,
                    MaxResolution = 256,
                    MinMipLevel = 0u,
                    IsSinglePage = false,
                    UseReceiverMask = false,
                    PunctualLightIndex = 1u,
                }
            };

            var registry = new VirtualShadowMapRegistry();
            registry.PrepareFrame(1ul, null, descriptors, descriptors.Length);
            Assert.That(registry.ProjectionCount, Is.EqualTo(7)); // 1 Spot + 6 Point faces

            // Compute projected device depth for a receiver point directly under Spot light at (0, 0, 0).
            Vector3 receiverWS = new Vector3(0f, 0f, 0f);
            Vector4 clipPos = vp * new Vector4(receiverWS.x, receiverWS.y, receiverWS.z, 1f);
            float receiverDeviceDepth = clipPos.z / clipPos.w;

            // Compute Spot light shadow depth for receiverWS and an occluder halfway up at (0, 2.5, 0).
            ref readonly VirtualShadowMapProjection spotProj = ref registry.Projections[0];
            Vector4 occShadow = spotProj.WorldToShadow * new Vector4(0f, 2.5f, 0f, 1f);
            float occDepth = occShadow.z / occShadow.w; // Reverse-Z: closer to light -> larger than receiver depth

            // Prepare Depth (all pixels at receiverDeviceDepth except pixel (0,0) = sky 0.0) & GBuffer1 (up normal oct = 0.5, 0.5).
            var depthTex = new Texture2D(width, height, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var depthPixels = new float[width * height];
            for (int i = 0; i < depthPixels.Length; i++) depthPixels[i] = receiverDeviceDepth;
            depthPixels[0] = 0.0f; // Sky pixel at (0, 0) in Reverse-Z
            depthTex.SetPixelData(depthPixels, 0);
            depthTex.Apply(false, true);

            var gbuf1Tex = new Texture2D(width, height, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None);
            var gbuf1Pixels = new Color[width * height];
            // Encode normal (0, 1, 0) in Vivid octahedron [0,1]^2: (0.5, 0.5) -> (0,0,1)? Let's check: use (0.5, 0.5) or neutral up;
            // For receiver at (0,0,0), Spot is at (0,5,0) (+Y) and Point is at (3,4,0) (+X,+Y), so normal (0,1,0) faces both!
            // In octahedron encoding: oct(0,1,0) = (0, 1) -> [0,1] = (0.5, 1.0).
            for (int i = 0; i < gbuf1Pixels.Length; i++) gbuf1Pixels[i] = new Color(0.5f, 1.0f, 0.0f, 1.0f);
            gbuf1Tex.SetPixels(gbuf1Pixels);
            gbuf1Tex.Apply(false, true);

            // Physical page pool: 2x2 pages of 8x8 (16x16), slice 0 = Final depth, slice 1 = Static depth.
            // Page (0,0) filled with occluder depth (shadowed Spot); Page (1,0) filled with 0.0 (unshadowed Point).
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

            // Upload occluder depth to physical page (0,0) via GPU compute (Texture2D does not support R32_UInt).
            var uploadShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute");
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

            // Map every Spot page table entry to physical page (0,0) [occluded], and every Point entry to (1,0) [lit].
            var pageTableData = new uint[registry.TotalPageTableEntries];
            int spotEntries = VirtualShadowMapAddressing.CalculateTotalMipChainEntries(2);
            for (int i = 0; i < pageTableData.Length; i++)
                pageTableData[i] = i < spotEntries ? EncodeNativePageTable(0u, 0u) : EncodeNativePageTable(1u, 0u);

            var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, registry.ProjectionCount, VirtualShadowMapProjection.Stride);
            var pageTableBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageTableData.Length, sizeof(uint));
            var prunedOffsetsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var prunedIndicesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            var vsmInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(int) * 2);
            var punctualBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, VividLightData.PunctualLightData.Stride);
            var logBaseBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));

            try
            {
                var projUpload = new VirtualShadowMapProjection[registry.ProjectionCount];
                Array.Copy(registry.Projections, projUpload, registry.ProjectionCount);
                projBuf.SetData(projUpload);
                pageTableBuf.SetData(pageTableData);
                // 1 cluster containing both lights (offset=0, count=2).
                prunedOffsetsBuf.SetData(new uint[] { 2u << ClusterPackingOffsetBits });
                prunedIndicesBuf.SetData(new uint[] { 0u, 1u });
                vsmInfoBuf.SetData(new int2[] { new int2(0, 0), new int2(1, 0) });

                float cosOuter = Mathf.Cos(45f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(35f * Mathf.Deg2Rad);
                float angleScale = 1.0f / Mathf.Max(cosInner - cosOuter, 1e-4f);
                float angleOffset = -cosOuter * angleScale;
                punctualBuf.SetData(new[]
                {
                    new VividLightData.PunctualLightData
                    {
                        positionWS = new Vector3(0f, 5f, 0f),
                        range = 12f,
                        color = Vector3.one,
                        lightType = 1u, // Spot
                        directionWS = Vector3.down,
                        angleScale = angleScale,
                        angleOffset = angleOffset,
                        shadowStrength = 1.0f,
                    },
                    new VividLightData.PunctualLightData
                    {
                        positionWS = new Vector3(3f, 4f, 0f),
                        range = 12f,
                        color = Vector3.one,
                        lightType = 0u, // Point
                        directionWS = Vector3.down,
                        shadowStrength = 1.0f,
                    }
                });
                logBaseBuf.SetData(new float[] { 1.05f });

                var cmd = new CommandBuffer();
                cmd.SetComputeTextureParam(shader, kernel, "_DepthTexture", depthTex);
                cmd.SetComputeTextureParam(shader, kernel, "_GBuffer1", gbuf1Tex);
                cmd.SetComputeTextureParam(shader, kernel, "_VSMPhysicalPagePool", poolRt);
                cmd.SetComputeTextureParam(shader, kernel, VirtualShadowMapLocalShadowResolver.LocalShadowMaskBitsRWId, maskRt);
                cmd.SetComputeBufferParam(shader, kernel, "_VSMPrototypePageTable", pageTableBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapProjectionSet.BufferId, projBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightOffsetCountId, prunedOffsetsBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightIndicesId, prunedIndicesBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightVSMInfoId, vsmInfoBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightsId, punctualBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.LogBaseBufferId, logBaseBuf);

                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, registry.ProjectionCount);
                cmd.SetComputeIntParam(shader, "_VSMPrototypePageTableEntryCount", pageTableData.Length);
                cmd.SetComputeIntParam(shader, "_VSMPrototypePageSize", pageSize);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.ClusterCountId, 1);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.PunctualLightCountId, 2);
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
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTParametersId, new Vector4(1f, 0f, 50f, 33.3f));
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTSettingsId, new Vector4(0f, 0f, 0f, 0f));
                cmd.DispatchCompute(shader, kernel, 2, 2, 1);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Dispose();

                var readback = AsyncGPUReadback.Request(maskRt);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                var data = readback.GetData<uint4>();

                // Sky pixel (0,0) must remain 0xFFFFFFFFu across all 4 words.
                Assert.That(data[0], Is.EqualTo(new uint4(0xFFFFFFFFu)));

                // Center pixel (8,8): Spot (slot 0) occluded -> ~0.0, Point (slot 1) lit -> ~1.0.
                uint4 centerMask = data[8 * width + 8];
                Assert.That(Unpack4Bit(centerMask, 0), Is.LessThanOrEqualTo(0.1f), "Occluded Spot light must unpack near 0.");
                Assert.That(Unpack4Bit(centerMask, 1), Is.GreaterThanOrEqualTo(0.9f), "Unoccluded Point light must unpack near 1.");
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
                vsmInfoBuf.Dispose();
                punctualBuf.Dispose();
                logBaseBuf.Dispose();
            }
        }

        [Test]
        public void GPU_ResolveLocalShadows_PointCubeFaceBoundarySeamlessSMRT()
        {
            if (!VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform()
                || !VirtualShadowMapLocalShadowResolver.IsMaskFormatSupported())
            {
                Assert.Ignore("VSM local SMRT requires Reverse-Z, compute shaders, and R32G32B32A32_UInt UAV support.");
            }

            ComputeShader shader = LoadResolveShader();
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel(VirtualShadowMapLocalShadowResolver.ResolveLocalKernelName);
            Assert.That(kernel, Is.GreaterThanOrEqualTo(0));

            const int width = 8;
            const int height = 8;
            const int pageSize = 128;

            // Point light at origin with generous SourceRadius (0.5m) so SMRT rays span the +X / +Z cube face seam
            // for receivers placed right along the 45-degree diagonal (x = 4, y = 0, z = 4).
            Vector3 receiverWS = new Vector3(4.0f, 0.0f, 4.0f);
            var camView = Matrix4x4.LookAt(new Vector3(4.0f, 3.0f, 1.0f), receiverWS, Vector3.up).inverse;
            var worldToCam = Matrix4x4.Scale(new Vector3(1, 1, -1)) * camView;
            var camProj = Matrix4x4.Perspective(60f, 1f, 0.1f, 50f);
            var vp = GL.GetGPUProjectionMatrix(camProj, true) * worldToCam;
            var invVp = vp.inverse;

            var descriptors = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 201ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = new Vector3(0f, 2f, 0f),
                    Rotation = Quaternion.identity,
                    Range = 16f,
                    SpotAngle = 90f,
                    SourceRadius = 0.5f,
                    NormalBias = 0f,
                    ResolutionLodBias = 0f,
                    TexelDitherScale = 1f,
                    MaxResolution = 256,
                    MinMipLevel = 0u,
                    IsSinglePage = false,
                    UseReceiverMask = false,
                    PunctualLightIndex = 0u,
                }
            };

            var registry = new VirtualShadowMapRegistry();
            registry.PrepareFrame(2ul, null, descriptors, descriptors.Length);
            Assert.That(registry.ProjectionCount, Is.EqualTo(6));

            Vector4 clipPos = vp * new Vector4(receiverWS.x, receiverWS.y, receiverWS.z, 1f);
            float receiverDeviceDepth = clipPos.z / clipPos.w;

            var depthTex = new Texture2D(width, height, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
            var depthPixels = new float[width * height];
            for (int i = 0; i < depthPixels.Length; i++) depthPixels[i] = receiverDeviceDepth;
            depthTex.SetPixelData(depthPixels, 0);
            depthTex.Apply(false, true);

            var gbuf1Tex = new Texture2D(width, height, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None);
            var gbuf1Pixels = new Color[width * height];
            for (int i = 0; i < gbuf1Pixels.Length; i++) gbuf1Pixels[i] = new Color(0.5f, 1.0f, 0.0f, 1.0f);
            gbuf1Tex.SetPixels(gbuf1Pixels);
            gbuf1Tex.Apply(false, true);

            // Physical page (0,0) = completely empty (0.0 depth, all rays unoccluded across both +X and +Z faces).
            var poolRt = new RenderTexture(pageSize, pageSize, 0, GraphicsFormat.R32_UInt)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            poolRt.Create();

            var maskRt = new RenderTexture(width, height, 0, GraphicsFormat.R32G32B32A32_UInt)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
            };
            maskRt.Create();

            var pageTableData = new uint[registry.TotalPageTableEntries];
            for (int i = 0; i < pageTableData.Length; i++)
                pageTableData[i] = EncodeNativePageTable(0u, 0u);

            var projBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, registry.ProjectionCount, VirtualShadowMapProjection.Stride);
            var pageTableBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pageTableData.Length, sizeof(uint));
            var prunedOffsetsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var prunedIndicesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var vsmInfoBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int) * 2);
            var punctualBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.PunctualLightData.Stride);
            var logBaseBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));

            try
            {
                var projUpload = new VirtualShadowMapProjection[registry.ProjectionCount];
                Array.Copy(registry.Projections, projUpload, registry.ProjectionCount);
                projBuf.SetData(projUpload);
                pageTableBuf.SetData(pageTableData);
                prunedOffsetsBuf.SetData(new uint[] { 1u << ClusterPackingOffsetBits });
                prunedIndicesBuf.SetData(new uint[] { 0u });
                vsmInfoBuf.SetData(new int2[] { new int2(0, 0) });
                punctualBuf.SetData(new[]
                {
                    new VividLightData.PunctualLightData
                    {
                        positionWS = new Vector3(0f, 2f, 0f),
                        range = 16f,
                        color = Vector3.one,
                        lightType = 0u, // Point
                        shapeRadiusSquared = 0.25f, // 0.5m radius
                        shadowStrength = 1.0f,
                    }
                });
                logBaseBuf.SetData(new float[] { 1.05f });

                var cmd = new CommandBuffer();
                cmd.SetComputeTextureParam(shader, kernel, "_DepthTexture", depthTex);
                cmd.SetComputeTextureParam(shader, kernel, "_GBuffer1", gbuf1Tex);
                cmd.SetComputeTextureParam(shader, kernel, "_VSMPhysicalPagePool", poolRt);
                cmd.SetComputeTextureParam(shader, kernel, VirtualShadowMapLocalShadowResolver.LocalShadowMaskBitsRWId, maskRt);
                cmd.SetComputeBufferParam(shader, kernel, "_VSMPrototypePageTable", pageTableBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapProjectionSet.BufferId, projBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightOffsetCountId, prunedOffsetsBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PrunedLightIndicesId, prunedIndicesBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightVSMInfoId, vsmInfoBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.PunctualLightsId, punctualBuf);
                cmd.SetComputeBufferParam(shader, kernel, VirtualShadowMapLocalLightGrid.LogBaseBufferId, logBaseBuf);

                cmd.SetComputeIntParam(shader, VirtualShadowMapProjectionSet.CountId, registry.ProjectionCount);
                cmd.SetComputeIntParam(shader, "_VSMPrototypePageTableEntryCount", pageTableData.Length);
                cmd.SetComputeIntParam(shader, "_VSMPrototypePageSize", pageSize);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.ClusterCountId, 1);
                cmd.SetComputeIntParam(shader, VirtualShadowMapLocalLightGrid.PunctualLightCountId, 1);
                cmd.SetComputeIntParam(shader, "_CSMOutputWidth", width);
                cmd.SetComputeIntParam(shader, "_CSMOutputHeight", height);
                cmd.SetComputeIntParam(shader, "_CSMFrameIndex", 2);
                cmd.SetComputeMatrixParam(shader, "_CSMInvViewProjMatrix", invVp);
                cmd.SetComputeMatrixParam(shader, VirtualShadowMapLocalLightGrid.CameraViewMatrixId, worldToCam);
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.CameraViewParamsId, new Vector4(0.01f, 1f, 0f, 0f));
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterParamsId, new Vector4(64f, 1f, 1f, 1f));
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalLightGrid.ClusterDepthParamsId, new Vector4(0.1f, 50f, 1.05f, 0f));
                cmd.SetComputeVectorParam(shader, "_VSMReceiverOffsetParameters", Vector4.zero);
                cmd.SetComputeVectorParam(shader, "_VSMReceiverViewForward", -worldToCam.GetRow(2));
                // 8 rays x 4 samples/ray with dual-cube-face SMRT enabled.
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTParametersId, new Vector4(8f, 4f, 50f, 33.3f));
                cmd.SetComputeVectorParam(shader, VirtualShadowMapLocalShadowResolver.LocalSMRTSettingsId, new Vector4(5f, 1f, 2f, 0f));
                cmd.DispatchCompute(shader, kernel, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Dispose();

                var readback = AsyncGPUReadback.Request(maskRt);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                var data = readback.GetData<uint4>();
                for (int i = 0; i < data.Length; i++)
                {
                    float visibility = Unpack4Bit(data[i], 0);
                    Assert.That(visibility, Is.GreaterThanOrEqualTo(0.9f),
                        $"Cube-face boundary dual-face SMRT pixel {i} must stay seamlessly lit without seam artifacts.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depthTex);
                UnityEngine.Object.DestroyImmediate(gbuf1Tex);
                poolRt.Release();
                maskRt.Release();
                projBuf.Dispose();
                pageTableBuf.Dispose();
                prunedOffsetsBuf.Dispose();
                prunedIndicesBuf.Dispose();
                vsmInfoBuf.Dispose();
                punctualBuf.Dispose();
                logBaseBuf.Dispose();
            }
        }
    }
}
