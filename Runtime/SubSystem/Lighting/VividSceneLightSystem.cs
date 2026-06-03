using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividSceneLightSystem : VividSubsystem<VividSceneLightSystem>

    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        internal static void EnsureInitialized()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            InsertIntoPlayerLoop();
        }

        protected override void OnDeinitialize()
        {
            VividLightRenderDatabase.instance.ReleaseSceneLightPrepareResources();
            RemoveFromPlayerLoop();
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
        }

        private static void PlayerLoopKick()
        {
            if (!IsInitialized)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemSceneLightKickMarker.Auto())
            {
                VividLightRenderDatabase.instance.BuildSceneLightSnapshotAndSchedulePrepare(true);
            }
        }

        private static void InsertIntoPlayerLoop()
        {
            var rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (var index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                var subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length + 1);
                var alreadyPresent = false;
                foreach (var nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type == typeof(VividSceneLightSystemPlayerLoopMarker))
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
            var rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (var index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                var subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length);
                foreach (var nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type != typeof(VividSceneLightSystemPlayerLoopMarker))
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
                type = typeof(VividSceneLightSystemPlayerLoopMarker),
                updateDelegate = PlayerLoopKick,
            };
        }

        private sealed class VividSceneLightSystemPlayerLoopMarker
        {
        }
    }
}