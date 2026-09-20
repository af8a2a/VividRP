using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.VirtualShadowMap
{
    internal enum VirtualShadowMapUnityCasterFailureReason : byte
    {
        None,
        MissingTerrainMaterial,
        UnsupportedShadowCasterPass,
    }

    internal readonly struct VirtualShadowMapUnityCasterFailure
        : IEquatable<VirtualShadowMapUnityCasterFailure>
    {
        internal VirtualShadowMapUnityCasterFailure(
            Component caster,
            Material material,
            Shader shader,
            int materialSlot,
            string passName,
            VirtualShadowMapUnityCasterFailureReason reason)
        {
            Caster = caster;
            Material = material;
            Shader = shader;
            MaterialSlot = materialSlot;
            PassName = passName;
            Reason = reason;
        }

        internal Component Caster { get; }
        internal Material Material { get; }
        internal Shader Shader { get; }
        internal int MaterialSlot { get; }
        internal string PassName { get; }
        internal VirtualShadowMapUnityCasterFailureReason Reason { get; }

        internal bool IsValid => Caster != null
            && Reason != VirtualShadowMapUnityCasterFailureReason.None;

        public bool Equals(VirtualShadowMapUnityCasterFailure other)
        {
            return Caster == other.Caster
                && Material == other.Material
                && Shader == other.Shader
                && MaterialSlot == other.MaterialSlot
                && string.Equals(PassName, other.PassName, StringComparison.Ordinal)
                && Reason == other.Reason;
        }

        public override bool Equals(object obj)
        {
            return obj is VirtualShadowMapUnityCasterFailure other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Caster);
            hash.Add(Material);
            hash.Add(Shader);
            hash.Add(MaterialSlot);
            hash.Add(PassName);
            hash.Add(Reason);
            return hash.ToHashCode();
        }
    }

    internal static class VirtualShadowMapUnityCasterCompatibility
    {
        internal const string CapabilityTagName = "VividVSMCaster";
        internal const string CapabilityTagValue = "2";
        internal const string CasterKeywordName = "VIVID_VSM_CASTER";
        // Opt-in contract: the ShadowCaster vertex path stays inside the
        // renderer's culling bounds, including any vertex deformation.
        internal const string ConservativeBoundsTagName = "VividVSMConservativeBounds";

        private const string ShadowCasterLightModeName = "ShadowCaster";
        private static readonly ShaderTagId s_LightModeTag = new("LightMode");
        private static readonly ShaderTagId s_ShadowCasterLightMode =
            new(ShadowCasterLightModeName);
        private static readonly ShaderTagId s_CapabilityTag =
            new(CapabilityTagName);
        private static readonly ShaderTagId s_CapabilityValue =
            new(CapabilityTagValue);
        private static readonly ShaderTagId s_ConservativeBoundsTag = new(ConservativeBoundsTagName);
        private static readonly ShaderTagId s_ConservativeBoundsValue = new("1");
        private static readonly List<Material> s_SharedMaterials = new();
        private static readonly HashSet<Material> s_TrackedCasterMaterials = new();

        private static RendererTracker s_RendererTracker;
        private static TerrainTracker s_TerrainTracker;
        private static MaterialTracker s_MaterialTracker;
        private static ShaderTracker s_ShaderTracker;
        private static bool s_TrackingInitialized;
        private static bool s_IsDirty = true;
        private static bool s_IsCompatible = true;
        private static bool s_HasUnboundedCasters = true;
        private static bool s_CollectCasterMaterials;
        private static bool s_HasReportedFailure;
        private static uint s_ValidationRevision;
        private static VirtualShadowMapUnityCasterFailure s_LastFailure;
        private static VirtualShadowMapUnityCasterFailure s_ReportedFailure;

        internal static bool IsReady()
        {
            EnsureTracking();
            ObjectDispatcherService.ProcessUpdates();
            if (s_IsDirty)
                ValidateTrackedCasters();

            return s_IsCompatible;
        }

        internal static bool IsCompatible => s_IsCompatible;
        internal static bool HasUnboundedCasters => s_HasUnboundedCasters;
        internal static uint ValidationRevision => s_ValidationRevision;
        internal static VirtualShadowMapUnityCasterFailure LastFailure => s_LastFailure;

        internal static bool TryValidateRenderer(
            Renderer renderer,
            bool activeOnly,
            out VirtualShadowMapUnityCasterFailure failure)
        {
            failure = default;
            if (renderer == null
                || !IsSceneCaster(renderer)
                || renderer.shadowCastingMode == ShadowCastingMode.Off
                || (activeOnly
                    && (!renderer.enabled
                        || renderer.forceRenderingOff
                        || renderer.gameObject == null
                        || !renderer.gameObject.activeInHierarchy)))
            {
                return true;
            }

            s_SharedMaterials.Clear();
            renderer.GetSharedMaterials(s_SharedMaterials);
            for (int materialIndex = 0;
                 materialIndex < s_SharedMaterials.Count;
                 materialIndex++)
            {
                Material material = s_SharedMaterials[materialIndex];
                if (s_CollectCasterMaterials && material != null)
                    s_TrackedCasterMaterials.Add(material);
                if (TryValidateMaterial(
                        material,
                        out Shader shader,
                        out string passName))
                {
                    continue;
                }

                failure = new VirtualShadowMapUnityCasterFailure(
                    renderer,
                    material,
                    shader,
                    materialIndex,
                    passName,
                    VirtualShadowMapUnityCasterFailureReason.UnsupportedShadowCasterPass);
                s_SharedMaterials.Clear();
                return false;
            }

            s_SharedMaterials.Clear();
            return true;
        }

        internal static bool TryValidateTerrain(
            Terrain terrain,
            bool activeOnly,
            out VirtualShadowMapUnityCasterFailure failure)
        {
            failure = default;
            if (terrain == null
                || !IsSceneCaster(terrain)
                || terrain.shadowCastingMode == ShadowCastingMode.Off
                || (activeOnly
                    && (!terrain.enabled
                        || terrain.gameObject == null
                        || !terrain.gameObject.activeInHierarchy)))
            {
                return true;
            }

            Material material = terrain.materialTemplate;
            if (material == null)
            {
                failure = new VirtualShadowMapUnityCasterFailure(
                    terrain,
                    null,
                    null,
                    0,
                    string.Empty,
                    VirtualShadowMapUnityCasterFailureReason.MissingTerrainMaterial);
                return false;
            }

            if (s_CollectCasterMaterials)
                s_TrackedCasterMaterials.Add(material);

            if (TryValidateMaterial(material, out Shader shader, out string passName))
                return true;

            failure = new VirtualShadowMapUnityCasterFailure(
                terrain,
                material,
                shader,
                0,
                passName,
                VirtualShadowMapUnityCasterFailureReason.UnsupportedShadowCasterPass);
            return false;
        }

        internal static bool HasConservativeBounds(Renderer renderer)
        {
            // Skinned/particle/trail bounds and procedural expansion are not
            // covered by the rigid-mesh contract, even with a tagged material.
            if (renderer is not MeshRenderer)
                return false;
            s_SharedMaterials.Clear();
            renderer.GetSharedMaterials(s_SharedMaterials);
            bool bounded = true;
            for (int i = 0; i < s_SharedMaterials.Count && bounded; i++)
            {
                Material material = s_SharedMaterials[i];
                Shader shader = material != null ? material.shader : null;
                if (shader == null) continue;
                for (int pass = 0; pass < material.passCount; pass++)
                {
                    if (shader.FindPassTagValue(pass, s_LightModeTag).Equals(s_ShadowCasterLightMode)
                        && !shader.FindPassTagValue(pass, s_ConservativeBoundsTag).Equals(s_ConservativeBoundsValue))
                    {
                        bounded = false;
                        break;
                    }
                }
            }
            s_SharedMaterials.Clear();
            return bounded;
        }

        internal static bool TryValidateMaterial(
            Material material,
            out Shader unsupportedShader,
            out string unsupportedPassName)
        {
            unsupportedShader = null;
            unsupportedPassName = string.Empty;
            Shader shader = material != null ? material.shader : null;
            if (shader == null)
                return true;

            int passCount = material.passCount;
            for (int passIndex = 0; passIndex < passCount; passIndex++)
            {
                if (!shader.FindPassTagValue(passIndex, s_LightModeTag)
                        .Equals(s_ShadowCasterLightMode))
                {
                    continue;
                }

                string passName = material.GetPassName(passIndex);
                if (!string.IsNullOrEmpty(passName)
                    && !material.GetShaderPassEnabled(passName))
                {
                    continue;
                }

                if (shader.FindPassTagValue(passIndex, s_CapabilityTag)
                        .Equals(s_CapabilityValue)
                    && shader.keywordSpace.FindKeyword(CasterKeywordName).isValid)
                {
                    continue;
                }

                unsupportedShader = shader;
                unsupportedPassName = passName;
                return false;
            }

            return true;
        }

        private static bool IsSceneCaster(Component caster)
        {
            GameObject gameObject = caster != null ? caster.gameObject : null;
            return gameObject != null
                && gameObject.scene.IsValid()
                && gameObject.scene.isLoaded
                && (caster.hideFlags & HideFlags.DontSave) == 0
                && (gameObject.hideFlags & HideFlags.DontSave) == 0;
        }

        internal static string FormatFailure(
            in VirtualShadowMapUnityCasterFailure failure)
        {
            if (!failure.IsValid)
                return "No incompatible Unity VSM shadow caster was found.";

            string gameObjectName = failure.Caster.gameObject != null
                ? failure.Caster.gameObject.name
                : "<missing GameObject>";
            if (failure.Reason
                == VirtualShadowMapUnityCasterFailureReason.MissingTerrainMaterial)
            {
                return $"Unity VSM caster '{gameObjectName}' ({failure.Caster.GetType().Name}) has no Terrain material template. Assign a VSM-compatible TerrainLit material or disable its shadow casting.";
            }

            string materialName = failure.Material != null
                ? failure.Material.name
                : "<missing Material>";
            string shaderName = failure.Shader != null
                ? failure.Shader.name
                : "<missing Shader>";
            string passName = string.IsNullOrEmpty(failure.PassName)
                ? ShadowCasterLightModeName
                : failure.PassName;
            return $"Unity VSM caster '{gameObjectName}' ({failure.Caster.GetType().Name}) material slot {failure.MaterialSlot} uses material '{materialName}', shader '{shaderName}', pass '{passName}' without the required {CapabilityTagName}={CapabilityTagValue} and {CasterKeywordName} variant contract. Implement the V2 tiled projection through VividRP Core.hlsl and the shared VSM depth writer, then add the variant and capability tag; otherwise disable this caster's shadows.";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTracking()
        {
            ObjectDispatcherService.UnregisterObjectTracker(s_RendererTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_TerrainTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_MaterialTracker);
            ObjectDispatcherService.UnregisterObjectTracker(s_ShaderTracker);
            s_RendererTracker = null;
            s_TerrainTracker = null;
            s_MaterialTracker = null;
            s_ShaderTracker = null;
            s_TrackedCasterMaterials.Clear();
            s_SharedMaterials.Clear();
            s_TrackingInitialized = false;
            s_IsDirty = true;
            s_IsCompatible = true;
            s_HasUnboundedCasters = true;
            s_CollectCasterMaterials = false;
            s_HasReportedFailure = false;
            s_ValidationRevision = 0u;
            s_LastFailure = default;
            s_ReportedFailure = default;
        }

        private static void EnsureTracking()
        {
            if (s_TrackingInitialized)
                return;

            s_TrackingInitialized = true;
            s_RendererTracker = new RendererTracker();
            s_TerrainTracker = new TerrainTracker();
            s_MaterialTracker = new MaterialTracker();
            s_ShaderTracker = new ShaderTracker();
            ObjectDispatcherService.RegisterObjectTracker(s_RendererTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_TerrainTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_MaterialTracker);
            ObjectDispatcherService.RegisterObjectTracker(s_ShaderTracker);
            s_IsDirty = true;
        }

        private static void ValidateTrackedCasters()
        {
            VirtualShadowMapUnityCasterFailure failure = default;
            bool compatible = true;
            s_HasUnboundedCasters = false;
            s_TrackedCasterMaterials.Clear();
            s_CollectCasterMaterials = true;
            try
            {
                Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include);
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (IsSceneCaster(renderer) && renderer.enabled && !renderer.forceRenderingOff
                        && renderer.gameObject.activeInHierarchy && renderer.shadowCastingMode != ShadowCastingMode.Off
                        && !HasConservativeBounds(renderer))
                        s_HasUnboundedCasters = true;
                    if (TryValidateRenderer(
                            renderer,
                            activeOnly: true,
                            out failure))
                    {
                        continue;
                    }

                    compatible = false;
                    break;
                }

                if (compatible)
                {
                    Terrain[] terrains = Object.FindObjectsByType<Terrain>(
                        FindObjectsInactive.Include);
                    for (int terrainIndex = 0;
                         terrainIndex < terrains.Length;
                         terrainIndex++)
                    {
                        Terrain terrain = terrains[terrainIndex];
                        // Trees/detail rendering is not covered by the mesh tag.
                        if (IsSceneCaster(terrain) && terrain.enabled && terrain.gameObject.activeInHierarchy
                            && terrain.shadowCastingMode != ShadowCastingMode.Off)
                            s_HasUnboundedCasters = true;
                        if (TryValidateTerrain(
                                terrain,
                                activeOnly: true,
                                out failure))
                        {
                            continue;
                        }

                        compatible = false;
                        break;
                    }
                }
            }
            finally
            {
                s_CollectCasterMaterials = false;
            }

            s_IsDirty = false;
            s_IsCompatible = compatible;
            s_LastFailure = compatible ? default : failure;
            s_ValidationRevision++;
            if (compatible)
            {
                s_HasReportedFailure = false;
                s_ReportedFailure = default;
                return;
            }

            if (s_HasReportedFailure && s_ReportedFailure.Equals(failure))
                return;

            s_HasReportedFailure = true;
            s_ReportedFailure = failure;
            Debug.LogError(
                $"[VividRP] Virtual Shadow Map disabled for this frame. {FormatFailure(failure)} Conventional CSM remains active.",
                failure.Caster);
        }

        private sealed class RendererTracker : ObjectTracker<Renderer>
        {
            internal RendererTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (changed.Count > 0 || destroyedId.Length > 0)
                    s_IsDirty = true;
            }
        }

        private sealed class TerrainTracker : ObjectTracker<Terrain>
        {
            internal TerrainTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (changed.Count > 0 || destroyedId.Length > 0)
                    s_IsDirty = true;
            }
        }

        private sealed class MaterialTracker : ObjectTracker<Material>
        {
            internal MaterialTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.Default)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (destroyedId.Length > 0)
                {
                    s_IsDirty = true;
                    return;
                }

                for (int changedIndex = 0;
                     changedIndex < changed.Count;
                     changedIndex++)
                {
                    if (changed[changedIndex] is Material material
                        && s_TrackedCasterMaterials.Contains(material))
                    {
                        s_IsDirty = true;
                        return;
                    }
                }
            }
        }

        private sealed class ShaderTracker : ObjectTracker<Shader>
        {
            internal ShaderTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.Assets)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                if (destroyedId.Length > 0)
                {
                    s_IsDirty = true;
                    return;
                }

                for (int changedIndex = 0;
                     changedIndex < changed.Count;
                     changedIndex++)
                {
                    if (changed[changedIndex] is not Shader shader)
                        continue;

                    foreach (Material material in s_TrackedCasterMaterials)
                    {
                        if (material != null && material.shader == shader)
                        {
                            s_IsDirty = true;
                            return;
                        }
                    }
                }
            }
        }
    }
}
