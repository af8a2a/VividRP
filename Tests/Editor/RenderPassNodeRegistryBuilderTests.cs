using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderPassNodeRegistryBuilderTests
    {
        private const string FullScreenPassTypeName = "VividRP.Runtime.FullScreenPass, VividRP.Runtime";

        [Serializable]
        private sealed class AutoRegisteredFullScreenPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => FullScreenPassTypeName;
        }

        [Test]
        public void GetPassType_ReturnsRegisteredPassType_WhenNodeIsAutoRegistered()
        {
            var node = new AutoRegisteredFullScreenPassNode();

            Assert.That(node.UsesPassScriptSelection, Is.False);
            Assert.That(node.GetPassType(), Is.EqualTo(typeof(FullScreenPass)));
        }

        [Test]
        public void BuildRegistrations_PreservesExistingClassName_WhenPassAlreadyKnown()
        {
            var existingRegistration = new RenderPassNodeRegistration("SavedFullScreenPassNode", FullScreenPassTypeName);

            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(FullScreenPass) },
                new[] { existingRegistration });

            Assert.That(registrations, Has.Count.EqualTo(1));
            Assert.That(registrations[0].NodeClassName, Is.EqualTo("SavedFullScreenPassNode"));
            Assert.That(registrations[0].PassTypeName, Is.EqualTo(FullScreenPassTypeName));
        }

        [Test]
        public void BuildRegistrations_GeneratesDistinctNodeNames_WhenPassNamesCollide()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[]
                {
                    typeof(NameCollisionA.CollisionPass),
                    typeof(NameCollisionB.CollisionPass),
                },
                includeTestAssemblies: true);

            var nodeClassNames = registrations.Select(item => item.NodeClassName).ToArray();

            Assert.That(registrations, Has.Count.EqualTo(2));
            Assert.That(nodeClassNames.Distinct().Count(), Is.EqualTo(2));
            Assert.That(nodeClassNames.Count(name => name.EndsWith("CollisionPass", StringComparison.Ordinal)), Is.EqualTo(2));
        }

        [Test]
        public void BuildSource_AndParseExistingRegistrations_RoundTripRegistrations()
        {
            var expected = new[]
            {
                new RenderPassNodeRegistration("FullScreenPass", FullScreenPassTypeName),
                new RenderPassNodeRegistration("SetupPass", "VividRP.Runtime.RenderPass.Core.SetupPass, VividRP.Runtime"),
            };

            var source = RenderPassNodeRegistryBuilder.BuildSource(expected);
            var parsed = RenderPassNodeRegistryBuilder.ParseExistingRegistrations(source);

            Assert.That(parsed.Select(item => item.NodeClassName), Is.EqualTo(expected.Select(item => item.NodeClassName)));
            Assert.That(parsed.Select(item => item.PassTypeName), Is.EqualTo(expected.Select(item => item.PassTypeName)));
        }

        private static class NameCollisionA
        {
            public sealed class CollisionPass : RasterPass
            {
                public override void Create()
                {
                }

                public override void Prepare(ContextContainer frameData)
                {
                }

                public override void Record(RasterGraphContext context)
                {
                }

                public override void Dispose()
                {
                }
            }
        }

        private static class NameCollisionB
        {
            public sealed class CollisionPass : RasterPass
            {
                public override void Create()
                {
                }

                public override void Prepare(ContextContainer frameData)
                {
                }

                public override void Record(RasterGraphContext context)
                {
                }

                public override void Dispose()
                {
                }
            }
        }
    }
}
