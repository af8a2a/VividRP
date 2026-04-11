using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using Object = UnityEngine.Object;

namespace VividRP.Runtime
{
    internal sealed class BoundProxySceneTracker<TProvider> : IDisposable
        where TProvider : Component, IBoundProxyProvider
    {
        private readonly ProviderObjectTracker m_ObjectTracker;
        private readonly List<TProvider> m_Providers = new();
        private readonly Dictionary<EntityId, int> m_ProviderIndexByEntityId = new();
        private bool m_IsDisposed;

        public BoundProxySceneTracker(
            ObjectDispatcherService.TypeTrackingFlags trackingFlags = ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
        {
            m_ObjectTracker = new ProviderObjectTracker(this, trackingFlags);
            RebuildTrackedProviders();
            ObjectDispatcherService.RegisterObjectTracker(m_ObjectTracker);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            ObjectDispatcherService.UnregisterObjectTracker(m_ObjectTracker);
            m_Providers.Clear();
            m_ProviderIndexByEntityId.Clear();
            m_IsDisposed = true;
        }

        internal int TrackedProviderCount => m_Providers.Count;

        internal void GetWorldData(List<BoundProxyWorldData> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            for (int providerIndex = 0; providerIndex < m_Providers.Count; providerIndex++)
            {
                TProvider provider = m_Providers[providerIndex];
                if (provider == null)
                {
                    continue;
                }

                if (BoundProxyUtility.TryCreateWorldData(provider, out BoundProxyWorldData worldData))
                {
                    results.Add(worldData);
                }
            }
        }

        internal bool TryGetWorldData(EntityId entityId, out BoundProxyWorldData worldData)
        {
            worldData = default;
            if (entityId.Equals(EntityId.None)
                || !m_ProviderIndexByEntityId.TryGetValue(entityId, out int providerIndex)
                || providerIndex < 0
                || providerIndex >= m_Providers.Count)
            {
                return false;
            }

            TProvider provider = m_Providers[providerIndex];
            return provider != null && BoundProxyUtility.TryCreateWorldData(provider, out worldData);
        }

        private void RebuildTrackedProviders()
        {
            m_Providers.Clear();
            m_ProviderIndexByEntityId.Clear();

            TProvider[] providers = Object.FindObjectsByType<TProvider>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.InstanceID);
            for (int providerIndex = 0; providerIndex < providers.Length; providerIndex++)
            {
                AddOrUpdateProvider(providers[providerIndex]);
            }
        }

        private void ProcessData(List<Object> changed, NativeArray<EntityId> changedId, NativeArray<EntityId> destroyedId)
        {
            for (int destroyedIndex = 0; destroyedIndex < destroyedId.Length; destroyedIndex++)
            {
                RemoveProvider(destroyedId[destroyedIndex]);
            }

            for (int changedIndex = 0; changedIndex < changed.Count; changedIndex++)
            {
                if (changed[changedIndex] is TProvider provider)
                {
                    AddOrUpdateProvider(provider);
                }
            }
        }

        private void AddOrUpdateProvider(TProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            EntityId entityId = provider.transform != null ? provider.transform.GetEntityId() : EntityId.None;
            if (entityId.Equals(EntityId.None))
            {
                return;
            }

            if (m_ProviderIndexByEntityId.TryGetValue(entityId, out int existingIndex))
            {
                m_Providers[existingIndex] = provider;
                return;
            }

            m_ProviderIndexByEntityId.Add(entityId, m_Providers.Count);
            m_Providers.Add(provider);
        }

        private void RemoveProvider(EntityId entityId)
        {
            if (entityId.Equals(EntityId.None)
                || !m_ProviderIndexByEntityId.TryGetValue(entityId, out int providerIndex))
            {
                return;
            }

            int lastIndex = m_Providers.Count - 1;
            TProvider lastProvider = m_Providers[lastIndex];
            m_Providers[providerIndex] = lastProvider;
            m_Providers.RemoveAt(lastIndex);
            m_ProviderIndexByEntityId.Remove(entityId);

            if (providerIndex == lastIndex || lastProvider == null || lastProvider.transform == null)
            {
                return;
            }

            EntityId lastEntityId = lastProvider.transform.GetEntityId();
            if (!lastEntityId.Equals(EntityId.None))
            {
                m_ProviderIndexByEntityId[lastEntityId] = providerIndex;
            }
        }

        private sealed class ProviderObjectTracker : ObjectTracker<TProvider>
        {
            private readonly BoundProxySceneTracker<TProvider> m_Owner;

            public ProviderObjectTracker(
                BoundProxySceneTracker<TProvider> owner,
                ObjectDispatcherService.TypeTrackingFlags trackingFlags)
                : base(trackingFlags)
            {
                m_Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public override void ProcessData(List<Object> changed, NativeArray<EntityId> changedId, NativeArray<EntityId> destroyedId)
            {
                m_Owner.ProcessData(changed, changedId, destroyedId);
            }
        }
    }
}
