using System;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.VirtualShadowMap;

namespace VividRP.Editor.Tests
{
    public class VirtualShadowMapGeometryBoundsTests
    {
        private static VividGPUDrivenSceneData Geometry()
        {
            var scene = new VividGPUDrivenSceneData();
            // Nonzero offsets and an unused outlier distinguish indexed bounds
            // from bounds over an entire shared vertex array.
            foreach (float3 p in new[] { new float3(999), new float3(-4, 2, 1),
                new float3(6, 2, 1), new float3(1, 2.01f, 1), new float3(-999) })
                scene.MutableVertices.Add(new VividMeshletVertex { Position = p });
            scene.MutableIndices.AddRange(new byte[] { 255, 0, 1, 2 });
            scene.MutableMeshlets.Add(new VividMeshlet { VertexOffset = 1, TriangleOffset = 1,
                VertexCount = 4, TriangleCount = 1, BoundingSphere = new float4(1, 2, 1, 8) });
            scene.MutableMeshLODNodes.Add(new VividMeshLODNode { MeshletCount = 1,
                Bounds = new float4(1, 2, 1, 12), Error = .1f, ParentError = .35f,
                ParentBounds = new float4(1, 2, 1, 14), LevelIndex = 2 });
            scene.MutableInstances.Add(new VividInstanceData { TotalMeshLODCount = 1, MeshLODLevelCount = 3 });
            return scene;
        }

        [Test]
        public void IndexedBounds_TightenSpaceButPreserveErrorDomainAndSerializedSpheres()
        {
            var scene = Geometry(); var original = scene.MeshLODNodes[0];
            using var bounds = new VirtualShadowMapGeometryBounds();
            using var oldTree = new VirtualShadowMapLODHierarchy();
            using var tree = new VirtualShadowMapLODHierarchy();
            bounds.Update(scene, true);
            oldTree.Update(scene.MeshLODNodes, scene.Instances, true);
            tree.Update(scene.MeshLODNodes, scene.Instances, true, bounds.CpuLodNodes);
            Assert.That(bounds.Meshlets.stride, Is.EqualTo(32));
            var box = bounds.CpuMeshlets[0]; Assert.That(box.IsValid, Is.True);
            for (int i = 1; i <= 3; i++)
                Assert.That(math.all(math.abs(scene.Vertices[i].Position - box.Center.xyz) <= box.Extent.xyz), Is.True);
            Assert.That(box.Extent.x, Is.LessThan(5.001f));
            Assert.That(box.Extent.y, Is.LessThan(.006f));
            Assert.That(tree.CpuNodes[0].Max.y - tree.CpuNodes[0].Min.y, Is.LessThan(.02f));
            Assert.That(tree.CpuNodes[0].ErrorBounds, Is.EqualTo(oldTree.CpuNodes[0].ErrorBounds));
            Assert.That(tree.CpuNodes[0].ErrorMagnitude, Is.EqualTo(oldTree.CpuNodes[0].ErrorMagnitude));
            Assert.That(scene.MeshLODNodes[0].Bounds, Is.EqualTo(original.Bounds));
            Assert.That(scene.MeshLODNodes[0].PackedParentErrorRadius, Is.EqualTo(original.PackedParentErrorRadius));
        }

        [TestCase(0)] // invalid index
        [TestCase(1)] // incomplete triangle
        [TestCase(2)] // nonfinite position
        [TestCase(3)] // invalid LOD range
        [TestCase(4)] // overflowing offset
        public void InvalidGeometry_FallsBackWithoutTighteningIncompleteLod(int kind)
        {
            var scene = Geometry();
            if (kind == 0) scene.MutableIndices[2] = 255;
            if (kind == 1) scene.MutableIndices.RemoveAt(3);
            if (kind == 2) scene.MutableVertices[2] = new VividMeshletVertex { Position = new float3(float.NaN) };
            if (kind == 3) { var node = scene.MutableMeshLODNodes[0]; node.MeshletCount = 2; scene.MutableMeshLODNodes[0] = node; }
            if (kind == 4) { var meshlet = scene.MutableMeshlets[0]; meshlet.VertexOffset = uint.MaxValue; scene.MutableMeshlets[0] = meshlet; }
            using var bounds = new VirtualShadowMapGeometryBounds();
            bounds.Update(scene, true);
            Assert.That(bounds.CpuLodNodes[0].IsValid, Is.False);
            if (kind != 3) Assert.That(bounds.CpuMeshlets[0].IsValid, Is.False);
            using var tree = new VirtualShadowMapLODHierarchy();
            tree.Update(scene.MeshLODNodes, scene.Instances, true, bounds.CpuLodNodes);
            Assert.That(tree.CpuNodes[0].Min.xyz, Is.EqualTo(scene.MeshLODNodes[0].Bounds.xyz - scene.MeshLODNodes[0].Bounds.w));
        }

        [Test]
        public void StableBounds_AllocateNothing_AndGeometryUploadRefreshesSameCount()
        {
            var scene = Geometry(); using var buffers = new VividGPUDrivenBufferSet();
            buffers.Upload(scene);
            var bounds = buffers.VSMGeometryBounds; var meshlets = bounds.Meshlets; var lod = bounds.LodNodes;
            for (int i = 0; i < 32; i++) bounds.Update(scene, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) bounds.Update(scene, false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            var vertex = scene.MutableVertices[2]; vertex.PositionY = 7; scene.MutableVertices[2] = vertex;
            buffers.Upload(scene, false, false, true);
            Assert.That(bounds.Meshlets, Is.SameAs(meshlets)); Assert.That(bounds.LodNodes, Is.SameAs(lod));
            Assert.That(bounds.CpuLodNodes[0].Center.y + bounds.CpuLodNodes[0].Extent.y, Is.GreaterThanOrEqualTo(7));
            Assert.That(buffers.VSMLODHierarchy.CpuNodes[0].Max.y, Is.GreaterThanOrEqualTo(7));
        }
    }
}
