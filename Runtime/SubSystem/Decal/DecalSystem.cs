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
        private const int k_InlineJobDecalThreshold = 64;
        private const int k_PrepareJobBatchSize = 32;
        private const int k_CullJobBatchSize = 64;
        private static readonly Quaternion s_ProjectorToDecalSpaceRotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
        private static readonly List<DecalProjector> s_Projectors = new();

        // Prepared snapshot produced by the PlayerLoop kick (or first SRP update) and consumed by SRP UpdateCore.
        // s_SourceProjectors[i] / s_Prepared[i] / s_CullingInstances[i] correspond to s_Sources[i] for i < s_SourceCount.
        private static NativeArray<DecalSourceData> s_Sources;
        private static NativeArray<DecalPreparedData> s_Prepared;
        private static NativeArray<CullingInstance> s_CullingInstances;
        private static NativeArray<float4> s_FrustumPlanes;
        private static NativeList<int> s_VisibleIndices;
        private static DecalProjector[] s_SourceProjectors = System.Array.Empty<DecalProjector>();
        private static int s_SourceCount;
        private static JobHandle s_PreparedJobHandle;
        private static JobHandle s_CullJobHandle;
        private static EntityId s_CullScheduledForCameraId;
        private static bool s_PrepareScheduled;
        private static bool s_CullScheduled;
        private static bool s_CullReady;
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
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
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

            CompleteScheduledWork();
            s_KickRan = false;

            RemoveFromPlayerLoop();
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#else
            FrameContextSystem.SubsystemPreRender -= Update;
#endif

            if (s_Sources.IsCreated)
                s_Sources.Dispose();
            if (s_Prepared.IsCreated)
                s_Prepared.Dispose();
            if (s_CullingInstances.IsCreated)
                s_CullingInstances.Dispose();
            if (s_FrustumPlanes.IsCreated)
                s_FrustumPlanes.Dispose();
            if (s_VisibleIndices.IsCreated)
                s_VisibleIndices.Dispose();
            for (int i = 0; i < s_SourceProjectors.Length; i++)
                s_SourceProjectors[i] = null;
            s_SourceCount = 0;

            s_Projectors.Clear();
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
                BuildSnapshotAndSchedulePrepare(true);
            }
        }

        private static void BuildSnapshotAndSchedulePrepare(bool allowAsyncSchedule)
        {
            // Drain any leftover cull/prepare from the previous frame (e.g. SRP swapped, no camera rendered).
            CompleteScheduledWork();

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
                return;

            var job = new PrepareDecalsJob
            {
                Sources = s_Sources,
                Prepared = s_Prepared,
                CullingInstances = s_CullingInstances,
            };

            if (!allowAsyncSchedule || ShouldRunInline(sourceCount))
            {
                job.Run(sourceCount);
                return;
            }

            s_PreparedJobHandle = job.Schedule(sourceCount, k_PrepareJobBatchSize);
            s_PrepareScheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }

        private static void EnsureSnapshotCapacity(int requiredCapacity)
        {
            int current = s_Sources.IsCreated ? s_Sources.Length : 0;
            if (current >= requiredCapacity
                && s_Prepared.IsCreated && s_Prepared.Length >= requiredCapacity
                && s_CullingInstances.IsCreated && s_CullingInstances.Length >= requiredCapacity
                && s_VisibleIndices.IsCreated && s_VisibleIndices.Capacity >= requiredCapacity)
                return;

            int newCapacity = math.max(8, math.ceilpow2(requiredCapacity));

            if (s_Sources.IsCreated)
                s_Sources.Dispose();
            if (s_Prepared.IsCreated)
                s_Prepared.Dispose();
            if (s_CullingInstances.IsCreated)
                s_CullingInstances.Dispose();
            if (s_VisibleIndices.IsCreated)
                s_VisibleIndices.Dispose();

            s_Sources = new NativeArray<DecalSourceData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            s_Prepared = new NativeArray<DecalPreparedData>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            s_CullingInstances = new NativeArray<CullingInstance>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            s_VisibleIndices = new NativeList<int>(newCapacity, Allocator.Persistent);

            if (!s_FrustumPlanes.IsCreated)
                s_FrustumPlanes = new NativeArray<float4>(6, Allocator.Persistent);

            if (s_SourceProjectors.Length < newCapacity)
            {
                var grown = new DecalProjector[newCapacity];
                System.Array.Copy(s_SourceProjectors, grown, s_SourceProjectors.Length);
                s_SourceProjectors = grown;
            }
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!s_Initialized || !s_KickRan || camera == null)
                return;

            // Persistent cull buffers are shared across cameras; drain any pending slot before reusing them.
            if (s_CullScheduled || s_CullReady)
                CompleteScheduledWork();

            if (s_SourceCount == 0)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCullScheduleMarker.Auto())
            {
                ScheduleOrRunCull(camera);
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

            if (!s_KickRan)
                BuildSnapshotAndSchedulePrepare(false);

            Camera camera = GetCamera(frameData);
            if (camera == null || s_SourceCount == 0)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
                {
                    CompleteScheduledWork();
                }
                UpdateLightDataEmpty(frameData);
                return;
            }

            // Cull was scheduled by OnBeginCameraRendering for this camera — just join the result.
            EntityId cameraId = camera.GetEntityId();
            if ((s_CullScheduled || s_CullReady) && s_CullScheduledForCameraId == cameraId)
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
                {
                    CompleteScheduledWork();
                }

                EmitVisibleDecals(frameData, s_VisibleIndices.AsArray());
                return;
            }

            // Miss path: prepared data is ready but cull was not scheduled (or was scheduled for a different camera).
            // Complete leftover cull, then run cull inline using the persistent buffers.
            using (RenderPassProfilingUtility.PrepareFrameSubsystemDecalCompleteMarker.Auto())
            {
                CompleteScheduledWork();
            }

            RunCullInlineOrComplete(camera);

            EmitVisibleDecals(frameData, s_VisibleIndices.AsArray());
        }

        private static bool ShouldRunInline(int decalCount)
        {
            return decalCount <= k_InlineJobDecalThreshold;
        }

        private static void CompleteScheduledWork()
        {
            bool completedCull = s_CullScheduled;
            if (s_CullScheduled)
                s_CullJobHandle.Complete();
            s_CullJobHandle = default;
            s_CullScheduled = false;
            s_CullReady = false;
            s_CullScheduledForCameraId = default;

            if (!completedCull && s_PrepareScheduled)
                s_PreparedJobHandle.Complete();

            s_PreparedJobHandle = default;
            s_PrepareScheduled = false;
        }

        private static void ScheduleOrRunCull(Camera camera)
        {
            var cullingJob = CreateFrustumCullingJob(camera);
            EntityId cameraId = camera.GetEntityId();

            if (!s_PrepareScheduled && ShouldRunInline(s_SourceCount))
            {
                cullingJob.Run(s_SourceCount);
                s_CullReady = true;
                s_CullScheduledForCameraId = cameraId;
                return;
            }

            s_CullJobHandle = cullingJob.Schedule(s_SourceCount, k_CullJobBatchSize, s_PreparedJobHandle);
            s_CullScheduled = true;
            s_CullScheduledForCameraId = cameraId;
            JobHandle.ScheduleBatchedJobs();
        }

        private static void RunCullInlineOrComplete(Camera camera)
        {
            var cullingJob = CreateFrustumCullingJob(camera);
            if (ShouldRunInline(s_SourceCount))
            {
                cullingJob.Run(s_SourceCount);
                return;
            }

            cullingJob.Schedule(s_SourceCount, k_CullJobBatchSize).Complete();
        }

        private static FrustumCullingJob CreateFrustumCullingJob(Camera camera)
        {
            float4x4 viewProj = (float4x4)(camera.projectionMatrix * camera.worldToCameraMatrix);
            CullingUtility.ExtractFrustumPlanes(viewProj, s_FrustumPlanes);

            s_VisibleIndices.Clear();
            if (s_VisibleIndices.Capacity < s_SourceCount)
                s_VisibleIndices.SetCapacity(s_SourceCount);

            return new FrustumCullingJob
            {
                FrustumPlanes = s_FrustumPlanes,
                Instances = s_CullingInstances.GetSubArray(0, s_SourceCount),
                VisibleIndices = s_VisibleIndices.AsParallelWriter(),
            };
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
