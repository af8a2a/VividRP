using System.Collections.Generic;
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

            FrameContextSystem.SubsystemPreRender -= Update;
            s_Projectors.Clear();
            s_ActiveDecals.Clear();
            s_Initialized = false;
        }

        internal static void Register(DecalProjector projector)
        {
            Initialize();

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

            for (int i = 0; i < s_Projectors.Count; i++)
            {
                DecalProjector projector = s_Projectors[i];
                if (projector == null || !projector.isActiveAndEnabled)
                    continue;

                if (!projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData wd))
                    continue;

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
        }

        internal static int ActiveDecalCount => s_ActiveDecals.Count;

        internal static void GetActiveDecals(List<DecalData> results)
        {
            results.Clear();
            results.AddRange(s_ActiveDecals);
        }
    }
}
