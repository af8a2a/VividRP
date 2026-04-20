using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal static class DecalSystem
    {
        private static readonly List<DecalProjector> s_Projectors = new();
        private static readonly List<DecalData> s_ActiveDecals = new();
        private static bool s_Initialized;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            FrameContextSystem.SubsystemPreRender -= Update;
            FrameContextSystem.SubsystemPreRender += Update;
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            if (!s_Initialized)
                return;
#if !UNITY_EDITOR
            FrameContextSystem.SubsystemPreRender -= Update;
#endif
            s_Projectors.Clear();
            s_ActiveDecals.Clear();
            s_Initialized = false;
        }

        internal static void Register(DecalProjector projector)
        {
            if (!s_Projectors.Contains(projector))
                s_Projectors.Add(projector);
        }

        internal static void Unregister(DecalProjector projector)
        {
            s_Projectors.Remove(projector);
        }

        private static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            s_ActiveDecals.Clear();

            if (s_Projectors.Count == 0)
                return;

            Camera camera = GetCamera(frameData);
            if (camera == null)
                return;

            // Build culling instances from active projectors
            var validProjectors = new List<DecalProjector>();
            var instances = new NativeArray<CullingInstance>(s_Projectors.Count, Allocator.TempJob);
            int instanceCount = 0;

            for (int i = 0; i < s_Projectors.Count; i++)
            {
                DecalProjector projector = s_Projectors[i];
                if (projector == null || !projector.isActiveAndEnabled)
                    continue;

                if (!projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData wd))
                    continue;

                validProjectors.Add(projector);
                instances[instanceCount] = new CullingInstance
                {
                    Bounds = new AABB
                    {
                        Center = new float4(wd.worldAabb.center, 0f),
                        Extents = new float4(wd.worldAabb.extents, 0f),
                    },
                    OriginalIndex = instanceCount,
                };
                instanceCount++;
            }

            if (instanceCount == 0)
            {
                instances.Dispose();
                return;
            }

            // Extract frustum planes
            var planes = new NativeArray<float4>(6, Allocator.TempJob);
            float4x4 viewProj = (float4x4)(camera.projectionMatrix * camera.worldToCameraMatrix);
            CullingUtility.ExtractFrustumPlanes(viewProj, planes);

            // Run frustum culling job
            var visibleIndices = new NativeList<int>(instanceCount, Allocator.TempJob);
            var cullingJob = new FrustumCullingJob
            {
                FrustumPlanes = planes,
                Instances = instances,
                VisibleIndices = visibleIndices.AsParallelWriter(),
            };
            cullingJob.Schedule(instanceCount, 64).Complete();

            // Gather visible decals
            for (int i = 0; i < visibleIndices.Length; i++)
            {
                int idx = visibleIndices[i];
                DecalProjector projector = validProjectors[idx];
                projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData wd);

                Matrix4x4 worldToDecal = Matrix4x4.TRS(
                    wd.worldCenter,
                    wd.worldRotation,
                    wd.boxSize).inverse;

                s_ActiveDecals.Add(new DecalData
                {
                    worldToDecal = worldToDecal,
                    baseColorTexture = projector.BaseColorTexture,
                    normalTexture = projector.NormalTexture,
                    baseColor = projector.BaseColor,
                    blendDistance = projector.BlendDistance,
                });
            }

            planes.Dispose();
            instances.Dispose();
            visibleIndices.Dispose();
        }

        private static Camera GetCamera(ContextContainer frameData)
        {
            if (frameData == null || !frameData.Contains<VividCameraData>())
                return null;

            return frameData.Get<VividCameraData>().camera;
        }

        internal static int ActiveDecalCount => s_ActiveDecals.Count;

        internal static void GetActiveDecals(List<DecalData> results)
        {
            results.Clear();
            results.AddRange(s_ActiveDecals);
        }
    }
}
