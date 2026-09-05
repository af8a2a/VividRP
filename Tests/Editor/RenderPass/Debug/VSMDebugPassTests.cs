using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VSMDebugPassTests
    {
        [TestCase(VSMDebugVisualizationMode.PageStates)]
        [TestCase(VSMDebugVisualizationMode.Requested)]
        [TestCase(VSMDebugVisualizationMode.Allocated)]
        [TestCase(VSMDebugVisualizationMode.Dirty)]
        [TestCase(VSMDebugVisualizationMode.Cached)]
        [TestCase(VSMDebugVisualizationMode.Unmapped)]
        [TestCase(VSMDebugVisualizationMode.Evicted)]
        [TestCase(VSMDebugVisualizationMode.Overflow)]
        public void PageModes_SerializeAndNormalize(VSMDebugVisualizationMode mode)
        {
            var pass = new VSMDebugPass();
            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(pass, typeof(VSMDebugPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new() { FieldName = "m_VisualizationMode", Value = (int)mode },
                });
            Assert.That(pass.VisualizationMode, Is.EqualTo(mode));
            Assert.That(VSMDebugPass.IsPageStateMode(mode), Is.True);
            pass.Dispose();
        }

        [Test]
        public void PageSnapshot_RequiresCurrentCameraAndCompletedFrame()
        {
            const ulong camera = 0x10000002aul;
            try
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                VirtualShadowMapPrototypeRuntime.MarkPageDebugSnapshot(camera, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 10), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkActive();
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(0x20000002aul, 10), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 11), Is.False);
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                VirtualShadowMapPrototypeRuntime.MarkActive();
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 10), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkPageDebugSnapshot(camera, 11);
                VirtualShadowMapPrototypeRuntime.MarkFallback(VirtualShadowMapPrototypeFallbackReason.ReceiverFeedbackUnavailable);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 11), Is.False);
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
                VirtualShadowMapPrototypeRuntime.MarkActive();
                Assert.That(VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(camera, 11), Is.False);
            }
            finally { VirtualShadowMapPrototypeRuntime.BeginFrame(); }
        }

        [Test]
        public void PageDebugBindings_AllocateZeroBytesAfterWarmup()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            try
            {
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsurePhysicalPageForBinding(), Is.True);
                var properties = new MaterialPropertyBlock();
                VirtualShadowMapPrototypeRuntime.MarkActive();
                VirtualShadowMapPrototypeRuntime.MarkPageDebugSnapshot(42ul, 10);
                for (int i = 0; i < 32; i++) VSMDebugPass.BindPageStateResources(properties);
                long before = GC.GetAllocatedBytesForCurrentThread();
                int hits = 0;
                for (int i = 0; i < 256; i++)
                {
                    VSMDebugPass.BindPageStateResources(properties);
                    if (VSMDebugPass.IsPageStateMode(VSMDebugVisualizationMode.PageStates)
                        && VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(42ul, 10)) hits++;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(hits, Is.EqualTo(256));
                Assert.That(allocated, Is.Zero);
            }
            finally { VirtualShadowMapPrototypeRuntime.ReleaseResources(); }
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void PageShader_RendersStatesAndOverflowWithoutDepthTextures(int mode)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            Shader shader = Shader.Find(VSMDebugPass.VSMDebugShaderName);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var target = new RenderTexture(new RenderTextureDescriptor(512, 512)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                depthStencilFormat = GraphicsFormat.None,
            });
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 12, 16);
            using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            var tableData = new uint[] { 0, 1, 2, 3, 0, 0, 4, 5, 0, 0, 0, 0 };
            // x is live state (including NEXT receiver feedback); w is the completed submission.
            var flags = new uint[] { 0, 2, 10, 11, 0, 0, 11, 6, 2, 1, 0, 0 };
            var snapshot = new uint[] { 0, 2, 10, 7, 64, 129, 11, 6, 0, 0, 0, 0 };
            var metadataData = new uint4[12];
            for (int i = 0; i < 12; i++) metadataData[i] = new uint4(flags[i], tableData[i], 11u, snapshot[i]);
            table.SetData(tableData); metadata.SetData(metadataData); counters.SetData(new uint[] { 3, 1, 2, 1 });
            try
            {
                target.Create();
                ShaderUtil.CompilePass(material, 1, true);
                var properties = new MaterialPropertyBlock();
                properties.SetBuffer("_VSMPrototypePageTable", table);
                properties.SetBuffer("_VSMPrototypePageMetadata", metadata);
                properties.SetBuffer("_VSMPrototypeAllocatorCounters", counters);
                properties.SetInt("_VSMDebugVisualizationMode", mode);
                properties.SetVector("_VSMDebugPageLayout", new Vector4(2, 12, 8, 0));
                properties.SetVector("_VSMDebugOutputSize", new Vector4(512, 512, 0, 0));
                using var command = new CommandBuffer();
                command.SetRenderTarget(target);
                command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, properties);
                Graphics.ExecuteCommandBuffer(command);
                var readback = AsyncGPUReadback.Request(target);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                var pixels = readback.GetData<Color>();
                int[] combinedStates = { 8, 5, 7, 6, 9, 10, 7, 6, 8, 8, 8, 8 };
                for (int page = 0; page < 16; page++)
                {
                    int cascade = page / 4;
                    int x = (cascade % 2) * 256 + (page % 2) * 128 + 64;
                    int y = 26 + (cascade / 2) * 243 + ((page % 4) / 2) * 121 + 60;
                    Color expected = new Color(0.015f, 0.015f, 0.015f, 1);
                    if (page < 12)
                    {
                        bool mapped = tableData[page] != 0 && (flags[page] & 2) != 0;
                        bool highlighted = mode == 3
                            || (mode == 4 && (snapshot[page] & 1) != 0)
                            || (mode == 5 && mapped)
                            || (mode == 6 && mapped && (snapshot[page] & 4) != 0)
                            || (mode == 7 && mapped && (snapshot[page] & 8) != 0 && (snapshot[page] & 4) == 0)
                            || (mode == 8 && !mapped)
                            || (mode == 9 && (snapshot[page] & 64) != 0)
                            || (mode == 10 && (snapshot[page] & 128) != 0);
                        if (highlighted) expected = PageColor(mode == 3 ? combinedStates[page] : mode);
                    }
                    AssertColor(pixels[y * 512 + x], expected);
                }
                // First lit pixel of the final digit in each GPU counter, and overflow bar.
                int[] counterModes = { 5, 4, 6, 10 };
                for (int column = 0; column < 4; column++)
                    AssertColor(pixels[3 * 512 + column * 128 + 33], PageColor(counterModes[column]));
                AssertColor(pixels[10 * 512 + 384 + 8], PageColor(10));
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [TestCase(8)]
        [TestCase(16)]
        public void PageShader_DisplaysEveryClipmapLevel(int levels)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var material = new Material(Shader.Find(VSMDebugPass.VSMDebugShaderName));
            var target = new RenderTexture(new RenderTextureDescriptor(512, 512)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                depthStencilFormat = GraphicsFormat.None,
            });
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels, 16);
            using var counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            var tableData = new uint[levels];
            var states = new uint4[levels];
            for (int i = 0; i < levels; i++)
            {
                tableData[i] = (uint)i + 1;
                states[i] = new uint4(2, tableData[i], 1, i % 2 == 0 ? 4u : 8u);
            }
            table.SetData(tableData);
            metadata.SetData(states);
            counters.SetData(new uint[] { (uint)levels, 0, 0, 0 });
            try
            {
                target.Create();
                var properties = new MaterialPropertyBlock();
                properties.SetBuffer("_VSMPrototypePageTable", table);
                properties.SetBuffer("_VSMPrototypePageMetadata", metadata);
                properties.SetBuffer("_VSMPrototypeAllocatorCounters", counters);
                properties.SetInt("_VSMDebugVisualizationMode", (int)VSMDebugVisualizationMode.PageStates);
                properties.SetVector("_VSMDebugPageLayout", new Vector4(1, levels, levels, 0));
                properties.SetVector("_VSMDebugOutputSize", new Vector4(512, 512, 0, 0));
                using var command = new CommandBuffer();
                command.SetRenderTarget(target);
                command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, properties);
                Graphics.ExecuteCommandBuffer(command);
                var readback = AsyncGPUReadback.Request(target);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                var pixels = readback.GetData<Color>();
                int columns = Mathf.CeilToInt(Mathf.Sqrt(levels));
                int rows = (levels + columns - 1) / columns;
                for (int i = 0; i < columns * rows; i++)
                {
                    int x = (int)((i % columns + 0.5f) * 512 / columns);
                    int y = 26 + (int)((i / columns + 0.5f) * 486 / rows);
                    Color expected = i < levels ? PageColor(i % 2 == 0 ? 6 : 7)
                        : new Color(0.015f, 0.015f, 0.015f, 1);
                    AssertColor(pixels[y * 512 + x], expected);
                }
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Color PageColor(int state)
        {
            return state switch
            {
                4 => new Color(1f, 0.85f, 0.1f, 1f),
                5 => new Color(0.1f, 1f, 0.3f, 1f),
                6 => new Color(1f, 0.45f, 0.05f, 1f),
                7 => new Color(0.1f, 0.55f, 1f, 1f),
                8 => new Color(0.3f, 0.3f, 0.3f, 1f),
                9 => new Color(0.7f, 0.25f, 1f, 1f),
                _ => new Color(1f, 0.05f, 0.1f, 1f),
            };
        }

        [Test]
        public void PageShader_NoSnapshotUsesHatchingWithoutPageBuffers()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var material = new Material(Shader.Find(VSMDebugPass.VSMDebugShaderName));
            var target = new RenderTexture(new RenderTextureDescriptor(16, 16)
            {
                graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                depthStencilFormat = GraphicsFormat.None,
            });
            try
            {
                target.Create();
                var properties = new MaterialPropertyBlock();
                properties.SetInt("_VSMPrototypeAvailable", 0);
                properties.SetInt("_VSMDebugVisualizationMode", (int)VSMDebugVisualizationMode.PageStates);
                properties.SetTexture("_VSMPrototypeStaticPhysicalPage", Texture2D.blackTexture);
                properties.SetTexture("_VSMPrototypeDynamicPhysicalPage", Texture2D.blackTexture);
                using var command = new CommandBuffer();
                command.SetRenderTarget(target);
                command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, properties);
                Graphics.ExecuteCommandBuffer(command);
                var readback = AsyncGPUReadback.Request(target);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                var pixels = readback.GetData<Color>();
                AssertColor(pixels[0], new Color(0.06f, 0.06f, 0.06f, 1f));
                AssertColor(pixels[8], new Color(0.3f, 0.16f, 0.04f, 1f));
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.01f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.01f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.01f));
            Assert.That(actual.a, Is.EqualTo(1f).Within(0.01f));
        }

        [Serializable]
        private sealed class DebugOutputNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(FinalBlitPass);
        }

        [Serializable]
        private sealed class AutoRegisteredVSMDebugPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VSMDebugPass);

            internal bool TryGetVisualizationMode(out VSMDebugVisualizationMode value)
            {
                return TryGetEnumParameterValue("m_VisualizationMode", out value);
            }

            internal bool TryGetExposure(out float value)
            {
                return TryGetFloatParameterValue("m_Exposure", out value);
            }

            internal bool TryGetPoolMode(out VSMDebugPoolMode value)
            {
                return TryGetEnumParameterValue("m_PoolMode", out value);
            }
        }

        [Test]
        public void Initialize_RegistersStandaloneColorOutput()
        {
            IRenderPass renderPass = new VSMDebugPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(resources.Textures[0].Name, Is.EqualTo("OutputTexture"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures[0].AttachmentIndex, Is.EqualTo(0));
            Assert.That(
                resources.Textures[0].Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void ApplySerializedParameters_UpdatesVisualizationModeAndExposure()
        {
            var pass = new VSMDebugPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(VSMDebugPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_VisualizationMode",
                        Value = (int)VSMDebugVisualizationMode.Occupancy,
                    },
                    new()
                    {
                        FieldName = "m_PoolMode",
                        Value = (int)VSMDebugPoolMode.Dynamic,
                    },
                });
            RenderGraphPassFloatParameterUtility.ApplyFloatParameters(
                pass,
                typeof(VSMDebugPass),
                new List<RenderGraphPassFloatParameter>
                {
                    new()
                    {
                        FieldName = "m_Exposure",
                        Value = 2f,
                    },
                });

            Assert.That(pass.VisualizationMode, Is.EqualTo(VSMDebugVisualizationMode.Occupancy));
            Assert.That(pass.PoolMode, Is.EqualTo(VSMDebugPoolMode.Dynamic));
            Assert.That(pass.Exposure, Is.EqualTo(2f));
        }

        [Test]
        public void Shader_LoadsAtomicDepthBitsFromPrototypePhysicalPage()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(VSMDebugPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "VSMDebug.shader");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);

            StringAssert.Contains("Texture2D<uint> _VSMPrototypeStaticPhysicalPage", source);
            StringAssert.Contains("Texture2D<uint> _VSMPrototypeDynamicPhysicalPage", source);
            StringAssert.Contains("max(staticRawDepth, dynamicRawDepth)", source);
            StringAssert.Contains("asfloat(rawDepth)", source);
            StringAssert.Contains("VIVID_VSM_DEBUG_OCCUPANCY", source);
        }

        [Test]
        public void Node_DefinesOutputAndInspectorParameters()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVSMDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
                Assert.That(node.TryGetVisualizationMode(out var mode), Is.True);
                Assert.That(node.TryGetPoolMode(out var poolMode), Is.True);
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(mode, Is.EqualTo(VSMDebugVisualizationMode.DeviceDepth));
                Assert.That(poolMode, Is.EqualTo(VSMDebugPoolMode.Combined));
                Assert.That(exposure, Is.EqualTo(0f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_PersistsVisualizationModeAndExposure()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVSMDebugPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);
                var output = new DebugOutputNode();
                RenderGraphTestUtility.AddTestNode(graph, output);
                graph.Connect(node.GetOutputPortByName("m_OutputTexture"), output.GetInputPortByName("source"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(2));
                Assert.That(
                    result.Passes[0].EnumParameters.Select(parameter => parameter.FieldName),
                    Is.EquivalentTo(new[] { "m_VisualizationMode", "m_PoolMode" }));
                Assert.That(
                    result.Passes[0].FloatParameters.Select(parameter => parameter.FieldName),
                    Is.EquivalentTo(new[] { "m_Exposure" }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
