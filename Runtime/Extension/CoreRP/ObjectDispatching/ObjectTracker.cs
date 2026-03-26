using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.GPUDriven.ObjectDispatching
{
    internal abstract class ObjectTracker
    {
        protected ObjectTracker(Type trackedType, ObjectDispatcherService.TypeTrackingFlags trackingFlags)
        {
            TrackedType = trackedType ?? throw new ArgumentNullException(nameof(trackedType));
            TrackingFlags = trackingFlags;
        }

        public Type TrackedType { get; }

        public ObjectDispatcherService.TypeTrackingFlags TrackingFlags { get; }

        public abstract void ProcessData(List<Object> changed, NativeArray<EntityId> changedId, NativeArray<EntityId> destroyedId);
    }

    internal abstract class ObjectTracker<T> : ObjectTracker
    {
        protected ObjectTracker(ObjectDispatcherService.TypeTrackingFlags trackingFlags)
            : base(typeof(T), trackingFlags)
        {
        }
    }
}
