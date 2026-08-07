using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTexturePageTableScatterTests
    {
        [Test]
        public void ScatterUpdate_MatchesUint2ShaderLayout()
        {
            var update = new VTPageTableScatterUpdate(17, 0x8f00aa55u);

            Assert.That(Marshal.SizeOf<VTPageTableScatterUpdate>(), Is.EqualTo(sizeof(uint) * 2));
            Assert.That(update.DestinationIndex, Is.EqualTo(17u));
            Assert.That(update.PackedValue, Is.EqualTo(0x8f00aa55u));
        }

        [Test]
        public void ScatterCompute_UsesBoundedUint2Writes()
        {
            string source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel ScatterPageTableUpdates"));
            Assert.That(source, Does.Contain("#pragma use_dxc"));
            Assert.That(source, Does.Contain("StructuredBuffer<uint2> _VTPageTableUpdates;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _VTPageTableDestination;"));
            Assert.That(source, Does.Contain("[numthreads(64, 1, 1)]"));
            Assert.That(source, Does.Contain("localUpdateIndex >= _VTPageTableUpdateCount"));
            Assert.That(source, Does.Contain("_VTPageTableDestination[update.x] = update.y;"));
        }

        [Test]
        public void ScatterCompute_WritesKnownPackedValuesToRequestedIndices()
        {
            Assume.That(SystemInfo.supportsComputeShaders, Is.True);
            ComputeShader shader = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.VirtualTexturePageTableScatterCompute;
            Assert.That(shader, Is.Not.Null);

            int kernel = shader.FindKernel("ScatterPageTableUpdates");
            var updates = new[]
            {
                new VTPageTableScatterUpdate(5, 0x11111111u),
                new VTPageTableScatterUpdate(1, 0x89abcdefu),
                new VTPageTableScatterUpdate(6, 0xfedcba98u),
            };
            var destinationValues = new uint[8];
            using var updateBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                updates.Length,
                VTPageTableScatterUpdate.Stride);
            using var destinationBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                destinationValues.Length,
                sizeof(uint));
            updateBuffer.SetData(updates);
            destinationBuffer.SetData(destinationValues);

            shader.SetBuffer(kernel, "_VTPageTableUpdates", updateBuffer);
            shader.SetBuffer(kernel, "_VTPageTableDestination", destinationBuffer);
            shader.SetInt("_VTPageTableUpdateBase", 1);
            shader.SetInt("_VTPageTableUpdateCount", 2);
            shader.Dispatch(kernel, 1, 1, 1);
            destinationBuffer.GetData(destinationValues);

            Assert.That(destinationValues[1], Is.EqualTo(0x89abcdefu));
            Assert.That(destinationValues[6], Is.EqualTo(0xfedcba98u));
            Assert.That(destinationValues[5], Is.Zero);
        }

        [Test]
        public void ScatterUploader_RecordsNonCullableGraphicsQueuePassWithExplicitDependencies()
        {
            string source = File.ReadAllText(GetPackageSourcePath(
                "Runtime",
                "SubSystem",
                "VirtualTexture",
                "Core",
                "VTPageTableScatterUploader.cs"));

            Assert.That(source, Does.Contain("renderGraph.AddComputePass<ScatterPassData>"));
            Assert.That(source, Does.Contain("builder.UseBuffer(destinationHandle, AccessFlags.Write);"));
            Assert.That(source, Does.Contain("builder.AllowPassCulling(false);"));
            Assert.That(source, Does.Contain("builder.EnableAsyncCompute(false);"));
            Assert.That(source, Does.Contain("context.cmd.SetBufferData("));
        }

        [Test]
        public void PipelineResources_ResolvePageTableScatterCompute()
        {
            ComputeShader shader = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.VirtualTexturePageTableScatterCompute;

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.HasKernel("ScatterPageTableUpdates"), Is.True);
        }

        private static string GetComputeShaderSourcePath()
        {
            return GetPackageSourcePath(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VirtualTexturePageTableScatter.compute");
        }

        private static string GetPackageSourcePath(params string[] relativeSegments)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
            };

            for (int packageIndex = 0; packageIndex < packageRoots.Length; packageIndex++)
            {
                string path = Path.Combine(packageRoots[packageIndex], Path.Combine(relativeSegments));
                if (File.Exists(path))
                    return path;
            }

            Assert.Fail($"Could not locate {Path.Combine(relativeSegments)} in a known package root.");
            return null;
        }
    }
}
