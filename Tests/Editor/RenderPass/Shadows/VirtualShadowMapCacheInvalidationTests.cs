using System;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.PrimitiveScene;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapCacheInvalidationTests
    {
        private const string ShaderPath = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const uint Allocated = 2, StaticDirty = 4, Cached = 8, DynamicDirty = 1u << 15;
        private static VirtualShadowMapInvalidationSource Source(float x, uint revision = 1, uint flags = 3, uint generation = 1)
            => new() { BoundsMin = new float4(x, .1f, .2f, 0), BoundsMax = new float4(x + .04f, .14f, .3f, 0),
                State = new uint4(revision, generation, flags, uint.MaxValue) };

        [Test]
        public void Queue_PreservesOldFootprintAndMergesMaterialAndPoolTransitions()
        {
            using var gpu = new Harness(1);
            var data = new[] { Source(.1f) };
            gpu.Update(data, true);
            Assert.That(gpu.Count, Is.Zero);
            data[0] = Source(.6f, 2, 1);
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(2));
            var items = gpu.ReadQueue();
            Assert.That(items[0].BoundsMin.x, Is.EqualTo(.1f));
            Assert.That(items[0].State.x, Is.EqualTo(StaticDirty));
            Assert.That(items[1].BoundsMin.x, Is.EqualTo(.6f));
            Assert.That(items[1].State.x, Is.EqualTo(DynamicDirty));
            data[0] = Source(.6f, 3, 3);
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(1));
            Assert.That(gpu.ReadQueue()[0].State.x, Is.EqualTo(StaticDirty | DynamicDirty));
            data[0].State.x++;
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(1)); // Material/VT change with identical bounds.
            gpu.Update(data);
            Assert.That(gpu.Count, Is.Zero);
        }

        [Test]
        public void Queue_RemovalSlotReuseAndCameraFilteringDoNotLoseOldDepth()
        {
            using var gpu = new Harness(1);
            var data = new[] { Source(.1f) };
            gpu.Update(data, true);
            data[0] = Source(.7f, 1, 1, 2); // Remove+add before any GPU consumption.
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(2));
            data[0] = default;
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(1));
            Assert.That(gpu.ReadQueue()[0].BoundsMin.x, Is.EqualTo(.7f));
            gpu.Update(data);
            Assert.That(gpu.Count, Is.Zero);
            data[0] = Source(.4f, 1, 3, 3); data[0].State.w = 2;
            gpu.Update(data, cameraMask: 1);
            Assert.That(gpu.Count, Is.Zero);
            gpu.Update(data, cameraMask: 2);
            Assert.That(gpu.Count, Is.EqualTo(1));
        }

        [Test]
        public void Queue_DeformationSwitchControlsContinuousInvalidation()
        {
            using var gpu = new Harness(1);
            var data = new[] { Source(.1f, flags: 5) };
            gpu.Update(data, true);
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(1));
            Assert.That(gpu.ReadQueue()[0].State.x, Is.EqualTo(DynamicDirty));
            gpu.Update(data, deformable: false);
            Assert.That(gpu.Count, Is.Zero);
            data[0].State.x++;
            gpu.Update(data, deformable: false);
            Assert.That(gpu.Count, Is.EqualTo(1)); // Still respects explicit edits.
        }

        [TestCase(false, true, true, 1u)]
        [TestCase(true, false, true, 1u)]
        [TestCase(true, true, false, 1u)]
        [TestCase(true, true, true, 0u)]
        public void HZB_OnlyValidStaticOcclusionSuppressesInvalidation(bool enabled, bool history, bool mapped, uint expected)
        {
            using var gpu = new Harness(1);
            var data = new[] { Source(.1f, flags: 5) };
            gpu.Update(data, true); gpu.Update(data);
            gpu.Process(enabled, history, mapped);
            Assert.That(gpu.ReadMetadata()[0].x & DynamicDirty, Is.EqualTo(expected * DynamicDirty));
            Assert.That(gpu.Counters[5], Is.EqualTo(1));
            Assert.That(gpu.Counters[6], Is.EqualTo(1 - expected));
            Assert.That(gpu.Counters[7], Is.EqualTo(expected));
        }

        [Test]
        public void HZB_VisibleAndCoplanarBoundsInvalidate_UnboundedNeverCull()
        {
            using var gpu = new Harness(1);
            var data = new[] { Source(.1f, flags: 5) };
            data[0].BoundsMin.z = .8f; data[0].BoundsMax.z = .8f;
            gpu.Update(data, true); gpu.Update(data);
            gpu.Process(true, true, true);
            Assert.That(gpu.ReadMetadata()[0].x & DynamicDirty, Is.Not.Zero);
            data[0] = Source(.1f, 2, 13); // Deforming with untrusted bounds.
            gpu.Update(data);
            gpu.Process(true, true, true);
            foreach (var page in gpu.ReadMetadata()) Assert.That(page.x & DynamicDirty, Is.Not.Zero);
        }

        [Test]
        public void LoadBalancer_DenseQueueExceedsWorkerCountAndTouchesUnrequestedPages()
        {
            const int count = 513;
            using var gpu = new Harness(count);
            var data = new VirtualShadowMapInvalidationSource[count];
            for (int i = 0; i < count; i++) data[i] = Source(.1f, flags: 1);
            gpu.Update(data, true);
            for (int i = 0; i < count; i++) data[i] = Source(.6f, 2, 1);
            gpu.Update(data);
            Assert.That(gpu.Count, Is.EqualTo(count * 2));
            gpu.Process(false, false, false);
            Assert.That(gpu.Counters[0], Is.EqualTo(512));
            Assert.That(gpu.Counters[7], Is.EqualTo(count * 2));
            var pages = gpu.ReadMetadata();
            // Metadata intentionally has no current-frame request flag.
            Assert.That(pages[0].x & DynamicDirty, Is.Not.Zero);
            Assert.That(pages[2].x & DynamicDirty, Is.Not.Zero);
            Assert.That(pages[1].x & DynamicDirty, Is.Zero);
        }

        [Test]
        public void ProductionOwner_IncrementalUploadStableAllocationAndAbortRecovery()
        {
            using var scene = new VividPrimitiveScene();
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
            using var cmd = new CommandBuffer();
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(UnsafeUtility.SizeOf<VirtualShadowMapInvalidationSource>(), Is.EqualTo(48));
                Assert.That(settings.virtualShadowMapCacheInvalidateUseHZB.value, Is.True);
                Assert.That(settings.virtualShadowMapCacheDeformableMeshesInvalidate.value, Is.True);
                Assert.That(settings.virtualShadowMapCacheFramesStaticThreshold.value, Is.EqualTo(100));
                scene.ShadowInvalidationTable.Set(0, Source(.1f));
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
                VirtualShadowMapCacheInvalidation.Record(cmd, true);
                Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
                VirtualShadowMapCacheInvalidation.CompleteFrame();
                var sourceBuffer = VirtualShadowMapCacheInvalidation.Sources;
                for (int i = 0; i < 32; i++) VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.False);
                Assert.That(VirtualShadowMapCacheInvalidation.Sources, Is.SameAs(sourceBuffer));
                scene.ShadowInvalidationTable.Set(0, Source(.4f, 2));
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                var read = new VirtualShadowMapInvalidationSource[1];
                sourceBuffer.GetData(read, 0, 0, 1);
                Assert.That(read[0].BoundsMin.x, Is.EqualTo(.4f));
                VirtualShadowMapCacheInvalidation.AbortFrame();
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
                VirtualShadowMapCacheInvalidation.Record(cmd, true); Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
                VirtualShadowMapCacheInvalidation.CompleteFrame();
                scene.InvalidateAllShadows();
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
                // Preparing a culled/aborted pass must not acknowledge a global epoch.
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
                scene.ShadowInvalidationTable.Set(8, Source(.8f));
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
                Assert.That(VirtualShadowMapCacheInvalidation.Sources.count, Is.EqualTo(16));
                VirtualShadowMapCacheInvalidation.Record(cmd, true);
                Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
                // Simulate an exception after state consumption, without a completion callback.
                VirtualShadowMapCacheInvalidation.Prepare(scene, shader, uint.MaxValue, true, true);
                Assert.That(VirtualShadowMapCacheInvalidation.NeedsFullRefresh, Is.True);
            }
            finally { VirtualShadowMapCacheInvalidation.Dispose(); Object.DestroyImmediate(settings); }
        }

        private sealed class Harness : IDisposable
        {
            private readonly ComputeShader shader;
            private readonly GraphicsBuffer sources, states, queue, args, dispatchArgs, metadata, projections, table;
            private readonly Texture2DArray hzb;
            private readonly int count, reset, update, prepare, process;
            internal readonly uint[] Counters = new uint[8];
            internal uint Count => Counters[3];
            internal Harness(int count)
            {
                this.count = count;
                shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
                reset = shader.FindKernel("VSMResetInvalidationQueue");
                update = shader.FindKernel("VSMUpdateInvalidationInstances");
                prepare = shader.FindKernel("VSMPrepareInvalidationQueue");
                process = shader.FindKernel("VSMProcessInvalidationQueue");
                sources = new(GraphicsBuffer.Target.Structured, count, 48);
                states = new(GraphicsBuffer.Target.Structured, count, 48);
                queue = new(GraphicsBuffer.Target.Structured, count * 2, 48);
                args = new(GraphicsBuffer.Target.Raw, 8, 4);
                dispatchArgs = new(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 3, 4);
                metadata = new(GraphicsBuffer.Target.Structured, 16, 16);
                projections = new(GraphicsBuffer.Target.Structured, 1, 160);
                projections.SetData(new[] { new VirtualShadowMapProjection { WorldToShadow = Matrix4x4.identity } });
                table = new(GraphicsBuffer.Target.Structured, 16, 4);
                hzb = new(256, 256, 2, TextureFormat.RFloat, true, true);
                for (int mip = 0; mip < hzb.mipmapCount; mip++)
                {
                    int size = Math.Max(1, 256 >> mip);
                    var colors = new Color[size * size];
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color(.8f, 0, 0, 0);
                    hzb.SetPixels(colors, 1, mip);
                }
                hzb.Apply(false, false);
                shader.SetInt("_VSMProjectionCount", 1);
                shader.SetInt("_VSMPrototypeVirtualResolution", 512);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 16);
                shader.SetInt("_VSMPrototypePageTableEntryCount", 16);
                shader.SetBuffer(reset, "_VSMInvalidationArgs", args);
                shader.SetBuffer(update, "_VSMInvalidationSources", sources);
                shader.SetBuffer(update, "_VSMInvalidationStates", states);
                shader.SetBuffer(update, "_VSMInvalidationQueue", queue);
                shader.SetBuffer(update, "_VSMInvalidationArgs", args);
                shader.SetBuffer(prepare, "_VSMInvalidationArgs", args);
                shader.SetBuffer(prepare, "_VSMInvalidationDispatchArgs", dispatchArgs);
                shader.SetBuffer(process, "_VSMInvalidationQueue", queue);
                shader.SetBuffer(process, "_VSMInvalidationArgs", args);
                shader.SetBuffer(process, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(process, "_VSMProjections", projections);
                shader.SetBuffer(process, "_VSMHZBPreviousTable", table);
                shader.SetTexture(process, "_VSMHZB", hzb);
            }
            internal void Update(VirtualShadowMapInvalidationSource[] data, bool initial = false, bool deformable = true, uint cameraMask = uint.MaxValue)
            {
                sources.SetData(data);
                shader.SetInt("_VSMInvalidationSourceCount", count);
                shader.SetInt("_VSMInvalidationReset", initial ? 1 : 0);
                shader.SetInt("_VSMDeformableMeshesInvalidate", deformable ? 1 : 0);
                shader.SetInt("_VSMInvalidationCameraMask", unchecked((int)cameraMask));
                shader.Dispatch(reset, 1, 1, 1);
                shader.Dispatch(update, (count + 63) / 64, 1, 1);
                args.GetData(Counters);
            }
            internal VirtualShadowMapInvalidationSource[] ReadQueue()
            {
                var data = new VirtualShadowMapInvalidationSource[count * 2]; queue.GetData(data); return data;
            }
            internal void Process(bool enabled, bool history, bool mapped)
            {
                var pages = new uint4[16]; var mappings = new uint[16];
                for (int i = 0; i < 16; i++) { pages[i].x = Allocated | Cached; mappings[i] = mapped ? (uint)i + 1u : 0u; }
                metadata.SetData(pages); VirtualShadowMapPageTableTestData.UploadSlots(shader, table, mappings);
                shader.SetInt("_VSMInvalidateUseHZB", enabled ? 1 : 0);
                shader.SetInt("_VSMHZBHistoryValid", history ? 1 : 0);
                shader.Dispatch(prepare, 1, 1, 1);
                shader.DispatchIndirect(process, dispatchArgs, 0);
                args.GetData(Counters);
            }
            internal uint4[] ReadMetadata() { var data = new uint4[16]; metadata.GetData(data); return data; }
            public void Dispose()
            {
                sources.Dispose(); states.Dispose(); queue.Dispose(); args.Dispose(); dispatchArgs.Dispose(); metadata.Dispose();
                projections.Dispose(); table.Dispose(); Object.DestroyImmediate(hzb); Object.DestroyImmediate(shader);
            }
        }
    }
}
