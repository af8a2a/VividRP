using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;

namespace VividRP.Runtime
{
    internal sealed class DDGISystem : IDisposable
    {
        private const uint InstanceMask = 0xFFu;

        internal static readonly int ProbeIrradianceTextureId = Shader.PropertyToID("_DDGIProbeIrradiance");
        internal static readonly int ProbeDistanceTextureId = Shader.PropertyToID("_DDGIProbeDistance");
        internal static readonly int ProbeDataTextureId = Shader.PropertyToID("_DDGIProbeData");

        private static DDGISystem s_Instance;

        private readonly DDGISceneCache m_SceneCache = new();
        private readonly DDGISceneCacheBuilder m_SceneCacheBuilder = new();
        private readonly BindlessTextureContainer m_BindlessTextures = new();

        private RayTracingAccelerationStructure m_AccelerationStructure;
        private GraphicsBuffer m_VolumeConstantsBuffer;
        private GraphicsBuffer m_InstanceBuffer;
        private GraphicsBuffer m_SubMeshBuffer;
        private GraphicsBuffer m_MaterialBuffer;
        private GraphicsBuffer m_VertexBuffer;
        private GraphicsBuffer m_IndexBuffer;
        private GraphicsBuffer m_DirectionalLightBuffer;
        private GraphicsBuffer m_PunctualLightBuffer;

        private RTHandle m_ProbeRayDataHandle;
        private RTHandle m_ProbeIrradianceHandle;
        private RTHandle m_ProbeDistanceHandle;
        private RTHandle m_ProbeDataHandle;
        private RTHandle m_ProbeVariabilityHandle;
        private RTHandle m_FallbackProbeIrradianceHandle;
        private RTHandle m_FallbackProbeDistanceHandle;
        private RTHandle m_FallbackProbeDataHandle;

        private DDGIVolume m_ActiveVolume;
        private DDGIProfile m_ActiveProfile;
        private Vector3Int m_ActiveProbeCounts;
        private int m_LastLayoutHash;
        private bool m_NeedsTextureClear;
        private int m_LastMultiVolumeWarningFrame = -1;
        private int m_LastSphereVolumeWarningFrame = -1;
        private int m_LastBindlessWarningFrame = -1;

        private DDGISystem()
        {
        }

        internal static DDGISystem instance => s_Instance ??= new DDGISystem();

        internal static void Shutdown()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        internal bool HasActiveVolume => m_ActiveVolume != null;

        internal DDGIVolume ActiveVolume => m_ActiveVolume;

        internal DDGIProfile ActiveProfile => m_ActiveProfile;

        internal Vector3Int ActiveProbeCounts => m_ActiveProbeCounts;

        internal int ProbesPerPlane => GetProbesPerPlane(m_ActiveProbeCounts);

        internal RayTracingAccelerationStructure AccelerationStructure => m_AccelerationStructure;

        internal GraphicsBuffer VolumeConstantsBuffer => m_VolumeConstantsBuffer;

        internal GraphicsBuffer InstanceBuffer => m_InstanceBuffer;

        internal GraphicsBuffer SubMeshBuffer => m_SubMeshBuffer;

        internal GraphicsBuffer MaterialBuffer => m_MaterialBuffer;

        internal GraphicsBuffer VertexBuffer => m_VertexBuffer;

        internal GraphicsBuffer IndexBuffer => m_IndexBuffer;

        internal GraphicsBuffer DirectionalLightBuffer => m_DirectionalLightBuffer;

        internal GraphicsBuffer PunctualLightBuffer => m_PunctualLightBuffer;

        internal RTHandle ProbeRayDataHandle => m_ProbeRayDataHandle;

        internal RTHandle ProbeIrradianceHandle => m_ProbeIrradianceHandle ?? m_FallbackProbeIrradianceHandle;

        internal RTHandle ProbeDistanceHandle => m_ProbeDistanceHandle ?? m_FallbackProbeDistanceHandle;

        internal RTHandle ProbeDataHandle => m_ProbeDataHandle ?? m_FallbackProbeDataHandle;

        internal RTHandle ProbeVariabilityHandle => m_ProbeVariabilityHandle;

        internal void Update(ContextContainer frameData)
        {
            DDGIRuntimeData runtimeData = frameData.GetOrCreate<DDGIRuntimeData>();
            runtimeData.Reset();

            EnsureFallbackTextures();
            runtimeData.supportsRayTracing = SystemInfo.supportsRayTracing;
            if (!runtimeData.supportsRayTracing)
            {
                m_ActiveVolume = null;
                return;
            }

            if (!m_BindlessTextures.IsAvailable)
            {
                if (Time.frameCount != m_LastBindlessWarningFrame)
                {
                    m_LastBindlessWarningFrame = Time.frameCount;
                    Debug.LogWarning(
                        $"[VividRP] DDGI probe tracing requires bindless texture descriptors. {m_BindlessTextures.UnavailableReason}");
                }

                m_ActiveVolume = null;
                return;
            }

            DDGIVolume activeVolume = SelectActiveVolume();
            if (activeVolume == null || !activeVolume.IsRuntimeSupported)
            {
                m_ActiveVolume = null;
                return;
            }

            DDGIProfile profile = DDGIProfileTable.GetProfile(activeVolume.Profile);
            Vector3Int probeCounts = activeVolume.ProbeCounts;
            int layoutHash = ComputeLayoutHash(activeVolume, profile, probeCounts);
            bool layoutChanged = layoutHash != m_LastLayoutHash || m_ActiveVolume != activeVolume;

            bool textureReallocated = EnsureProbeTextures(activeVolume, profile, probeCounts);
            if (layoutChanged || textureReallocated)
            {
                m_NeedsTextureClear = true;
            }

            bool sceneChanged = m_SceneCacheBuilder.Build(activeVolume, m_BindlessTextures, m_SceneCache);
            if (sceneChanged || layoutChanged)
            {
                UploadSceneCache();
                RebuildAccelerationStructure();
            }

            UpdateLightBuffers(frameData.GetOrCreate<VividLightData>());
            m_BindlessTextures.PreRender();
            UploadVolumeConstants(activeVolume, profile);

            DDGIRootConstants rootConstants = BuildRootConstants(probeCounts, profile);
            ShaderVariablesDDGI shaderVariables = ShaderVariablesDDGI.Create(activeVolume, profile);

            m_ActiveVolume = activeVolume;
            m_ActiveProfile = profile;
            m_ActiveProbeCounts = probeCounts;
            m_LastLayoutHash = layoutHash;

            runtimeData.hasActiveVolume = true;
            runtimeData.isRuntimeReady = true;
            runtimeData.clearProbeTextures = m_NeedsTextureClear;
            runtimeData.activeVolume = activeVolume;
            runtimeData.probesPerPlane = GetProbesPerPlane(probeCounts);
            runtimeData.profileId = activeVolume.Profile;
            runtimeData.rootConstants = rootConstants;
            runtimeData.shaderVariables = shaderVariables;
            runtimeData.volumeConstantsBuffer = m_VolumeConstantsBuffer;
            runtimeData.instanceBuffer = m_InstanceBuffer;
            runtimeData.subMeshBuffer = m_SubMeshBuffer;
            runtimeData.materialBuffer = m_MaterialBuffer;
            runtimeData.vertexBuffer = m_VertexBuffer;
            runtimeData.indexBuffer = m_IndexBuffer;
            runtimeData.directionalLightBuffer = m_DirectionalLightBuffer;
            runtimeData.punctualLightBuffer = m_PunctualLightBuffer;
        }

        internal void BindGlobalQueryState(CommandBuffer cmd)
        {
            if (cmd == null)
            {
                return;
            }

            ShaderVariablesDDGI shaderVariables = m_ActiveVolume != null
                ? ShaderVariablesDDGI.Create(m_ActiveVolume, m_ActiveProfile)
                : ShaderVariablesDDGI.CreateDisabled();

            ConstantBuffer.PushGlobal(cmd, shaderVariables, ShaderVariablesDDGI.ConstantBufferShaderId);
            cmd.SetGlobalTexture(
                ProbeIrradianceTextureId,
                m_ActiveVolume != null ? ProbeIrradianceHandle : m_FallbackProbeIrradianceHandle);
            cmd.SetGlobalTexture(
                ProbeDistanceTextureId,
                m_ActiveVolume != null ? ProbeDistanceHandle : m_FallbackProbeDistanceHandle);
            cmd.SetGlobalTexture(
                ProbeDataTextureId,
                m_ActiveVolume != null ? ProbeDataHandle : m_FallbackProbeDataHandle);
        }

        internal void ConsumeClearRequest()
        {
            m_NeedsTextureClear = false;
        }

        public void Dispose()
        {
            ReleaseBuffer(ref m_VolumeConstantsBuffer);
            ReleaseBuffer(ref m_InstanceBuffer);
            ReleaseBuffer(ref m_SubMeshBuffer);
            ReleaseBuffer(ref m_MaterialBuffer);
            ReleaseBuffer(ref m_VertexBuffer);
            ReleaseBuffer(ref m_IndexBuffer);
            ReleaseBuffer(ref m_DirectionalLightBuffer);
            ReleaseBuffer(ref m_PunctualLightBuffer);

            ReleaseHandle(ref m_ProbeRayDataHandle);
            ReleaseHandle(ref m_ProbeIrradianceHandle);
            ReleaseHandle(ref m_ProbeDistanceHandle);
            ReleaseHandle(ref m_ProbeDataHandle);
            ReleaseHandle(ref m_ProbeVariabilityHandle);
            ReleaseHandle(ref m_FallbackProbeIrradianceHandle);
            ReleaseHandle(ref m_FallbackProbeDistanceHandle);
            ReleaseHandle(ref m_FallbackProbeDataHandle);

            m_AccelerationStructure?.Dispose();
            m_AccelerationStructure = null;
            m_BindlessTextures.Dispose();
            m_ActiveVolume = null;
            m_ActiveProfile = default;
            m_ActiveProbeCounts = default;
            m_LastLayoutHash = 0;
            m_NeedsTextureClear = false;
            m_LastBindlessWarningFrame = -1;
        }

        private DDGIVolume SelectActiveVolume()
        {
            DDGIVolume[] volumes = Object.FindObjectsByType<DDGIVolume>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID);
            DDGIVolume firstSupportedVolume = null;
            int supportedVolumeCount = 0;
            bool hasSphereVolume = false;

            for (int index = 0; index < volumes.Length; index++)
            {
                DDGIVolume volume = volumes[index];
                if (volume == null || !volume.isActiveAndEnabled)
                {
                    continue;
                }

                if (volume.BoundProxyShape.shape == BoundProxyShapeType.Sphere)
                {
                    hasSphereVolume = true;
                    continue;
                }

                if (!volume.IsRuntimeSupported)
                {
                    continue;
                }

                supportedVolumeCount++;
                firstSupportedVolume ??= volume;
            }

            if (hasSphereVolume && Time.frameCount != m_LastSphereVolumeWarningFrame)
            {
                m_LastSphereVolumeWarningFrame = Time.frameCount;
                Debug.LogWarning("[VividRP] DDGI v1 ignores sphere volumes at runtime. Use a box DDGI volume instead.");
            }

            if (supportedVolumeCount > 1 && Time.frameCount != m_LastMultiVolumeWarningFrame)
            {
                m_LastMultiVolumeWarningFrame = Time.frameCount;
                Debug.LogWarning("[VividRP] DDGI v1 supports a single active box volume. The first enabled box volume will be used.");
            }

            return firstSupportedVolume;
        }

        private bool EnsureProbeTextures(DDGIVolume volume, DDGIProfile profile, Vector3Int probeCounts)
        {
            int probesPerPlane = GetProbesPerPlane(probeCounts);
            int planeCount = Mathf.Max(probeCounts.y, 1);
            int irradianceTexelCount = profile.IrradianceTexelCount;
            int distanceTexelCount = profile.DistanceTexelCount;

            bool reallocated = false;
            reallocated |= EnsureTextureArray(
                ref m_ProbeRayDataHandle,
                Mathf.Max(profile.RaysPerProbe, 1),
                Mathf.Max(probesPerPlane, 1),
                Mathf.Max(planeCount, 1),
                GraphicsFormat.R32G32_SFloat,
                FilterMode.Point,
                "DDGI Probe Ray Data");
            reallocated |= EnsureTextureArray(
                ref m_ProbeIrradianceHandle,
                Mathf.Max(probeCounts.x * irradianceTexelCount, 1),
                Mathf.Max(probeCounts.z * irradianceTexelCount, 1),
                Mathf.Max(planeCount, 1),
                GraphicsFormat.A2B10G10R10_UNormPack32,
                FilterMode.Bilinear,
                "DDGI Probe Irradiance");
            reallocated |= EnsureTextureArray(
                ref m_ProbeDistanceHandle,
                Mathf.Max(probeCounts.x * distanceTexelCount, 1),
                Mathf.Max(probeCounts.z * distanceTexelCount, 1),
                Mathf.Max(planeCount, 1),
                GraphicsFormat.R16G16_SFloat,
                FilterMode.Bilinear,
                "DDGI Probe Distance");
            reallocated |= EnsureTextureArray(
                ref m_ProbeDataHandle,
                Mathf.Max(probeCounts.x, 1),
                Mathf.Max(probeCounts.z, 1),
                Mathf.Max(planeCount, 1),
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Point,
                "DDGI Probe Data");
            reallocated |= EnsureTextureArray(
                ref m_ProbeVariabilityHandle,
                Mathf.Max(probeCounts.x * irradianceTexelCount, 1),
                Mathf.Max(probeCounts.z * irradianceTexelCount, 1),
                Mathf.Max(planeCount, 1),
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Point,
                "DDGI Probe Variability");
            return reallocated;
        }

        private void EnsureFallbackTextures()
        {
            EnsureTextureArray(
                ref m_FallbackProbeIrradianceHandle,
                1,
                1,
                1,
                GraphicsFormat.A2B10G10R10_UNormPack32,
                FilterMode.Bilinear,
                "DDGI Fallback Irradiance");
            EnsureTextureArray(
                ref m_FallbackProbeDistanceHandle,
                1,
                1,
                1,
                GraphicsFormat.R16G16_SFloat,
                FilterMode.Bilinear,
                "DDGI Fallback Distance");
            EnsureTextureArray(
                ref m_FallbackProbeDataHandle,
                1,
                1,
                1,
                GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Point,
                "DDGI Fallback Probe Data");
        }

        private void UploadSceneCache()
        {
            EnsureStructuredBuffer(ref m_InstanceBuffer, m_SceneCache.Instances.Count, Marshal.SizeOf<DDGIInstanceData>());
            EnsureStructuredBuffer(ref m_SubMeshBuffer, m_SceneCache.SubMeshes.Count, Marshal.SizeOf<DDGISubMeshData>());
            EnsureStructuredBuffer(ref m_MaterialBuffer, m_SceneCache.Materials.Count, Marshal.SizeOf<DDGIMaterialData>());
            EnsureStructuredBuffer(ref m_VertexBuffer, m_SceneCache.Vertices.Count, Marshal.SizeOf<DDGIVertexData>());
            EnsureStructuredBuffer(ref m_IndexBuffer, m_SceneCache.Indices.Count, sizeof(uint));

            if (m_SceneCache.Instances.Count > 0)
            {
                m_InstanceBuffer.SetData(m_SceneCache.Instances);
            }

            if (m_SceneCache.SubMeshes.Count > 0)
            {
                m_SubMeshBuffer.SetData(m_SceneCache.SubMeshes);
            }

            if (m_SceneCache.Materials.Count > 0)
            {
                m_MaterialBuffer.SetData(m_SceneCache.Materials);
            }

            if (m_SceneCache.Vertices.Count > 0)
            {
                m_VertexBuffer.SetData(m_SceneCache.Vertices);
            }

            if (m_SceneCache.Indices.Count > 0)
            {
                m_IndexBuffer.SetData(m_SceneCache.Indices);
            }
        }

        private void RebuildAccelerationStructure()
        {
            m_AccelerationStructure ??= new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings
            {
                managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                layerMask = ~0,
            });

            m_AccelerationStructure.ClearInstances();

            for (int instanceIndex = 0; instanceIndex < m_SceneCache.Renderers.Count; instanceIndex++)
            {
                MeshRenderer renderer = m_SceneCache.Renderers[instanceIndex];
                if (renderer == null)
                {
                    continue;
                }

                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                int subMeshCount = mesh != null ? mesh.subMeshCount : 0;
                if (subMeshCount <= 0)
                {
                    continue;
                }

                RayTracingSubMeshFlags[] subMeshFlags = new RayTracingSubMeshFlags[subMeshCount];
                for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                {
                    subMeshFlags[subMeshIndex] = RayTracingSubMeshFlags.Enabled;
                }

                m_AccelerationStructure.AddInstance(
                    renderer,
                    subMeshFlags,
                    enableTriangleCulling: false,
                    frontTriangleCounterClockwise: false,
                    InstanceMask,
                    (uint)instanceIndex);
            }
        }

        private void UpdateLightBuffers(VividLightData lightData)
        {
            int directionalLightCount = lightData != null ? lightData.directionalLightCount : 0;
            int punctualLightCount = lightData != null ? lightData.punctualLightCount : 0;

            EnsureStructuredBuffer(ref m_DirectionalLightBuffer, directionalLightCount, VividLightData.DirectionalLightData.Stride);
            EnsureStructuredBuffer(ref m_PunctualLightBuffer, punctualLightCount, VividLightData.PunctualLightData.Stride);

            if (lightData != null && directionalLightCount > 0)
            {
                m_DirectionalLightBuffer.SetData(lightData.directionalLights, 0, 0, directionalLightCount);
            }

            if (lightData != null && punctualLightCount > 0)
            {
                m_PunctualLightBuffer.SetData(lightData.punctualLights, 0, 0, punctualLightCount);
            }
        }

        private void UploadVolumeConstants(DDGIVolume volume, DDGIProfile profile)
        {
            EnsureStructuredBuffer(ref m_VolumeConstantsBuffer, 1, Marshal.SizeOf<DDGIVolumeDescGPUPacked>());
            DDGIVolumeDescGPUPacked[] upload = { DDGIVolumeDescGPUPacked.Create(volume, profile) };
            m_VolumeConstantsBuffer.SetData(upload);
        }

        private static DDGIRootConstants BuildRootConstants(Vector3Int probeCounts, DDGIProfile profile)
        {
            return new DDGIRootConstants
            {
                volumeIndex = 0u,
                volumeConstantsIndex = 0u,
                volumeResourceIndicesIndex = 0u,
                reductionInputSizeX = (uint)Mathf.Max(probeCounts.x * profile.IrradianceTexelCount, 1),
                reductionInputSizeY = (uint)Mathf.Max(probeCounts.z * profile.IrradianceTexelCount, 1),
                reductionInputSizeZ = (uint)Mathf.Max(probeCounts.y, 1),
            };
        }

        private static int ComputeLayoutHash(DDGIVolume volume, DDGIProfile profile, Vector3Int probeCounts)
        {
            HashCode hash = new HashCode();
            hash.Add(volume != null ? volume.GetEntityId() : EntityId.None);
            hash.Add(probeCounts.x);
            hash.Add(probeCounts.y);
            hash.Add(probeCounts.z);
            hash.Add(profile.RaysPerProbe);
            hash.Add(profile.IrradianceTexelCount);
            hash.Add(profile.DistanceTexelCount);
            return hash.ToHashCode();
        }

        private static int GetProbesPerPlane(Vector3Int probeCounts)
        {
            return Mathf.Max(1, probeCounts.x * probeCounts.z);
        }

        private static bool EnsureTextureArray(
            ref RTHandle handle,
            int width,
            int height,
            int slices,
            GraphicsFormat format,
            FilterMode filterMode,
            string name)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format,
                depthStencilFormat = GraphicsFormat.None,
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = slices,
                msaaSamples = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
            };

            return RenderingUtils.ReAllocateHandleIfNeeded(
                ref handle,
                descriptor,
                filterMode,
                TextureWrapMode.Clamp,
                name: name);
        }

        private static void EnsureStructuredBuffer(ref GraphicsBuffer buffer, int count, int stride)
        {
            int effectiveCount = Mathf.Max(count, 1);
            if (buffer != null && buffer.count == effectiveCount && buffer.stride == stride)
            {
                return;
            }

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, effectiveCount, stride);
        }

        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            buffer?.Dispose();
            buffer = null;
        }

        private static void ReleaseHandle(ref RTHandle handle)
        {
            handle?.Release();
            handle = null;
        }
    }
}
