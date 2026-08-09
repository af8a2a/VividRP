using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingIndirectShaderTests
    {

        [Test]
        public void GeneratedNodeRegistry_ContainsDeferredLightingPassNode()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[]
            {
                typeof(DeferredLightingPass),
                typeof(DeferredDirectionalLightingPass),
            });

            Assert.That(
                registrations.Any(registration =>
                    registration.NodeClassName == nameof(DeferredLightingPass)
                    && registration.PassType == typeof(DeferredLightingPass)),
                Is.True);
            Assert.That(
                registrations.Any(registration =>
                    registration.NodeClassName == nameof(DeferredDirectionalLightingPass)
                    && registration.PassType == typeof(DeferredDirectionalLightingPass)),
                Is.True);
            Assert.That(
                registrations.Any(registration => registration.NodeClassName.Contains("PreIntegratedFGD")),
                Is.False);
        }
    }
}
