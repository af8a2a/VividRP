using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapClipmapTests
    {
        private static void Update(VirtualShadowMapClipmapLayout layout, Vector3 position,
            ulong camera = 1, ulong light = 2, Quaternion? rotation = null, Bounds? bounds = null)
        {
            layout.Update(position, rotation ?? Quaternion.identity,
                bounds ?? new Bounds(Vector3.zero, Vector3.one * 100), 150, 512, 2, 1, camera, light);
        }

        [Test]
        public void Layout_UsesPowerOfTwoLevelsAndStablePageAlignedXY()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Update(layout, new Vector3(0.1f, 0.1f, 0));
            Assert.That(layout.Count, Is.EqualTo(8));
            Assert.That(layout.Radii[0], Is.EqualTo(4));
            Assert.That(layout.Radii[7], Is.EqualTo(512));
            Matrix4x4 view = layout.Views[0];
            Update(layout, new Vector3(1.9f, 1.9f, 0));
            Assert.That(layout.Views[0], Is.EqualTo(view));
            Update(layout, new Vector3(2.1f, -0.1f, 0));
            Assert.That(layout.OriginX[0], Is.EqualTo(-1));
            Assert.That(layout.OriginY[0], Is.EqualTo(-3));
            Assert.That(layout.Views[0].m03, Is.EqualTo(view.m03 - 2));
            Assert.That(layout.Views[0].m13, Is.EqualTo(view.m13 + 2));
        }

        [Test]
        public void Layout_RetainedPageHasIdenticalLocalTexelAndDepthAfterScroll()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Quaternion rotation = Quaternion.Euler(35, 23, 0);
            Update(layout, rotation * new Vector3(0.1f, 0.1f, 0), rotation: rotation);
            Matrix4x4 before = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            long x = layout.OriginX[0], y = layout.OriginY[0];
            Update(layout, rotation * new Vector3(2.1f, -0.1f, 0), rotation: rotation);
            Matrix4x4 after = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[0], layout.Views[0]);
            Vector3 point = rotation * new Vector3(1.25f, -1.25f, 8);
            Vector3 a = before.MultiplyPoint3x4(point), b = after.MultiplyPoint3x4(point);
            Assert.That((a.x - b.x) * 512, Is.EqualTo((layout.OriginX[0] - x) * 128).Within(0.001));
            Assert.That((a.y - b.y) * 512, Is.EqualTo((layout.OriginY[0] - y) * 128).Within(0.001));
            Assert.That(b.z, Is.EqualTo(a.z).Within(1e-7));
        }

        [Test]
        public void Layout_DepthIntervalSurvivesShrinkButExpandsOnEscape()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            Update(layout, Vector3.zero);
            float min = layout.DepthMin, max = layout.DepthMax;
            Update(layout, new Vector3(10, -10, 30), bounds: new Bounds(Vector3.zero, Vector3.one));
            Assert.That(layout.DepthMin, Is.EqualTo(min));
            Assert.That(layout.DepthMax, Is.EqualTo(max));
            Update(layout, new Vector3(0, 0, 2000));
            Assert.That(layout.DepthMax, Is.GreaterThanOrEqualTo(2150));
            Assert.That(layout.DepthMin, Is.LessThanOrEqualTo(-50));
            Assert.That(layout.DepthMax, Is.Not.EqualTo(max));
        }

        [Test]
        public void ProjectionHistory_AdvancesOnlyWhenRecordedAndResetsOnBasisChanges()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.one * 0.1f);
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresFeedbackReset, Is.True);
            projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            Update(layout, new Vector3(2.1f, 0.1f, 0));
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.RequiresFeedbackReset, Is.False);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            // Skipping Record must not hide the same pending scroll next frame.
            projections.Reset();
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.True);
            Assert.That(projections.LayoutRecorded, Is.False);
            projections.CommitRecordedLayout();
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresRemap, Is.False);
            Update(layout, new Vector3(100, 0, 0));
            projections.PrepareClipmaps(layout);
            Assert.That(projections.RequiresFeedbackReset, Is.True);
            Assert.That(projections.Generation, Is.EqualTo(generation));
            for (int basis = 0; basis < 4; basis++)
            {
                Update(layout, basis == 3 ? new Vector3(0, 0, 4000) : Vector3.zero,
                    camera: basis == 0 ? 3ul : 1ul, light: basis == 1 ? 4ul : 2ul,
                    rotation: basis == 2 ? Quaternion.Euler(10, 20, 0) : Quaternion.identity);
                projections.PrepareClipmaps(layout);
                Assert.That(projections.RequiresFeedbackReset, Is.True);
                Assert.That(projections.Generation, Is.EqualTo(generation + 1));
            }
        }

        [Test]
        public void CacheKey_ClipmapGenerationAllowsMoreThanFourLevelsAndTracksNonProjectionInputs()
        {
            var key = Key();
            Assert.That(key.IsValid, Is.True);
            Assert.That(key.Equals(Key()), Is.True);
            Assert.That(key.Equals(Key(generation: 2)), Is.False);
            Assert.That(key.Equals(Key(mask: 1)), Is.False);
            Assert.That(key.Equals(Key(lod: 2)), Is.False);
            Assert.That(key.Equals(Key(error: 2)), Is.False);
        }

        [Test]
        public void ReceiverQualityChanges_DoNotRemapOrInvalidateRecordedCasterGeometry()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            Update(layout, Vector3.zero);
            projections.PrepareClipmaps(layout);
            projections.CommitRecordedLayout();
            ulong generation = projections.Generation;
            var buffer = projections.Buffer;
            layout.Update(Vector3.zero, Quaternion.identity, new Bounds(Vector3.zero, Vector3.one * 100),
                150, 512, 2, 3, 1, 2, transitionFraction: 0.4f);
            projections.PrepareClipmaps(layout);
            Assert.That(layout.NormalBias, Is.EqualTo(3));
            Assert.That(layout.BlendBorder, Is.EqualTo(0.2f));
            Assert.That(projections.Generation, Is.EqualTo(generation));
            Assert.That(projections.RequiresRemap, Is.False);
            Assert.That(projections.RequiresFeedbackReset, Is.False);
            Assert.That(projections.Buffer, Is.SameAs(buffer));
        }

        private static VirtualShadowMapPrototypeCacheKey Key(ulong generation = 1, int mask = -1,
            int lod = -1, float error = 1)
            => new(1, 1, 1, 1, 8, 512, lod, error, 1, new Vector4(0, 0, 1, 0), mask, generation);

        [TestCase(1, 0)]
        [TestCase(-1, 1)]
        [TestCase(4, 0)]
        [TestCase(0, 0, true)]
        public void Remap_PreservesFeedbackAndSlotsAndAllocatorReusesHoles(int dx, int dy, bool resetBasis = false)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute");
            Assert.That(source, Is.Not.Null);
            ComputeShader shader = Object.Instantiate(source);
            try
            {
                // Eight levels exercise indexing above the legacy four-cascade limit.
                const int count = 128, capacity = 4;
                var tableData = new uint[count];
                var metadataData = new uint4[count];
                tableData[0] = 1;
                tableData[10] = 4;
                tableData[117] = 3;
                for (int i = 0; i < count; i++)
                    metadataData[i] = new uint4(tableData[i] == 0 ? 0u : 11u, tableData[i], 7, 8);
                using var previous = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
                using var previousMetadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
                using var remap = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 16);
                previous.SetData(tableData);
                previousMetadata.SetData(metadataData);
                var remaps = new int4[8];
                for (int i = 0; i < 8; i++) remaps[i] = new int4(dx, dy, resetBasis ? 1 : 0, 0);
                remap.SetData(remaps);
                int reset = shader.FindKernel("VSMResetPhysicalOwners");
                int move = shader.FindKernel("VSMRemapPages");
                int allocate = shader.FindKernel("VSMPrototypeAllocatePages");
                shader.SetInt("_VSMProjectionCount", 8);
                shader.SetInt("_VSMPrototypePageTableEntryCount", count);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 7);
                shader.SetBuffer(reset, "_VSMPrototypePhysicalPageOwners", owners);
                shader.Dispatch(reset, 1, 1, 1);
                shader.SetBuffer(move, "_VSMPreviousPageTable", previous);
                shader.SetBuffer(move, "_VSMPreviousPageMetadata", previousMetadata);
                shader.SetBuffer(move, "_VSMProjectionRemap", remap);
                shader.SetBuffer(move, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(move, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(move, "_VSMPrototypePhysicalPageOwners", owners);
                shader.Dispatch(move, 2, 1, 1);
                var actual = new uint[count];
                var actualMetadata = new uint4[count];
                var actualOwners = new uint[capacity];
                table.GetData(actual);
                metadata.GetData(actualMetadata);
                owners.GetData(actualOwners);
                for (int dest = 0; dest < count; dest++)
                {
                    int x = dest % 4 + dx, y = dest % 16 / 4 + dy;
                    bool inside = !resetBasis && x >= 0 && x < 4 && y >= 0 && y < 4;
                    int src = dest / 16 * 16 + y * 4 + x;
                    Assert.That(actual[dest], Is.EqualTo(inside ? tableData[src] : 0u));
                    Assert.That(actualMetadata[dest], Is.EqualTo(inside ? metadataData[src] : uint4.zero));
                    if (actual[dest] != 0)
                        Assert.That(actualOwners[actual[dest] - 1], Is.EqualTo((uint)dest + 1));
                }
                int free = System.Array.IndexOf(actualOwners, 0u);
                int missing = System.Array.IndexOf(actual, 0u);
                Assert.That(free, Is.GreaterThanOrEqualTo(0));
                actualMetadata[missing] = new uint4(1, 0, 7, 0);
                metadata.SetData(actualMetadata);
                shader.SetBuffer(allocate, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(allocate, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(allocate, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(allocate, "_VSMPrototypeAllocatorCounters", counters);
                shader.Dispatch(allocate, 1, 1, 1);
                var allocated = new uint[count];
                table.GetData(allocated);
                Assert.That(allocated[missing], Is.EqualTo((uint)free + 1));
                for (int i = 0; i < count; i++)
                    if (actual[i] != 0) Assert.That(allocated[i], Is.EqualTo(actual[i]));
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
