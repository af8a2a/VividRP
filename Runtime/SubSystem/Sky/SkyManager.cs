using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class SkyManager
    {
        private static readonly Dictionary<SkyType, ISkyRenderer> s_Renderers = new();
        private static readonly VividSkyData s_CachedSkyData = new();
        private static readonly SkyAmbientProbeConvolution s_AmbientProbeConvolution = new();
        private static readonly SkySpecularCache s_SpecularCache = new();

        private static bool s_Initialized;
        private static bool s_UpdateRequested;
        private static float s_LastUpdateTime;

        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            RegisterRenderer(new HDRISkyRenderer(), resources);
            RegisterRenderer(new PhysicallyBasedSkyRenderer(), resources);
            s_AmbientProbeConvolution.Build(resources);
            s_SpecularCache.Build(resources);
            s_CachedSkyData.Reset();
            s_LastUpdateTime = float.NegativeInfinity;
            s_UpdateRequested = false;
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            foreach (var renderer in s_Renderers.Values)
                renderer.Dispose();

            s_AmbientProbeConvolution.Cleanup();
            s_SpecularCache.Dispose();
            s_Renderers.Clear();
            s_CachedSkyData.Reset();
            s_LastUpdateTime = float.NegativeInfinity;
            s_UpdateRequested = false;
            s_Initialized = false;
        }

        internal static void RequestUpdate()
        {
            s_UpdateRequested = true;
        }

        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            if (frameData == null)
                return;

            if (!s_Initialized)
                Initialize();

            var skyData = frameData.GetOrCreate<VividSkyData>();
            skyData.Reset();

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var activeSkyType = skySettings?.skyType.value ?? SkyType.HDRI;
            var hasActiveSky =
                s_Renderers.TryGetValue(activeSkyType, out var renderer)
                && activeSkyType != SkyType.None
                && renderer != null
                && renderer.IsActive();

            if (hasActiveSky)
            {
                var context = new SkyRendererContext(
                    frameData.GetOrCreate<VividCameraData>(),
                    frameData.GetOrCreate<VividLightData>());
                var skyHash = renderer.GetSkyHash(context);

                if (NeedsUpdate(activeSkyType, skySettings, skyHash))
                {
                    s_CachedSkyData.Reset();
                    renderer.Update(context, s_CachedSkyData, cmd);
                    s_CachedSkyData.activeSkyType = activeSkyType;
                    s_CachedSkyData.skyHash = skyHash;
                    s_LastUpdateTime = Time.realtimeSinceStartup;
                    s_UpdateRequested = false;
                }
            }
            else
            {
                s_CachedSkyData.Reset();
            }

            UpdateSpecularCubemap(cmd, s_CachedSkyData);
            UpdateDiffuseAmbientProbe(cmd, s_CachedSkyData);

            skyData.CopyFrom(s_CachedSkyData);
        }

        internal static RTHandle GetSpecularCubemapHandle()
        {
            if (!s_Initialized)
                Initialize();

            return s_SpecularCache.Cubemap;
        }

        internal static int GetSpecularCubemapMaxMip(VividSkyData skyData = null)
        {
            if (!s_Initialized)
                Initialize();

            var source = skyData?.specularCubemap;
            if (skyData != null && !s_SpecularCache.HasSource(source))
                return source != null ? Mathf.Max(0, source.mipmapCount - 1) : 0;

            return s_SpecularCache.MaxMipLevel;
        }

        internal static void ImportSpecularCubemap(RenderGraphTexture texture, VividSkyData skyData = null)
        {
            if (texture == null || !PassRecorder.IsPassTextureImportActive)
                return;

            UpdateSpecularCubemap(skyData);
            var handle = GetSpecularCubemapHandle();
            if (handle != null)
                PassRecorder.ImportTexture(texture, handle);
        }

        private static bool NeedsUpdate(SkyType skyType, SkySettingsVolume skySettings, int skyHash)
        {
            if (s_CachedSkyData.activeSkyType != skyType || s_CachedSkyData.specularCubemap == null)
                return true;

            if (s_UpdateRequested)
                return true;

            var updateMode = skySettings?.updateMode.value ?? SkyUpdateMode.OnChanged;
            var updatePeriod = skySettings?.updatePeriod.value ?? 0.0f;
            var elapsedTime = Time.realtimeSinceStartup - s_LastUpdateTime;

            return updateMode switch
            {
                SkyUpdateMode.OnDemand => false,
                SkyUpdateMode.Realtime => skyHash != s_CachedSkyData.skyHash || updatePeriod <= 0.0f || elapsedTime >= updatePeriod,
                _ => skyHash != s_CachedSkyData.skyHash,
            };
        }

        private static void RegisterRenderer(ISkyRenderer renderer, VividRPCoreResources resources)
        {
            renderer.Build(resources);
            s_Renderers[renderer.Type] = renderer;
        }

        private static void UpdateSpecularCubemap(CommandBuffer cmd, VividSkyData skyData)
        {
            s_SpecularCache.Update(
                cmd,
                skyData?.specularCubemap,
                skyData?.skyHash ?? 0);
        }

        private static void UpdateSpecularCubemap(VividSkyData skyData)
        {
            s_SpecularCache.Update(
                skyData?.specularCubemap,
                skyData?.skyHash ?? 0);
        }

        private static void UpdateDiffuseAmbientProbe(CommandBuffer cmd, VividSkyData skyData)
        {
            if (skyData != null && skyData.ambientProbeCubemap != null)
            {
                skyData.hasDiffuseSH = false;
                skyData.diffuseSH = default;
                var useDefaultBuffer = !s_AmbientProbeConvolution.IsSupported;

                if (!useDefaultBuffer)
                {
                    s_AmbientProbeConvolution.RequestUpdate(
                        cmd,
                        skyData.ambientProbeCubemap,
                        skyData.ambientProbeTint,
                        skyData.ambientProbeExposure,
                        skyData.ambientProbeRotation,
                        skyData.ambientProbeHash);
                }

                s_AmbientProbeConvolution.BindGlobalBuffer(cmd, useDefaultBuffer);
                return;
            }

            if (skyData != null)
            {
                skyData.hasDiffuseSH = false;
                skyData.diffuseSH = default;
            }

            s_AmbientProbeConvolution.BindGlobalBuffer(cmd, true);
        }
    }
}
