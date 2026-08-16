using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Runtime;

namespace VividRP.Editor.Tools
{
    internal static class CornellBoxSceneBuilder
    {
        private const string SceneFolder =
            "Packages/com.vivid.render-pipelines/Editor/SceneTemplates/"
            + "CornellBox";
        private const string MaterialFolder = SceneFolder + "/Materials";
        private const string MeshFolder = SceneFolder + "/Meshes";
        private const string Tier1ScenePath = SceneFolder + "/BoxScene.unity";
        private const string Tier2ScenePath =
            SceneFolder + "/BoxScene_Tier2_Transmission.unity";
        private const string Tier3ScenePath =
            SceneFolder + "/BoxScene_Tier3_DielectricBox.unity";
        private const string Tier3DragonScenePath =
            SceneFolder + "/BoxScene_Tier3_DragonAttenuation.unity";
        private const string BackupScenePath =
            SceneFolder + "/BoxScene_PreCodex_Backup.unity";
        private const string Tier1ProfilePath =
            SceneFolder + "/CornellBox_PathTracing_Profile.asset";
        private const string Tier2ProfilePath =
            SceneFolder + "/CornellBox_Tier2_PathTracing_Profile.asset";
        private const string Tier3ProfilePath =
            SceneFolder + "/CornellBox_Tier3_PathTracing_Profile.asset";
        private const string Tier3DragonProfilePath =
            SceneFolder
            + "/CornellBox_Tier3_DragonAttenuation_Profile.asset";
        private const string Tier1SceneTemplatePath =
            SceneFolder + "/CornellBox_PathTracing.scenetemplate";
        private const string Tier2SceneTemplatePath =
            SceneFolder + "/CornellBox_Tier2_Transmission.scenetemplate";
        private const string Tier3SceneTemplatePath =
            SceneFolder + "/CornellBox_Tier3_DielectricBox.scenetemplate";
        private const string Tier3DragonSceneTemplatePath =
            SceneFolder
            + "/CornellBox_Tier3_DragonAttenuation.scenetemplate";
        private const string WhiteTexturePath =
            MaterialFolder + "/CB_EmissionWhite.asset";
        private const string Tier3DielectricMeshPath =
            MeshFolder + "/CB_Tier3_DielectricTallBox.asset";
        private const string DragonAttenuationModelPath =
            "Packages/com.vivid.render-pipelines/Samples/DragonAttenuation/"
            + "glTF/DragonAttenuation.gltf";
        private const string DragonAttenuationMaterialPath =
            "Packages/com.vivid.render-pipelines/Samples/DragonAttenuation/"
            + "Materials/Dragon with Attenuation VividRP.mat";
        private const string HdriPath =
            "Packages/com.vivid.render-pipelines/Texture/Default/"
            + "DefaultHDRISky.exr";
        private const string StandardLitShaderName =
            "VividRP/Material/StandardLit";

        [MenuItem("VividRP/Samples/Rebuild Cornell Box Path Tracing Scene")]
        private static void RebuildTier1Scene()
        {
            SaveBackupIfNeeded();
            if (!TryCreateSharedMaterials(
                    out var white,
                    out var red,
                    out var green,
                    out var emission))
                return;

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var sceneRoot = NewObject("CornellBox_Tier1_Opaque_Emission");
            BuildArchitecture(sceneRoot.transform, white, red, green);
            BuildTier1ReferenceProps(sceneRoot.transform, white);
            BuildEmissionLighting(sceneRoot.transform, emission);
            BuildCamera(sceneRoot.transform);
            BuildGlobalVolume(
                sceneRoot.transform,
                Tier1ProfilePath,
                8,
                5);
            ConfigureRenderSettings();
            EnsureHDRPAutoExposureImplementation();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, Tier1ScenePath))
            {
                Debug.LogError(
                    $"[VividRP] Failed to save Tier 1 Cornell Box scene: "
                    + Tier1ScenePath);
                return;
            }

            CreateOrUpdateSceneTemplate(
                Tier1ScenePath,
                Tier1SceneTemplatePath,
                "Cornell Box Tier 1 - Opaque + Emission (VividRP)",
                "Tier 1 VividRP path-tracing reference: StandardLit opaque "
                + "geometry, HDRI importance sampling, emissive mesh "
                + "lighting, automatic exposure, and no Unity Light "
                + "components.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[VividRP] Built Cornell Box Tier 1. "
                + "Lighting uses HDRI and a StandardLit emissive mesh; "
                + "the scene contains no Unity Light components.");
        }

        [MenuItem("VividRP/Samples/Rebuild Cornell Box Tier 2 - Transmission")]
        private static void RebuildTier2Scene()
        {
            if (!TryCreateSharedMaterials(
                    out var white,
                    out var red,
                    out var green,
                    out var emission))
                return;

            var shader = Shader.Find(StandardLitShaderName);
            var roughDark = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_Tier2_RoughDark.mat",
                shader,
                new Color(0.075f, 0.060f, 0.040f, 1.0f),
                0.0f,
                0.04f,
                Color.black);
            var glossyRed = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_Tier2_GlossRed.mat",
                shader,
                new Color(0.42f, 0.018f, 0.025f, 1.0f),
                0.0f,
                0.94f,
                Color.black);
            ConfigureClearCoat(glossyRed, 0.35f, 0.96f);
            var solidGlass = CreateOrUpdateTransmissionMaterial(
                MaterialFolder + "/CB_Tier2_SolidGlass.mat",
                shader);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var sceneRoot = NewObject(
                "CornellBox_Tier2_Opaque_Transmission_Emission");
            BuildArchitecture(sceneRoot.transform, white, red, green);
            BuildTier2ReferenceProps(
                sceneRoot.transform,
                roughDark,
                glossyRed,
                solidGlass);
            BuildEmissionLighting(sceneRoot.transform, emission);
            BuildCamera(
                sceneRoot.transform,
                new Vector3(0.0f, 3.0f, -9.1f),
                new Vector3(0.0f, 2.60f, 0.35f));
            BuildGlobalVolume(
                sceneRoot.transform,
                Tier2ProfilePath,
                8,
                6);
            ConfigureRenderSettings();
            EnsureHDRPAutoExposureImplementation();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, Tier2ScenePath))
            {
                Debug.LogError(
                    $"[VividRP] Failed to save Tier 2 Cornell Box scene: "
                    + Tier2ScenePath);
                return;
            }

            CreateOrUpdateSceneTemplate(
                Tier2ScenePath,
                Tier2SceneTemplatePath,
                "Cornell Box Tier 2 - Transmission + Emission (VividRP)",
                "Tier 2 VividRP path-tracing reference inspired by the "
                + "Cornell sphere box: rough and glossy opaque StandardLit "
                + "spheres, a solid dielectric transmission sphere with "
                + "IOR 1.52, HDRI and emissive mesh lighting, automatic "
                + "exposure, and no Unity Light components.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[VividRP] Built Cornell Box Tier 2 with opaque and solid "
                + "transmission materials. Lighting remains HDRI and "
                + "emissive-mesh only.");
        }

        [MenuItem("VividRP/Samples/Rebuild Cornell Box Tier 3 - Dielectric Box")]
        private static void RebuildTier3Scene()
        {
            if (!TryCreateSharedMaterials(
                    out var white,
                    out var red,
                    out var green,
                    out var emission))
                return;

            var shader = Shader.Find(StandardLitShaderName);
            var dielectric = CreateOrUpdateDielectricBoxMaterial(
                MaterialFolder + "/CB_Tier3_FrostedDielectric.mat",
                shader);
            var dielectricMesh = CreateOrUpdateCuboidMesh(
                Tier3DielectricMeshPath,
                new Vector3(1.75f, 3.3f, 1.75f));

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var sceneRoot = NewObject(
                "CornellBox_Tier3_DielectricBox_ColorTransmission");
            BuildArchitecture(sceneRoot.transform, white, red, green);
            BuildTier3ReferenceProps(
                sceneRoot.transform,
                white,
                dielectric,
                dielectricMesh);
            BuildEmissionLighting(sceneRoot.transform, emission);
            BuildCamera(sceneRoot.transform);
            BuildGlobalVolume(
                sceneRoot.transform,
                Tier3ProfilePath,
                8,
                6);
            ConfigureRenderSettings();
            EnsureHDRPAutoExposureImplementation();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, Tier3ScenePath))
            {
                Debug.LogError(
                    $"[VividRP] Failed to save Tier 3 Cornell Box scene: "
                    + Tier3ScenePath);
                return;
            }

            CreateOrUpdateSceneTemplate(
                Tier3ScenePath,
                Tier3SceneTemplatePath,
                "Cornell Box Tier 3 - Dielectric Box (VividRP)",
                "Tier 3 VividRP path-tracing reference based on Tier 1. "
                + "The tall right box is a closed frosted thin-walled "
                + "dielectric shell with IOR 1.46 so "
                + "colored indirect light penetrates the white body. "
                + "Lighting remains HDRI and emissive-mesh only, with no "
                + "Unity Light components.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[VividRP] Built Cornell Box Tier 3 with a frosted thin-walled "
                + "dielectric tall box and color transmission.");
        }

        [MenuItem(
            "VividRP/Samples/Rebuild Cornell Box Tier 3 - Dragon Attenuation")]
        private static void RebuildTier3DragonAttenuationScene()
        {
            if (!TryCreateSharedMaterials(
                    out var white,
                    out var red,
                    out var green,
                    out var emission)
                || !TryLoadDragonAttenuationAssets(
                    out var dragonModel,
                    out var dragonMaterial))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var sceneRoot = NewObject(
                "CornellBox_Tier3_DragonAttenuation");
            BuildArchitecture(sceneRoot.transform, white, red, green);
            BuildTier3DragonAttenuationProps(
                sceneRoot.transform,
                dragonModel,
                dragonMaterial);
            BuildEmissionLighting(sceneRoot.transform, emission);
            BuildCamera(
                sceneRoot.transform,
                new Vector3(0.0f, 2.45f, -8.6f),
                new Vector3(0.0f, 1.65f, 0.70f));
            BuildGlobalVolume(
                sceneRoot.transform,
                Tier3DragonProfilePath,
                8,
                8);
            ConfigureRenderSettings();
            EnsureHDRPAutoExposureImplementation();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, Tier3DragonScenePath))
            {
                Debug.LogError(
                    "[VividRP] Failed to save Tier 3 Dragon Attenuation "
                    + "Cornell Box scene: "
                    + Tier3DragonScenePath);
                return;
            }

            CreateOrUpdateSceneTemplate(
                Tier3DragonScenePath,
                Tier3DragonSceneTemplatePath,
                "Cornell Box Tier 3 - Dragon Attenuation (VividRP)",
                "Tier 3 VividRP path-tracing reference based on Tier 1. "
                + "The packaged Khronos DragonAttenuation mesh uses the "
                + "included Dragon with Attenuation VividRP StandardLit "
                + "material for solid dielectric absorption and colored "
                + "transmission. Lighting remains HDRI and emissive-mesh "
                + "only, with no Unity Light components.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[VividRP] Built Cornell Box Tier 3 Dragon Attenuation "
                + "variant with the packaged Khronos sample asset.");
        }

        [MenuItem("VividRP/Samples/Rebuild Cornell Box Tier Variants")]
        private static void RebuildTierVariants()
        {
            RebuildTier1Scene();
            RebuildTier2Scene();
            RebuildTier3Scene();
            RebuildTier3DragonAttenuationScene();
        }

        [MenuItem("VividRP/Samples/Create Cornell Box Scene Template")]
        private static void CreateSceneTemplates()
        {
            CreateOrUpdateSceneTemplate(
                Tier1ScenePath,
                Tier1SceneTemplatePath,
                "Cornell Box Tier 1 - Opaque + Emission (VividRP)",
                "Tier 1 VividRP path-tracing reference with opaque geometry "
                + "and emissive-mesh lighting.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Tier2ScenePath)
                != null)
            {
                CreateOrUpdateSceneTemplate(
                    Tier2ScenePath,
                    Tier2SceneTemplatePath,
                    "Cornell Box Tier 2 - Transmission + Emission (VividRP)",
                    "Tier 2 VividRP path-tracing reference with opaque and "
                    + "solid dielectric transmission geometry.");
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Tier3ScenePath)
                != null)
            {
                CreateOrUpdateSceneTemplate(
                    Tier3ScenePath,
                    Tier3SceneTemplatePath,
                    "Cornell Box Tier 3 - Dielectric Box (VividRP)",
                    "Tier 3 VividRP path-tracing reference based on Tier 1 "
                    + "with a frosted thin-walled dielectric tall box and "
                    + "color transmission.");
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    Tier3DragonScenePath)
                != null)
            {
                CreateOrUpdateSceneTemplate(
                    Tier3DragonScenePath,
                    Tier3DragonSceneTemplatePath,
                    "Cornell Box Tier 3 - Dragon Attenuation (VividRP)",
                    "Tier 3 VividRP path-tracing reference based on Tier 1 "
                    + "with the packaged Khronos DragonAttenuation mesh and "
                    + "included VividRP solid attenuation material.");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateOrUpdateSceneTemplate(
            string scenePath,
            string sceneTemplatePath,
            string templateName,
            string description)
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (sceneAsset == null)
            {
                Debug.LogError(
                    $"[VividRP] Cornell Box source scene was not found: "
                    + scenePath);
                return;
            }

            var sceneTemplate =
                AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(
                    sceneTemplatePath);
            if (sceneTemplate == null)
            {
                sceneTemplate = SceneTemplateService.CreateTemplateFromScene(
                    sceneAsset,
                    sceneTemplatePath);
            }
            else
            {
                Undo.RecordObject(
                    sceneTemplate,
                    "Refresh Cornell Box scene template");
                RefreshTemplateDependencies(sceneTemplate, sceneAsset);
            }

            sceneTemplate.templateScene = sceneAsset;
            sceneTemplate.templateName = templateName;
            sceneTemplate.description = description;
            sceneTemplate.templatePipeline =
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Packages/com.vivid.render-pipelines/Editor/SceneTemplates/"
                    + "VividBasicScenePipeline.cs");
            sceneTemplate.addToDefaults = false;

            for (var index = 0;
                 index < sceneTemplate.dependencies.Length;
                 index++)
            {
                var dependency = sceneTemplate.dependencies[index];
                dependency.instantiationMode =
                    TemplateInstantiationMode.Reference;
                sceneTemplate.dependencies[index] = dependency;
            }

            EditorUtility.SetDirty(sceneTemplate);
            AssetDatabase.SaveAssetIfDirty(sceneTemplate);
            Debug.Log(
                $"[VividRP] Created or updated Cornell Box Scene Template: "
                + sceneTemplatePath);
        }

        private static void RefreshTemplateDependencies(
            SceneTemplateAsset destination,
            SceneAsset sceneAsset)
        {
            var temporaryPath = AssetDatabase.GenerateUniqueAssetPath(
                SceneFolder + "/CornellBox_PathTracing_Temporary.scenetemplate");
            var generated = SceneTemplateService.CreateTemplateFromScene(
                sceneAsset,
                temporaryPath);

            var dependencies =
                new DependencyInfo[generated.dependencies.Length];
            for (var index = 0;
                 index < generated.dependencies.Length;
                 index++)
            {
                dependencies[index] = new DependencyInfo
                {
                    dependency = generated.dependencies[index].dependency,
                    instantiationMode =
                        TemplateInstantiationMode.Reference
                };
            }

            destination.dependencies = dependencies;

            AssetDatabase.DeleteAsset(temporaryPath);
        }

        private static void BuildArchitecture(
            Transform root,
            Material white,
            Material red,
            Material green)
        {
            var architecture = NewObject("Architecture");
            architecture.transform.SetParent(root, false);

            CreateCube(
                "Floor",
                architecture.transform,
                new Vector3(0.0f, -0.05f, 0.0f),
                Vector3.zero,
                new Vector3(6.0f, 0.1f, 6.0f),
                white);
            CreateCube(
                "Ceiling",
                architecture.transform,
                new Vector3(0.0f, 6.05f, 0.0f),
                Vector3.zero,
                new Vector3(6.0f, 0.1f, 6.0f),
                white);
            CreateCube(
                "Back Wall",
                architecture.transform,
                new Vector3(0.0f, 3.0f, 3.05f),
                Vector3.zero,
                new Vector3(6.0f, 6.0f, 0.1f),
                white);
            CreateCube(
                "Left Wall - Red",
                architecture.transform,
                new Vector3(-3.05f, 3.0f, 0.0f),
                Vector3.zero,
                new Vector3(0.1f, 6.0f, 6.0f),
                red);
            CreateCube(
                "Right Wall - Green",
                architecture.transform,
                new Vector3(3.05f, 3.0f, 0.0f),
                Vector3.zero,
                new Vector3(0.1f, 6.0f, 6.0f),
                green);
        }

        private static void BuildTier1ReferenceProps(
            Transform root,
            Material white)
        {
            var props = NewObject("Tier 1 Props - Opaque");
            props.transform.SetParent(root, false);

            CreateCube(
                "Short Box",
                props.transform,
                new Vector3(-1.15f, 1.0f, 0.45f),
                new Vector3(0.0f, -17.0f, 0.0f),
                new Vector3(1.8f, 2.0f, 1.8f),
                white);
            CreateCube(
                "Tall Box",
                props.transform,
                new Vector3(1.10f, 1.65f, 1.15f),
                new Vector3(0.0f, 18.0f, 0.0f),
                new Vector3(1.75f, 3.3f, 1.75f),
                white);
        }

        private static void BuildTier2ReferenceProps(
            Transform root,
            Material roughDark,
            Material glossyRed,
            Material solidGlass)
        {
            var props = NewObject(
                "Tier 2 Props - Opaque and Solid Transmission");
            props.transform.SetParent(root, false);

            CreateSphere(
                "Opaque Rough Sphere",
                props.transform,
                new Vector3(-1.45f, 1.15f, 0.80f),
                2.30f,
                roughDark);
            CreateSphere(
                "Opaque Glossy Red Sphere",
                props.transform,
                new Vector3(1.45f, 1.15f, 0.80f),
                2.30f,
                glossyRed);
            CreateSphere(
                "Solid Glass Sphere - IOR 1.52",
                props.transform,
                new Vector3(0.0f, 0.85f, -0.85f),
                1.70f,
                solidGlass);
        }

        private static void BuildTier3ReferenceProps(
            Transform root,
            Material opaqueWhite,
            Material dielectric,
            Mesh dielectricMesh)
        {
            var props = NewObject(
                "Tier 3 Props - Opaque and Dielectric Boxes");
            props.transform.SetParent(root, false);

            CreateCube(
                "Short Box - Opaque White",
                props.transform,
                new Vector3(-1.15f, 1.0f, 0.45f),
                new Vector3(0.0f, -17.0f, 0.0f),
                new Vector3(1.8f, 2.0f, 1.8f),
                opaqueWhite);
            CreateMeshObject(
                "Tall Box - Frosted Dielectric Shell IOR 1.46",
                props.transform,
                new Vector3(1.10f, 1.65f, 1.15f),
                new Vector3(0.0f, 18.0f, 0.0f),
                dielectricMesh,
                dielectric);
        }

        private static void BuildTier3DragonAttenuationProps(
            Transform root,
            GameObject dragonModel,
            Material dragonMaterial)
        {
            var props = NewObject(
                "Tier 3 Props - Khronos Dragon Attenuation");
            props.transform.SetParent(root, false);

            var modelInstance = PrefabUtility.InstantiatePrefab(
                dragonModel) as GameObject;
            if (modelInstance == null)
                modelInstance = Object.Instantiate(dragonModel);

            if (PrefabUtility.IsPartOfPrefabInstance(modelInstance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    modelInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }
            modelInstance.name =
                "DragonAttenuation - Solid Dielectric IOR 1.50";
            modelInstance.transform.SetParent(props.transform, false);
            modelInstance.transform.localPosition =
                new Vector3(0.0f, 0.842f, 0.70f);
            modelInstance.transform.localEulerAngles =
                new Vector3(0.0f, -12.0f, 0.0f);
            modelInstance.transform.localScale = Vector3.one * 1.15f;

            var backdrop = FindChildByName(
                modelInstance.transform,
                "Cloth Backdrop");
            if (backdrop != null)
                Object.DestroyImmediate(backdrop.gameObject);

            var dragon = FindChildByName(
                modelInstance.transform,
                "Dragon");
            if (dragon == null)
            {
                Debug.LogError(
                    "[VividRP] DragonAttenuation model does not contain "
                    + "the expected Dragon child.");
                return;
            }

            foreach (var renderer in dragon.GetComponentsInChildren<Renderer>(
                         true))
            {
                renderer.sharedMaterial = dragonMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                GameObjectUtility.SetStaticEditorFlags(
                    renderer.gameObject,
                    StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        private static void BuildEmissionLighting(
            Transform root,
            Material emission)
        {
            var lighting = NewObject("Lighting - Emissive and HDRI Only");
            lighting.transform.SetParent(root, false);
            CreateCube(
                "Ceiling Emissive Panel",
                lighting.transform,
                new Vector3(0.0f, 5.94f, 0.20f),
                Vector3.zero,
                new Vector3(2.1f, 0.04f, 1.55f),
                emission);
        }

        private static void BuildCamera(Transform root)
        {
            BuildCamera(
                root,
                new Vector3(0.0f, 3.0f, -8.6f),
                new Vector3(0.0f, 3.0f, 0.35f));
        }

        private static void BuildCamera(
            Transform root,
            Vector3 position,
            Vector3 target)
        {
            var cameraObject = NewObject("Main Camera");
            cameraObject.transform.SetParent(root, false);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = position;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                target - cameraObject.transform.position,
                Vector3.up);

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50.0f;
            camera.usePhysicalProperties = true;
            camera.sensorSize = new Vector2(36.0f, 24.0f);
            camera.focalLength = 28.0f;
            camera.focusDistance = Vector3.Distance(position, target);
            camera.aperture = 8.0f;

            var cameraData = cameraObject.AddComponent<VividAdditionalCameraData>();
            cameraData.volumeLayerMask = 1;
            cameraData.stopNaNs = true;
            cameraData.dithering = true;
            cameraData.antialiasing = VividAntialiasingMode.None;
        }

        private static void BuildGlobalVolume(
            Transform root,
            string profilePath,
            int maxBounceCount,
            int russianRouletteStartBounce)
        {
            var profile = GetOrCreateProfile(profilePath);
            ConfigureVolumeProfile(
                profile,
                maxBounceCount,
                russianRouletteStartBounce);

            var volumeObject = NewObject(
                "Global Volume - Path Tracing Ground Truth");
            volumeObject.transform.SetParent(root, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100.0f;
            volume.weight = 1.0f;
            volume.sharedProfile = profile;
        }

        private static void ConfigureVolumeProfile(
            VolumeProfile profile,
            int maxBounceCount,
            int russianRouletteStartBounce)
        {
            Undo.RecordObject(profile, "Configure Cornell Box volume profile");
            profile.components.RemoveAll(component => component == null);

            var skySettings = GetOrAdd<SkySettingsVolume>(profile);
            Set(skySettings.skyType, SkyType.HDRI);
            Set(skySettings.updateMode, SkyUpdateMode.OnChanged);
            Set(skySettings.includeSunInBaking, false);
            Set(
                skySettings.generatedCubemapQuality,
                SkyGeneratedCubemapQuality.High);
            Set(skySettings.renderingSpace, RenderingSpace.World);

            var hdriSky = GetOrAdd<HDRISkyVolume>(profile);
            Set(
                hdriSky.skyCubemap,
                AssetDatabase.LoadAssetAtPath<Cubemap>(HdriPath));
            Set(hdriSky.skyIntensityMode, SkyIntensityMode.Multiplier);
            Set(hdriSky.multiplier, 0.30f);
            Set(hdriSky.exposure, 0.0f);
            Set(hdriSky.rotation, 112.0f);

            var pathTracing =
                GetOrAdd<ReferencedPathTracingSettingsVolume>(profile);
            Set(pathTracing.deterministicSampling, true);
            Set(pathTracing.fixedSeed, 12648430);
            Set(
                pathTracing.pathSamplingMode,
                ReferencedPathTracingSamplingMode.IndexedBnd);
            Set(pathTracing.maxBounceCount, maxBounceCount);
            Set(
                pathTracing.russianRouletteStartBounce,
                russianRouletteStartBounce);
            Set(pathTracing.enableReGIR, false);
            Set(pathTracing.shadingPointLightSelection, false);
            Set(pathTracing.lightSpatialIndex, false);
            Set(pathTracing.enableShaderExecutionReordering, false);
            Set(pathTracing.targetSampleCount, 4096);
            Set(
                pathTracing.environmentMode,
                ReferencedPathTracingEnvironmentMode.Hdri);
            Set(pathTracing.environmentLighting, true);
            Set(pathTracing.environmentCameraVisible, false);
            Set(
                pathTracing.environmentSamplingMode,
                ReferencedPathTracingEnvironmentSamplingMode
                    .ImportanceSampling);
            Set(
                pathTracing.environmentEstimatorMode,
                ReferencedPathTracingEnvironmentEstimatorMode.Mis);

            var exposure = GetOrAdd<AutoExposure>(profile);
            Set(exposure.enabled, true);
            Set(
                exposure.exposureMode,
                AutoExposureExposureMode.AutomaticHistogram);
            Set(
                exposure.meteringMode,
                AutoExposureMeteringMode.ProceduralMask);
            Set(exposure.compensation, 0.35f);
            Set(exposure.limitMin, -4.0f);
            Set(exposure.limitMax, 12.0f);
            Set(
                exposure.adaptationMode,
                AutoExposureAdaptationMode.Progressive);
            Set(exposure.adaptationSpeedDarkToLight, 2.0f);
            Set(exposure.adaptationSpeedLightToDark, 1.5f);
            Set(exposure.histogramPercentages, new Vector2(10.0f, 90.0f));
            Set(exposure.histogramUseCurveRemapping, false);
            Set(exposure.targetMidGray, TargetMidGray.Grey18);
            Set(exposure.centerAroundExposureTarget, false);
            Set(exposure.proceduralCenter, new Vector2(0.5f, 0.5f));
            Set(exposure.proceduralRadii, new Vector2(0.46f, 0.46f));
            Set(exposure.proceduralSoftness, 0.75f);
            Set(exposure.maskMinIntensity, -12.0f);
            Set(exposure.maskMaxIntensity, 16.0f);

            var tonemapping = GetOrAdd<Tonemapping>(profile);
            Set(tonemapping.mode, TonemappingMode.ACES);
            Set(tonemapping.useFullACES, true);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private static VolumeProfile GetOrCreateProfile(string profilePath)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                profilePath);
            if (profile != null)
                return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = System.IO.Path.GetFileNameWithoutExtension(
                profilePath);
            AssetDatabase.CreateAsset(profile, profilePath);
            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            component.active = true;
            EditorUtility.SetDirty(component);
            return component;
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.reflectionIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        private static void EnsureHDRPAutoExposureImplementation()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline
                as VividRenderPipelineAsset;
            if (pipeline == null
                || pipeline.AutoExposureImplementation
                    == AutoExposureImplementationPath.HDRP)
            {
                return;
            }

            Undo.RecordObject(pipeline, "Use HDRP auto exposure");
            pipeline.AutoExposureImplementation =
                AutoExposureImplementationPath.HDRP;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
        }

        private static bool TryCreateSharedMaterials(
            out Material white,
            out Material red,
            out Material green,
            out Material emission)
        {
            EnsureFolder(
                "Packages/com.vivid.render-pipelines/Editor/SceneTemplates",
                "CornellBox");
            EnsureFolder(SceneFolder, "Materials");
            EnsureFolder(SceneFolder, "Meshes");

            white = null;
            red = null;
            green = null;
            emission = null;

            var shader = Shader.Find(StandardLitShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[VividRP] Required shader was not found: "
                    + StandardLitShaderName);
                return false;
            }

            white = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_White.mat",
                shader,
                new Color(0.725f, 0.710f, 0.680f, 1.0f),
                0.0f,
                0.12f,
                Color.black);
            red = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_Red.mat",
                shader,
                new Color(0.630f, 0.065f, 0.050f, 1.0f),
                0.0f,
                0.10f,
                Color.black);
            green = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_Green.mat",
                shader,
                new Color(0.140f, 0.450f, 0.091f, 1.0f),
                0.0f,
                0.10f,
                Color.black);
            emission = CreateOrUpdateMaterial(
                MaterialFolder + "/CB_CeilingEmitter.mat",
                shader,
                new Color(0.02f, 0.02f, 0.02f, 1.0f),
                0.0f,
                0.0f,
                new Color(22.0f, 19.5f, 15.0f, 1.0f));
            return true;
        }

        private static bool TryLoadDragonAttenuationAssets(
            out GameObject model,
            out Material material)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(
                DragonAttenuationModelPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(
                DragonAttenuationMaterialPath);
            if (model != null
                && material != null
                && material.shader != null
                && material.shader.name == StandardLitShaderName)
            {
                return true;
            }

            Debug.LogError(
                "[VividRP] Tier 3 Dragon Attenuation requires "
                + DragonAttenuationModelPath
                + " and a StandardLit material at "
                + DragonAttenuationMaterialPath
                + ".");
            return false;
        }

        private static Material CreateOrUpdateMaterial(
            string path,
            Shader shader,
            Color baseColor,
            float metallic,
            float smoothness,
            Color emission)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                Undo.RecordObject(material, "Configure Cornell Box material");
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_WorkflowMode", 1.0f);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0.0f);
            material.SetFloat("_AlphaClip", 0.0f);
            material.SetFloat("_Cull", 2.0f);
            material.SetFloat("_ReceiveShadows", 1.0f);
            material.SetColor("_EmissionColor", emission);
            material.SetFloat("_ClearCoatMask", 0.0f);
            material.SetFloat("_ClearCoatSmoothness", 1.0f);
            material.SetFloat("_ThinWalledTransmission", 0.0f);
            material.SetFloat("_TransmissionWeight", 0.0f);
            material.SetTexture("_TransmissionMap", null);
            material.SetColor("_TransmissionColor", Color.white);
            material.SetFloat("_TransmissionDepth", 0.0f);
            material.SetColor("_TransmissionScatter", Color.black);
            material.SetFloat("_TransmissionScatterAnisotropy", 0.0f);
            material.SetFloat("_SpecularIOR", 1.5f);

            if (emission.maxColorComponent > 0.0f)
            {
                material.SetTexture("_EmissionMap", GetOrCreateWhiteTexture());
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.SetTexture("_EmissionMap", Texture2D.blackTexture);
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            StandardLitMaterialUtility.SetupMaterial(material, null, false);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void ConfigureClearCoat(
            Material material,
            float mask,
            float smoothness)
        {
            Undo.RecordObject(material, "Configure Cornell Box clear coat");
            material.SetFloat("_ClearCoatMask", mask);
            material.SetFloat("_ClearCoatSmoothness", smoothness);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        private static Material CreateOrUpdateTransmissionMaterial(
            string path,
            Shader shader)
        {
            var material = CreateOrUpdateMaterial(
                path,
                shader,
                new Color(0.97f, 0.985f, 1.0f, 1.0f),
                0.0f,
                0.995f,
                Color.black);

            Undo.RecordObject(
                material,
                "Configure Cornell Box solid transmission");
            material.SetFloat("_Surface", 0.0f);
            material.SetFloat("_Cull", 0.0f);
            material.SetFloat("_ThinWalledTransmission", 0.0f);
            material.SetFloat("_TransmissionWeight", 1.0f);
            material.SetTexture("_TransmissionMap", null);
            material.SetColor(
                "_TransmissionColor",
                new Color(0.985f, 0.995f, 1.0f, 1.0f));
            material.SetFloat("_TransmissionDepth", 1.0f);
            material.SetColor("_TransmissionScatter", Color.black);
            material.SetFloat("_TransmissionScatterAnisotropy", 0.0f);
            material.SetFloat("_SpecularIOR", 1.52f);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Material CreateOrUpdateDielectricBoxMaterial(
            string path,
            Shader shader)
        {
            var material = CreateOrUpdateMaterial(
                path,
                shader,
                new Color(0.95f, 0.95f, 0.94f, 1.0f),
                0.0f,
                0.72f,
                Color.black);

            Undo.RecordObject(
                material,
                "Configure Cornell Box frosted dielectric");
            material.SetFloat("_Surface", 0.0f);
            material.SetFloat("_Cull", 0.0f);
            material.SetFloat("_ThinWalledTransmission", 1.0f);
            material.SetFloat("_TransmissionWeight", 0.65f);
            material.SetTexture("_TransmissionMap", null);
            material.SetColor(
                "_TransmissionColor",
                new Color(0.985f, 0.995f, 1.0f, 1.0f));
            material.SetFloat("_TransmissionDepth", 0.0f);
            material.SetColor("_TransmissionScatter", Color.black);
            material.SetFloat("_TransmissionScatterAnisotropy", 0.0f);
            material.SetFloat("_SpecularIOR", 1.46f);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Texture2D GetOrCreateWhiteTexture()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                WhiteTexturePath);
            if (texture == null)
            {
                texture = new Texture2D(
                    1,
                    1,
                    TextureFormat.RGBA32,
                    false,
                    true)
                {
                    name = "CB_EmissionWhite",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply(false, false);
                AssetDatabase.CreateAsset(texture, WhiteTexturePath);
            }

            return texture;
        }

        private static Mesh CreateOrUpdateCuboidMesh(
            string path,
            Vector3 size)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                Undo.RecordObject(mesh, "Configure Cornell Box cuboid mesh");
                mesh.Clear();
            }

            var half = size * 0.5f;
            var x = half.x;
            var y = half.y;
            var z = half.z;
            mesh.vertices = new[]
            {
                new Vector3(-x, -y, -z), new Vector3(-x, y, -z),
                new Vector3(x, y, -z), new Vector3(x, -y, -z),
                new Vector3(-x, -y, z), new Vector3(x, -y, z),
                new Vector3(x, y, z), new Vector3(-x, y, z),
                new Vector3(-x, -y, z), new Vector3(-x, y, z),
                new Vector3(-x, y, -z), new Vector3(-x, -y, -z),
                new Vector3(x, -y, -z), new Vector3(x, y, -z),
                new Vector3(x, y, z), new Vector3(x, -y, z),
                new Vector3(-x, y, -z), new Vector3(-x, y, z),
                new Vector3(x, y, z), new Vector3(x, y, -z),
                new Vector3(-x, -y, z), new Vector3(-x, -y, -z),
                new Vector3(x, -y, -z), new Vector3(x, -y, z)
            };
            mesh.normals = new[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.forward,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down
            };
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right,
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right,
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right,
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Mesh mesh,
            Material material)
        {
            var gameObject = NewObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localEulerAngles = localEulerAngles;
            gameObject.transform.localScale = Vector3.one;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            GameObjectUtility.SetStaticEditorFlags(
                gameObject,
                StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.ReflectionProbeStatic);
            return gameObject;
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localEulerAngles = localEulerAngles;
            gameObject.transform.localScale = localScale;

            var renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            gameObject.isStatic = true;
            GameObjectUtility.SetStaticEditorFlags(
                gameObject,
                StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.ReflectionProbeStatic);
            return gameObject;
        }

        private static GameObject CreateSphere(
            string name,
            Transform parent,
            Vector3 localPosition,
            float diameter,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one * diameter;

            var renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            gameObject.isStatic = true;
            GameObjectUtility.SetStaticEditorFlags(
                gameObject,
                StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.ReflectionProbeStatic);
            return gameObject;
        }

        private static GameObject NewObject(string name)
        {
            return new GameObject(name);
        }

        private static Transform FindChildByName(
            Transform root,
            string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        private static void SaveBackupIfNeeded()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != Tier1ScenePath
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(Tier1ScenePath)
                    == null
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(BackupScenePath)
                    != null)
            {
                return;
            }

            EditorSceneManager.SaveScene(
                activeScene,
                BackupScenePath,
                true);
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void Set<T>(VolumeParameter<T> parameter, T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }
    }
}
