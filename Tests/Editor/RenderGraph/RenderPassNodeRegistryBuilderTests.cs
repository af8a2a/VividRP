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
        [Test]
        public void BuildRegistrations_IncludesAutoRegistrablePassType()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(FullScreenPass) });

            Assert.That(registrations, Has.Count.EqualTo(1));
            Assert.That(registrations[0].NodeClassName, Is.EqualTo("FullScreenPass"));
            Assert.That(registrations[0].PassType, Is.EqualTo(typeof(FullScreenPass)));
        }

        [Test]
        public void BuildRegistrations_ExcludesAbstractPassTypes()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(RasterPass) });

            Assert.That(registrations, Is.Empty);
        }

        [Test]
        public void BuildRegistrations_ExcludesObsoletePassTypes()
        {
#pragma warning disable CS0618
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(DeprecatedPass) },
                includeTestAssemblies: true);
#pragma warning restore CS0618

            Assert.That(registrations, Is.Empty);
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
        public void BuildSource_AndParseExistingClassNames_RoundTripClassNames()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(FullScreenPass) });

            var source = RenderPassNodeRegistryBuilder.BuildSource(registrations);
            var parsed = RenderPassNodeRegistryBuilder.ParseExistingClassNames(source);

            Assert.That(parsed, Is.EqualTo(registrations.Select(item => item.NodeClassName).ToArray()));
        }

        [Test]
        public void BuildSource_GeneratesEmptyMarkerClasses()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                new[] { typeof(FullScreenPass) });

            var source = RenderPassNodeRegistryBuilder.BuildSource(registrations);

            Assert.That(source, Does.Contain("internal sealed class FullScreenPass : RenderPassNodeData { }"));
            Assert.That(source, Does.Not.Contain("RegisteredPassTypeName"));
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

                public override void Record(RasterPassContext context)
                {
                }

                public override void Dispose()
                {
                }
            }
        }

        [Obsolete("Only used to verify deprecated pass filtering.")]
        public sealed class DeprecatedPass : RasterPass
        {
            public override void Create()
            {
            }

            public override void Prepare(ContextContainer frameData)
            {
            }

            public override void Record(RasterPassContext context)
            {
            }

            public override void Dispose()
            {
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

                public override void Record(RasterPassContext context)
                {
                }

                public override void Dispose()
                {
                }
            }
        }
    }
}
