using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal static class DecalSystem
    {
        private static readonly Quaternion s_ProjectorToDecalSpaceRotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
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
            if (!s_Initialized)
                Initialize();

            if (projector == null)
                return;

            if (!s_Projectors.Contains(projector))
                s_Projectors.Add(projector);
        }

        internal static void Unregister(DecalProjector projector)
        {
            s_Projectors.Remove(projector);
        }

        private static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private static void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!s_Initialized)
                Initialize();

            s_ActiveDecals.Clear();

            if (s_Projectors.Count == 0)
            {
                UpdateLightData(frameData);
                return;
            }

            Camera camera = GetCamera(frameData);
            if (camera == null)
            {
                UpdateLightData(frameData);
                return;
            }

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
                UpdateLightData(frameData);
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

                s_ActiveDecals.Add(new DecalData
                {
                    worldToDecal = CreateWorldToDecalMatrix(wd),
                    baseColorTexture = projector.BaseColorTexture,
                    normalTexture = projector.NormalTexture,
                    baseColor = projector.BaseColor,
                    blendDistance = NormalizeBlendDistance(projector.BlendDistance, wd.boxSize),
                });
            }

            planes.Dispose();
            instances.Dispose();
            visibleIndices.Dispose();

            UpdateLightData(frameData);
        }

        private static void UpdateLightData(ContextContainer frameData)
        {
            if (frameData == null)
                return;

            var gpuDrivenDecalData = frameData.GetOrCreate<VividGPUDrivenDecalData>();
            var gpuDrivenDecalEnabled = TryResolveGPUDrivenDecalBindlessContainer(out var bindlessTextureContainer);
            gpuDrivenDecalData.isEnabled = gpuDrivenDecalEnabled;

            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.decalCount = s_ActiveDecals.Count;

            if (lightData.decalCount == 0)
                return;

            if (lightData.decalClusterData.Length < lightData.decalCount)
                lightData.decalClusterData = new VividLightData.DecalClusterData[lightData.decalCount];

            for (int i = 0; i < s_ActiveDecals.Count; i++)
            {
                lightData.decalClusterData[i] = CreateDecalClusterData(
                    s_ActiveDecals[i],
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer);
            }
        }

        internal static VividLightData.DecalClusterData CreateDecalClusterData(
            DecalData decal,
            bool gpuDrivenDecalEnabled,
            BindlessTextureContainer bindlessTextureContainer)
        {
            return new VividLightData.DecalClusterData
            {
                worldToDecal = decal.worldToDecal,
                baseColor = decal.baseColor,
                baseColorTextureIndex = ResolveBindlessTextureIndex(
                    decal.baseColorTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                normalTextureIndex = ResolveBindlessTextureIndex(
                    decal.normalTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                blendDistance = decal.blendDistance,
                padding = 0f,
            };
        }

        internal static Matrix4x4 CreateWorldToDecalMatrix(in BoundProxyWorldData worldData)
        {
            // Match HDRP's decal space: authoring local Z is projection depth, shader samples the XZ plane.
            Vector3 decalSpaceSize = new(worldData.boxSize.x, worldData.boxSize.z, worldData.boxSize.y);
            Quaternion decalSpaceRotation = worldData.worldRotation * s_ProjectorToDecalSpaceRotation;
            return Matrix4x4.TRS(worldData.worldCenter, decalSpaceRotation, decalSpaceSize).inverse;
        }

        internal static float NormalizeBlendDistance(float blendDistance, Vector3 boxSize)
        {
            if (blendDistance <= 0.0f)
                return 0.0f;

            var minDimension = Mathf.Min(
                Mathf.Abs(boxSize.x),
                Mathf.Abs(boxSize.y));

            if (minDimension <= 1e-5f)
                return 0.0f;

            return Mathf.Clamp(blendDistance / minDimension, 0.0f, 0.5f);
        }

        private static bool TryResolveGPUDrivenDecalBindlessContainer(out BindlessTextureContainer bindlessTextureContainer)
        {
            bindlessTextureContainer = null;

            var asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset == null || !asset.EnableGPUDriven || !asset.EnableGPUDrivenDecal)
                return false;

            var gpuDrivenSystem = VividGPUDrivenSystem.instance;
            if (gpuDrivenSystem == null || !gpuDrivenSystem.IsAvailable)
                return false;

            bindlessTextureContainer = gpuDrivenSystem.BindlessTextureContainer;
            return bindlessTextureContainer != null && bindlessTextureContainer.IsAvailable;
        }

        private static uint ResolveBindlessTextureIndex(
            Texture texture,
            bool gpuDrivenDecalEnabled,
            BindlessTextureContainer bindlessTextureContainer)
        {
            if (!gpuDrivenDecalEnabled || bindlessTextureContainer == null)
                return BindlessTextureContainer.InvalidTextureIndex;

            return bindlessTextureContainer.TryGetOrCreateIndex(texture, out var textureIndex)
                ? textureIndex
                : BindlessTextureContainer.InvalidTextureIndex;
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
