using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    /// <summary>
    /// Tests for CullingUtility.ExtractFrustumPlanes and FrustumCullingJob.
    ///
    /// Plane convention after extraction (Gribb/Hartmann):
    ///   plane.xyz = outward-facing normal (pointing AWAY from frustum interior)
    ///   plane.w   = signed distance from origin
    ///   A point P is INSIDE the half-space when: dot(plane.xyz, P) + plane.w >= 0
    ///   An AABB is CULLED when its farthest point in the plane-normal direction is still < 0.
    /// </summary>
    public sealed class FrustumCullingTests
    {
        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Builds a minimal orthographic view-projection matrix (row-major convention,
        /// stored column-major as Unity.Mathematics expects):
        ///   maps [left,right] x [bottom,top] x [near,far] -> [-1,1]^3
        /// </summary>
        private static float4x4 OrthoVP(float left, float right,
                                         float bottom, float top,
                                         float near, float far)
        {
            // Standard OpenGL ortho, stored column-major.
            float rml = right - left;
            float tmb = top - bottom;
            float fmn = far - near;

            // Column-major: each float4 is a column.
            return new float4x4(
                new float4(2f / rml, 0, 0, 0),
                new float4(0, 2f / tmb, 0, 0),
                new float4(0, 0, -2f / fmn, 0),
                new float4(-(right + left) / rml, -(top + bottom) / tmb, -(far + near) / fmn, 1)
            );
        }

        private static AABB MakeAABB(float cx, float cy, float cz,
                                      float ex, float ey, float ez)
        {
            return new AABB
            {
                Center  = new float4(cx, cy, cz, 0f),
                Extents = new float4(ex, ey, ez, 0f),
            };
        }

        // ------------------------------------------------------------------ ExtractFrustumPlanes

        [Test]
        public void ExtractFrustumPlanes_OutputArrayLength_IsSix()
        {
            var vp = OrthoVP(-1, 1, -1, 1, 0.1f, 100f);
            using var planes = new NativeArray<float4>(6, Allocator.Temp);
            CullingUtility.ExtractFrustumPlanes(vp, planes);
            Assert.AreEqual(6, planes.Length);
        }

        [Test]
        public void ExtractFrustumPlanes_AllPlanes_AreNormalized()
        {
            var vp = OrthoVP(-2, 2, -1, 1, 1f, 50f);
            using var planes = new NativeArray<float4>(6, Allocator.Temp);
            CullingUtility.ExtractFrustumPlanes(vp, planes);

            for (int i = 0; i < 6; i++)
            {
                float len = math.length(planes[i].xyz);
                Assert.That(len, Is.EqualTo(1f).Within(1e-5f),
                    $"Plane {i} normal is not unit length (length={len})");
            }
        }

        [Test]
        public void ExtractFrustumPlanes_SymmetricOrtho_LeftAndRightPlanesAreOpposite()
        {
            // For a symmetric frustum the left and right plane normals should be mirrored
            // on the X axis and have the same |w|.
            var vp = OrthoVP(-1, 1, -1, 1, 1f, 100f);
            using var planes = new NativeArray<float4>(6, Allocator.Temp);
            CullingUtility.ExtractFrustumPlanes(vp, planes);

            float4 left  = planes[0];
            float4 right = planes[1];

            Assert.That(left.x,  Is.EqualTo(-right.x).Within(1e-5f), "Left/right normal X mismatch");
            Assert.That(left.y,  Is.EqualTo(right.y).Within(1e-5f),  "Left/right normal Y should match");
            Assert.That(left.z,  Is.EqualTo(right.z).Within(1e-5f),  "Left/right normal Z should match");
            Assert.That(math.abs(left.w), Is.EqualTo(math.abs(right.w)).Within(1e-5f),
                "Left/right plane distances should have same magnitude for a symmetric frustum");
        }

        [Test]
        public void ExtractFrustumPlanes_OriginInsideFrustum_AllPlanesYieldPositiveSignedDistance()
        {
            // The origin is inside a [-1,1]^3 frustum.
            // For each plane: dot(normal, origin) + w == w, which must be >= 0 when inside.
            var vp = OrthoVP(-1, 1, -1, 1, -1f, 1f);
            using var planes = new NativeArray<float4>(6, Allocator.Temp);
            CullingUtility.ExtractFrustumPlanes(vp, planes);

            for (int i = 0; i < 6; i++)
            {
                float d = planes[i].w; // dot(normal, float3.zero) + w
                Assert.That(d, Is.GreaterThanOrEqualTo(0f),
                    $"Plane {i}: origin should be on the positive (inside) side, got d={d}");
            }
        }

        // ------------------------------------------------------------------ FrustumCullingJob (via Execute directly, no scheduling)

        private static NativeArray<float4> BuildOrthoPlanes(
            float left, float right,
            float bottom, float top,
            float near, float far,
            Allocator allocator = Allocator.Temp)
        {
            var vp = OrthoVP(left, right, bottom, top, near, far);
            var planes = new NativeArray<float4>(6, allocator);
            CullingUtility.ExtractFrustumPlanes(vp, planes);
            return planes;
        }

        /// <summary>Runs the job synchronously on the main thread for testing purposes.</summary>
        private static NativeList<int> RunCullingJob(
            NativeArray<float4> planes,
            NativeArray<CullingInstance> instances)
        {
            var visible = new NativeList<int>(instances.Length, Allocator.Temp);
            var job = new FrustumCullingJob
            {
                FrustumPlanes  = planes,
                Instances      = instances,
                VisibleIndices = visible.AsParallelWriter(),
            };
            // Execute synchronously for deterministic testing.
            for (int i = 0; i < instances.Length; i++)
                job.Execute(i);
            return visible;
        }

        [Test]
        public void FrustumCullingJob_AABBFullyInsideFrustum_IsVisible()
        {
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(0, 0, 0, 0.5f, 0.5f, 0.5f), OriginalIndex = 0 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(1, visible.Length);
            Assert.AreEqual(0, visible[0]);
        }

        [Test]
        public void FrustumCullingJob_AABBFullyOutsideFrustum_IsNotVisible()
        {
            // Frustum covers [-5,5]^3; AABB sits at X=10, far outside.
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(10, 0, 0, 0.5f, 0.5f, 0.5f), OriginalIndex = 7 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(0, visible.Length);
        }

        [Test]
        public void FrustumCullingJob_AABBStraddlesFrustumBorder_IsVisible()
        {
            // AABB center at X=4.8, extents 0.5 → reaches to X=5.3, just over the right border.
            // Its closest point to the interior is at X=4.3, which is inside → should be VISIBLE.
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(4.8f, 0, 0, 0.5f, 0.5f, 0.5f), OriginalIndex = 3 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(1, visible.Length, "Straddling AABB must not be culled");
        }

        [Test]
        public void FrustumCullingJob_MultipleInstances_OnlyVisibleOnesReturned()
        {
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(4, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB( 0,  0,  0, 1, 1, 1), OriginalIndex = 0 }, // visible
                [1] = new CullingInstance { Bounds = MakeAABB(20,  0,  0, 1, 1, 1), OriginalIndex = 1 }, // culled (X+)
                [2] = new CullingInstance { Bounds = MakeAABB( 0, 20,  0, 1, 1, 1), OriginalIndex = 2 }, // culled (Y+)
                [3] = new CullingInstance { Bounds = MakeAABB( 0,  0,  3, 1, 1, 1), OriginalIndex = 3 }, // visible
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(2, visible.Length);
            Assert.That(visible.ToArray(Allocator.Temp), Is.EquivalentTo(new[] { 0, 3 }));
        }

        [Test]
        public void FrustumCullingJob_EmptyInstanceList_ReturnsNoVisibleIndices()
        {
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(0, Allocator.Temp);
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(0, visible.Length);
        }

        [Test]
        public void FrustumCullingJob_OriginalIndex_IsPreservedCorrectly()
        {
            // Ensure the OriginalIndex carried in CullingInstance is reported, not the job index.
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(0, 0, 0, 0.1f, 0.1f, 0.1f), OriginalIndex = 42 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(1, visible.Length);
            Assert.AreEqual(42, visible[0]);
        }

        [Test]
        public void FrustumCullingJob_AABBJustTouchingFrustumBoundary_IsVisible()
        {
            // Center at X=4, extents=1 → farthest point is exactly at X=5 (the boundary).
            // The point on the boundary is NOT outside (d == 0 is not < -r when r=0 residual),
            // so it should be visible.
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(4f, 0, 0, 1f, 0.5f, 0.5f), OriginalIndex = 99 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(1, visible.Length, "AABB touching the boundary should not be culled");
        }

        [Test]
        public void FrustumCullingJob_AABBJustBeyondFrustumBoundary_IsCulled()
        {
            // Center at X=6, extents=0.5 → nearest point at X=5.5, entirely outside right plane (X=5).
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                [0] = new CullingInstance { Bounds = MakeAABB(6f, 0, 0, 0.5f, 0.5f, 0.5f), OriginalIndex = 0 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(0, visible.Length, "AABB entirely beyond boundary must be culled");
        }
        
        [Test]
        public void FrustumCullingJob_ZeroExtentsAABB_Inside_IsVisible()
        {
            using var planes = BuildOrthoPlanes(-5, 5, -5, 5, -5, 5);
            using var instances = new NativeArray<CullingInstance>(1, Allocator.Temp)
            {
                // 模拟一个点 (Extents = 0)
                [0] = new CullingInstance { Bounds = MakeAABB(0, 0, 0, 0, 0, 0), OriginalIndex = 1 }
            };
            using var visible = RunCullingJob(planes, instances);

            Assert.AreEqual(1, visible.Length, "Zero extent AABB (point) inside should be visible.");
        }
    }
}
