using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Copy this file into Assets/Editor of the independent URP control project only.
public static class APVControlHarness
{
    public const string Folder = "Assets/APVControl";
    public const string ScenePath = Folder + "/Control.unity";
    public const string SetPath = Folder + "/BakingSet.asset";
    private static int s_Errors;
    public static string BakeResult { get; private set; } = "Not started";

    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("Control already exists; reopen it instead of replacing the bake.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "APVControl");
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline == null) throw new InvalidOperationException("The stock URP template PC pipeline is missing.");
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_LightProbeSystem").intValue = (int)LightProbeSystem.ProbeVolumes;
        serialized.FindProperty("m_ProbeVolumeSHBands").intValue = (int)ProbeVolumeSHBands.SphericalHarmonicsL2;
        serialized.FindProperty("m_ShEvalMode").intValue = (int)ShEvalMode.PerPixel;
        serialized.FindProperty("m_SupportProbeVolumeGPUStreaming").boolValue = false;
        serialized.FindProperty("m_SupportProbeVolumeDiskStreaming").boolValue = false;
        serialized.FindProperty("m_SupportProbeVolumeScenarios").boolValue = false;
        serialized.FindProperty("m_SupportProbeVolumeScenarioBlending").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        // The blank template disables exposure globally; a Volume alone does not enable it.
        var exposureSettings = GraphicsSettings.GetRenderPipelineSettings<URPExposureSettings>();
        if (exposureSettings == null) throw new InvalidOperationException("URP exposure settings are missing.");
        typeof(URPExposureSettings).GetProperty("UseExposure").SetValue(exposureSettings, true);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        if (renderer == null) throw new InvalidOperationException("The stock PC renderer is missing.");
        // In particular, disable the template SSAO: it is not part of APV diffuse GI.
        foreach (var feature in renderer.rendererFeatures)
        {
            feature.SetActive(false);
            EditorUtility.SetDirty(feature);
        }
        EditorUtility.SetDirty(renderer);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
        mesh.name = "APV Source Cube";
        Object.DestroyImmediate(primitive);
        AssetDatabase.CreateAsset(mesh, Folder + "/Cube.asset");
        var white = MakeMaterial("White", new Color(.65f, .65f, .65f));
        var red = MakeMaterial("Red", new Color(.65f, .035f, .025f));
        var green = MakeMaterial("Green", new Color(.035f, .55f, .04f));
        var root = new GameObject("Static Native GI Geometry").transform;
        Cube(root, mesh, white, "Floor", new Vector3(0, -.15f, 0), new Vector3(6.3f, .3f, 6.3f));
        Cube(root, mesh, white, "Ceiling", new Vector3(0, 4.15f, 0), new Vector3(6.3f, .3f, 6.3f));
        Cube(root, mesh, white, "Back", new Vector3(0, 2, 3.15f), new Vector3(6.3f, 4, .3f));
        Cube(root, mesh, red, "Red Wall", new Vector3(-3.15f, 2, 0), new Vector3(.3f, 4, 6.3f));
        Cube(root, mesh, green, "Green Wall", new Vector3(3.15f, 2, 0), new Vector3(.3f, 4, 6.3f));
        Cube(root, mesh, white, "Short Block", new Vector3(-1.1f, .6f, 0), new Vector3(1.2f, 1.2f, 1.2f));
        Cube(root, mesh, white, "Tall Block", new Vector3(1.15f, 1.1f, 1), new Vector3(1.2f, 2.2f, 1.2f));

        var light = new GameObject("Mixed Point - Baked Indirect").AddComponent<Light>();
        light.type = LightType.Point;
        light.lightUnit = LightUnit.Candela;
        light.intensity = 1500;
        light.range = 15;
        light.color = Color.white;
        light.lightmapBakeType = LightmapBakeType.Mixed;
        light.shadows = LightShadows.Soft;
        light.transform.position = new Vector3(0, 3, -.8f);
        light.gameObject.AddComponent<UniversalAdditionalLightData>();
        RenderSettings.sun = null;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0;
        RenderSettings.reflectionIntensity = 0;

        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 2, -8.5f);
        camera.transform.LookAt(new Vector3(0, 1.8f, .5f));
        camera.fieldOfView = 49;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100;
        camera.allowHDR = true;
        camera.backgroundColor = Color.black;
        camera.clearFlags = CameraClearFlags.SolidColor;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.None;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, Folder + "/Profile.asset");
        profile.Add<ExposureVolume>(true).fixedExposure.Override(5);
        profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        profile.Add<ProbeVolumesOptions>(true);
        foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100;
        volume.sharedProfile = profile;

        var pv = new GameObject("Adaptive Probe Volume").AddComponent<ProbeVolume>();
        pv.mode = ProbeVolume.Mode.Local;
        pv.transform.position = new Vector3(0, 2, 0);
        pv.size = new Vector3(8, 6, 8);
        pv.overrideRendererFilters = true;
        pv.minRendererVolumeSize = 0;
        var lighting = new LightingSettings
        {
            name = "APV Control Lighting", bakedGI = true, realtimeGI = false,
            mixedBakeMode = MixedLightingMode.IndirectOnly, directSampleCount = 64,
            indirectSampleCount = 256, environmentSampleCount = 64, maxBounces = 4
        };
        AssetDatabase.CreateAsset(lighting, Folder + "/LightingSettings.asset");
        Lightmapping.lightingSettings = lighting;
        EditorSceneManager.SaveScene(scene, ScenePath);
        var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
        typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(set, null);
        set.minDistanceBetweenProbes = .5f;
        set.simplificationLevels = 2;
        set.minRendererVolumeSize = 0;
        set.skyOcclusion = false;
        AssetDatabase.CreateAsset(set, SetPath);
        set.TryAddScene(AssetDatabase.GUIDFromAssetPath(ScenePath));
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        EditorApplication.QueuePlayerLoopUpdate();
    }

    public static void Bake()
    {
        if (AdaptiveProbeVolumes.isRunning || Lightmapping.isRunning) throw new InvalidOperationException("Bake already active.");
        EnsurePipeline();
        var reference = ProbeReferenceVolume.instance;
        if (!reference.isInitialized) throw new InvalidOperationException("Render the control camera once to initialize URP/APV.");
        reference.SetActiveBakingSet(AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath));
        s_Errors = 0;
        BakeResult = "Baking";
        Application.logMessageReceived += OnLog;
        EditorApplication.update += UpdateBake;
        try
        {
            if (!AdaptiveProbeVolumes.BakeAsync()) throw new InvalidOperationException("Native APV bake could not start.");
        }
        catch
        {
            EditorApplication.update -= UpdateBake;
            Application.logMessageReceived -= OnLog;
            BakeResult = "Failed to start";
            throw;
        }
    }

    private static void EnsurePipeline()
    {
        // Batch Editor has no regular Game View loop. Prepare the official pipeline explicitly.
        var method = typeof(RenderPipelineManager).GetMethod("TryPrepareRenderPipeline",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) throw new MissingMethodException("RenderPipelineManager.TryPrepareRenderPipeline");
        method.Invoke(null, new object[] { GraphicsSettings.currentRenderPipeline });
    }

    public static void Capture(string path)
    {
        EnsurePipeline();
        var camera = Camera.main;
        var target = RenderTexture.GetTemporary(960, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        try
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP StandardRequest is unsupported.");
            for (int i = 0; i < 3; i++) RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }

    private static void OnLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) s_Errors++;
    }

    private static void UpdateBake()
    {
        if (AdaptiveProbeVolumes.isRunning || Lightmapping.isRunning) return;
        EditorApplication.update -= UpdateBake;
        Application.logMessageReceived -= OnLog;
        BakeResult = s_Errors == 0 && AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath).HasBakedData() ? "Completed" : "Failed";
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[APV Control] " + BakeResult);
    }

    public static void SetDisplay(bool directLight, float apvWeight)
    {
        GameObject.Find("Mixed Point - Baked Indirect").GetComponent<Light>().enabled = directLight;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder + "/Profile.asset");
        profile.TryGet<ProbeVolumesOptions>(out var options);
        options.intensityMultiplier.Override(apvWeight);
        EditorApplication.QueuePlayerLoopUpdate();
    }

    public static void SetExposure(float ev100)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder + "/Profile.asset");
        profile.TryGet<ExposureVolume>(out var exposure);
        exposure.fixedExposure.Override(ev100);
        EditorUtility.SetDirty(profile);
    }

    public static void SampleProbes()
    {
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Folder + "/APVControlSample.compute");
        var positions = new[] { new Vector4(-2.5f, .2f, -1, 1), new Vector4(2.5f, .2f, -1, 1), new Vector4(-2.5f, .2f, 1, 1), new Vector4(2.5f, .2f, 1, 1) };
        var normals = new[] { new Vector4(0, 1, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 1, 0, 0) };
        var p = new ComputeBuffer(4, 16);
        var n = new ComputeBuffer(4, 16);
        var output = new ComputeBuffer(4, 16);
        try
        {
            p.SetData(positions);
            n.SetData(normals);
            shader.EnableKeyword("PROBE_VOLUMES_L2");
            using var cmd = new CommandBuffer();
            var reference = ProbeReferenceVolume.instance;
            reference.BindAPVRuntimeResources(cmd, true);
            reference.UpdateShaderVariablesProbeVolumes(cmd, VolumeManager.instance.stack.GetComponent<ProbeVolumesOptions>(), 0, false);
            cmd.SetComputeBufferParam(shader, 0, "_ProbePositions", p);
            cmd.SetComputeBufferParam(shader, 0, "_ProbeNormals", n);
            cmd.SetComputeBufferParam(shader, 0, "_ProbeResults", output);
            cmd.SetComputeIntParam(shader, "_ProbeCount", 4);
            cmd.DispatchCompute(shader, 0, 4, 1, 1);
            SessionState.SetString("APVControl.Samples", "Pending");
            cmd.RequestAsyncReadback(output, request =>
            {
                try
                {
                    var result = request.hasError ? "Readback failed" : string.Join(";", Array.ConvertAll(request.GetData<Vector4>().ToArray(), v => v.ToString("G7")));
                    SessionState.SetString("APVControl.Samples", result);
                }
                finally { p.Dispose(); n.Dispose(); output.Dispose(); }
            });
            Graphics.ExecuteCommandBuffer(cmd);
        }
        catch { p.Dispose(); n.Dispose(); output.Dispose(); throw; }
    }

    public static string Status()
    {
        var core = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ProbeReferenceVolume).Assembly);
        var urp = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly);
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        return $"Unity={Application.unityVersion}; device={SystemInfo.graphicsDeviceType}; Core={core.version}:{core.resolvedPath}; URP={urp.version}:{urp.resolvedPath}; APV={pipeline?.supportProbeVolume}; initialized={ProbeReferenceVolume.instance.isInitialized}; loaded={ProbeReferenceVolume.instance.DataHasBeenLoaded()}; result={BakeResult}; samples={SessionState.GetString("APVControl.Samples", "not sampled")}";
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        material.SetFloat("_Metallic", 0);
        material.SetFloat("_Smoothness", .15f);
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
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveGI = ReceiveGI.LightProbes;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
    }
}
