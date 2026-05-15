using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
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

        // Prepared snapshot produced by the PlayerLoop kick and consumed by SRP UpdateCore.
        // s_SourceProjectors[i] / s_Prepared[i] correspond to s_Sources[i] for i < s_SourceCount.
        private static NativeArray<DecalSourceData> s_Sources;
        private static NativeArray<DecalPreparedData> s_Prepared;
        private static DecalProjector[] s_SourceProjectors = System.Array.Empty<DecalProjector>();
        private static int s_SourceCount;
        private static JobHandle s_PreparedJobHandle;
        private static bool s_KickRan;
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
            InsertIntoPlayerLoop();
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            if (!s_Initialized)
                return;

            s_PreparedJobHandle.Complete();
            s_PreparedJobHandle = default;
            s_KickRan = false;

            RemoveFromPlayerLoop();

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#else
            FrameContextSystem.SubsystemPreRender -= Update;
#endif

            if (s_Sources.IsCreated)
                s_Sources.Dispose();
            if (s_Prepared.IsCreated)
                s_Prepared.Dispose();
            for (int i = 0; i < s_SourceProjectors.Length; i++)
                s_SourceProjectors[i] = null;
            s_SourceCount = 0;

            s_Projectors.Clear();
            s_ActiveDecals.Clear();
            s_Initialized = false;
        }

#if UNITY_EDITOR
        private static void OnBeforeAssemblyReload()
        {
            Deinitialize();
        }
#endif

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

        private static void PlayerLoopKick()
        {
            if (!s_Initialized)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalKickMarker.Auto())
            {
                // If a previous kick never reached its consumer (e.g. domain reload, pipeline swap), drain it first.
                s_PreparedJobHandle.Complete();

                int projectorCount = s_Projectors.Count;
                EnsureSnapshotCapacity(projectorCount);

                int sourceCount = 0;
                for (int i = 0; i < projectorCount; i++)
                {
                    DecalProjector projector = s_Projectors[i];
                    if (projector == null || !projector.isActiveAndEnabled)
                        continue;

                    if (!projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData wd))
                        continue;

                    s_SourceProjectors[sourceCount] = projector;
                    s_Sources[sourceCount] = new DecalSourceData
                    {
                        worldCenter = wd.worldCenter,
                        worldRotation = wd.worldRotation,
                        boxSize = wd.boxSize,
                        baseColor = (Vector4)projector.BaseColor,
                        blendDistance = projector.BlendDistance,
                        metallic = projector.Metallic,
                        roughness = projector.Roughness,
                    };
                    sourceCount++;
                }

                s_SourceCount = sourceCount;
                s_KickRan = true;

                if (sourceCount == 0)
                {
                    s_PreparedJobHandle = default;
                    return;
                }

                var job = new PrepareDecalsJob
                {
                    Sources = s_Sources,
                    Prepared = s_Prepared,
                };
                s_PreparedJobHandle = job.Schedule(sourceCount, 32);
            }
        }

        private static void EnsureSnapshotCapacity(int requiredCapacity)
        {
            int current = s_Sources.IsCreated ? s_Sources.Length : 0;
            if (current >= requiredCapacity && s_Prepared.IsCreated && s_Prepared.Length >= requiredCapacity)
                return;

            int newCapacity = math.max(8, math.ceilpow2(requiredCapacity));

            if (s_Sources.IsCreated)
                s_Sources.Dispose();
            if (s_Prepared.IsCreated)
                s_Prepared.Dispose();

            s_Sources = new NativeArray<DecalSourceData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            s_Prepared = new NativeArray<DecalPreparedData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            if (s_SourceProjectors.Length < newCapacity)
            {
                var grown = new DecalProjector[newCapacity];
                System.Array.Copy(s_SourceProjectors, grown, s_SourceProjectors.Length);
                s_SourceProjectors = grown;
            }
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

            // First frame after init/domain reload: the PreLateUpdate kick has not run yet for this pipeline,
            // fall back to the original synchronous path so we never read stale or empty prepared data.
            if (!s_KickRan)
            {
                UpdateCoreSynchronous(frameData);
                return;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
            {
                s_PreparedJobHandle.Complete();
                s_PreparedJobHandle = default;
            }

            if (s_SourceCount == 0)
            {
                UpdateLightDataEmpty(frameData);
                return;
            }

            Camera camera = GetCamera(frameData);
            if (camera == null)
            {
                UpdateLightDataEmpty(frameData);
                return;
            }

            var instances = new NativeArray<CullingInstance>(s_SourceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < s_SourceCount; i++)
            {
                instances[i] = new CullingInstance
                {
                    Bounds = new AABB
                    {
                        Center = s_Prepared[i].worldAabbCenter,
                        Extents = s_Prepared[i].worldAabbExtents,
                    },
                    OriginalIndex = i,
                };
            }

            var planes = new NativeArray<float4>(6, Allocator.TempJob);
            float4x4 viewProj = (float4x4)(camera.projectionMatrix * camera.worldToCameraMatrix);
            CullingUtility.ExtractFrustumPlanes(viewProj, planes);

            var visibleIndices = new NativeList<int>(s_SourceCount, Allocator.TempJob);
            var cullingJob = new FrustumCullingJob
            {
                FrustumPlanes = planes,
                Instances = instances,
                VisibleIndices = visibleIndices.AsParallelWriter(),
            };
            cullingJob.Schedule(s_SourceCount, 64).Complete();

            EmitVisibleDecals(frameData, visibleIndices.AsArray());

            planes.Dispose();
            instances.Dispose();
            visibleIndices.Dispose();
        }

        private static void EmitVisibleDecals(ContextContainer frameData, NativeArray<int> visibleIndices)
        {
            var gpuDrivenDecalData = frameData.GetOrCreate<VividGPUDrivenDecalData>();
            var gpuDrivenDecalEnabled = TryResolveGPUDrivenDecalBindlessContainer(out var bindlessTextureContainer);
            gpuDrivenDecalData.isEnabled = gpuDrivenDecalEnabled;

            var lightData = frameData.GetOrCreate<VividLightData>();
            int visibleCount = visibleIndices.Length;
            lightData.decalCount = visibleCount;

            if (visibleCount == 0)
                return;

            if (lightData.decalClusterData.Length < visibleCount)
                lightData.decalClusterData = new VividLightData.DecalClusterData[visibleCount];

            for (int i = 0; i < visibleCount; i++)
            {
                int idx = visibleIndices[i];
                DecalProjector projector = s_SourceProjectors[idx];
                DecalPreparedData prepared = s_Prepared[idx];

                float4x4 m = prepared.worldToDecal;
                Matrix4x4 worldToDecal = new(m.c0, m.c1, m.c2, m.c3);

                lightData.decalClusterData[i] = new VividLightData.DecalClusterData
                {
                    worldToDecal = worldToDecal,
                    baseColor = prepared.baseColor,
                    baseColorTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.BaseColorTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    normalTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.NormalTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    metallicTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.MetallicTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    roughnessTextureIndex = ResolveBindlessTextureIndex(
                        projector != null ? projector.RoughnessTexture : null,
                        gpuDrivenDecalEnabled,
                        bindlessTextureContainer),
                    blendDistance = prepared.normalizedBlendDistance,
                    metallic = prepared.clampedMetallic,
                    roughness = prepared.clampedRoughness,
                    padding = 0f,
                };
            }
        }

        private static void UpdateCoreSynchronous(ContextContainer frameData)
        {
            s_ActiveDecals.Clear();

            if (s_Projectors.Count == 0)
            {
                UpdateLightDataEmpty(frameData);
                return;
            }

            Camera camera = GetCamera(frameData);
            if (camera == null)
            {
                UpdateLightDataEmpty(frameData);
                return;
            }

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
                UpdateLightDataEmpty(frameData);
                return;
            }

            var planes = new NativeArray<float4>(6, Allocator.TempJob);
            float4x4 viewProj = (float4x4)(camera.projectionMatrix * camera.worldToCameraMatrix);
            CullingUtility.ExtractFrustumPlanes(viewProj, planes);

            var visibleIndices = new NativeList<int>(instanceCount, Allocator.TempJob);
            var cullingJob = new FrustumCullingJob
            {
                FrustumPlanes = planes,
                Instances = instances,
                VisibleIndices = visibleIndices.AsParallelWriter(),
            };
            cullingJob.Schedule(instanceCount, 64).Complete();

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
                    metallicTexture = projector.MetallicTexture,
                    roughnessTexture = projector.RoughnessTexture,
                    baseColor = projector.BaseColor,
                    blendDistance = NormalizeBlendDistance(projector.BlendDistance, wd.boxSize),
                    metallic = projector.Metallic,
                    roughness = projector.Roughness,
                });
            }

            planes.Dispose();
            instances.Dispose();
            visibleIndices.Dispose();

            UpdateLightDataFromActiveDecals(frameData);
        }

        private static void UpdateLightDataFromActiveDecals(ContextContainer frameData)
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

        private static void UpdateLightDataEmpty(ContextContainer frameData)
        {
            if (frameData == null)
                return;

            var gpuDrivenDecalData = frameData.GetOrCreate<VividGPUDrivenDecalData>();
            gpuDrivenDecalData.isEnabled = TryResolveGPUDrivenDecalBindlessContainer(out _);

            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.decalCount = 0;
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
                metallicTextureIndex = ResolveBindlessTextureIndex(
                    decal.metallicTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                roughnessTextureIndex = ResolveBindlessTextureIndex(
                    decal.roughnessTexture,
                    gpuDrivenDecalEnabled,
                    bindlessTextureContainer),
                blendDistance = decal.blendDistance,
                metallic = Mathf.Clamp01(decal.metallic),
                roughness = Mathf.Clamp01(decal.roughness),
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
            if (texture == null || !gpuDrivenDecalEnabled || bindlessTextureContainer == null)
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

        private static void InsertIntoPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length + 1);
                bool alreadyPresent = false;
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type == typeof(DecalSystemPlayerLoopMarker))
                        alreadyPresent = true;
                    updatedSubSystems.Add(nestedSystem);
                }

                if (!alreadyPresent)
                    updatedSubSystems.Add(CreatePlayerLoopSystem());

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static void RemoveFromPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length);
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type != typeof(DecalSystemPlayerLoopMarker))
                        updatedSubSystems.Add(nestedSystem);
                }

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static PlayerLoopSystem CreatePlayerLoopSystem()
        {
            return new PlayerLoopSystem
            {
                type = typeof(DecalSystemPlayerLoopMarker),
                updateDelegate = PlayerLoopKick,
            };
        }

        private sealed class DecalSystemPlayerLoopMarker
        {
        }
    }
}
