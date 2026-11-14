using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tools
{
    /// <summary>Independent, emission-only Meshlet/SDF fixture. Existing dirty scenes stay loaded.</summary>
    [InitializeOnLoad]
    public static class EmissiveCornellBoxSceneBuilder
    {
        public const string Folder = "Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox/EmissiveCeiling";
        public const string ScenePath = Folder + "/CornellBoxEmissiveCeiling.unity";
        public const string ProfilePath = Folder + "/Emissive Ceiling Profile.asset";
        public const string CachePath = Folder + "/Emissive Ceiling Surface Cache.asset";
        public const string MaterialPath = Folder + "/Ceiling Emitter.mat";
        public const string ProxyPath = Folder + "/Ceiling Emitter Proxy.asset";
        private const string SourceFolder = "Packages/com.vivid.render-pipelines/Samples/SurfaceCacheCornellBox";
        private const string SourceScenePath = SourceFolder + "/SurfaceCacheCornellBoxBright.unity";
        private const int Layer = 29;
        public const float EmissionRadiance = 200f;
        public const float ExposureEV100 = 4f;
        private static readonly List<(Camera camera, bool enabled)> s_PreviewCameras = new();

        static EmissiveCornellBoxSceneBuilder()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (previous, scene) =>
            {
                if (scene.path == ScenePath) return;
                RestorePreviewCameras();
                if (previous.path == ScenePath && previous.isLoaded)
                    foreach (var root in previous.GetRootGameObjects())
                        foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            };
            AssemblyReloadEvents.beforeAssemblyReload += RestorePreviewCameras;
            EditorApplication.quitting += RestorePreviewCameras;
            EditorApplication.delayCall += () =>
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.isLoaded && scene.path == ScenePath) IsolatePreviewCamera(scene);
            };
        }

        [MenuItem("VividRP/Samples/Create Emissive Ceiling Cornell Box")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("The emissive fixture already exists; use Open instead of overwriting it.");
            if (VividRenderPipelineAsset.GetActiveAsset()?.RenderGraphAsset == null)
                throw new InvalidOperationException("An active VividRP render graph is required.");

            EnsureFolder(Folder);
            var created = new List<string>();
            Scene original = SceneManager.GetActiveScene();
            Scene source = SceneManager.GetSceneByPath(SourceScenePath);
            bool ownSource = !source.isLoaded;
            if (ownSource)
                source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            Scene scene = default;
            bool saved = false;
            try
            {
                var sources = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshletRenderer>(true)).ToArray();
                var floor = sources.Single(renderer => renderer.name == "Floor");
                var sourceCamera = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single();
                var sourceVolume = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Volume>(true))
                    .Single(volume => volume.sharedProfile != null && volume.sharedProfile.name == "CornellBox Profile");

                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Cornell Box - Emissive Ceiling") { layer = Layer };
                foreach (var wall in sources.Where(renderer => renderer.name != "Overlapping Panels"))
                    Copy(wall, root.transform, wall.name, wall.transform.position, wall.transform.localScale, wall.transform.rotation);
                Copy(floor, root.transform, "Short White Block", new Vector3(-.75f, .55f, 1.25f),
                    new Vector3(1.05f, 1.1f, 1.05f), Quaternion.Euler(0, -18, 0));
                Copy(floor, root.transform, "Tall White Block", new Vector3(.75f, .95f, 2.55f),
                    new Vector3(1.05f, 1.9f, 1.05f), Quaternion.Euler(0, 17, 0));

                var mesh = new Mesh { name = "Downward Ceiling Emitter 1.6 x 1.2" };
                mesh.vertices = new[] { new Vector3(-.8f, 0, -.6f), new Vector3(.8f, 0, -.6f),
                    new Vector3(.8f, 0, .6f), new Vector3(-.8f, 0, .6f) };
                mesh.normals = Enumerable.Repeat(Vector3.down, 4).ToArray();
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                Save(mesh, Folder + "/Ceiling Emitter Mesh.asset");
                string collectionPath = VividMeshletCollectionAssetImporter.CreateAssetForMesh(mesh, 0);
                if (string.IsNullOrEmpty(collectionPath))
                    throw new InvalidOperationException("Could not generate the emitter Meshlet collection.");
                created.Add(collectionPath);

                Color emission = new Color(EmissionRadiance, EmissionRadiance, EmissionRadiance, 1);
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    emission = emission.gamma;
                var material = new Material(floor.sourceMaterials[0]) { name = "Ceiling Emitter" };
                material.SetColor("_BaseColor", new Color(.78f, .78f, .78f, 1));
                material.SetColor("_EmissionColor", emission);
                // Built-in whiteTexture does not retain a material asset reference after reimport.
                var emissionMap = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
                    { name = "Emitter White", filterMode = FilterMode.Point };
                emissionMap.SetPixel(0, 0, Color.white);
                emissionMap.Apply(false, false);
                Save(emissionMap, Folder + "/Emitter White.asset");
                material.SetTexture("_EmissionMap", emissionMap);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                Save(material, MaterialPath);
                var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                proxy.name = "Ceiling Emitter Proxy";
                proxy.SourceMaterial = material;
                proxy.Model = GPUDrivenMaterialProxyModel.StandardLit;
                proxy.MaterialGraph = null;
                proxy.BaseColor = material.GetColor("_BaseColor");
                proxy.Roughness = .8f;
                proxy.EmissionColor = emission;
                proxy.CullMode = CullMode.Back;
                Save(proxy, ProxyPath);

                var emitterObject = new GameObject("Ceiling Emitter - Downward Only") { layer = Layer };
                emitterObject.transform.SetParent(root.transform, false);
                emitterObject.transform.localPosition = new Vector3(0, 2.965f, 1.85f);
                emitterObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = emitterObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                var emitter = emitterObject.AddComponent<MeshletRenderer>();
                emitter.CaptureSourceFromRenderer(meshRenderer);
                emitter.SetMeshletCollections(new[] { AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(collectionPath) });
                emitter.SetMaterialProxies(new[] { proxy });
                emitter.SetTakeOverSourceRenderer(true);
                Object.DestroyImmediate(meshRenderer);
                Object.DestroyImmediate(emitterObject.GetComponent<MeshFilter>());

                var sdf = ScriptableObject.CreateInstance<VividMeshSdfAsset>();
                sdf.name = "Ceiling Emitter Thin Shell SDF";
                VividMeshSdfBaker.Bake(sdf, mesh, mesh, .0125f, 192, VividMeshSdfSignMode.ThinShell, .025f);
                Save(sdf, Folder + "/Ceiling Emitter SDF.asset");
                AssetDatabase.AddObjectToAsset(sdf.DistanceField, sdf);
                EditorUtility.SetDirty(sdf);
                AssetDatabase.SaveAssetIfDirty(sdf);
                var serialized = new SerializedObject(emitter);
                serialized.FindProperty("m_MeshSdf").objectReferenceValue = sdf;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var renderers = root.GetComponentsInChildren<MeshletRenderer>();
                var cache = VividSdfSurfaceCacheBaker.Bake(renderers, CachePath, 64);
                created.Add(CachePath);
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Emissive Ceiling Profile";
                Save(profile, ProfilePath);
                var baseProfile = sourceVolume.HasInstantiatedProfile() ? sourceVolume.profile : sourceVolume.sharedProfile;
                foreach (var component in baseProfile.components)
                {
                    var clone = Object.Instantiate(component);
                    clone.name = component.name;
                    profile.components.Add(clone);
                    AssetDatabase.AddObjectToAsset(clone, profile);
                }
                Get<SkySettingsVolume>().skyType.Override(SkyType.None);
                if (profile.TryGet<HDRISkyVolume>(out var hdri)) hdri.active = false;
                if (profile.TryGet<PhysicallyBasedSkyVolume>(out var physicalSky)) physicalSky.active = false;
                var exposure = Get<AutoExposure>();
                exposure.enabled.Override(true);
                exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
                exposure.fixedExposure.Override(ExposureEV100);
                if (profile.TryGet<LocalExposure>(out var localExposure)) localExposure.active = false;
                var gi = Get<SdfDiffuseGISettings>();
                gi.enabled.Override(true);
                gi.surfaceCache.Override(cache);
                gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather);
                gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);
                gi.realtimeOnly.Override(true);
                gi.occludedSky.Override(false);
                gi.farRadianceCache.Override(false);
                gi.segmentedSdfTracing.Override(false);
                gi.surfaceCacheBounceOrders.Override(8);
                gi.surfaceCacheRadiositySamples.Override(64);
                gi.surfaceCacheRadiosityAccumulate.Override(true);
                gi.surfaceCacheRadiosityBatchLimit.Override(32);
                gi.surfaceCacheFullUpdateCardLimit.Override(128);
                gi.screenProbeSpatialKernel.Override(SdfScreenProbeSpatialKernel.Adaptive5x5);
                gi.screenProbeSpatialAnchor.Override(true);
                gi.screenProbeSamplingGuide.Override(true);
                gi.screenProbeHierarchicalGuide.Override(true);
                gi.spatialDenoise.Override(SdfDiffuseSpatialDenoise.Atrous3);
                gi.pixelTemporal.Override(true);
                gi.pixelHistoryMaxAge.Override(32);
                gi.correctReceiverSelfHits.Override(true);
                foreach (var component in profile.components) EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);

                var volumeObject = new GameObject("Emission Only GI Volume") { layer = Layer };
                volumeObject.transform.SetParent(root.transform, false);
                var volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100;
                volume.sharedProfile = profile;
                var cameraObject = Object.Instantiate(sourceCamera.gameObject);
                cameraObject.name = "Emissive Cornell Camera";
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.layer = Layer;
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 1.5f, -3.8f);
                camera.transform.LookAt(new Vector3(0, 1.5f, 2));
                camera.fieldOfView = 42;
                camera.cullingMask = 1 << Layer;
                camera.depth = Mathf.Max(100, sourceCamera.depth + 1);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.scene = scene;
                var cameraData = camera.GetComponent<VividAdditionalCameraData>();
                cameraData.volumeLayerMask = 1 << Layer;
                cameraData.antialiasing = VividAntialiasingMode.None;
                RenderSettings.sun = null;
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0;
                RenderSettings.reflectionIntensity = 0;
                RenderSettings.fog = false;
                foreach (var renderer in renderers)
                    if (!renderer.TryValidate(out var message) || renderer.meshSdf == null)
                        throw new InvalidOperationException(renderer.name + ": " + message);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save the new emissive scene.");
                created.Add(ScenePath);
                saved = true;
                Selection.activeGameObject = root;
                IsolatePreviewCamera(scene);

                T Get<T>() where T : VolumeComponent
                {
                    if (profile.TryGet<T>(out var value)) return value;
                    value = profile.Add<T>(true);
                    AssetDatabase.AddObjectToAsset(value, profile);
                    return value;
                }
            }
            finally
            {
                if (!saved)
                {
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    for (int i = created.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(created[i]);
                    if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                }
                if (ownSource && source.isLoaded) EditorSceneManager.CloseScene(source, true);
            }

            void Save(Object asset, string path)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    throw new InvalidOperationException("Refusing to overwrite " + path);
                AssetDatabase.CreateAsset(asset, path);
                created.Add(path);
            }
        }

        [MenuItem("VividRP/Samples/Open Emissive Ceiling Cornell Box")]
        public static void Open()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            IsolatePreviewCamera(scene);
        }

        private static void IsolatePreviewCamera(Scene scene)
        {
            RestorePreviewCameras();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = true;
            // Different Surface Cache identities currently share a GPU scene. Keep the fixture's
            // interactive preview single-camera, without saving or closing another dirty scene.
            foreach (var camera in Camera.allCameras)
            {
                if (camera.gameObject.scene == scene) continue;
                s_PreviewCameras.Add((camera, camera.enabled));
                camera.enabled = false;
            }
        }

        private static void RestorePreviewCameras()
        {
            foreach (var item in s_PreviewCameras)
                if (item.camera != null) item.camera.enabled = item.enabled;
            s_PreviewCameras.Clear();
        }

        private static MeshletRenderer Copy(MeshletRenderer source, Transform parent, string name,
            Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var copy = Object.Instantiate(source.gameObject, parent);
            copy.name = name;
            copy.transform.SetPositionAndRotation(position, rotation);
            copy.transform.localScale = scale;
            foreach (var child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
            return copy.GetComponent<MeshletRenderer>();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
