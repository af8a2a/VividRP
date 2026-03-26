using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenObjectTracker : IDisposable
    {
        private readonly TextureTracker m_TextureTracker;

        public VividGPUDrivenObjectTracker(BindlessTextureContainer bindlessTextureContainer)
        {
            if (bindlessTextureContainer == null)
            {
                throw new ArgumentNullException(nameof(bindlessTextureContainer));
            }

            m_TextureTracker = new TextureTracker(
                bindlessTextureContainer,
                ObjectDispatcherService.TypeTrackingFlags.Assets
            );

            ObjectDispatcherService.RegisterObjectTracker(m_TextureTracker);
        }

        public void Dispose()
        {
            ObjectDispatcherService.UnregisterObjectTracker(m_TextureTracker);
        }

        private sealed class TextureTracker : ObjectTracker<Texture>
        {
            private readonly BindlessTextureContainer m_BindlessTextureContainer;

            public TextureTracker(
                BindlessTextureContainer bindlessTextureContainer,
                ObjectDispatcherService.TypeTrackingFlags trackingFlags)
                : base(trackingFlags)
            {
                m_BindlessTextureContainer = bindlessTextureContainer ?? throw new ArgumentNullException(nameof(bindlessTextureContainer));
            }

            public override void ProcessData(List<Object> changed, NativeArray<EntityId> changedId, NativeArray<EntityId> destroyedId)
            {
                m_BindlessTextureContainer.AddPotentialDirtyTextureRange(changedId, changed);
                m_BindlessTextureContainer.AddPotentialDestroyedDirtyTextureRange(destroyedId);
            }
        }
    }
}
