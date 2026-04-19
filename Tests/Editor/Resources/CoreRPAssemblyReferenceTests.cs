using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering;

namespace VividRP.Editor.Tests
{
    public class CoreRPAssemblyReferenceTests
    {
        [Test]
        public void MarkerType_IsCompiledIntoCoreRuntime_WhenUsingCoreRPAsmRef()
        {
            Assembly coreRuntimeAssembly = typeof(CoreUtils).Assembly;

            Type markerType = coreRuntimeAssembly.GetType("UnityEngine.Rendering.VividCoreRPExtensionsAssemblyMarker");

            Assert.That(markerType, Is.Not.Null);
            Assert.That(markerType?.Assembly.GetName().Name, Is.EqualTo("Unity.RenderPipelines.Core.Runtime"));
        }
    }
}
