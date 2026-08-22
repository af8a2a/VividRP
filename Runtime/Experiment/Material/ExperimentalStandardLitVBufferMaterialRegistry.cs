using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using UnityMaterial = UnityEngine.Material;

namespace VividRP.Runtime.Experimental.Material
{
    internal static class ExperimentalStandardLitVBufferMaterialRegistry
    {
        internal const string ShaderName = "VividRP/Experimental/Material/StandardLit";
        internal const string MaterialIndexPropertyName = "_VividExperimentalVBufferMaterialIndex";

        private const int InitialCapacity = 16;
        private const int MissingBridgeScanInterval = 120;

        private static readonly int s_MaterialIndexId = Shader.PropertyToID(MaterialIndexPropertyName);
        private static readonly Dictionary<MeshRenderer, int> s_Renderers = new();
        private static readonly Dictionary<EntityId, DesiredMaterial> s_DesiredMaterials = new();
        private static readonly Dictionary<EntityId, MaterialEntry> s_Entries = new();
        private static readonly List<MaterialEntry> s_Slots = new() { null };
        private static readonly Stack<int> s_FreeSlots = new();
        private static readonly List<EntityId> s_RemovedMaterialIds = new();
        private static readonly List<MeshRenderer> s_RemovedRenderers = new();
        private static readonly HashSet<string> s_IssuedWarnings = new();
        private static readonly MaterialPropertyBlock s_PropertyBlock = new();

        private static GraphicsBuffer s_MaterialBuffer;
        private static ExperimentalVBufferMaterialData[] s_Upload = Array.Empty<ExperimentalVBufferMaterialData>();
        private static VividGPUDrivenSystem s_System;
        private static bool s_Initialized;
        private static int s_LastMissingBridgeScanFrame = int.MinValue;

        private sealed class MaterialEntry
        {
            internal UnityMaterial Material;
            internal int Slot;
            internal int ReferenceCount;
            internal int ContentHash;
            internal bool Valid;
            internal ExperimentalVBufferMaterialData Data;
            internal VirtualTextureGPUDrivenTextureBackend.ExternalSurfaceBindingLease BaseLease;
            internal VirtualTextureGPUDrivenTextureBackend.ExternalSurfaceBindingLease AuxiliaryLease;

            internal void ReleaseBindings()
            {
                BaseLease?.Dispose();
                AuxiliaryLease?.Dispose();
                BaseLease = null;
                AuxiliaryLease = null;
                Valid = false;
            }
        }

        private readonly struct DesiredMaterial
        {
            internal DesiredMaterial(UnityMaterial material, int referenceCount)
            {
                Material = material;
                ReferenceCount = referenceCount;
            }

            internal UnityMaterial Material { get; }
            internal int ReferenceCount { get; }

            internal DesiredMaterial AddReference()
            {
                return new DesiredMaterial(Material, ReferenceCount + 1);
            }
        }

        internal static int RegisteredRendererCount => s_Renderers.Count;
        internal static int RegisteredMaterialCount => s_Entries.Count;
        internal static GraphicsBuffer MaterialBuffer => s_MaterialBuffer;
        internal static int MaterialCount => Mathf.Max(1, s_Slots.Count);

        internal static int GetReferenceCount(UnityMaterial material)
        {
            return material != null
                   && s_Entries.TryGetValue(material.GetEntityId(), out MaterialEntry entry)
                ? entry.ReferenceCount
                : 0;
        }

        internal static int GetMaterialSlot(UnityMaterial material)
        {
            return material != null
                   && s_Entries.TryGetValue(material.GetEntityId(), out MaterialEntry entry)
                ? entry.Slot
                : 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            Shutdown();
        }

        internal static void Register(MeshRenderer renderer)
        {
            if (renderer == null)
                return;

            EnsureInitialized();
            if (s_Renderers.TryGetValue(renderer, out int referenceCount))
            {
                s_Renderers[renderer] = referenceCount + 1;
                return;
            }

            s_Renderers.Add(renderer, 1);
            ApplyRendererMaterialIndices(renderer, useValidSlots: false);
        }

        internal static void Unregister(MeshRenderer renderer)
        {
            if (ReferenceEquals(renderer, null))
                return;

            if (!s_Renderers.TryGetValue(renderer, out int referenceCount))
                return;

            if (referenceCount > 1)
            {
                s_Renderers[renderer] = referenceCount - 1;
                return;
            }

            if (renderer != null)
                ApplyRendererMaterialIndices(renderer, useValidSlots: false);
            s_Renderers.Remove(renderer);
        }

        internal static void MarkDirty()
        {
            foreach (MaterialEntry entry in s_Entries.Values)
                entry.ContentHash = int.MinValue;
        }

        internal static bool Prepare(VividGPUDrivenSystem system, out string unavailableReason)
        {
            EnsureInitialized();
            if (!ReferenceEquals(s_System, system))
            {
                s_System = system;
                InvalidateBindings();
            }

            CollectDesiredMaterials();
            ReconcileEntries();

            bool backendAvailable = system != null
                                    && system.IsAvailable
                                    && system.UsesVirtualTexture;
            unavailableReason = backendAvailable
                ? string.Empty
                : system == null
                    ? "The GPUDriven system is not initialized."
                    : system.UsesVirtualTexture
                        ? system.UnavailableReason
                        : "The active GPUDriven texture backend is not Virtual Texture.";

            for (int slot = 1; slot < s_Slots.Count; slot++)
            {
                MaterialEntry entry = s_Slots[slot];
                if (entry == null || entry.Material == null)
                    continue;

                int contentHash = ComputeContentHash(entry.Material);
                if (!backendAvailable)
                {
                    entry.ReleaseBindings();
                    entry.Data = CreateErrorRecord();
                    entry.ContentHash = contentHash;
                    WarnOnce("backend", unavailableReason, entry.Material);
                    continue;
                }

                if (entry.Valid && entry.ContentHash == contentHash)
                    continue;

                entry.ReleaseBindings();
                if (TryBuildRecord(system, entry.Material, out ExperimentalVBufferMaterialData data, out string reason))
                {
                    entry.Data = data;
                    entry.ContentHash = contentHash;
                    entry.Valid = true;
                }
                else
                {
                    entry.Data = CreateErrorRecord();
                    entry.ContentHash = contentHash;
                    WarnOnce("record", reason, entry.Material);
                }
            }

            ApplyRendererMaterialIndices();
            UploadMaterialTable();
            return backendAvailable;
        }

        internal static void WarnAboutMissingBridges(int frameIndex)
        {
            if (frameIndex == s_LastMissingBridgeScanFrame
                || (s_LastMissingBridgeScanFrame != int.MinValue
                    && frameIndex > s_LastMissingBridgeScanFrame
                    && frameIndex - s_LastMissingBridgeScanFrame < MissingBridgeScanInterval))
            {
                return;
            }

            s_LastMissingBridgeScanFrame = frameIndex;
            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                FindObjectsInactive.Exclude);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null || s_Renderers.ContainsKey(renderer))
                    continue;

                UnityMaterial[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (!IsSupportedMaterial(materials[materialIndex]))
                        continue;

                    WarnOnce(
                        $"bridge:{renderer.GetEntityId()}",
                        $"MeshRenderer '{renderer.name}' is missing {nameof(ExperimentalStandardLitVBufferRenderer)}.",
                        renderer);
                    break;
                }
            }
        }

        internal static void Shutdown()
        {
            if (s_Initialized)
                FrameContextSystem.SubsystemDispose -= Shutdown;

            foreach (MeshRenderer renderer in s_Renderers.Keys)
            {
                if (renderer != null)
                    ApplyRendererMaterialIndices(renderer, useValidSlots: false);
            }

            foreach (MaterialEntry entry in s_Entries.Values)
            {
                entry.ReleaseBindings();
                if (entry.Material != null)
                    entry.Material.SetFloat(s_MaterialIndexId, 0.0f);
            }

            s_MaterialBuffer?.Dispose();
            s_MaterialBuffer = null;
            s_Upload = Array.Empty<ExperimentalVBufferMaterialData>();
            s_Renderers.Clear();
            s_DesiredMaterials.Clear();
            s_Entries.Clear();
            s_Slots.Clear();
            s_Slots.Add(null);
            s_FreeSlots.Clear();
            s_RemovedMaterialIds.Clear();
            s_RemovedRenderers.Clear();
            s_IssuedWarnings.Clear();
            s_System = null;
            s_LastMissingBridgeScanFrame = int.MinValue;
            s_Initialized = false;
        }

        private static void EnsureInitialized()
        {
            if (s_Initialized)
                return;

            FrameContextSystem.SubsystemDispose -= Shutdown;
            FrameContextSystem.SubsystemDispose += Shutdown;
            s_Initialized = true;
        }

        private static void CollectDesiredMaterials()
        {
            s_DesiredMaterials.Clear();
            s_RemovedRenderers.Clear();
            foreach (MeshRenderer renderer in s_Renderers.Keys)
            {
                if (renderer == null)
                {
                    s_RemovedRenderers.Add(renderer);
                    continue;
                }

                UnityMaterial[] materials = renderer.sharedMaterials;
                var rendererMaterialIds = new HashSet<EntityId>();
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    UnityMaterial material = materials[materialIndex];
                    if (!IsSupportedMaterial(material))
                        continue;

                    EntityId id = material.GetEntityId();
                    if (!rendererMaterialIds.Add(id))
                        continue;

                    if (s_DesiredMaterials.TryGetValue(id, out DesiredMaterial desired))
                        s_DesiredMaterials[id] = desired.AddReference();
                    else
                        s_DesiredMaterials.Add(id, new DesiredMaterial(material, 1));
                }
            }

            for (int index = 0; index < s_RemovedRenderers.Count; index++)
                s_Renderers.Remove(s_RemovedRenderers[index]);
        }

        private static void ReconcileEntries()
        {
            s_RemovedMaterialIds.Clear();
            foreach (KeyValuePair<EntityId, MaterialEntry> pair in s_Entries)
            {
                if (!s_DesiredMaterials.ContainsKey(pair.Key))
                    s_RemovedMaterialIds.Add(pair.Key);
            }

            for (int index = 0; index < s_RemovedMaterialIds.Count; index++)
                RemoveEntry(s_RemovedMaterialIds[index]);

            foreach (KeyValuePair<EntityId, DesiredMaterial> pair in s_DesiredMaterials)
            {
                if (s_Entries.TryGetValue(pair.Key, out MaterialEntry entry))
                {
                    entry.ReferenceCount = pair.Value.ReferenceCount;
                    continue;
                }

                int slot = s_FreeSlots.Count > 0 ? s_FreeSlots.Pop() : s_Slots.Count;
                entry = new MaterialEntry
                {
                    Material = pair.Value.Material,
                    Slot = slot,
                    ReferenceCount = pair.Value.ReferenceCount,
                    ContentHash = int.MinValue,
                    Data = CreateErrorRecord(),
                };
                if (slot == s_Slots.Count)
                    s_Slots.Add(entry);
                else
                    s_Slots[slot] = entry;
                entry.Material.SetFloat(s_MaterialIndexId, 0.0f);
                s_Entries.Add(pair.Key, entry);
            }
        }

        private static void RemoveEntry(EntityId materialId)
        {
            if (!s_Entries.Remove(materialId, out MaterialEntry entry))
                return;

            entry.ReleaseBindings();
            if (entry.Material != null)
                entry.Material.SetFloat(s_MaterialIndexId, 0.0f);
            s_Slots[entry.Slot] = null;
            s_FreeSlots.Push(entry.Slot);
        }

        private static void InvalidateBindings()
        {
            foreach (MaterialEntry entry in s_Entries.Values)
            {
                entry.ReleaseBindings();
                entry.ContentHash = int.MinValue;
                entry.Data = CreateErrorRecord();
                if (entry.Material != null)
                    entry.Material.SetFloat(s_MaterialIndexId, 0.0f);
            }
        }

        private static void ApplyRendererMaterialIndices()
        {
            foreach (MeshRenderer renderer in s_Renderers.Keys)
            {
                if (renderer != null)
                    ApplyRendererMaterialIndices(renderer, useValidSlots: true);
            }
        }

        private static void ApplyRendererMaterialIndices(
            MeshRenderer renderer,
            bool useValidSlots)
        {
            UnityMaterial[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                int slot = 0;
                UnityMaterial material = materials[materialIndex];
                if (useValidSlots
                    && IsSupportedMaterial(material)
                    && s_Entries.TryGetValue(material.GetEntityId(), out MaterialEntry entry)
                    && entry.Valid)
                {
                    slot = entry.Slot;
                }

                s_PropertyBlock.Clear();
                renderer.GetPropertyBlock(s_PropertyBlock, materialIndex);
                s_PropertyBlock.SetFloat(s_MaterialIndexId, slot);
                renderer.SetPropertyBlock(s_PropertyBlock, materialIndex);
            }
        }

        private static bool TryBuildRecord(
            VividGPUDrivenSystem system,
            UnityMaterial material,
            out ExperimentalVBufferMaterialData data,
            out string reason)
        {
            data = CreateErrorRecord();
            bool normalEnabled = material.IsKeywordEnabled("_NORMALMAP");
            bool rmoEnabled = material.IsKeywordEnabled("_RMOMAP");
            bool metallicEnabled = !rmoEnabled && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP");
            bool roughnessEnabled = !rmoEnabled && material.IsKeywordEnabled("_ROUGHNESSMAP");
            bool occlusionEnabled = !rmoEnabled && material.IsKeywordEnabled("_OCCLUSIONMAP");
            bool emissionEnabled = material.IsKeywordEnabled("_EMISSION");
            if (metallicEnabled && roughnessEnabled)
            {
                reason = "The transition VBuffer supports either Metallic Map or Roughness Map, not both simultaneously.";
                return false;
            }

            Texture baseMap = material.GetTexture("_BaseMap");
            Texture normalMap = normalEnabled ? material.GetTexture("_BumpMap") : null;
            Texture maskMap = rmoEnabled
                ? material.GetTexture("_RMOMap")
                : metallicEnabled
                    ? material.GetTexture("_MetallicGlossMap")
                    : roughnessEnabled
                        ? material.GetTexture("_RoughnessMap")
                        : null;
            var baseTextures = new GPUDrivenSurfaceTextureSet(
                null,
                baseMap,
                normalMap,
                maskMap,
                rmoEnabled
                    ? GPUDrivenMaterialMaskMode.RoughnessMetallicOcclusion
                    : metallicEnabled
                        ? GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness
                        : roughnessEnabled
                            ? GPUDrivenMaterialMaskMode.Roughness
                            : GPUDrivenMaterialMaskMode.None);
            if (!system.TryAcquireExternalSurfaceBinding(baseTextures, out var baseLease, out reason))
                return false;

            VirtualTextureGPUDrivenTextureBackend.ExternalSurfaceBindingLease auxiliaryLease = null;
            try
            {
                Texture emissionMap = emissionEnabled ? material.GetTexture("_EmissionMap") : null;
                Texture occlusionMap = occlusionEnabled ? material.GetTexture("_OcclusionMap") : null;
                if (emissionMap != null || occlusionMap != null)
                {
                    var auxiliaryTextures = new GPUDrivenSurfaceTextureSet(
                        null,
                        emissionMap,
                        null,
                        occlusionMap,
                        occlusionEnabled
                            ? GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness
                            : GPUDrivenMaterialMaskMode.None);
                    if (!system.TryAcquireExternalSurfaceBinding(
                            auxiliaryTextures,
                            out auxiliaryLease,
                            out reason))
                    {
                        baseLease.Dispose();
                        return false;
                    }
                }

                ExperimentalVBufferMaterialFeatureFlags featureFlags =
                    ExperimentalVBufferMaterialFeatureFlags.None;
                if (normalEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.NormalMap;
                if (rmoEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.RMOMap;
                else if (metallicEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.MetallicMap;
                else if (roughnessEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.RoughnessMap;
                if (material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"))
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.SmoothnessFromAlbedoAlpha;
                if (occlusionEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.OcclusionMap;
                if (emissionEnabled)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.EmissionMap;
                if (material.IsKeywordEnabled("_CLEARCOAT"))
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.ClearCoat;
                if (GetFloat(material, "_ReceiveSSR", 1.0f) > 0.5f)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.ReceiveSSR;
                if (GetFloat(material, "_ReceiveDecals", 1.0f) > 0.5f)
                    featureFlags |= ExperimentalVBufferMaterialFeatureFlags.ReceiveDecals;

                data.BaseBinding = baseLease.Binding;
                data.AuxiliaryBinding = auxiliaryLease?.Binding ?? CreateEmptyBinding();
                data.BaseColor = ToFloat4(material.GetColor("_BaseColor"));
                data.BaseMapST = ToFloat4(material.GetVector("_BaseMap_ST"));
                data.EmissionColor = ToFloat4(material.GetColor("_EmissionColor"));
                data.BaseSurface = new float4(
                    GetFloat(material, "_Metallic", 0.0f),
                    GetFloat(material, "_Smoothness", 0.5f),
                    GetFloat(material, "_BumpScale", 1.0f),
                    GetFloat(material, "_OcclusionStrength", 1.0f));
                data.BaseRemap0 = new float4(
                    GetFloat(material, "_MetallicRemapMin", 0.0f),
                    GetFloat(material, "_MetallicRemapMax", 1.0f),
                    GetFloat(material, "_SmoothnessRemapMin", 0.0f),
                    GetFloat(material, "_SmoothnessRemapMax", 1.0f));
                data.BaseRemap1 = new float4(
                    GetFloat(material, "_AORemapMin", 0.0f),
                    GetFloat(material, "_AORemapMax", 1.0f),
                    GetFloat(material, "_SpecularIOR", 1.5f),
                    GetFloat(material, "_ClearCoatMask", 0.0f));
                data.BaseClosure = new float4(
                    GetFloat(material, "_ClearCoatSmoothness", 1.0f),
                    GetFloat(material, "_TransmissionWeight", 0.0f),
                    GetFloat(material, "_SubsurfaceWeight", 0.0f),
                    0.0f);
                data.FeatureFlags = new uint4((uint)featureFlags, 0u, 0u, 0u);

                MaterialEntry entry = s_Entries[material.GetEntityId()];
                entry.BaseLease = baseLease;
                entry.AuxiliaryLease = auxiliaryLease;
                reason = string.Empty;
                return true;
            }
            catch
            {
                baseLease.Dispose();
                auxiliaryLease?.Dispose();
                throw;
            }
        }

        private static void UploadMaterialTable()
        {
            int count = Mathf.Max(1, s_Slots.Count);
            int stride = Marshal.SizeOf<ExperimentalVBufferMaterialData>();
            if (stride != ExperimentalVBufferContract.MaterialRecordStride)
            {
                throw new InvalidOperationException(
                    $"Experimental VBuffer material stride is {stride}; expected {ExperimentalVBufferContract.MaterialRecordStride}.");
            }

            if (s_Upload.Length < count)
                s_Upload = new ExperimentalVBufferMaterialData[Mathf.NextPowerOfTwo(count)];
            Array.Clear(s_Upload, 0, count);
            s_Upload[0] = CreateErrorRecord();
            for (int slot = 1; slot < s_Slots.Count; slot++)
                s_Upload[slot] = s_Slots[slot]?.Data ?? s_Upload[0];

            if (s_MaterialBuffer == null
                || s_MaterialBuffer.count < count
                || s_MaterialBuffer.stride != stride)
            {
                s_MaterialBuffer?.Dispose();
                s_MaterialBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    Mathf.NextPowerOfTwo(count),
                    stride)
                {
                    name = "Vivid Experimental VBuffer Materials",
                };
            }
            s_MaterialBuffer.SetData(s_Upload, 0, 0, count);
        }

        private static ExperimentalVBufferMaterialData CreateErrorRecord()
        {
            return new ExperimentalVBufferMaterialData
            {
                BaseBinding = CreateEmptyBinding(),
                AuxiliaryBinding = CreateEmptyBinding(),
                BaseColor = new float4(1.0f, 0.0f, 1.0f, 1.0f),
                BaseMapST = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                BaseSurface = new float4(0.0f, 0.25f, 1.0f, 1.0f),
                BaseRemap0 = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                BaseRemap1 = new float4(0.0f, 1.0f, 1.5f, 0.0f),
                BaseClosure = new float4(1.0f, 0.0f, 0.0f, 0.0f),
            };
        }

        private static VividSurfaceBindingData CreateEmptyBinding()
        {
            return new VividSurfaceBindingData
            {
                BaseColorResource = VividSurfaceBindingData.InvalidResource,
                NormalResource = VividSurfaceBindingData.InvalidResource,
                MaskResource = VividSurfaceBindingData.InvalidResource,
                Flags = VividSurfaceBindingFlags.None,
            };
        }

        private static bool IsSupportedMaterial(UnityMaterial material)
        {
            return material != null
                   && material.shader != null
                   && material.shader.name == ShaderName;
        }

        private static int ComputeContentHash(UnityMaterial material)
        {
            var hash = new HashCode();
            string[] textures =
            {
                "_BaseMap", "_BumpMap", "_RMOMap", "_MetallicGlossMap", "_RoughnessMap",
                "_EmissionMap", "_OcclusionMap",
            };
            for (int index = 0; index < textures.Length; index++)
            {
                Texture texture = material.GetTexture(textures[index]);
                hash.Add(texture != null ? texture.GetEntityId() : EntityId.None);
            }

            string[] vectors =
            {
                "_BaseColor", "_BaseMap_ST", "_EmissionColor",
            };
            for (int index = 0; index < vectors.Length; index++)
                hash.Add(material.GetVector(vectors[index]));

            string[] floats =
            {
                "_Metallic", "_Smoothness", "_BumpScale", "_OcclusionStrength",
                "_MetallicRemapMin", "_MetallicRemapMax", "_SmoothnessRemapMin", "_SmoothnessRemapMax",
                "_AORemapMin", "_AORemapMax", "_SpecularIOR", "_ClearCoatMask", "_ClearCoatSmoothness",
                "_TransmissionWeight", "_SubsurfaceWeight", "_ReceiveSSR", "_ReceiveDecals",
            };
            for (int index = 0; index < floats.Length; index++)
                hash.Add(GetFloat(material, floats[index], 0.0f));

            string[] keywords =
            {
                "_NORMALMAP", "_RMOMAP", "_METALLICSPECGLOSSMAP", "_ROUGHNESSMAP", "_OCCLUSIONMAP",
                "_EMISSION", "_CLEARCOAT", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
            };
            for (int index = 0; index < keywords.Length; index++)
                hash.Add(material.IsKeywordEnabled(keywords[index]));
            return hash.ToHashCode();
        }

        private static float GetFloat(UnityMaterial material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static float4 ToFloat4(Vector4 value)
        {
            return new float4(value.x, value.y, value.z, value.w);
        }

        private static float4 ToFloat4(Color value)
        {
            return new float4(value.r, value.g, value.b, value.a);
        }

        private static void WarnOnce(string category, string reason, UnityEngine.Object context)
        {
            string key = $"{category}:{reason}";
            if (!s_IssuedWarnings.Add(key))
                return;

            Debug.LogWarning(
                $"[VividRP] Experimental StandardLit VBuffer resolved to error material slot 0: {reason}",
                context);
        }
    }
}
