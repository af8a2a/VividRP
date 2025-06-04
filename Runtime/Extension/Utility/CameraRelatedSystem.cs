using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    internal abstract class CameraRelatedSystem<T> where T : CameraRelatedSystem<T>, new()
    {
        protected static Dictionary<(Camera, int), T> s_Cameras = new Dictionary<(Camera, int), T>();
        protected static List<(Camera, int)> s_Cleanup = new List<(Camera, int)>(); // Recycled to reduce GC pressure

        public Camera camera { get; protected set; }
        public string name { get; protected set; } // Needs to be cached because camera.name generates GCAllocs

        protected CameraRelatedSystem() { }

        protected virtual void InitializeBase(Camera camera)
        {
            this.camera = camera;
            this.name = camera.name;
            Initialize(camera);
        }

        protected abstract void Initialize(Camera camera);

        /// <summary>
        /// Get the existing system for the provided camera or create a new if it does not exist yet.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="xrMultipassId"></param>
        /// <returns></returns>
        public static T GetOrCreate(Camera camera, int xrMultipassId = 0)
        {
            if (!s_Cameras.TryGetValue((camera, xrMultipassId), out var system))
            {
                system = new T();
                system.InitializeBase(camera);
                s_Cameras.Add((camera, xrMultipassId), system);
            }

            return system;
        }

        /// <summary>
        /// Force recreate the system for the provided camera.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="xrMultipassId"></param>
        /// <returns></returns>
        public static T ReCreate(Camera camera, int xrMultipassId = 0)
        {
            if (s_Cameras.TryGetValue((camera, xrMultipassId), out var system))
            {
                system.Dispose();
                s_Cameras.Remove((camera, xrMultipassId));
            }

            system = new T();
            system.InitializeBase(camera);
            s_Cameras.Add((camera, xrMultipassId), system);

            return system;
        }

        internal static void ClearAll()
        {
            foreach (var cameraKey in s_Cameras)
            {
                cameraKey.Value.Dispose();
            }

            s_Cameras.Clear();
            s_Cleanup.Clear();
        }

        /// <summary>
        /// Look for any camera that hasn't been used in the last frame and remove them from the pool.
        /// </summary>
        internal static void CleanUnused()
        {
            foreach (var key in s_Cameras.Keys)
            {
                var system = s_Cameras[key];
                Camera camera = system.camera;

                // Unfortunately, the scene view camera is always isActiveAndEnabled == false so we can't rely on this. For this reason we never release it (which should be fine in the editor)
                if (camera != null && camera.cameraType == CameraType.SceneView)
                    continue;

                if (camera == null)
                {
                    s_Cleanup.Add(key);
                    continue;
                }

                UniversalAdditionalCameraData additionalCameraData = null;
                if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR)
                    camera.gameObject.TryGetComponent(out additionalCameraData);

                bool hasPersistentHistory = additionalCameraData != null && additionalCameraData.hasPersistentHistory;
                // We keep preview camera around as they are generally disabled/enabled every frame. They will be destroyed later when camera.camera is null
                // TODO: Add "isPersistent", it will Mark the Camera as persistent so it won't be destroyed if the camera is disabled.
                if (!camera.isActiveAndEnabled && camera.cameraType != CameraType.Preview && !hasPersistentHistory)
                    s_Cleanup.Add(key);
            }

            foreach (var cam in s_Cleanup)
            {
                s_Cameras[cam].Dispose();
                s_Cameras.Remove(cam);
            }

            s_Cleanup.Clear();
        }

        public abstract void Dispose();
    }


}

