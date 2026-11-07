using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tools
{
    /// <summary>Small, isolated APV bake/receive fixture. Does not edit the source pipeline or Sponza.</summary>
    public static class MeshletAPVValidationScene
    {
        public const string Folder = "Packages/com.vivid.render-pipelines/Samples/MeshletAPV";
        public const string ScenePath = Folder + "/MeshletAPV.unity";
        public const string PipelinePath = Folder + "/MeshletAPVPipeline.asset";
        public const string RenderGraphPath = Folder + "/RenderGraph.vrdg";
        public const string BakingSetPath = Folder + "/MeshletAPVBakingSet.asset";

        [MenuItem("VividRP/Samples/Open Meshlet APV Validation Scene")]
        public static void Open()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scenes before opening the APV fixture.");
            var pipeline = AssetDatabase.LoadAssetAtPath<VividRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
                throw new InvalidOperationException("Create the Meshlet APV fixture first.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        [MenuItem("VividRP/Samples/Create Meshlet APV Validation Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("The APV fixture already exists. Open it instead of overwriting its bake.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scenes before creating the isolated APV fixture.");

            EnsureFolder(Folder);
            var sourcePipeline = VividRenderPipelineAsset.GetActiveAsset();
            if (sourcePipeline == null || sourcePipeline.RenderGraphAsset == null)
                throw new InvalidOperationException("An active VividRP pipeline and render graph are required.");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourcePipeline.RenderGraphAsset), RenderGraphPath))
                throw new InvalidOperationException("Could not copy the validation render graph.");
            var pipeline = Object.Instantiate(sourcePipeline);
            pipeline.RenderGraphAsset = AssetDatabase.LoadAssetAtPath<RenderGraphData>(RenderGraphPath);
            pipeline.name = "Meshlet APV Validation Pipeline";
            pipeline.SupportProbeVolume = true;
            pipeline.ProbeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL2;
            pipeline.EnableGPUDriven = true;
            pipeline.AutoExposureImplementation = AutoExposureImplementationPath.HDRP;
            AssetDatabase.CreateAsset(pipeline, PipelinePath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = "APV Source Cube";
            Object.DestroyImmediate(primitive);
            AssetDatabase.CreateAsset(mesh, Folder + "/Cube.asset");

            var white = Material("White", new Color(0.65f, 0.65f, 0.65f));
            var red = Material("Red", new Color(0.65f, 0.035f, 0.025f));
            var green = Material("Green", new Color(0.035f, 0.55f, 0.04f));
            var geometry = new GameObject("Static Meshlet GI Geometry");
            Cube(geometry.transform, mesh, white, "Floor", new Vector3(0, -0.15f, 0), new Vector3(6.3f, 0.3f, 6.3f));
            Cube(geometry.transform, mesh, white, "Ceiling", new Vector3(0, 4.15f, 0), new Vector3(6.3f, 0.3f, 6.3f));
            Cube(geometry.transform, mesh, white, "Back", new Vector3(0, 2, 3.15f), new Vector3(6.3f, 4, 0.3f));
            Cube(geometry.transform, mesh, red, "Red Wall", new Vector3(-3.15f, 2, 0), new Vector3(0.3f, 4, 6.3f));
            Cube(geometry.transform, mesh, green, "Green Wall", new Vector3(3.15f, 2, 0), new Vector3(0.3f, 4, 6.3f));
            Cube(geometry.transform, mesh, white, "Short Block", new Vector3(-1.1f, 0.6f, 0), new Vector3(1.2f, 1.2f, 1.2f));
            Cube(geometry.transform, mesh, white, "Tall Block", new Vector3(1.15f, 1.1f, 1), new Vector3(1.2f, 2.2f, 1.2f));
            var conversion = MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderersRecursively(geometry);
            if (!conversion.Success)
                throw new InvalidOperationException(conversion.ErrorMessage);

            var lightObject = new GameObject("Mixed Point - Baked Indirect");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Point;
            sun.lightUnit = LightUnit.Candela;
            sun.intensity = 1500;
            sun.range = 15;
            sun.color = Color.white;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            sun.transform.position = new Vector3(0, 3, -0.8f);
            lightObject.AddComponent<VividAdditionalLightData>();
            RenderSettings.sun = null;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 2, -8.5f);
            camera.transform.LookAt(new Vector3(0, 1.8f, 0.5f));
            camera.fieldOfView = 49;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.allowHDR = true;
            camera.backgroundColor = Color.black;
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.AddComponent<VividAdditionalCameraData>().antialiasing = VividAntialiasingMode.None;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Folder + "/Profile.asset");
            var sky = profile.Add<SkySettingsVolume>(true);
            sky.skyType.Override(SkyType.None);
            var exposure = profile.Add<AutoExposure>(true);
            exposure.enabled.Override(true);
            exposure.exposureMode.Override(AutoExposureExposureMode.Fixed);
            exposure.fixedExposure.Override(7);
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            profile.Add<ProbeVolumesOptions>(true);
            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100;
            volume.sharedProfile = profile;

            var probeVolume = new GameObject("Adaptive Probe Volume").AddComponent<ProbeVolume>();
            probeVolume.mode = ProbeVolume.Mode.Local;
            probeVolume.transform.position = new Vector3(0, 2, 0);
            probeVolume.size = new Vector3(8, 6, 8);
            probeVolume.overrideRendererFilters = true;
            probeVolume.minRendererVolumeSize = 0;

            var lighting = new LightingSettings
            {
                name = "Meshlet APV Lighting",
                bakedGI = true,
                realtimeGI = false,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
                directSampleCount = 64,
                indirectSampleCount = 256,
                environmentSampleCount = 64,
                maxBounces = 4
            };
            AssetDatabase.CreateAsset(lighting, Folder + "/LightingSettings.asset");
            Lightmapping.lightingSettings = lighting;
            EditorSceneManager.SaveScene(scene, ScenePath);

            var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            // CoreRP's authoring UI calls this internal initializer; CreateInstance alone leaves
            // virtual-offset and dilation settings zero-initialized in this Editor version.
            var defaults = typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (defaults == null)
                throw new MissingMethodException(typeof(ProbeVolumeBakingSet).FullName, "SetDefaults");
            defaults.Invoke(set, null);
            set.name = "Meshlet APV Baking Set";
            set.minDistanceBetweenProbes = 0.5f;
            set.simplificationLevels = 2;
            set.minRendererVolumeSize = 0;
            set.skyOcclusion = false;
            AssetDatabase.CreateAsset(set, BakingSetPath);
            set.TryAddScene(AssetDatabase.GUIDFromAssetPath(ScenePath));
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            // Switch only to the separate validation asset; the source asset remains untouched.
            // Scene saving/import can unload the clone originally held before NewScene.
            pipeline = AssetDatabase.LoadAssetAtPath<VividRenderPipelineAsset>(PipelinePath);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[VividRP] Created Meshlet APV fixture. Bake with VividRP/Lighting/Bake Meshlet APV after the pipeline initializes.");
        }

        private static Material Material(string name, Color color)
        {
            var shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
                throw new InvalidOperationException("StandardLit shader is unavailable.");
            var material = new Material(shader) { name = "APV " + name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", 0.15f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
            return material;
        }

        private static void Cube(Transform root, Mesh mesh, Material material, string name, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
