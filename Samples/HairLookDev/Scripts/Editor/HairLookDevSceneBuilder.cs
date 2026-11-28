using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Samples.HairLookDev.Editor
{
    public static class HairLookDevSceneBuilder
    {
        private const string PackageRoot = "Packages/com.vivid.render-pipelines/Samples/HairLookDev";
        private const string GroomsRoot = PackageRoot + "/Grooms";
        private const string MaterialsRoot = PackageRoot + "/Materials";
        private const string ScenePath = PackageRoot + "/HairLookDev.unity";
        private const string VolumeProfilePath = PackageRoot + "/HairLookDev_StudioVolume.asset";
        private const string TmpMaterialPath = "Packages/com.vivid.render-pipelines/Samples/FurLookDev/Materials/Studio_TMP_SDF.mat";

        [MenuItem("VividRP/Hair/Build HairLookDev Studio Scene")]
        public static void BuildStudioScene()
        {
            EnsureFolder(PackageRoot);
            EnsureFolder(MaterialsRoot);

            var sphereGroomAsset = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(GroomsRoot + "/HairSphere_16k.vividhair");
            var bustGroomAsset = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(GroomsRoot + "/HairBust_48k.vividhair");
            var headMesh = LoadFirstMesh(GroomsRoot + "/HairBust_Head.obj");
            var proxyMesh = LoadFirstMesh(GroomsRoot + "/HairBust_ShadowProxy.obj");

            if (sphereGroomAsset == null || bustGroomAsset == null)
            {
                Debug.LogError(
                    "[HairLookDev] Missing .vividhair assets under " + GroomsRoot
                    + ". Run Tools~/Houdini/Export-VividHairGroom.py first.");
                return;
            }

            var standardShader = Shader.Find("VividRP/Material/StandardLit");
            var hairShader = Shader.Find("VividRP/Material/Hair");
            if (standardShader == null || hairShader == null)
            {
                Debug.LogError("[HairLookDev] Required shaders VividRP/Material/StandardLit or VividRP/Material/Hair not found.");
                return;
            }

            // Create / update studio materials
            var matWall = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/HairStudio_Wall.mat", standardShader,
                new Color(0.20f, 0.21f, 0.23f, 1.0f), metallic: 0.0f, smoothness: 0.12f);
            var matFloor = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/HairStudio_Floor.mat", standardShader,
                new Color(0.14f, 0.15f, 0.17f, 1.0f), metallic: 0.0f, smoothness: 0.22f);
            var matPlinth = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/HairStudio_Plinth.mat", standardShader,
                new Color(0.08f, 0.09f, 0.10f, 1.0f), metallic: 0.05f, smoothness: 0.20f);
            var matMannequin = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/HairStudio_Mannequin.mat", standardShader,
                new Color(0.56f, 0.44f, 0.38f, 1.0f), metallic: 0.0f, smoothness: 0.26f);
            var matSphereCore = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/HairStudio_SphereCore.mat", standardShader,
                new Color(0.12f, 0.08f, 0.06f, 1.0f), metallic: 0.0f, smoothness: 0.18f);

            var sphereShading = new HairGroomShadingParameters
            {
                AbsorptionMode = HairGroomAbsorptionMode.Melanin,
                BaseColor = new Color(0.32f, 0.18f, 0.09f, 1.0f),
                Melanin = 0.55f,
                MelaninRedness = 0.30f,
                LongitudinalRoughness = 0.26f,
                AzimuthalRoughness = 0.42f,
                Ior = 1.55f,
                CuticleAngleDegrees = 3.0f,
                MedullaWeight = 0.0f,
                MedullaRoughness = 0.65f,
                DualScatterGain = 1.0f,
            };

            var bustShading = new HairGroomShadingParameters
            {
                AbsorptionMode = HairGroomAbsorptionMode.Melanin,
                BaseColor = new Color(0.18f, 0.11f, 0.06f, 1.0f),
                Melanin = 0.72f,
                MelaninRedness = 0.18f,
                LongitudinalRoughness = 0.28f,
                AzimuthalRoughness = 0.46f,
                Ior = 1.55f,
                CuticleAngleDegrees = 3.0f,
                MedullaWeight = 0.0f,
                MedullaRoughness = 0.65f,
                DualScatterGain = 1.0f,
            };

            // Higher-roughness dry/matte natural hair variant for direct A/B roughness comparison
            var bustMatteShading = new HairGroomShadingParameters
            {
                AbsorptionMode = HairGroomAbsorptionMode.Melanin,
                BaseColor = new Color(0.18f, 0.11f, 0.06f, 1.0f),
                Melanin = 0.78f,
                MelaninRedness = 0.22f,
                LongitudinalRoughness = 0.48f,
                AzimuthalRoughness = 0.58f,
                Ior = 1.48f,
                CuticleAngleDegrees = 3.5f,
                MedullaWeight = 0.0f,
                MedullaRoughness = 0.65f,
                DualScatterGain = 1.0f,
            };

            var matHairSphere = CreateOrUpdateHairMaterial(
                MaterialsRoot + "/HairStudio_Sphere_Chiang.mat", hairShader, in sphereShading);
            var matHairBust = CreateOrUpdateHairMaterial(
                MaterialsRoot + "/HairStudio_Bust_Chiang.mat", hairShader, in bustShading);
            var matHairBustMatte = CreateOrUpdateHairMaterial(
                MaterialsRoot + "/HairStudio_BustMatte_Chiang.mat", hairShader, in bustMatteShading);
            var tmpMat = AssetDatabase.LoadAssetAtPath<Material>(TmpMaterialPath);

            var volumeProfile = CreateOrUpdateStudioVolumeProfile();
            AssetDatabase.SaveAssets();

            // Create new indoor LookDev scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Re-load all asset references after NewScene so no native UnityEngine.Object handles go stale.
            volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            sphereGroomAsset = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(GroomsRoot + "/HairSphere_16k.vividhair");
            bustGroomAsset = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(GroomsRoot + "/HairBust_48k.vividhair");
            headMesh = LoadFirstMesh(GroomsRoot + "/HairBust_Head.obj");
            proxyMesh = LoadFirstMesh(GroomsRoot + "/HairBust_ShadowProxy.obj");
            matWall = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Wall.mat");
            matFloor = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Floor.mat");
            matPlinth = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Plinth.mat");
            matMannequin = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Mannequin.mat");
            matSphereCore = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_SphereCore.mat");
            matHairSphere = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Sphere_Chiang.mat");
            matHairBust = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_Bust_Chiang.mat");
            matHairBustMatte = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "/HairStudio_BustMatte_Chiang.mat");
            tmpMat = AssetDatabase.LoadAssetAtPath<Material>(TmpMaterialPath);

            var studioRoot = new GameObject("HairLookDev_Studio");

            // 1. Studio Enclosure (6m x 5m x 3.4m)
            var room = new GameObject("StudioRoom");
            room.transform.SetParent(studioRoot.transform, false);
            CreateBox(room.transform, "Floor", new Vector3(0.0f, -0.05f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matFloor);
            CreateBox(room.transform, "Ceiling", new Vector3(0.0f, 3.45f, 0.0f), new Vector3(6.0f, 0.1f, 5.0f), matWall);
            CreateBox(room.transform, "BackWall", new Vector3(0.0f, 1.7f, 2.45f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "FrontWall", new Vector3(0.0f, 1.7f, -2.55f), new Vector3(6.0f, 3.4f, 0.1f), matWall);
            CreateBox(room.transform, "LeftWall", new Vector3(-3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);
            CreateBox(room.transform, "RightWall", new Vector3(3.0f, 1.7f, 0.0f), new Vector3(0.1f, 3.4f, 5.0f), matWall);

            // 2. Global Volume (Fixed EV100 = 0 + ACES + PT Settings)
            var volGo = new GameObject("Studio_GlobalVolume");
            volGo.transform.SetParent(studioRoot.transform, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10.0f;
            volume.sharedProfile = volumeProfile;

            // 3. Three-point calibrated indoor studio lighting (EV100 = 0, calibrated for both Realtime & Reference PT)
            var lightsRoot = new GameObject("StudioLights");
            lightsRoot.transform.SetParent(studioRoot.transform, false);

            CreateStudioLight(
                lightsRoot.transform,
                "KeyLight_FrontRight",
                LightType.Spot,
                new Vector3(1.15f, 2.35f, -1.75f),
                new Vector3(0.20f, 1.05f, 0.0f),
                new Color(1.0f, 0.96f, 0.91f),
                intensity: 105.0f,
                range: 9.5f,
                spotAngle: 84.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "FillLight_FrontLeft",
                LightType.Point,
                new Vector3(-1.55f, 1.55f, -1.30f),
                Vector3.zero,
                new Color(0.84f, 0.91f, 1.0f),
                intensity: 44.0f,
                range: 9.0f,
                spotAngle: 60.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "RimLight_BackTop",
                LightType.Spot,
                new Vector3(0.0f, 1.95f, 1.45f),
                new Vector3(0.0f, 1.06f, 0.0f),
                new Color(1.0f, 0.91f, 0.78f),
                intensity: 135.0f,
                range: 9.0f,
                spotAngle: 82.0f);

            CreateStudioLight(
                lightsRoot.transform,
                "CeilingSoftFill",
                LightType.Point,
                new Vector3(0.0f, 2.75f, -0.25f),
                Vector3.zero,
                new Color(0.95f, 0.96f, 1.0f),
                intensity: 34.0f,
                range: 9.5f,
                spotAngle: 60.0f);

            // 4. Station 01: HairSphere_16k (Left)
            var station01 = new GameObject("Station01_HairSphere_16k");
            station01.transform.SetParent(studioRoot.transform, false);
            station01.transform.localPosition = new Vector3(-0.78f, 0.0f, 0.0f);

            CreateCylinder(station01.transform, "Plinth", new Vector3(0.0f, 0.40f, 0.0f), new Vector3(0.36f, 0.40f, 0.36f), matPlinth);

            var sphereAnchor = new GameObject("HairSphere_Anchor");
            sphereAnchor.transform.SetParent(station01.transform, false);
            sphereAnchor.transform.localPosition = new Vector3(0.0f, 1.06f, 0.0f);

            var innerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            innerSphere.name = "EmitterCoreSphere";
            innerSphere.transform.SetParent(sphereAnchor.transform, false);
            innerSphere.transform.localPosition = Vector3.zero;
            innerSphere.transform.localScale = Vector3.one * 0.318f; // radius 0.159m inside 0.16m roots
            innerSphere.GetComponent<Renderer>().sharedMaterial = matSphereCore;
            Object.DestroyImmediate(innerSphere.GetComponent<Collider>());

            var sphereGroom = sphereAnchor.AddComponent<HairGroomRenderer>();
            sphereGroom.GroomAsset = sphereGroomAsset;
            sphereGroom.RadiusScale = 1.0f;
            sphereGroom.Shading = sphereShading;
            sphereGroom.PathTracingMaterial = matHairSphere;
            sphereGroom.EnablePathTracingDotsProxy = false;

            // 5. Station 02: HairBust_48k Glossy Baseline (Center, R=0.28 / Azim=0.46)
            var station02 = CreateBustStation(
                studioRoot.transform,
                "Station02_HairBust_48k_Glossy",
                "HairBust_Anchor",
                new Vector3(0.0f, 0.0f, 0.0f),
                matPlinth,
                matMannequin,
                headMesh,
                proxyMesh,
                bustGroomAsset,
                in bustShading,
                matHairBust,
                out var bustGroom,
                out var shadowProxyRenderer);

            // 6. Station 03: HairBust_48k Higher-Roughness Matte Comparison (Right, R=0.54 / Azim=0.70)
            var station03 = CreateBustStation(
                studioRoot.transform,
                "Station03_HairBust_48k_Matte",
                "HairBust_Matte_Anchor",
                new Vector3(0.78f, 0.0f, 0.0f),
                matPlinth,
                matMannequin,
                headMesh,
                proxyMesh,
                bustGroomAsset,
                in bustMatteShading,
                matHairBustMatte,
                out var bustMatteGroom,
                out var matteShadowProxyRenderer);

            // 7. Wall Title & Live Telemetry Board (grouped under StudioLabelsRoot so PT mode hides them cleanly)
            var labelsRoot = new GameObject("StudioLabels");
            labelsRoot.transform.SetParent(studioRoot.transform, false);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station01",
                new Vector3(-0.78f, 0.72f, -0.19f),
                "01: SPHERE (16K)\nR=0.26 / N=0.42",
                0.32f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station02",
                new Vector3(0.0f, 0.72f, -0.19f),
                "02: BUST GLOSSY (48K)\nR=0.28 / N=0.46",
                0.32f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "Label_Station03",
                new Vector3(0.78f, 0.72f, -0.19f),
                "03: BUST MATTE (48K)\nR=0.48 / N=0.58",
                0.32f,
                tmpMat);

            CreateStudioLabel(
                labelsRoot.transform,
                "WallTitle",
                new Vector3(0.0f, 1.72f, 1.15f),
                "VIVIDRP STRAND HAIR LOOKDEV STUDIO",
                0.92f,
                tmpMat);

            var telemetryText = CreateStudioLabel(
                labelsRoot.transform,
                "WallTelemetry",
                new Vector3(0.0f, 1.48f, 1.15f),
                "INITIALIZING...",
                0.48f,
                tmpMat);

            // 8. Studio Camera & Presets
            var rigsRoot = new GameObject("CameraRigs");
            rigsRoot.transform.SetParent(studioRoot.transform, false);

            var rigOverview = CreateCameraRig(rigsRoot.transform, "Rig_Overview", new Vector3(0.0f, 1.16f, -2.05f), new Vector3(0.0f, 1.07f, 0.0f));
            var rigBustFront = CreateCameraRig(rigsRoot.transform, "Rig_BustFront", new Vector3(-0.22f, 1.10f, -0.58f), new Vector3(0.0f, 1.06f, 0.0f));
            var rigBustBacklit = CreateCameraRig(rigsRoot.transform, "Rig_BustBacklit", new Vector3(0.30f, 1.10f, -0.56f), new Vector3(0.0f, 1.06f, 0.0f));
            var rigSphereClose = CreateCameraRig(rigsRoot.transform, "Rig_SphereCloseUp", new Vector3(-0.78f, 1.12f, -0.62f), new Vector3(-0.78f, 1.06f, 0.0f));
            var rigRoughnessCompare = CreateCameraRig(rigsRoot.transform, "Rig_RoughnessCompare", new Vector3(0.39f, 1.13f, -1.24f), new Vector3(0.39f, 1.05f, 0.0f));

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(studioRoot.transform, false);
            camGo.transform.SetPositionAndRotation(rigOverview.position, rigOverview.rotation);
            var camera = camGo.AddComponent<Camera>();
            camera.fieldOfView = 38.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.055f, 0.06f, 1.0f);
            var camData = camGo.AddComponent<VividAdditionalCameraData>();
            camData.antialiasing = VividAntialiasingMode.None;

            // 9. Controller wiring
            var controller = studioRoot.AddComponent<VividHairLookDevController>();
            controller.StudioCamera = camera;
            controller.StudioVolume = volume;
            controller.SphereGroom = sphereGroom;
            controller.BustGroom = bustGroom;
            controller.BustMatteGroom = bustMatteGroom;
            controller.BustShadowProxyRenderer = shadowProxyRenderer;
            controller.BustMatteShadowProxyRenderer = matteShadowProxyRenderer;
            controller.StudioLabelsRoot = labelsRoot;
            controller.StatusTelemetryText = telemetryText;
            controller.OverviewRig = rigOverview;
            controller.BustFrontRig = rigBustFront;
            controller.BustBacklitRig = rigBustBacklit;
            controller.SphereCloseUpRig = rigSphereClose;
            controller.RoughnessCompareRig = rigRoughnessCompare;
            controller.RenderMode = HairLookDevRenderMode.RealtimeStrandVisibilityBuffer;
            controller.ActiveCameraPreset = HairLookDevCameraPreset.Overview;
            controller.ApplyModeAndTelemetry();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HairLookDev] Built studio scene at {ScenePath}");
        }

        private static GameObject CreateBustStation(
            Transform parent,
            string stationName,
            string anchorName,
            Vector3 localPosition,
            Material matPlinth,
            Material matMannequin,
            Mesh headMesh,
            Mesh proxyMesh,
            HairGroomAsset bustGroomAsset,
            in HairGroomShadingParameters shading,
            Material pathTracingMaterial,
            out HairGroomRenderer bustGroom,
            out Renderer shadowProxyRenderer)
        {
            var station = new GameObject(stationName);
            station.transform.SetParent(parent, false);
            station.transform.localPosition = localPosition;

            CreateCylinder(station.transform, "Plinth", new Vector3(0.0f, 0.40f, 0.0f), new Vector3(0.36f, 0.40f, 0.36f), matPlinth);

            var bustAnchor = new GameObject(anchorName);
            bustAnchor.transform.SetParent(station.transform, false);
            bustAnchor.transform.localPosition = new Vector3(0.0f, 1.04f, 0.0f);
            // Face is +Z in Houdini/OBJ -> rotate 180 deg around Y to face camera at -Z
            bustAnchor.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);

            if (headMesh != null)
            {
                var headGo = new GameObject("MannequinHead");
                headGo.transform.SetParent(bustAnchor.transform, false);
                headGo.AddComponent<MeshFilter>().sharedMesh = headMesh;
                var headRenderer = headGo.AddComponent<MeshRenderer>();
                headRenderer.sharedMaterial = matMannequin;
            }

            shadowProxyRenderer = null;
            if (proxyMesh != null)
            {
                var proxyGo = new GameObject("ShadowProxy_Inverted");
                proxyGo.transform.SetParent(bustAnchor.transform, false);
                proxyGo.AddComponent<MeshFilter>().sharedMesh = proxyMesh;
                shadowProxyRenderer = proxyGo.AddComponent<MeshRenderer>();
                shadowProxyRenderer.sharedMaterial = matMannequin;
                shadowProxyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                shadowProxyRenderer.enabled = false;
            }

            bustGroom = bustAnchor.AddComponent<HairGroomRenderer>();
            bustGroom.GroomAsset = bustGroomAsset;
            bustGroom.RadiusScale = 1.0f;
            bustGroom.Shading = shading;
            bustGroom.PathTracingMaterial = pathTracingMaterial;
            bustGroom.EnablePathTracingDotsProxy = false;
            return station;
        }

        private static VolumeProfile CreateOrUpdateStudioVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "HairLookDev_StudioVolume";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            // Sky = None (enclosed indoor studio)
            var sky = GetOrAddVolumeComponent<SkySettingsVolume>(profile);
            sky.active = true;
            sky.skyType.overrideState = true;
            sky.skyType.value = SkyType.None;

            // Fixed Exposure EV100 = 0
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

            // ACES Tonemapping
            var tonemap = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemap.active = true;
            tonemap.mode.overrideState = true;
            tonemap.mode.value = TonemappingMode.ACES;

            // Bloom Off for unbiased LookDev measurements
            var bloom = GetOrAddVolumeComponent<Bloom>(profile);
            bloom.active = false;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.0f;

            // Reference Path Tracing settings (6 bounces for rich hair multiple scattering)
            var pt = GetOrAddVolumeComponent<ReferencedPathTracingSettingsVolume>(profile);
            pt.active = true;
            pt.deterministicSampling.overrideState = true;
            pt.deterministicSampling.value = true;
            pt.maxBounceCount.overrideState = true;
            pt.maxBounceCount.value = 6;
            pt.russianRouletteStartBounce.overrideState = true;
            pt.russianRouletteStartBounce.value = 4;
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

        private static Material CreateOrUpdateHairMaterial(
            string path,
            Shader shader,
            in HairGroomShadingParameters shading)
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

            mat.SetColor("_HairBaseColor", shading.BaseColor);
            mat.SetFloat("_HairAbsorptionModel", (float)shading.AbsorptionMode);
            mat.SetFloat("_HairMelanin", shading.Melanin);
            mat.SetFloat("_HairMelaninRedness", shading.MelaninRedness);
            mat.SetFloat("_HairLongitudinalRoughness", shading.LongitudinalRoughness);
            mat.SetFloat("_HairAzimuthalRoughness", shading.AzimuthalRoughness);
            mat.SetFloat("_HairIor", shading.Ior);
            mat.SetFloat("_HairCuticleAngleDegrees", shading.CuticleAngleDegrees);
            mat.SetFloat("_HairFresnelApproximation", 1.0f);
            mat.SetColor("_HairEmissionColor", Color.black);
            mat.SetFloat("_HairMaterialVersion", 1.0f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
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
            tmp.rectTransform.sizeDelta = new Vector2(3.6f, 0.6f);
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
