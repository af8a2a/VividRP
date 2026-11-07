using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.AdaptiveProbeVolume
{
    /// <summary>Editor-only source geometry bridge to Unity's native APV baker.</summary>
    [InitializeOnLoad]
    public static class MeshletAPVBaking
    {
        private static readonly List<GameObject> s_Proxies = new();
        private static ProbeVolumeBakingSet s_BakingSet;
        private static int s_BakeErrors;
        public static bool IsRunning { get; private set; }
        public static string LastResult { get; private set; } = "Not started";
        public static int ProxyCount => s_Proxies.Count;

        static MeshletAPVBaking()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorApplication.delayCall += RecoverInterruptedProxies;
        }

        [MenuItem("VividRP/Lighting/Bake Meshlet APV")]
        public static void Bake()
        {
            if (IsRunning || Lightmapping.isRunning || AdaptiveProbeVolumes.isRunning)
                throw new InvalidOperationException("A lighting bake is already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bake APV in Edit Mode.");

            var pipeline = VividRenderPipelineAsset.GetActiveAsset();
            if (pipeline == null || !pipeline.SupportProbeVolume || !ProbeReferenceVolume.instance.isInitialized)
                throw new InvalidOperationException("Enable APV on the active VividRP asset before baking.");

            s_BakingSet = ProbeReferenceVolume.instance.currentBakingSet;
            if (s_BakingSet == null)
            {
                var activeScene = AssetDatabase.GUIDFromAssetPath(SceneManager.GetActiveScene().path);
                foreach (var guid in AssetDatabase.FindAssets("t:ProbeVolumeBakingSet"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && ContainsScene(candidate, activeScene))
                    {
                        s_BakingSet = candidate;
                        ProbeReferenceVolume.instance.SetActiveBakingSet(candidate);
                        break;
                    }
                }
            }
            if (s_BakingSet == null)
                throw new InvalidOperationException("Assign the scene to an APV Baking Set first.");
            foreach (var guid in s_BakingSet.scenesInBakingSet)
            {
                var scene = SceneManager.GetSceneByPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("Open all scenes in the Baking Set before exporting Meshlet geometry.");
            }

            RecoverInterruptedProxies();
            try
            {
                CreateProxies(s_BakingSet);
                if (s_Proxies.Count == 0)
                    throw new InvalidOperationException("No static MeshletRenderer source geometry eligible for APV baking.");

                s_BakeErrors = 0;
                IsRunning = true;
                LastResult = "Baking";
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Update;
                // Proxies must exist before placement, virtual offset AND input extraction.
                if (!AdaptiveProbeVolumes.BakeAsync())
                    throw new InvalidOperationException("Unity could not start the APV bake. See the Console.");
            }
            catch
            {
                LastResult = "Failed to start";
                Finish();
                throw;
            }
        }

        [MenuItem("VividRP/Lighting/Cancel Meshlet APV Bake")]
        public static void Cancel()
        {
            if (!IsRunning)
                return;
            try
            {
                if (AdaptiveProbeVolumes.isRunning)
                    AdaptiveProbeVolumes.Cancel();
            }
            finally
            {
                LastResult = "Cancelled";
                Finish();
            }
        }

        private static void Update()
        {
            if (AdaptiveProbeVolumes.isRunning || Lightmapping.isRunning)
                return;
            LastResult = s_BakeErrors == 0 && s_BakingSet != null && s_BakingSet.HasBakedData()
                ? "Completed" : "Failed (see Console)";
            Finish();
            Debug.Log($"[VividRP] Meshlet APV bake: {LastResult}. Temporary renderers removed.");
        }

        private static void Finish()
        {
            EditorApplication.update -= Update;
            Application.logMessageReceived -= OnLog;
            DestroyProxies();
            IsRunning = false;
            s_BakingSet = null;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_BakeErrors++;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                Cancel();
        }

        private static void OnSceneClosing(Scene scene, bool removingScene) => Cancel();

        internal static void CreateProxies(ProbeVolumeBakingSet bakingSet)
        {
            if (bakingSet == null)
                throw new ArgumentNullException(nameof(bakingSet));
            if (s_Proxies.Count != 0)
                throw new InvalidOperationException("A Meshlet bake geometry session is already active.");
            try
            {
                var sources = Object.FindObjectsByType<MeshletRenderer>(FindObjectsSortMode.None);
                foreach (var source in sources)
                {
                    if (!source.isActiveAndEnabled || !source.sourceRenderingEnabled || source.sourceWasSkinned
                        || (GameObjectUtility.GetStaticEditorFlags(source.gameObject) & StaticEditorFlags.ContributeGI) == 0)
                        continue;
                    var scene = source.gameObject.scene;
                    if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                        continue;
                    if (bakingSet != null && !ContainsScene(bakingSet, AssetDatabase.GUIDFromAssetPath(scene.path)))
                        continue;
                    if (source.TryGetComponent<MeshRenderer>(out var existing) && existing.enabled)
                        continue;
                    if (source.sourceMesh == null || source.sourceMaterials.Count == 0)
                        throw new InvalidOperationException($"'{source.name}' is missing its source mesh or materials.");

                    var materials = new Material[source.sourceMaterials.Count];
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var material = source.sourceMaterials[i];
                        if (material == null || (material.FindPass("Meta") < 0 && material.FindPass("META") < 0))
                            throw new InvalidOperationException($"'{source.name}' material slot {i} needs a Meta pass for GI baking.");
                        materials[i] = material;
                    }

                    var proxy = new GameObject("__VividMeshletAPVBakeProxy");
                    s_Proxies.Add(proxy);
                    // An identity child preserves non-uniform scale, negative scale and parent shear.
                    proxy.transform.SetParent(source.transform, false);
                    proxy.layer = source.gameObject.layer;
                    proxy.tag = "EditorOnly";
                    proxy.hideFlags = HideFlags.HideInHierarchy;
                    proxy.AddComponent<MeshletAPVBakeProxy>().owner = source;
                    proxy.AddComponent<MeshFilter>().sharedMesh = source.sourceMesh;
                    var renderer = proxy.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = source.shadowCastingMode;
                    renderer.receiveShadows = source.receiveShadows;
                    renderer.renderingLayerMask = source.renderingLayerMask;
                    renderer.receiveGI = ReceiveGI.LightProbes;
                    GameObjectUtility.SetStaticEditorFlags(proxy, StaticEditorFlags.ContributeGI);
                }
            }
            catch
            {
                DestroyProxies();
                throw;
            }
        }

        private static bool ContainsScene(ProbeVolumeBakingSet set, GUID guid)
        {
            foreach (var candidate in set.scenesInBakingSet)
                if (candidate == guid)
                    return true;
            return false;
        }

        internal static void DestroyProxies()
        {
            foreach (var proxy in s_Proxies)
                if (proxy != null)
                    Object.DestroyImmediate(proxy);
            s_Proxies.Clear();
        }

        [MenuItem("VividRP/Lighting/Clean Interrupted Meshlet APV Proxies")]
        public static void RecoverInterruptedProxies()
        {
            if (IsRunning || AdaptiveProbeVolumes.isRunning || Lightmapping.isRunning)
                return;
            foreach (var marker in Resources.FindObjectsOfTypeAll<MeshletAPVBakeProxy>())
            {
                if (marker != null && !EditorUtility.IsPersistent(marker) && marker.gameObject.scene.IsValid())
                    Object.DestroyImmediate(marker.gameObject);
            }
            s_Proxies.Clear();
        }
    }
}
