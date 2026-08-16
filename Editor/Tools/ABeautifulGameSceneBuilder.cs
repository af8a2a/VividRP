using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Runtime;

namespace VividRP.Editor.Tools
{
    internal static class ABeautifulGameSceneBuilder
    {
        private const string PackageRoot =
            "Packages/com.vivid.render-pipelines";
        private const string SampleRoot =
            PackageRoot + "/Samples/ABeautifulGame";
        private const string MaterialRoot = SampleRoot + "/Materials";
        private const string ModelPath =
            SampleRoot + "/glTF/ABeautifulGame.gltf";
        private const string TemplateRoot =
            PackageRoot + "/Editor/SceneTemplates/ABeautifulGame";
        private const string ScenePath =
            TemplateRoot + "/ABeautifulGame_VividRP.unity";
        private const string TemplatePath =
            TemplateRoot + "/ABeautifulGame_VividRP.scenetemplate";
        private const string ProfilePath =
            TemplateRoot + "/ABeautifulGame_VividRP_Profile.asset";
        private const string TemplatePipelinePath =
            PackageRoot
            + "/Editor/SceneTemplates/VividBasicScenePipeline.cs";

        [MenuItem(
            "VividRP/Samples/Rebuild Built-in A Beautiful Game Scene")]
        private static void Rebuild()
        {
            var materials = LoadMaterials();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                ProfilePath);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                ModelPath);
            if (profile == null || modelAsset == null)
            {
                throw new InvalidOperationException(
                    "A Beautiful Game package assets are incomplete.");
            }
            ConfigureProfile(profile);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var model = PrefabUtility.InstantiatePrefab(modelAsset)
                as GameObject;
            if (model == null)
                model = UnityEngine.Object.Instantiate(modelAsset);
            model.name = "ABeautifulGame";
            model.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            model.transform.localScale = Vector3.one * 4.0f;

            foreach (var renderer in
                     model.GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                var replacements = new Material[sourceMaterials.Length];
                for (var index = 0;
                     index < sourceMaterials.Length;
                     index++)
                {
                    var source = sourceMaterials[index];
                    if (source == null
                        || !materials.TryGetValue(
                            source.name,
                            out var replacement))
                    {
                        throw new InvalidOperationException(
                            "No VividRP material mapping for "
                            + (source != null ? source.name : "<null>")
                            + ".");
                    }

                    replacements[index] = replacement;
                }

                renderer.sharedMaterials = replacements;
                ConfigureRenderer(renderer);
            }
            SetStaticRecursive(model);

            var volumeObject = new GameObject("VividRP Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100.0f;
            volume.sharedProfile = profile;

            BuildCamera();
            BuildStudio(materials);

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.reflectionIntensity = 1.0f;
            RenderSettings.defaultReflectionMode =
                DefaultReflectionMode.Skybox;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save the built-in A Beautiful Game scene.");
            }

            CreateTemplate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[VividRP] Built the package-owned A Beautiful Game "
                + "scene and SceneTemplate.");
        }

        private static void ConfigureProfile(VolumeProfile profile)
        {
            if (!profile.TryGet<ReferencedPathTracingSettingsVolume>(
                    out var pathTracing))
            {
                throw new InvalidOperationException(
                    "A Beautiful Game path-tracing settings are missing.");
            }

            Undo.RecordObject(
                pathTracing,
                "Configure A Beautiful Game path tracing");
            pathTracing.targetSampleCount.overrideState = true;
            pathTracing.targetSampleCount.value = 1024;
            pathTracing.maxBounceCount.overrideState = true;
            pathTracing.maxBounceCount.value = 8;
            pathTracing.russianRouletteStartBounce.overrideState = true;
            pathTracing.russianRouletteStartBounce.value = 8;
            EditorUtility.SetDirty(pathTracing);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private static Dictionary<string, Material> LoadMaterials()
        {
            string[] names =
            {
                "King_Black", "King_White",
                "Queen_Black", "Queen_White",
                "Chessboard",
                "Pawn_Top_White", "Pawn_Body_White",
                "Pawn_Top_Black", "Pawn_Body_Black",
                "Castle_Black", "Castle_White",
                "Knight_Black", "Knight_White",
                "Bishop_Black", "Bishop_White"
            };
            var result = new Dictionary<string, Material>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
                result.Add(name, LoadMaterial("ABG_" + name));

            result.Add("Studio Base", LoadMaterial("ABG_StudioBase"));
            result.Add(
                "Studio Backdrop",
                LoadMaterial("ABG_StudioBackdrop"));
            result.Add(
                "Key Emission",
                LoadMaterial("ABG_KeyEmission"));
            result.Add(
                "Fill Emission",
                LoadMaterial("ABG_FillEmission"));
            return result;
        }

        private static Material LoadMaterial(string name)
        {
            var path = MaterialRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new InvalidOperationException(
                    "A Beautiful Game material is missing: " + path);
            }

            return material;
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("A Beautiful Game Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            var target = new Vector3(0.0f, 0.30f, 0.0f);
            camera.transform.position = new Vector3(3.8f, 2.7f, -4.5f);
            camera.transform.LookAt(target);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.usePhysicalProperties = true;
            camera.sensorSize = new Vector2(36.0f, 24.0f);
            camera.focalLength = 50.0f;
            camera.focusDistance = Vector3.Distance(
                camera.transform.position,
                target);
            camera.aperture = 8.0f;
            camera.iso = 100;
            camera.shutterSpeed = 1.0f / 60.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100.0f;
            camera.allowHDR = true;
            camera.allowMSAA = false;

            var cameraData =
                cameraObject.AddComponent<VividAdditionalCameraData>();
            cameraData.volumeLayerMask = 1;
            cameraData.stopNaNs = true;
            cameraData.dithering = true;
            cameraData.antialiasing = VividAntialiasingMode.None;
        }

        private static void BuildStudio(
            IReadOnlyDictionary<string, Material> materials)
        {
            var studio = new GameObject(
                "A Beautiful Game - VividRP Studio");
            CreatePrimitive(
                "Chess Plinth",
                studio.transform,
                new Vector3(0.0f, -0.085f, 0.0f),
                new Vector3(3.25f, 0.16f, 3.25f),
                Quaternion.identity,
                materials["Studio Base"]);
            CreatePrimitive(
                "Studio Floor",
                studio.transform,
                new Vector3(0.0f, -0.24f, 0.0f),
                new Vector3(10.0f, 0.12f, 10.0f),
                Quaternion.identity,
                materials["Studio Backdrop"]);
            CreatePrimitive(
                "Studio Backdrop",
                studio.transform,
                new Vector3(0.0f, 2.5f, 3.6f),
                new Vector3(10.0f, 5.0f, 0.12f),
                Quaternion.identity,
                materials["Studio Backdrop"]);
            CreatePrimitive(
                "Warm Key Emissive Panel",
                studio.transform,
                new Vector3(-2.4f, 3.8f, -0.8f),
                new Vector3(2.4f, 0.05f, 1.3f),
                Quaternion.Euler(15.0f, 0.0f, -24.0f),
                materials["Key Emission"]);
            CreatePrimitive(
                "Cool Fill Emissive Panel",
                studio.transform,
                new Vector3(4.0f, 3.0f, 1.8f),
                new Vector3(1.5f, 0.05f, 1.5f),
                Quaternion.Euler(35.0f, 0.0f, 55.0f),
                materials["Fill Emission"]);
            SetStaticRecursive(studio);
        }

        private static void CreatePrimitive(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localRotation = rotation;
            gameObject.transform.localScale = scale;
            var renderer = gameObject.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer);
            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.rayTracingMode = RayTracingMode.Static;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private static void SetStaticRecursive(GameObject root)
        {
            foreach (var child in
                     root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = true;
                GameObjectUtility.SetStaticEditorFlags(
                    child.gameObject,
                    StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        private static void CreateTemplate()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                ScenePath);
            var template = AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(
                TemplatePath);
            if (template == null)
            {
                template = SceneTemplateService.CreateTemplateFromScene(
                    scene,
                    TemplatePath);
            }
            else
            {
                var temporaryPath = AssetDatabase.GenerateUniqueAssetPath(
                    TemplateRoot
                    + "/ABeautifulGame_Temporary.scenetemplate");
                var generated =
                    SceneTemplateService.CreateTemplateFromScene(
                        scene,
                        temporaryPath);
                var dependencies =
                    new DependencyInfo[generated.dependencies.Length];
                for (var index = 0;
                     index < dependencies.Length;
                     index++)
                {
                    dependencies[index] = new DependencyInfo
                    {
                        dependency =
                            generated.dependencies[index].dependency,
                        instantiationMode =
                            TemplateInstantiationMode.Reference
                    };
                }

                template.dependencies = dependencies;
                AssetDatabase.DeleteAsset(temporaryPath);
            }

            template.templateScene = scene;
            template.templateName =
                "A Beautiful Game - VividRP Path Tracing";
            template.description =
                "Independent VividRP path-tracing showcase for the CC BY "
                + "4.0 A Beautiful Game chess asset. Uses StandardLit "
                + "opaque and solid dielectric materials, the built-in "
                + "HDRI, emissive-mesh lighting, automatic exposure, and "
                + "no Unity Light components.";
            template.templatePipeline =
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    TemplatePipelinePath);
            template.addToDefaults = false;

            for (var index = 0;
                 index < template.dependencies.Length;
                 index++)
            {
                var dependency = template.dependencies[index];
                dependency.instantiationMode =
                    TemplateInstantiationMode.Reference;
                template.dependencies[index] = dependency;
            }

            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssetIfDirty(template);
        }
    }
}
