using System;
using System.Linq;
using NUnit.Framework;
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
    public sealed class ReferencedPathTracingEnvironmentSamplingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredEnvironmentSamplingPassNode
            : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() =>
                typeof(ReferencedPathTracingEnvironmentSamplingPass);
        }

        [Test]
        public void Initialize_RegistersEnvironmentInputAndPersistentDistributionOutput()
        {
            IRenderPass renderPass =
                new ReferencedPathTracingEnvironmentSamplingPass();

            var resources = renderPass.Initialize();
            var environment = resources.Textures.Single();
            var distribution = resources.Buffers.Single();

            Assert.That(environment.Name, Is.EqualTo("PathTracingEnvironment"));
            Assert.That(environment.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(environment.Texture.desc.Dimension, Is.EqualTo(TextureDimension.Cube));
            Assert.That(
                environment.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(distribution.Name, Is.EqualTo("EnvironmentImportanceDistribution"));
            Assert.That(distribution.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                distribution.Buffer.desc.Count,
                Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.ElementCount));
            Assert.That(
                distribution.Buffer.desc.Stride,
                Is.EqualTo(sizeof(float)));
            Assert.That(
                distribution.Buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        [Test]
        public void Layout_PacksHdriCdfsAndAtmosphereOpticalDepthWithoutOverlap()
        {
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout.MarginalOffset,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout.HeaderElementCount));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout.ConditionalOffset,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout.MarginalOffset
                    + ReferencedPathTracingEnvironmentImportanceLayout.MarginalResolution));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout
                    .EnvironmentElementCount,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout.ConditionalOffset
                    + ReferencedPathTracingEnvironmentImportanceLayout.ConditionalResolution
                    * ReferencedPathTracingEnvironmentImportanceLayout.MarginalResolution));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout.ConditionalResolution,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout.MarginalResolution * 2));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereValidOffset,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout
                        .EnvironmentElementCount));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout
                    .AtmosphereDataOffset,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout
                        .EnvironmentElementCount
                    + ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereHeaderElementCount));
            Assert.That(
                ReferencedPathTracingEnvironmentImportanceLayout.ElementCount,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereDataOffset
                    + ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereRadialResolution
                    * ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereZenithResolution
                    * ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereChannelCount));
        }

        [Test]
        public void ShaderContract_BuildsReferenceAtmosphereOpticalDepthLut()
        {
            var packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(
                        ReferencedPathTracingEnvironmentSamplingPass)
                        .Assembly);
            Assert.That(packageInfo, Is.Not.Null);
            var packageRoot = packageInfo.resolvedPath;
            var computeSource = System.IO.File.ReadAllText(
                System.IO.Path.Combine(
                    packageRoot,
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingEnvironmentSampling.compute"));
            var commonSource = System.IO.File.ReadAllText(
                System.IO.Path.Combine(
                    packageRoot,
                    "Shaders",
                    "Core",
                    "Private",
                    "GlobalIllumination",
                    "ReferencedPathtracing",
                    "ReferencedPathtracingCommon.hlsl"));

            Assert.That(
                computeSource,
                Does.Contain(
                    "#pragma kernel ComputeAtmosphereOpticalDepth"));
            Assert.That(
                computeSource,
                Does.Contain(
                    "ReferencedPathtracingIntegrateAtmosphereDensityReference"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingIntersectAtmospherePlanetSpace"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereDensity"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereTransmittanceLut"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereTransmittanceReference"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateAtmosphereTransmittanceRelativeError"));
        }

        [Test]
        public void RenderGraphNode_DefinesEnvironmentInputAndDistributionOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredEnvironmentSamplingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(
                    node.GetInputPortByName("m_EnvironmentTexture"),
                    Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName(
                        "m_EnvironmentImportanceDistribution"),
                    Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
