using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class SkyManager
    {
        private static readonly Dictionary<SkyType, ISkyRenderer> s_Renderers = new();
        private static readonly VividSkyData s_CachedSkyData = new();

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
            s_CachedSkyData.Reset();
            s_LastUpdateTime = float.NegativeInfinity;
            s_UpdateRequested = false;
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
            foreach (var renderer in s_Renderers.Values)
                renderer.Dispose();

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

        internal static void Update(ContextContainer frameData)
        {
            if (frameData == null)
                return;

            if (!s_Initialized)
                Initialize();

            var skyData = frameData.GetOrCreate<VividSkyData>();
            skyData.Reset();

            var skySettings = VividVolumeManagerUtility.GetSkySettingsVolume();
            var activeSkyType = skySettings?.skyType.value ?? SkyType.HDRI;
            if (activeSkyType == SkyType.None)
            {
                s_CachedSkyData.Reset();
                return;
            }

            if (!s_Renderers.TryGetValue(activeSkyType, out var renderer) || renderer == null || !renderer.IsActive())
            {
                s_CachedSkyData.Reset();
                return;
            }

            var context = new SkyRendererContext(
                frameData.GetOrCreate<VividCameraData>(),
                frameData.GetOrCreate<VividLightData>());
            var skyHash = renderer.GetSkyHash(context);

            if (!NeedsUpdate(activeSkyType, skySettings, skyHash))
            {
                skyData.CopyFrom(s_CachedSkyData);
                return;
            }

            s_CachedSkyData.Reset();
            renderer.Update(context, s_CachedSkyData);
            s_CachedSkyData.activeSkyType = activeSkyType;
            s_CachedSkyData.skyHash = skyHash;
            s_LastUpdateTime = Time.realtimeSinceStartup;
            s_UpdateRequested = false;

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
    }
}
