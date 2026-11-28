using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Samples.SubsurfaceScatteringLookDev.Editor
{
    /// <summary>
    /// Builds the VividRP Subsurface Scattering LookDev studio scene featuring the
    /// Lee Perry-Smith (LPS) 3D Head Scan (Infinite-Realities, CC-BY 3.0) across three
    /// calibrated side-by-side stations:
    ///   01 (Left):   Opaque Reference (SSS Weight = 0)
    ///   02 (Center): Hero Human Skin (Burley 2015 MFP 5.5 mm, dual-lobe sebum + peach fuzz)
    ///   03 (Right):  High-Translucency Warm Wax (Burley 2015 MFP 18 mm)
    /// </summary>
    public static class SubsurfaceLookDevSceneBuilder
    {
        private const string PackageRoot = "Packages/com.vivid.render-pipelines/Samples/SubsurfaceScatteringLookDev";
        private const string ModelsRoot = PackageRoot + "/Models";
        private const string TexturesRoot = PackageRoot + "/Textures";
        private const string MaterialsRoot = PackageRoot + "/Materials";
        private const string ScenePath = PackageRoot + "/SubsurfaceScatteringLookDev.unity";
        private const string VolumeProfilePath = PackageRoot + "/SubsurfaceScatteringLookDev_StudioVolume.asset";
        private const string TmpMaterialPath = "Packages/com.vivid.render-pipelines/Samples/FurLookDev/Materials/Studio_TMP_SDF.mat";

        private const string LpsHeadObjPath = ModelsRoot + "/LPSHead.obj";
        private const string ColorMapPath = TexturesRoot + "/Map-COL.jpg";
        private const string NormalMapPath = TexturesRoot + "/Infinite-Level_02_Tangent_SmoothUV.jpg";
        private const string SpecMapPath = TexturesRoot + "/Map-SPEC.jpg";

        [MenuItem("VividRP/Skin & SSS/Build SubsurfaceScattering LookDev Scene")]
        public static void BuildStudioScene()
        {
            EnsureFolder(PackageRoot);
            EnsureFolder(ModelsRoot);
            EnsureFolder(TexturesRoot);
            EnsureFolder(MaterialsRoot);

            ConfigureImportedAssets();

            var lpsMesh = LoadFirstMesh(LpsHeadObjPath);
            var colMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorMapPath);
            var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath);
            var specMap = AssetDatabase.LoadAssetAtPath<Texture2D>(SpecMapPath);

            if (lpsMesh == null || colMap == null || normalMap == null || specMap == null)
            {
                Debug.LogError(
                    $"[SubsurfaceLookDev] Missing LPS Head assets under {PackageRoot}. " +
                    $"mesh={lpsMesh != null} col={colMap != null} normal={normalMap != null} spec={specMap != null}");
                return;
            }

            var standardShader = Shader.Find("VividRP/Material/StandardLit");
            var skinShader = Shader.Find("VividRP/Material/Skin/SkinSlab");
            if (standardShader == null || skinShader == null)
            {
                Debug.LogError("[SubsurfaceLookDev] Required shaders VividRP/Material/StandardLit or VividRP/Material/Skin/SkinSlab not found.");
                return;
            }

            // Studio enclosure materials
            var matWall = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/SSSStudio_Wall.mat", standardShader,
                new Color(0.16f, 0.17f, 0.19f, 1.0f), metallic: 0.0f, smoothness: 0.12f);
            var matFloor = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/SSSStudio_Floor.mat", standardShader,
                new Color(0.11f, 0.12f, 0.14f, 1.0f), metallic: 0.0f, smoothness: 0.22f);
            var matPlinth = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/SSSStudio_Plinth.mat", standardShader,
                new Color(0.07f, 0.08f, 0.09f, 1.0f), metallic: 0.05f, smoothness: 0.22f);

            // Station 01: Opaque Reference (same maps & dual-lobe specular, SSS Weight = 0)
            var matOpaqueRef = CreateOrUpdateSkinMaterial(
                MaterialsRoot + "/LPSHead_01_OpaqueReference.mat",
                skinShader,
                colMap,
                normalMap,
                specMap,
                skinTint: new Color(0.92f, 0.88f, 0.85f, 1.0f),
                bumpScale: 0.90f,
                specMapStrength: 0.65f,
                skinRoughness: 0.48f,
                sebumWeight: 0.16f,
                sebumRoughness: 0.18f,
                fuzzWeight: 0.16f,
                sssWeight: 0.0f,
                sssTint: new Color(0.96f, 0.52f, 0.38f, 1.0f),
                sssTintBlend: 0.18f,
                sssRadiusMm: 6.5f,
                sssRadiusScale: new Vector4(1.0f, 0.34f, 0.18f, 0.0f));

            // Station 02: Hero Human Skin (Burley 2015 MFP 6.5 mm, red hemoglobin terminator bleed)
            var matHumanSkin = CreateOrUpdateSkinMaterial(
                MaterialsRoot + "/LPSHead_02_HumanSkin_SSS.mat",
                skinShader,
                colMap,
                normalMap,
                specMap,
                skinTint: new Color(0.92f, 0.88f, 0.85f, 1.0f),
                bumpScale: 0.90f,
                specMapStrength: 0.65f,
                skinRoughness: 0.48f,
                sebumWeight: 0.16f,
                sebumRoughness: 0.18f,
                fuzzWeight: 0.18f,
                sssWeight: 1.0f,
                sssTint: new Color(0.96f, 0.52f, 0.38f, 1.0f),
                sssTintBlend: 0.22f,
                sssRadiusMm: 6.5f,
                sssRadiusScale: new Vector4(1.0f, 0.34f, 0.18f, 0.0f));

            // Station 03: High-Translucency Warm Beeswax / Alabaster Bust (MFP 18 mm)
            var matWarmWax = CreateOrUpdateSkinMaterial(
                MaterialsRoot + "/LPSHead_03_WarmWax_SSS.mat",
                skinShader,
                baseMap: null,
                normalMap: normalMap,
                specMap: null,
                skinTint: new Color(0.82f, 0.64f, 0.44f, 1.0f),
                bumpScale: 0.55f,
                specMapStrength: 0.0f,
                skinRoughness: 0.38f,
                sebumWeight: 0.24f,
                sebumRoughness: 0.14f,
                fuzzWeight: 0.04f,
                sssWeight: 1.0f,
                sssTint: new Color(1.0f, 0.56f, 0.24f, 1.0f),
                sssTintBlend: 0.35f,
                sssRadiusMm: 18.0f,
                sssRadiusScale: new Vector4(1.0f, 0.45f, 0.16f, 0.0f));

            var volumeProfile = CreateOrUpdateStudioVolumeProfile();
            AssetDatabase.SaveAssets();

            // Create clean indoor LookDev scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Re-load all asset references after NewScene so no native handles go stale
            volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            lpsMesh = LoadFirstMesh(LpsHeadObjPath);
            matWall = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/SSSStudio_Wall.mat");
            matFloor = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/SSSStudio_Floor.mat");
            matPlinth = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/SSSStudio_Plinth.mat");
            matOpaqueRef = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/LPSHead_01_OpaqueReference.mat");
            matHumanSkin = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/LPSHead_02_HumanSkin_SSS.mat");
            matWarmWax = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/LPSHead_03_WarmWax_SSS.mat");
            var tmpMat = AssetDatabase.LoadAssetAtPath<Material>(TmpMaterialPath);

            var studioRoot = new GameObject("SubsurfaceLookDev_Studio");

            // 1. Studio Enclosure (6m x 5m x 3.4m)
            var room = new GameObject("StudioRoom");
            room.transform.SetParent(studioRoot.transform, false);
            CreateBox(room.transform, "Floor", new Vector3(0.0f, -0.05f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matFloor);
            CreateBox(room.transform, "Ceiling", new Vector3(0.0f, 3.45f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matWall);
            CreateBox(room.transform, "BackWall", new Vector3(0.0f, 1.7f, 2.45f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "FrontWall", new Vector3(0.0f, 1.7f, -2.55f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "LeftWall", new Vector3(-3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);
            CreateBox(room.transform, "RightWall", new Vector3(3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);

            // 2. Global Volume (Fixed EV100 = 0 + ACES)
            var volGo = new GameObject("Studio_GlobalVolume");
            volGo.transform.SetParent(studioRoot.transform, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10.0f;
            volume.sharedProfile = volumeProfile;

            // 3. Calibrated dramatic raking-key + warm back-rim lighting (unclipped EV100 = 0 portrait exposure)
            var lightsRoot = new GameObject("StudioLights");
            lightsRoot.transform.SetParent(studioRoot.transform, false);

            CreateStudioLight(
                lightsRoot.transform,
                "KeyLight_SideRight",
                LightType.Spot,
                new Vector3(1.85f, 1.70f, -0.95f),
                new Vector3(0.0f, 1.00f, 0.0f),
                new Color(1.0f, 0.96f, 0.91f),
                intensity: 34.0f,
                range: 9.0f,
                spotAngle: 78.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "RimLight_BackLeft",
                LightType.Spot,
                new Vector3(-1.35f, 1.55f, 1.25f),
                new Vector3(0.0f, 1.00f, 0.0f),
                new Color(1.0f, 0.87f, 0.72f),
                intensity: 38.0f,
                range: 9.0f,
                spotAngle: 78.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "FillLight_FrontLeftSoft",
                LightType.Point,
                new Vector3(-1.90f, 1.35f, -1.55f),
                Vector3.zero,
                new Color(0.80f, 0.89f, 1.0f),
                intensity: 7.5f,
                range: 8.5f,
                spotAngle: 60.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "BackdropSoftWash",
                LightType.Point,
                new Vector3(0.0f, 2.25f, 1.55f),
                Vector3.zero,
                new Color(0.88f, 0.92f, 1.0f),
                intensity: 14.0f,
                range: 6.0f,
                spotAngle: 60.0f);

            // 4. Three calibrated LPS Head stations (X = -0.58, 0.00, +0.58)
            CreateHeadStation(
                studioRoot.transform,
                "Station01_OpaqueReference",
                new Vector3(-0.58f, 0.0f, 0.0f),
                lpsMesh,
                matPlinth,
                matOpaqueRef);

            CreateHeadStation(
                studioRoot.transform,
                "Station02_HumanSkin_SSS",
                new Vector3(0.0f, 0.0f, 0.0f),
                lpsMesh,
                matPlinth,
                matHumanSkin);

            CreateHeadStation(
                studioRoot.transform,
                "Station03_WarmWax_SSS",
                new Vector3(0.58f, 0.0f, 0.0f),
                lpsMesh,
                matPlinth,
                matWarmWax);

            // 5. Studio wall & station labels
            var labelsRoot = new GameObject("StudioLabels");
            labelsRoot.transform.SetParent(studioRoot.transform, false);

            CreateStudioLabel(
                labelsRoot.transform,
                "WallTitle",
                new Vector3(0.0f, 1.66f, 1.15f),
                "VIVIDRP SUBSURFACE SCATTERING LOOKDEV STUDIO",
                1.05f,
                tmpMat);

            var telemetryText = CreateStudioLabel(
                labelsRoot.transform,
                "WallTelemetry",
                new Vector3(0.0f, 1.48f, 1.15f),
                "MODEL: LEE PERRY-SMITH HEAD SCAN (CC-BY 3.0)  |  SSS: ON",
                0.42f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station01",
                new Vector3(-0.58f, 0.72f, -0.19f),
                "01: OPAQUE REF\nSSS OFF (0 mm)",
                0.38f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station02",
                new Vector3(0.0f, 0.72f, -0.19f),
                "02: HUMAN SKIN\nBURLEY SSS (6.5 mm)",
                0.38f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station03",
                new Vector3(0.58f, 0.72f, -0.19f),
                "03: WARM WAX\nBURLEY SSS (18 mm)",
                0.38f,
                tmpMat);

            // 6. Camera Rigs + Main Camera
            var rigsRoot = new GameObject("CameraRigs");
            rigsRoot.transform.SetParent(studioRoot.transform, false);

            var overviewRig = CreateCameraRig(rigsRoot.transform, "Rig_Overview", new Vector3(0.0f, 1.07f, -1.65f), new Vector3(0.0f, 1.02f, 0.0f));
            var heroCloseRig = CreateCameraRig(rigsRoot.transform, "Rig_HeroFaceCloseUp", new Vector3(-0.08f, 1.04f, -0.58f), new Vector3(0.0f, 1.02f, 0.0f));
            var heroThreeQuarterRig = CreateCameraRig(rigsRoot.transform, "Rig_HeroThreeQuarter", new Vector3(-0.36f, 1.05f, -0.50f), new Vector3(0.0f, 1.01f, 0.0f));
            var opaqueVsSkinRig = CreateCameraRig(rigsRoot.transform, "Rig_OpaqueVsSkinSplit", new Vector3(-0.29f, 1.04f, -0.96f), new Vector3(-0.29f, 1.01f, 0.0f));
            var waxCloseRig = CreateCameraRig(rigsRoot.transform, "Rig_WaxCloseUp", new Vector3(0.32f, 1.05f, -0.54f), new Vector3(0.58f, 1.01f, 0.0f));

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(studioRoot.transform, false);
            camGo.transform.SetPositionAndRotation(overviewRig.position, overviewRig.rotation);
            var camera = camGo.AddComponent<Camera>();
            camera.fieldOfView = 35.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50.0f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.025f, 1.0f);
            camGo.AddComponent<VividAdditionalCameraData>();

            // 7. Interactive LookDev Controller
            var controller = studioRoot.AddComponent<VividSubsurfaceLookDevController>();
            controller.StudioCamera = camera;
            controller.StudioVolume = volume;
            controller.StudioLabelsRoot = labelsRoot;
            controller.StatusTelemetryText = telemetryText;
            controller.OverviewRig = overviewRig;
            controller.HeroFaceCloseUpRig = heroCloseRig;
            controller.HeroThreeQuarterRig = heroThreeQuarterRig;
            controller.OpaqueVsSkinSplitRig = opaqueVsSkinRig;
            controller.WaxCloseUpRig = waxCloseRig;
            controller.ActiveCameraPreset = SubsurfaceLookDevCameraPreset.Overview;
            controller.EnableSubsurfaceScattering = true;
            controller.QualityMode = SubsurfaceLookDevQualityMode.BlueNoise16Tap;
            controller.DebugMode = SubsurfaceLookDevDebugMode.Composite;
            controller.GlobalWorldScale = 1.0f;
            controller.MaxScreenRadiusPixels = 48.0f;
            controller.ApplySettings();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[SubsurfaceLookDev] Successfully built and saved " + ScenePath);
        }

        private static void ConfigureImportedAssets()
        {
            if (AssetImporter.GetAtPath(LpsHeadObjPath) is ModelImporter modelImporter)
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

            ConfigureTexture(ColorMapPath, TextureImporterType.Default, sRgb: true, maxSize: 4096);
            ConfigureTexture(NormalMapPath, TextureImporterType.NormalMap, sRgb: false, maxSize: 4096);
            ConfigureTexture(SpecMapPath, TextureImporterType.Default, sRgb: false, maxSize: 4096);
        }

        private static void ConfigureTexture(string path, TextureImporterType type, bool sRgb, int maxSize)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;

            bool changed = false;
            if (importer.textureType != type)
            {
                importer.textureType = type;
                changed = true;
            }
            if (type == TextureImporterType.Default && importer.sRGBTexture != sRgb)
            {
                importer.sRGBTexture = sRgb;
                changed = true;
            }
            if (importer.maxTextureSize != maxSize)
            {
                importer.maxTextureSize = maxSize;
                changed = true;
            }
            if (importer.anisoLevel != 8)
            {
                importer.anisoLevel = 8;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }

        private static GameObject CreateHeadStation(
            Transform parent,
            string stationName,
            Vector3 localPos,
            Mesh headMesh,
            Material matPlinth,
            Material matHead)
        {
            var station = new GameObject(stationName);
            station.transform.SetParent(parent, false);
            station.transform.localPosition = localPos;

            CreateCylinder(station.transform, "Plinth", new Vector3(0.0f, 0.40f, 0.0f), new Vector3(0.32f, 0.40f, 0.32f), matPlinth);

            var headGo = new GameObject("LPSHead");
            headGo.transform.SetParent(station.transform, false);
            headGo.transform.localPosition = new Vector3(0.0f, 0.80f, 0.0f);
            // LeePerrySmith.glb faces +Z; rotate 180 deg so face looks toward camera at -Z
            headGo.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            headGo.transform.localScale = Vector3.one;

            var mf = headGo.AddComponent<MeshFilter>();
            mf.sharedMesh = headMesh;
            var mr = headGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = matHead;

            return station;
        }

        private static Material CreateOrUpdateSkinMaterial(
            string path,
            Shader shader,
            Texture2D baseMap,
            Texture2D normalMap,
            Texture2D specMap,
            Color skinTint,
            float bumpScale,
            float specMapStrength,
            float skinRoughness,
            float sebumWeight,
            float sebumRoughness,
            float fuzzWeight,
            float sssWeight,
            Color sssTint,
            float sssTintBlend,
            float sssRadiusMm,
            Vector4 sssRadiusScale)
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

            mat.SetFloat("_Cull", 2.0f);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetVector("_BaseMap_ST", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
            mat.SetColor("_SkinColor", skinTint);
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", bumpScale);
            mat.SetTexture("_SpecMap", specMap);
            mat.SetFloat("_SpecMapStrength", specMapStrength);

            mat.SetFloat("_SkinRoughness", skinRoughness);
            mat.SetFloat("_SkinIor", 1.40f);
            mat.SetFloat("_SebumWeight", sebumWeight);
            mat.SetFloat("_SebumRoughness", sebumRoughness);
            mat.SetFloat("_FuzzWeight", fuzzWeight);
            mat.SetFloat("_FuzzRoughness", 0.68f);
            mat.SetColor("_FuzzColor", new Color(0.96f, 0.85f, 0.76f, 1.0f));

            mat.SetFloat("_SubsurfaceWeight", sssWeight);
            mat.SetColor("_SubsurfaceColor", sssTint);
            mat.SetFloat("_SubsurfaceColorBlend", sssTintBlend);
            mat.SetFloat("_SubsurfaceRadiusMm", sssRadiusMm);
            mat.SetVector("_SubsurfaceRadiusScale", sssRadiusScale);
            mat.SetFloat("_SubsurfaceTransmission", 0.35f);

            // Disable procedural pores/freckles when using real scanned LPS Head maps
            mat.SetFloat("_PoreScale", 56.0f);
            mat.SetFloat("_PoreNormalStrength", 0.0f);
            mat.SetFloat("_ComplexionVariation", 0.0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile CreateOrUpdateStudioVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "SubsurfaceScatteringLookDev_StudioVolume";
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
            float smoothness)
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
