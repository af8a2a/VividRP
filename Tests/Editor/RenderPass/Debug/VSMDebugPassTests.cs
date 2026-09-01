using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
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

            StringAssert.Contains("Texture2D<uint> _VSMPrototypePhysicalPage", source);
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
                Assert.That(node.TryGetExposure(out var exposure), Is.True);
                Assert.That(mode, Is.EqualTo(VSMDebugVisualizationMode.DeviceDepth));
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

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(
                    result.Passes[0].EnumParameters.Select(parameter => parameter.FieldName),
                    Is.EquivalentTo(new[] { "m_VisualizationMode" }));
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
