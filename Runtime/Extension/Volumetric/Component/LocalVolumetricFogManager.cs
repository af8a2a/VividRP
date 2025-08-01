using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace UnityEngine.Rendering.Universal
{
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct VolumetricLightingBuffer
    {
        public fixed float _VBufferCoordToViewDirWS[16];

        public Vector4 _VBufferViewportSize;
        public Vector4 _VBufferLightingViewportScale;
        public Vector4 _VBufferLightingViewportLimit;
        public Vector4 _VBufferDistanceEncodingParams;
        public Vector4 _VBufferDistanceDecodingParams;
        public Vector4 _GlobalFogDensity;

        public uint _VBufferSliceCount;
        public float _VBufferRcpSliceCount;
        public float _VBufferVoxelSize;
        public uint _VisibleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct VolumetricFogRenderingData
    {
        public Vector4 viewSpaceBounds;
        public uint startSliceIndex;
        public uint sliceCount;
        public uint padding0;

        public uint padding1;
        // public fixed float obbVertexPostionWS[8 * 4];
    }


    class LocalVolumetricFogManager
    {
        // Allocate graphics buffers by chunk to avoid reallocating them too often
        private static readonly int k_IndirectBufferChunkSize = 50;

        // TODO: make it configurable
        private static readonly int k_MaxVolumeCountOnScreen = 200;

        static LocalVolumetricFogManager s_Manager;

        public static LocalVolumetricFogManager manager
        {
            get
            {
                if (s_Manager == null)
                {
                    s_Manager = new LocalVolumetricFogManager();
                }

                return s_Manager;
            }
        }

        internal int maxVolumeCountOnScreen => k_MaxVolumeCountOnScreen;

        internal List<LocalVolumetricFog> volumes => m_Volumes;

        internal Material textureFogMaterial;

        List<LocalVolumetricFog> m_Volumes = null;

        internal GraphicsBuffer volumeSliceIndexBuffer;

        internal GraphicsBuffer globalIndirectArgBuffer;
        internal GraphicsBuffer globalIndirectionBuffer;
        internal GraphicsBuffer volumetricFogRenderingBuffer;

        LocalVolumetricFogManager()
        {
            m_Volumes = new List<LocalVolumetricFog>();
        }

        public void RegisterVolume(LocalVolumetricFog volume)
        {
            if (!m_Volumes.Contains(volume))
            {
                m_Volumes.Add(volume);
                ResizeBuffersIfNeeded();
            }
        }

        public void DeRegisterVolume(LocalVolumetricFog volume)
        {
            if (m_Volumes.Contains(volume))
            {
                m_Volumes.Remove(volume);
                ResizeBuffersIfNeeded();
            }
        }

        int GetNeededBufferCount()
            => Mathf.Max(k_IndirectBufferChunkSize, Mathf.CeilToInt(m_Volumes.Count / (float)k_IndirectBufferChunkSize) * k_IndirectBufferChunkSize);

        internal unsafe void InitializeGraphicsBuffer()
        {
            volumeSliceIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, 3 * 4, sizeof(uint));
            volumeSliceIndexBuffer.SetData(new List<uint> { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 });

            int count = GetNeededBufferCount();
            AllocateIndirectBuffers(count);

            volumetricFogRenderingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, k_MaxVolumeCountOnScreen, sizeof(VolumetricFogRenderingData));
        }

        unsafe void ResizeBuffersIfNeeded()
        {
            if (globalIndirectArgBuffer == null || !globalIndirectArgBuffer.IsValid())
                return;

            int count = GetNeededBufferCount();

            if (count > globalIndirectArgBuffer.count)
                Resize(count);
            if (count < globalIndirectionBuffer.count - k_IndirectBufferChunkSize)
                Resize(count + k_IndirectBufferChunkSize);

            void Resize(int bufferCount)
            {
                globalIndirectArgBuffer.Release();
                globalIndirectionBuffer.Release();
                AllocateIndirectBuffers(bufferCount);
            }
        }

        unsafe void AllocateIndirectBuffers(int count)
        {
            globalIndirectArgBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, count, sizeof(GraphicsBuffer.IndirectDrawArgs));
            globalIndirectionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, count, sizeof(uint));

            // Initialize with zeros to prevent weird behaviours
            var zeros = new NativeArray<byte>(count * Mathf.Max(sizeof(GraphicsBuffer.IndirectDrawArgs), sizeof(uint)), Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            globalIndirectArgBuffer.SetData(zeros, 0, 0, count * sizeof(GraphicsBuffer.IndirectDrawArgs));
            globalIndirectionBuffer.SetData(zeros, 0, 0, count * sizeof(uint));
            zeros.Dispose();
        }

        internal void CleanupGraphicsBuffers()
        {
            globalIndirectArgBuffer?.Release();
            globalIndirectionBuffer?.Release();
            volumetricFogRenderingBuffer?.Release();
            volumeSliceIndexBuffer?.Release();
            globalIndirectArgBuffer = null;
            globalIndirectionBuffer = null;
            volumetricFogRenderingBuffer = null;
            volumeSliceIndexBuffer = null;
        }

        public bool IsInitialized() => volumeSliceIndexBuffer != null && volumeSliceIndexBuffer.IsValid();

        public static class RegisterLocalVolumetricFogEarlyUpdate
        {
            internal static void PrepareFogDrawCalls()
            {
                if (!LocalVolumetricFogManager.manager?.IsInitialized() ?? true)
                    return;

                var volumes = LocalVolumetricFogManager.s_Manager.m_Volumes;
                for (int i = 0; i < volumes.Count; ++i)
                {
                    volumes[i].PrepareDrawCall(i);
                }
            }
#if UNITY_EDITOR
            [UnityEditor.InitializeOnLoadMethod]
#else
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
            internal static void Init()
            {
                var currentLoopSystem = LowLevel.PlayerLoop.GetCurrentPlayerLoop();
                RegisterFogUpdateBeforeScriptUpdate(typeof(RegisterLocalVolumetricFogEarlyUpdate), PrepareFogDrawCalls, ref currentLoopSystem);
                LowLevel.PlayerLoop.SetPlayerLoop(currentLoopSystem);
            }

            internal static bool RegisterFogUpdateBeforeScriptUpdate(Type updateType, PlayerLoopSystem.UpdateFunction updateFunction,
                ref PlayerLoopSystem playerLoop)
            {
                if (updateType == null || updateFunction == null)
                    return false;

                if (playerLoop.subSystemList != null)
                {
                    for (var i = 0; i < playerLoop.subSystemList.Length; ++i)
                    {
                        var subLoop = playerLoop.subSystemList[i];

                        if (subLoop.type == typeof(Update.ScriptRunBehaviourUpdate))
                        {
                            int currentSystemCount = playerLoop.subSystemList.Length;
                            var newSystemList = new PlayerLoopSystem[currentSystemCount + 1];
                            Array.Copy(playerLoop.subSystemList, 0, newSystemList, 0, i); // Copy first part of the system list
                            // Inject system update just before the script behaviour update
                            newSystemList[i] = new PlayerLoopSystem
                            {
                                type = updateType,
                                updateDelegate = updateFunction
                            };
                            // Copy the rest of the system list after
                            Array.Copy(playerLoop.subSystemList, i, newSystemList, i + 1, currentSystemCount - i); // Copy second part of the system list
                            playerLoop.subSystemList = newSystemList;
                            return true;
                        }

                        if (RegisterFogUpdateBeforeScriptUpdate(updateType, updateFunction, ref playerLoop.subSystemList[i]))
                            return true;
                    }
                }

                return false;
            }
        }
    }
}