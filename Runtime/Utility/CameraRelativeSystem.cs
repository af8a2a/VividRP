using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
//     USAGE    
//     // 1. 定义具体的状态（只关心数据和清理）
//     public class TemporalHistoryState : CameraRelativeState
//     {
//         public int FrameCount;
//         public Vector2 LastJitter;
//         public Vector3 PreviousCameraPosition;
//         public Matrix4x4 PreviousViewProjMatrix;
//     
//         // 假设有历史 RTHandle 需要管理
//         // public RTHandle TAAHistoryBuffer;
//
//         public override void Dispose()
//         {
//             // TAAHistoryBuffer?.Release();
//         }
//     }
//
// // 2. 派生具体系统
//     public class TemporalHistorySystem : CameraRelativeSystem<TemporalHistoryState>
//     {
//         public void Tick(Camera camera, ContextContainer frameData)
//         {
//             // 直接复用基类的 GetOrCreate
//             var state = GetOrCreate(camera);
//             var temporalData = frameData.Create<CameraRelativeTemporalContext>();
//
//             state.FrameCount++;
//             temporalData.JitterOffset = CalculateJitter(state.FrameCount, camera.pixelWidth, camera.pixelHeight);
//
//             // ... CRR 矩阵推导逻辑 (与之前相同) ...
//         
//             // 更新状态机的“上一帧”数据
//             state.PreviousCameraPosition = camera.transform.position;
//             state.PreviousViewProjMatrix = temporalData.NonJitteredViewProj;
//             state.LastJitter = temporalData.JitterOffset;
//         }
//     
//         // 如果有特殊需求，可以重写 Dispose，但通常基类的已经足够
//     }
    
    public abstract class CameraRelativeState : IDisposable
    {
        // 记录此状态所属的相机 ID（可选，用于调试）
        public EntityId cameraEntityID { get; internal set; }

        // 强制派生类实现物理资源的释放逻辑
        public abstract void Dispose();
    }
    
    

    ///  2. 派生具体系统
    /// <typeparam name="TState"></typeparam>
    public class CameraRelativeSystem<TState> : IDisposable where TState : CameraRelativeState, new() // 约束：必须是 State 且可无参实例化
    {
        // protected 允许子类在必要时遍历所有状态
        protected Dictionary<Camera, TState> m_CameraStates = new();

        /// <summary>
        /// 获取或创建相机的专属状态
        /// </summary>
        public TState GetOrCreateBase(Camera camera)
        {
            if (!m_CameraStates.TryGetValue(camera, out TState state))
            {
                state = new TState();
                state.cameraEntityID = camera.GetEntityId();

                // 允许子类在状态刚创建时进行额外的初始化操作
                OnStateCreated(camera, state);

                m_CameraStates[camera] = state;
            }

            return state;
        }

        public bool TryGetBase(Camera camera, out TState state)
        {
            if (camera == null)
            {
                state = null;
                return false;
            }

            return m_CameraStates.TryGetValue(camera, out state);
        }

        /// <summary>
        /// 虚方法：提供给子类的初始化 Hook
        /// </summary>
        protected virtual void OnStateCreated(Camera camera, TState state)
        {
        }


        /// <summary>
        /// 显式清理指定相机的状态
        /// </summary>
        public void CleanupCamera(Camera camera)
        {
            if (m_CameraStates.TryGetValue(camera, out TState state))
            {
                state.Dispose();
                m_CameraStates.Remove(camera);
            }
        }

        /// <summary>
        /// 垃圾回收：清理底层已被销毁的 Camera 状态
        /// 建议在 Renderer 的 Setup 阶段每隔几百帧调用一次，或在管线 Dispose 时调用
        /// </summary>
        public void PurgeDestroyedCameras()
        {
            // 收集所有变为 null 的 Camera (Unity 重载了 == 运算符，底层 C++ 对象销毁时会判定为 null)
            List<Camera> deadCameras = null;
            foreach (var kvp in m_CameraStates)
            {
                if (kvp.Key == null)
                {
                    deadCameras ??= new List<Camera>();
                    deadCameras.Add(kvp.Key);
                }
            }

            if (deadCameras != null)
            {
                foreach (var cam in deadCameras)
                {
                    m_CameraStates[cam].Dispose();
                    m_CameraStates.Remove(cam);
                }
            }
        }


        public void Dispose()
        {
            foreach (var state in m_CameraStates.Values)
            {
                state.Dispose();
            }

            m_CameraStates.Clear();
        }
    }
}
