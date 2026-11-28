using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Samples.GlassLookDev.Editor
{
    /// <summary>
    /// Builds the VividRP Solid Optical & Volumetric Glass LookDev studio scene featuring:
    ///   - Hero Centerpiece: Khronos DragonAttenuation Amber Glass Dragon (Beer-Lambert + Backface Snell)
    ///   - 4 Calibration Spheres: Clear Optical Glass, Frosted Rough Glass, Emerald Volume Glass,
    ///     and High-IOR Dispersive Crystal (Cauchy/Fraunhofer RGB Abbe Dispersion)
    ///   - High-contrast optical checkerboard floor & backdrop for unambiguous refraction & blur verification.
    /// </summary>
    public static class GlassLookDevSceneBuilder
    {
        private const string PackageRoot = "Packages/com.vivid.render-pipelines/Samples/GlassLookDev";
        private const string ModelsRoot = PackageRoot + "/Models";
        private const string MaterialsRoot = PackageRoot + "/Materials";
        private const string ScenePath = PackageRoot + "/GlassLookDev.unity";
        private const string VolumeProfilePath = PackageRoot + "/GlassLookDev_StudioVolume.asset";

        private const string DragonObjPath = ModelsRoot + "/DragonAttenuation.obj";
        private const string DragonThicknessPath = "Packages/com.vivid.render-pipelines/Samples/DragonAttenuation/glTF/Dragon_ThicknessMap.jpg";
        private const string CheckerboardPath = "Packages/com.vivid.render-pipelines/Samples/DragonAttenuation/glTF/checkerboard.png";
        private const string TmpMaterialPath = "Packages/com.vivid.render-pipelines/Samples/FurLookDev/Materials/Studio_TMP_SDF.mat";

        [MenuItem("VividRP/Glass & Refraction/Build GlassLookDev Studio Scene")]
        public static void BuildStudioScene()
        {
            EnsureFolder(PackageRoot);
            EnsureFolder(ModelsRoot);
            EnsureFolder(MaterialsRoot);

            ConfigureImportedAssets();

            var dragonMesh = LoadFirstMesh(DragonObjPath);
            var thicknessTex = AssetDatabase.LoadAssetAtPath<Texture2D>(DragonThicknessPath);
            var checkerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CheckerboardPath);

            if (dragonMesh == null)
            {
                Debug.LogError("[GlassLookDev] Missing DragonAttenuation.obj under " + ModelsRoot);
                return;
            }

            var standardShader = Shader.Find("VividRP/Material/StandardLit");
            var glassShader = Shader.Find("VividRP/Material/Glass/OpticalGlass");
            if (standardShader == null || glassShader == null)
            {
                Debug.LogError("[GlassLookDev] Required shaders VividRP/Material/StandardLit or VividRP/Material/Glass/OpticalGlass not found.");
                return;
            }

            // Studio enclosure materials
            var matWall = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/GlassStudio_Wall.mat", standardShader,
                new Color(0.18f, 0.19f, 0.22f, 1.0f), metallic: 0.0f, smoothness: 0.12f, baseMap: null, tiling: Vector2.one);
            var matCheckerBackdrop = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/GlassStudio_CheckerBackdrop.mat", standardShader,
                new Color(0.78f, 0.80f, 0.83f, 1.0f), metallic: 0.0f, smoothness: 0.18f, baseMap: checkerTex, tiling: new Vector2(10.0f, 5.5f));
            var matCheckerFloor = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/GlassStudio_CheckerFloor.mat", standardShader,
                new Color(0.72f, 0.74f, 0.78f, 1.0f), metallic: 0.0f, smoothness: 0.25f, baseMap: checkerTex, tiling: new Vector2(12.0f, 10.0f));
            var matPlinth = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/GlassStudio_Plinth.mat", standardShader,
                new Color(0.08f, 0.09f, 0.11f, 1.0f), metallic: 0.05f, smoothness: 0.25f, baseMap: null, tiling: Vector2.one);

            // Hero: Amber DragonAttenuation Glass (Beer-Lambert rich honey-amber volume + crisp specular)
            var matAmberDragon = CreateOrUpdateGlassMaterial(
                MaterialsRoot + "/Glass_Hero_AmberDragon.mat",
                glassShader,
                ior: 1.50f,
                smoothness: 0.96f,
                transColor: new Color(0.92f, 0.50f, 0.05f, 1.0f),
                transDepth: 0.11f,
                dispersion: 0.18f,
                thicknessMap: thicknessTex,
                useThicknessMap: 0.0f,
                thicknessScale: 1.0f,
                refractionStrength: 1.0f,
                clearCoatMask: 0.15f);

            // Station 01: Clear Optical Crown Glass
            var matClearGlass = CreateOrUpdateGlassMaterial(
                MaterialsRoot + "/Glass_01_ClearOptical.mat",
                glassShader,
                ior: 1.52f,
                smoothness: 0.99f,
                transColor: new Color(0.97f, 0.99f, 0.98f, 1.0f),
                transDepth: 0.60f,
                dispersion: 0.0f,
                thicknessMap: null,
                useThicknessMap: 0.0f,
                thicknessScale: 1.0f,
                refractionStrength: 1.0f,
                clearCoatMask: 0.0f);

            // Station 02: Frosted Rough Glass (Same-frame OpaqueColorPyramid Mip blur)
            var matFrostedGlass = CreateOrUpdateGlassMaterial(
                MaterialsRoot + "/Glass_02_FrostedRough.mat",
                glassShader,
                ior: 1.50f,
                smoothness: 0.62f,
                transColor: new Color(0.93f, 0.96f, 0.99f, 1.0f),
                transDepth: 0.35f,
                dispersion: 0.0f,
                thicknessMap: null,
                useThicknessMap: 0.0f,
                thicknessScale: 1.0f,
                refractionStrength: 0.95f,
                clearCoatMask: 0.0f);

            // Station 03: Emerald Volumetric Absorption Glass (Deep Beer-Lambert)
            var matEmeraldGlass = CreateOrUpdateGlassMaterial(
                MaterialsRoot + "/Glass_03_EmeraldVolume.mat",
                glassShader,
                ior: 1.57f,
                smoothness: 0.97f,
                transColor: new Color(0.10f, 0.82f, 0.36f, 1.0f),
                transDepth: 0.14f,
                dispersion: 0.15f,
                thicknessMap: null,
                useThicknessMap: 0.0f,
                thicknessScale: 1.0f,
                refractionStrength: 1.0f,
                clearCoatMask: 0.10f);

            // Station 04: High-IOR Dispersive Crystal (Cauchy/Fraunhofer RGB Abbe Dispersion)
            var matDispersiveCrystal = CreateOrUpdateGlassMaterial(
                MaterialsRoot + "/Glass_04_DispersiveCrystal.mat",
                glassShader,
                ior: 1.92f,
                smoothness: 0.995f,
                transColor: new Color(0.95f, 0.97f, 1.0f, 1.0f),
                transDepth: 0.80f,
                dispersion: 1.40f,
                thicknessMap: null,
                useThicknessMap: 0.0f,
                thicknessScale: 1.0f,
                refractionStrength: 1.15f,
                clearCoatMask: 0.0f);

            var volumeProfile = CreateOrUpdateStudioVolumeProfile();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Reload all asset references after NewScene
            volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            dragonMesh = LoadFirstMesh(DragonObjPath);
            matWall = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/GlassStudio_Wall.mat");
            matCheckerBackdrop = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/GlassStudio_CheckerBackdrop.mat");
            matCheckerFloor = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/GlassStudio_CheckerFloor.mat");
            matPlinth = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/GlassStudio_Plinth.mat");
            matAmberDragon = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/Glass_Hero_AmberDragon.mat");
            matClearGlass = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/Glass_01_ClearOptical.mat");
            matFrostedGlass = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/Glass_02_FrostedRough.mat");
            matEmeraldGlass = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/Glass_03_EmeraldVolume.mat");
            matDispersiveCrystal = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/Glass_04_DispersiveCrystal.mat");
            var tmpMat = AssetDatabase.LoadAssetAtPath<Material>(TmpMaterialPath);

            var studioRoot = new GameObject("GlassLookDev_Studio");

            // 1. Studio Enclosure with optical checkerboard floor & back-wall panel
            var room = new GameObject("StudioRoom");
            room.transform.SetParent(studioRoot.transform, false);
            CreateBox(room.transform, "Floor", new Vector3(0.0f, -0.05f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matCheckerFloor);
            CreateBox(room.transform, "Ceiling", new Vector3(0.0f, 3.45f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matWall);
            CreateBox(room.transform, "BackWall", new Vector3(0.0f, 1.7f, 2.45f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "CheckerboardPanel", new Vector3(0.0f, 1.15f, 1.15f), new Vector3(3.4f, 1.9f, 0.04f), matCheckerBackdrop);
            CreateBox(room.transform, "FrontWall", new Vector3(0.0f, 1.7f, -2.55f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "LeftWall", new Vector3(-3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);
            CreateBox(room.transform, "RightWall", new Vector3(3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);

            // 2. Global Volume (Fixed EV100 = 0 + ACES + PT settings)
            var volGo = new GameObject("Studio_GlobalVolume");
            volGo.transform.SetParent(studioRoot.transform, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10.0f;
            volume.sharedProfile = volumeProfile;

            // 3. Studio Lighting (bright soft key + overhead backlight + cool fill for crisp glass highlights)
            var lightsRoot = new GameObject("StudioLights");
            lightsRoot.transform.SetParent(studioRoot.transform, false);

            CreateStudioLight(
                lightsRoot.transform,
                "KeyLight_FrontRight",
                LightType.Spot,
                new Vector3(1.55f, 2.15f, -1.50f),
                new Vector3(0.0f, 0.90f, 0.0f),
                new Color(1.0f, 0.97f, 0.93f),
                intensity: 68.0f,
                range: 10.0f,
                spotAngle: 82.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "RimLight_BackTop",
                LightType.Spot,
                new Vector3(-0.60f, 2.20f, 0.95f),
                new Vector3(0.0f, 0.85f, 0.0f),
                new Color(1.0f, 0.94f, 0.86f),
                intensity: 54.0f,
                range: 9.0f,
                spotAngle: 86.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "BackdropFill_Left",
                LightType.Point,
                new Vector3(-1.60f, 1.45f, -0.60f),
                Vector3.zero,
                new Color(0.86f, 0.92f, 1.0f),
                intensity: 24.0f,
                range: 9.0f,
                spotAngle: 60.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "BackdropWash_Center",
                LightType.Point,
                new Vector3(0.0f, 1.60f, 0.45f),
                Vector3.zero,
                new Color(0.96f, 0.97f, 1.0f),
                intensity: 22.0f,
                range: 8.0f,
                spotAngle: 60.0f);

            // 4. Hero Centerpiece: Khronos DragonAttenuation Amber Glass Dragon (upright on plinth)
            var heroStation = new GameObject("Hero_AmberDragon");
            heroStation.transform.SetParent(studioRoot.transform, false);
            heroStation.transform.localPosition = new Vector3(0.0f, 0.0f, 0.22f);
            CreateBox(heroStation.transform, "DragonPlinth", new Vector3(0.0f, 0.36f, 0.0f), new Vector3(0.72f, 0.72f, 0.38f), matPlinth);

            var dragonGo = new GameObject("DragonAttenuation");
            dragonGo.transform.SetParent(heroStation.transform, false);
            dragonGo.transform.localPosition = new Vector3(0.0f, 0.72f, 0.0f);
            dragonGo.transform.localRotation = Quaternion.Euler(0.0f, 15.0f, 0.0f);
            dragonGo.AddComponent<MeshFilter>().sharedMesh = dragonMesh;
            dragonGo.AddComponent<MeshRenderer>().sharedMaterial = matAmberDragon;

            // 5. Four Optical Glass Calibration Spheres in front arc
            CreateSphereStation(studioRoot.transform, "Station01_ClearOptical", new Vector3(-0.82f, 0.0f, -0.18f), 0.24f, matPlinth, matClearGlass);
            CreateSphereStation(studioRoot.transform, "Station02_FrostedRough", new Vector3(-0.30f, 0.0f, -0.34f), 0.22f, matPlinth, matFrostedGlass);
            CreateSphereStation(studioRoot.transform, "Station03_EmeraldVolume", new Vector3(0.30f, 0.0f, -0.34f), 0.22f, matPlinth, matEmeraldGlass);
            CreateSphereStation(studioRoot.transform, "Station04_DispersiveCrystal", new Vector3(0.82f, 0.0f, -0.18f), 0.24f, matPlinth, matDispersiveCrystal);

            // 6. Studio Labels (dark backing header bar for readability over checkerboard)
            var labelsRoot = new GameObject("StudioLabels");
            labelsRoot.transform.SetParent(studioRoot.transform, false);

            CreateBox(labelsRoot.transform, "TitleBackdropBar", new Vector3(0.0f, 1.82f, 1.10f), new Vector3(3.4f, 0.42f, 0.02f), matPlinth);
            CreateStudioLabel(labelsRoot.transform, "WallTitle", new Vector3(0.0f, 1.90f, 1.07f),
                "VIVIDRP SOLID OPTICAL & VOLUMETRIC GLASS LOOKDEV", 0.96f, tmpMat);
            var telemetryText = CreateStudioLabel(labelsRoot.transform, "WallTelemetry", new Vector3(0.0f, 1.72f, 1.07f),
                "REFRACTION: ON  |  BACKFACE SNELL: ON  |  ABBE DISPERSION: ON", 0.38f, tmpMat);

            CreateStudioLabel(labelsRoot.transform, "Label_01", new Vector3(-0.82f, 0.42f, -0.34f), "01: CLEAR\nIOR 1.52", 0.30f, tmpMat);
            CreateStudioLabel(labelsRoot.transform, "Label_02", new Vector3(-0.30f, 0.42f, -0.50f), "02: FROSTED\nROUGH 0.38", 0.30f, tmpMat);
            CreateStudioLabel(labelsRoot.transform, "Label_03", new Vector3(0.30f, 0.42f, -0.50f), "03: EMERALD\nBEER-LAMBERT", 0.30f, tmpMat);
            CreateStudioLabel(labelsRoot.transform, "Label_04", new Vector3(0.82f, 0.42f, -0.34f), "04: CRYSTAL\nIOR 1.92 DISP", 0.30f, tmpMat);

            // 7. Camera Rigs + Main Camera
            var rigsRoot = new GameObject("CameraRigs");
            rigsRoot.transform.SetParent(studioRoot.transform, false);

            var overviewRig = CreateCameraRig(rigsRoot.transform, "Rig_Overview", new Vector3(0.0f, 1.12f, -2.35f), new Vector3(0.0f, 0.86f, 0.0f));
            var dragonCloseRig = CreateCameraRig(rigsRoot.transform, "Rig_AmberDragonCloseUp", new Vector3(-0.08f, 1.04f, -0.78f), new Vector3(0.0f, 0.92f, 0.22f));
            var fourPresetsRig = CreateCameraRig(rigsRoot.transform, "Rig_FourPresetsRow", new Vector3(0.0f, 0.72f, -1.85f), new Vector3(0.0f, 0.62f, -0.26f));
            var dispersionCloseRig = CreateCameraRig(rigsRoot.transform, "Rig_DispersionCloseUp", new Vector3(0.82f, 0.66f, -0.82f), new Vector3(0.82f, 0.64f, -0.18f));
            var sideProfileRig = CreateCameraRig(rigsRoot.transform, "Rig_SideProfile", new Vector3(-1.45f, 1.00f, -1.15f), new Vector3(0.0f, 0.84f, 0.0f));

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(studioRoot.transform, false);
            camGo.transform.SetPositionAndRotation(overviewRig.position, overviewRig.rotation);
            var camera = camGo.AddComponent<Camera>();
            camera.fieldOfView = 36.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50.0f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.025f, 1.0f);
            camGo.AddComponent<VividAdditionalCameraData>();

            // 8. Interactive Controller
            var controller = studioRoot.AddComponent<VividGlassLookDevController>();
            controller.StudioCamera = camera;
            controller.StudioVolume = volume;
            controller.StudioLabelsRoot = labelsRoot;
            controller.StatusTelemetryText = telemetryText;
            controller.OverviewRig = overviewRig;
            controller.AmberDragonCloseUpRig = dragonCloseRig;
            controller.FourPresetsRowRig = fourPresetsRig;
            controller.DispersionCloseUpRig = dispersionCloseRig;
            controller.SideProfileRig = sideProfileRig;
            controller.ActiveCameraPreset = GlassLookDevCameraPreset.Overview;
            controller.EnableRefraction = true;
            controller.EnableDispersion = true;
            controller.EnableBackfaceRefraction = true;
            controller.DebugMode = GlassLookDevDebugMode.Composite;
            controller.ApplySettings();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[GlassLookDev] Successfully built and saved " + ScenePath);
        }

        private static void ConfigureImportedAssets()
        {
            if (AssetImporter.GetAtPath(DragonObjPath) is ModelImporter modelImporter)
            {
                bool changed = false;
                if (modelImporter.importNormals != ModelImporterNormals.Import)
                {
                    modelImporter.importNormals = ModelImporterNormals.Import;
                    changed = true;
                }
                if (modelImporter.importTangents != ModelImporterTangents.CalculateMikk)
                {
                    modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
                    changed = true;
                }
                if (modelImporter.materialImportMode != ModelImporterMaterialImportMode.None)
                {
                    modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
                    changed = true;
                }
                if (changed)
                    modelImporter.SaveAndReimport();
            }
        }

        private static void CreateSphereStation(
            Transform parent,
            string name,
            Vector3 localPos,
            float diameter,
            Material matPlinth,
            Material matGlass)
        {
            var station = new GameObject(name);
            station.transform.SetParent(parent, false);
            station.transform.localPosition = localPos;

            float plinthHeight = 0.52f;
            CreateCylinder(station.transform, "Plinth", new Vector3(0.0f, plinthHeight * 0.5f, 0.0f), new Vector3(0.24f, plinthHeight * 0.5f, 0.24f), matPlinth);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GlassSphere";
            sphere.transform.SetParent(station.transform, false);
            sphere.transform.localPosition = new Vector3(0.0f, plinthHeight + diameter * 0.5f, 0.0f);
            sphere.transform.localScale = Vector3.one * diameter;
            sphere.GetComponent<Renderer>().sharedMaterial = matGlass;
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
        }

        private static Material CreateOrUpdateGlassMaterial(
            string path,
            Shader shader,
            float ior,
            float smoothness,
            Color transColor,
            float transDepth,
            float dispersion,
            Texture2D thicknessMap,
            float useThicknessMap,
            float thicknessScale,
            float refractionStrength,
            float clearCoatMask)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_SpecularIOR", ior);
            mat.SetFloat("_TransmissionWeight", 1.0f);
            mat.SetColor("_TransmissionColor", transColor);
            mat.SetFloat("_TransmissionDepth", transDepth);
            mat.SetColor("_TransmissionScatter", Color.black);
            mat.SetFloat("_TransmissionScatterAnisotropy", 0.0f);
            mat.SetFloat("_ThinWalledTransmission", 0.0f);
            mat.SetFloat("_Dispersion", dispersion);
            mat.SetTexture("_ThicknessMap", thicknessMap);
            mat.SetFloat("_UseThicknessMap", useThicknessMap);
            mat.SetFloat("_ThicknessScale", thicknessScale);
            mat.SetFloat("_RefractionStrength", refractionStrength);
            mat.SetFloat("_BackfaceSpecularIntensity", 0.25f);
            mat.SetFloat("_ClearCoatMask", clearCoatMask);
            mat.SetFloat("_ClearCoatSmoothness", 0.96f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile CreateOrUpdateStudioVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "GlassLookDev_StudioVolume";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            var sky = GetOrAddVolumeComponent<SkySettingsVolume>(profile);
            sky.active = true;
            sky.skyType.overrideState = true;
            sky.skyType.value = SkyType.None;

            var exposure = GetOrAddVolumeComponent<AutoExposure>(profile);
            exposure.active = true;
            exposure.enabled.overrideState = true;
            exposure.enabled.value = true;
            exposure.exposureMode.overrideState = true;
            exposure.exposureMode.value = AutoExposureExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = 0.0f;
            exposure.mode.overrideState = true;
            exposure.mode.value = AutoExposureMode.Manual;
            exposure.manualEV100.overrideState = true;
            exposure.manualEV100.value = 0.0f;
            exposure.exposureCompensation.overrideState = true;
            exposure.exposureCompensation.value = 1.0f;

            var tonemap = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.overrideState = true;
            tonemap.mode.value = TonemappingMode.ACES;

            var bloom = GetOrAddVolumeComponent<Bloom>(profile);
            bloom.active = false;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.0f;

            var pt = GetOrAddVolumeComponent<ReferencedPathTracingSettingsVolume>(profile);
            pt.active = true;
            pt.deterministicSampling.overrideState = true;
            pt.deterministicSampling.value = true;
            pt.maxBounceCount.overrideState = true;
            pt.maxBounceCount.value = 8;
            pt.russianRouletteStartBounce.overrideState = true;
            pt.russianRouletteStartBounce.value = 5;
            pt.enableReGIR.overrideState = true;
            pt.enableReGIR.value = true;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing) && existing != null)
                return existing;

            var comp = profile.Add<T>(overrides: true);
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, profile);
            EditorUtility.SetDirty(profile);
            return comp;
        }

        private static Material CreateOrUpdateLitMaterial(
            string path,
            Shader shader,
            Color baseColor,
            float metallic,
            float smoothness,
            Texture2D baseMap,
            Vector2 tiling)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Mesh LoadFirstMesh(string assetPath)
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (objs == null)
                return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] is Mesh mesh)
                    return mesh;
            }
            return null;
        }

        private static void CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void CreateCylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void CreateStudioLight(
            Transform parent,
            string name,
            LightType type,
            Vector3 pos,
            Vector3 lookTarget,
            Color color,
            float intensity,
            float range,
            float spotAngle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (type == LightType.Spot || type == LightType.Directional)
                go.transform.LookAt(lookTarget, Vector3.up);

            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.shadows = LightShadows.Soft;
            go.AddComponent<VividAdditionalLightData>();
        }

        private static Transform CreateCameraRig(Transform parent, string name, Vector3 pos, Vector3 lookTarget)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.LookAt(lookTarget, Vector3.up);
            return go.transform;
        }

        private static TMP_Text CreateStudioLabel(
            Transform parent,
            string name,
            Vector3 pos,
            string content,
            float fontSize,
            Material tmpMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            var fontPath = AssetDatabase.GUIDToAssetPath("1e3843b942b74aabb3c0a44e7a6f5190");
            if (!string.IsNullOrEmpty(fontPath))
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (fontAsset != null)
                    tmp.font = fontAsset;
            }
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.92f, 0.94f, 0.98f, 1.0f);
            tmp.rectTransform.sizeDelta = new Vector2(4.4f, 0.6f);
            if (tmpMat != null)
                tmp.fontSharedMaterial = tmpMat;
            var tmpRenderer = go.GetComponent<MeshRenderer>();
            if (tmpRenderer != null)
            {
                tmpRenderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
                tmpRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return tmp;
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string parent = Path.GetDirectoryName(assetFolderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolderPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
