using System;
using VividRP.Runtime.GPUDriven.Bindless;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSystem : IDisposable
    {
        private static VividGPUDrivenSystem s_Instance;

        private readonly VividGPUDrivenBufferSet m_BufferSet;
        private readonly VividGPUDrivenSceneDataBuilder m_SceneDataBuilder;
        private bool m_IsDisposed;

        public VividGPUDrivenSystem()
            : this(new BindlessTextureContainer(), new VividGPUDrivenSceneDataBuilder())
        {
        }

        internal VividGPUDrivenSystem(IBindlessTextureDescriptorAllocator allocator)
            : this(new BindlessTextureContainer(allocator), new VividGPUDrivenSceneDataBuilder())
        {
        }

        private VividGPUDrivenSystem(
            BindlessTextureContainer bindlessTextureContainer,
            VividGPUDrivenSceneDataBuilder sceneDataBuilder
        )
        {
            BindlessTextureContainer = bindlessTextureContainer ?? throw new ArgumentNullException(nameof(bindlessTextureContainer));
            m_BufferSet = new VividGPUDrivenBufferSet();
            m_SceneDataBuilder = sceneDataBuilder ?? throw new ArgumentNullException(nameof(sceneDataBuilder));
            SceneData = new VividGPUDrivenSceneData();
        }

        public static VividGPUDrivenSystem instance => s_Instance ??= new VividGPUDrivenSystem();

        public BindlessTextureContainer BindlessTextureContainer { get; }

        public VividGPUDrivenSceneData SceneData { get; }

        public bool IsAvailable => BindlessTextureContainer.IsAvailable;

        public string UnavailableReason => BindlessTextureContainer.UnavailableReason;

        internal VividGPUDrivenBufferSet BufferSet => m_BufferSet;

        public void PrepareFrame()
        {
            ThrowIfDisposed();

            BindlessTextureContainer.PreRender();
            m_SceneDataBuilder.Build(SceneData, VividMeshletRendererDatabase.instance, BindlessTextureContainer);
            m_BufferSet.Upload(SceneData);
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            m_BufferSet.BindGlobals(cmd);
        }

        public static void Shutdown()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            SceneData.Clear();
            m_BufferSet.Dispose();
            BindlessTextureContainer.Dispose();
            m_IsDisposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenSystem));
            }
        }
    }
}
