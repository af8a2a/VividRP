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
        private const string SceneFolder = "Assets/Scenes/CornellBox";
        private const string MaterialFolder = SceneFolder + "/Materials";
        private const string Tier1ScenePath = SceneFolder + "/BoxScene.unity";
        private const string Tier2ScenePath =
            SceneFolder + "/BoxScene_Tier2_Transmission.unity";
        private const string BackupScenePath =
            SceneFolder + "/BoxScene_PreCodex_Backup.unity";
        private const string Tier1ProfilePath =
            SceneFolder + "/CornellBox_PathTracing_Profile.asset";
        private const string Tier2ProfilePath =
            SceneFolder + "/CornellBox_Tier2_PathTracing_Profile.asset";
        private const string Tier1SceneTemplatePath =
            SceneFolder + "/CornellBox_PathTracing.scenetemplate";
        private const string Tier2SceneTemplatePath =
            SceneFolder + "/CornellBox_Tier2_Transmission.scenetemplate";
        private const string WhiteTexturePath =
            MaterialFolder + "/CB_EmissionWhite.asset";
        private const string HdriPath =
            "Assets/Scenes/ClassicSponza/Art/Generic/Skies/05-18_Day_D.hdr";
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

        [MenuItem("VividRP/Samples/Rebuild Cornell Box Tier Variants")]
        private static void RebuildTierVariants()
        {
            RebuildTier1Scene();
            RebuildTier2Scene();
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
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Scenes", "CornellBox");
            EnsureFolder(SceneFolder, "Materials");

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
