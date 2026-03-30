using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class SkyManager
    {
        private static readonly Dictionary<SkyType, ISkyRenderer> s_Renderers = new();
        private static readonly VividSkyData s_CachedSkyData = new();
        private static readonly SkyAmbientProbeConvolution s_AmbientProbeConvolution = new();

        private static bool s_Initialized;
        private static bool s_UpdateRequested;
        private static float s_LastUpdateTime;

        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            RegisterRenderer(new HDRISkyRenderer(s_AmbientProbeConvolution), resources);
            RegisterRenderer(new PhysicallyBasedSkyRenderer(), resources);
            s_AmbientProbeConvolution.Build(resources);
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

            UpdateDiffuseAmbientProbe(cmd, s_CachedSkyData);

            skyData.CopyFrom(s_CachedSkyData);
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

        private static void UpdateDiffuseAmbientProbe(CommandBuffer cmd, VividSkyData skyData)
        {
            if (skyData != null && skyData.specularCubemap != null)
            {
                skyData.hasDiffuseSH = false;
                skyData.diffuseSH = default;

                if (skyData.activeSkyType != SkyType.HDRI && s_AmbientProbeConvolution.IsSupported)
                {
                    s_AmbientProbeConvolution.RequestUpdate(
                        cmd,
                        skyData.specularCubemap,
                        skyData.tint,
                        skyData.exposure,
                        skyData.rotation,
                        skyData.skyHash);
                }

                s_AmbientProbeConvolution.BindGlobalBuffer(cmd);
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
