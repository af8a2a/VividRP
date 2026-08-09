using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class HDRISkyRendererTests
    {

        [Test]
        public void GetSkyHash_DoesNotAllocate_ForDefaultHdriPath()
        {
            var renderer = new HDRISkyRenderer();
            var context = new SkyRendererContext(new VividCameraData(), new VividLightData());

            renderer.GetSkyHash(context);
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 64; i++)
                renderer.GetSkyHash(context);

            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }
    }
}
