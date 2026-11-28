using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapUnifiedPhase0Tests
    {
        private const string ProductionShader = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";

        [Test]
        public void UnifiedStructs_MatchHLSLSizeAndFieldOffsets()
        {
            Assert.That(Marshal.SizeOf<VirtualShadowMapProjection>(), Is.EqualTo(256));
            Assert.That(VirtualShadowMapProjection.Stride, Is.EqualTo(256));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.WorldToClip)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.WorldToShadow)).ToInt32(), Is.EqualTo(64));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.SelectionSphere)).ToInt32(), Is.EqualTo(128));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.Parameters)).ToInt32(), Is.EqualTo(144));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.LightDirectionAndSpot)).ToInt32(), Is.EqualTo(160));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.ProjectionScales)).ToInt32(), Is.EqualTo(176));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.Addressing)).ToInt32(), Is.EqualTo(192));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.Meta)).ToInt32(), Is.EqualTo(208));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.IdentityAndOrigin)).ToInt32(), Is.EqualTo(224));
            Assert.That(Marshal.OffsetOf<VirtualShadowMapProjection>(nameof(VirtualShadowMapProjection.Extended)).ToInt32(), Is.EqualTo(240));

            Assert.That(Marshal.SizeOf<VividVSMNextMapEntry>(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<VividVSMNextMapEntry>(nameof(VividVSMNextMapEntry.NextVSMId)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<VividVSMNextMapEntry>(nameof(VividVSMNextMapEntry.PageOffsetX)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<VividVSMNextMapEntry>(nameof(VividVSMNextMapEntry.PageOffsetY)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<VividVSMNextMapEntry>(nameof(VividVSMNextMapEntry.Flags)).ToInt32(), Is.EqualTo(12));

            Assert.That(Marshal.SizeOf<VividVSMActiveView>(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<VividVSMActiveView>(nameof(VividVSMActiveView.VSMId)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<VividVSMActiveView>(nameof(VividVSMActiveView.MipLevel)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<VividVSMActiveView>(nameof(VividVSMActiveView.VirtualResolution)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<VividVSMActiveView>(nameof(VividVSMActiveView.BoundRectIndex)).ToInt32(), Is.EqualTo(12));
        }

        [Test]
        public void Addressing_MipChainAndOwnerDescriptorRoundTrip()
        {
            Assert.That(VirtualShadowMapAddressing.CalculateMipCount(32), Is.EqualTo(6));
            Assert.That(VirtualShadowMapAddressing.CalculateTotalMipChainEntries(32), Is.EqualTo(1365));
            Assert.That(VirtualShadowMapAddressing.CalculateMipEntryOffset(32, 0), Is.EqualTo(0));
            Assert.That(VirtualShadowMapAddressing.CalculateMipEntryOffset(32, 1), Is.EqualTo(1024));
            Assert.That(VirtualShadowMapAddressing.CalculateMipEntryOffset(32, 5), Is.EqualTo(1364));

            // Coarsest mip of a 32x32 pyramid sits at base + 1364.
            Assert.That(VirtualShadowMapAddressing.CalculatePageIndex(100u, 32u, false, 5u, uint2.zero), Is.EqualTo(1464u));
            // Single-page VSM always maps directly to baseOffset.
            Assert.That(VirtualShadowMapAddressing.CalculatePageIndex(42u, 1u, true, 0u, new uint2(7u, 9u)), Is.EqualTo(42u));

            VividVSMOwnerFlags flags = VividVSMOwnerFlags.Valid | VividVSMOwnerFlags.DetailGeometry | VividVSMOwnerFlags.Remapped;
            uint2 encoded = VirtualShadowMapAddressing.EncodePhysicalOwnerDesc(317u, 5u, new uint2(19u, 27u), flags);
            VirtualShadowMapAddressing.DecodePhysicalOwnerDesc(encoded, out uint vsmId, out uint mip, out uint2 pageXY, out VividVSMOwnerFlags decodedFlags);
            Assert.That(vsmId, Is.EqualTo(317u));
            Assert.That(mip, Is.EqualTo(5u));
            Assert.That(pageXY, Is.EqualTo(new uint2(19u, 27u)));
            Assert.That(decodedFlags, Is.EqualTo(flags));
        }

        [Test]
        public void PointFaceMatrices_AgreeWithGetCubeFaceAndProjectCenters()
        {
            Vector3 lightPos = new Vector3(3.5f, -2.0f, 7.25f);
            float range = 25.0f;
            Vector3[] probeDirs =
            {
                new Vector3(1.0f, 0.2f, -0.15f),
                new Vector3(-1.0f, -0.25f, 0.1f),
                new Vector3(0.1f, 1.0f, -0.2f),
                new Vector3(-0.15f, -1.0f, 0.25f),
                new Vector3(0.2f, -0.1f, 1.0f),
                new Vector3(-0.2f, 0.15f, -1.0f),
            };

            for (int expectedFace = 0; expectedFace < VirtualShadowMapRegistry.PointFaceCount; expectedFace++)
            {
                Vector3 dir = probeDirs[expectedFace].normalized;
                Assert.That(VirtualShadowMapAddressing.GetCubeFace(dir), Is.EqualTo(expectedFace));

                VirtualShadowMapRegistry.BuildPointFaceMatrices(lightPos, range, expectedFace,
                    out Matrix4x4 view, out Matrix4x4 proj, out float nearPlane);
                Assert.That(nearPlane, Is.InRange(0.02f, 0.2f));

                Matrix4x4 worldToShadow = VividShadowData.BuildWorldToShadowMatrix(proj, view);
                Vector3 samplePos = lightPos + dir * 5.0f;
                Vector4 shadowH = worldToShadow * new Vector4(samplePos.x, samplePos.y, samplePos.z, 1.0f);
                Assert.That(shadowH.w, Is.GreaterThan(0.0f));
                Vector3 uvw = new Vector3(shadowH.x / shadowH.w, shadowH.y / shadowH.w, shadowH.z / shadowH.w);
                Assert.That(uvw.x, Is.InRange(0.0f, 1.0f));
                Assert.That(uvw.y, Is.InRange(0.0f, 1.0f));
                Assert.That(uvw.z, Is.InRange(0.0f, 1.0f));
            }
        }

        [Test]
        public void Registry_IsolatesPerLightInvalidationAndAllocatesZeroInSteadyState()
        {
            var registry = new VirtualShadowMapRegistry();
            var clipmaps = new VirtualShadowMapClipmapLayout();
            clipmaps.Update(Vector3.zero, Quaternion.Euler(45f, 30f, 0f),
                new Bounds(Vector3.zero, Vector3.one * 50f), 100f, 512, 6, 1f, 1ul, 10ul);

            var locals = new[]
            {
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 100ul,
                    LightType = VividVSMLightType.Point,
                    PositionWS = new Vector3(2f, 3f, 4f),
                    Rotation = Quaternion.identity,
                    Range = 15f,
                    SourceRadius = 0.2f,
                    MaxResolution = 1024,
                    UseReceiverMask = true,
                    PunctualLightIndex = 0u,
                },
                new VividVSMLocalLightDescriptor
                {
                    LightEntityId = 200ul,
                    LightType = VividVSMLightType.Spot,
                    PositionWS = new Vector3(-5f, 4f, 1f),
                    Rotation = Quaternion.Euler(30f, 0f, 0f),
                    Range = 20f,
                    SpotAngle = 60f,
                    MaxResolution = 1024,
                    UseReceiverMask = true,
                    PunctualLightIndex = 1u,
                },
            };

            // Frame 1: initial registration.
            registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
            registry.CommitRecordedFrame();
            int dirCount = registry.DirectionalCount;
            Assert.That(registry.ProjectionCount, Is.EqualTo(dirCount + 6 + 1));

            // Frame 2: move ONLY the point light. Directional and Spot must remain valid.
            locals[0].PositionWS = new Vector3(3f, 3f, 4f);
            registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
            Assert.That(registry.RequiresRemap, Is.True);
            for (int d = 0; d < dirCount; d++)
                Assert.That(registry.NextMap[d].Flags & (uint)VividVSMNextMapFlags.Valid, Is.Not.Zero);
            for (int f = 0; f < 6; f++)
                Assert.That(registry.NextMap[dirCount + f].Flags & (uint)VividVSMNextMapFlags.InvalidateAll, Is.Not.Zero);
            Assert.That(registry.NextMap[dirCount + 6].Flags & (uint)VividVSMNextMapFlags.Valid, Is.Not.Zero);
            Assert.That(registry.NextMap[dirCount + 6].NextVSMId, Is.EqualTo((uint)(dirCount + 6)));
            registry.CommitRecordedFrame();

            // Frame 3: remove the point light; Spot shifts from (dirCount + 6) to (dirCount) and preserves cache.
            var spotOnly = new[] { locals[1] };
            registry.PrepareFrame(1ul, clipmaps, spotOnly, 1);
            Assert.That(registry.ProjectionCount, Is.EqualTo(dirCount + 1));
            Assert.That(registry.NextMap[dirCount + 6].Flags & (uint)VividVSMNextMapFlags.Valid, Is.Not.Zero);
            Assert.That(registry.NextMap[dirCount + 6].NextVSMId, Is.EqualTo((uint)dirCount));
            registry.CommitRecordedFrame();

            // Steady-state zero-GC verification.
            for (int w = 0; w < 16; w++)
            {
                registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
                registry.CommitRecordedFrame();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
            {
                registry.PrepareFrame(1ul, clipmaps, locals, locals.Length);
                registry.CommitRecordedFrame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void GPU_PropagateMappedMips_PropagatesLocalMipChainAndClearsStaleAliases()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProductionShader);
            Assert.That(source, Is.Not.Null);
            var shader = Object.Instantiate(source);

            // One local VSM with 8x8 mip0 pages (4 mips: 64 + 16 + 4 + 1 = 85 entries).
            const int axis = 8;
            const int totalEntries = 85;
            using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VirtualShadowMapProjection.Stride);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalEntries, sizeof(uint));
            using var offsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
            try
            {
                var proj = new VirtualShadowMapProjection
                {
                    Addressing = new uint4(0u, 0u, axis, 4u),
                    Meta = new uint4((uint)VividVSMLightType.Spot, 0u, 0u, 0u),
                };
                projections.SetData(new[] { proj });
                offsets.SetData(new[] { int2.zero });

                // Map mip3 (coarsest root, slot 5) and mip1 page (1, 0) (slot 9). Seed stale aliases elsewhere.
                var entries = new uint[totalEntries];
                for (int i = 0; i < totalEntries; i++) entries[i] = 0x8030007Fu; // Stale alias
                uint rootIndex = VirtualShadowMapAddressing.CalculatePageIndex(0u, axis, false, 3u, uint2.zero);
                uint mip1Index = VirtualShadowMapAddressing.CalculatePageIndex(0u, axis, false, 1u, new uint2(1u, 0u));
                entries[rootIndex] = 0x80000005u; // Native slot (5, 0)
                entries[mip1Index] = 0x80000009u; // Native slot (9, 0)
                table.SetData(entries);

                int kernel = shader.FindKernel("VSMPropagateMappedMips");
                shader.SetInt("_VSMProjectionCount", 1);
                shader.SetBuffer(kernel, "_VSMProjections", projections);
                shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(kernel, "_VSMClipmapPageOffsets", offsets);
                shader.Dispatch(kernel, 1, 1, 1);

                var actual = new uint[totalEntries];
                table.GetData(actual);

                // Mip0 pages under mip1(1,0) -> (2..3, 0..1) must inherit slot 9 with LODOffset = 1.
                for (uint y = 0; y < 2; y++)
                for (uint x = 2; x < 4; x++)
                {
                    uint idx = VirtualShadowMapAddressing.CalculatePageIndex(0u, axis, false, 0u, new uint2(x, y));
                    Assert.That(actual[idx], Is.EqualTo(0x80000000u | (1u << 20) | 9u));
                }
                // Mip0 page (0, 0) has no native mip1/mip2 parent, so it must inherit mip3 root slot 5 with LODOffset = 3.
                uint corner0 = VirtualShadowMapAddressing.CalculatePageIndex(0u, axis, false, 0u, uint2.zero);
                Assert.That(actual[corner0], Is.EqualTo(0x80000000u | (3u << 20) | 5u));
                // Native entries remain untouched.
                Assert.That(actual[rootIndex], Is.EqualTo(0x80000005u));
                Assert.That(actual[mip1Index], Is.EqualTo(0x80000009u));
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }
    }
}
