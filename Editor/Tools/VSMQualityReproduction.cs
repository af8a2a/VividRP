using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    // Diagnostic capture only. PNG encoding/readback/I/O are excluded from performance baselines.
    internal sealed class VSMQualityReproduction : IDisposable
    {
        internal const int FrameCount = 601, CaptureStride = 20, WarmupFrames = 60, RoiSize = 256;
        private static VSMQualityReproduction s_Active;
        internal static bool IsRunning => s_Active != null;
        private static readonly Vector3 s_Offset = new Vector3(2, 0, 2);
        private static readonly string[] s_ParameterNames = {
            "_DepthTexture", "_GBuffer1", "_VSMReceiverDebugShadow", "_VSMReceiverDebugOutput", "_VSMReceiverDebugData",
            "_VSMPrototypeStaticPhysicalPage", "_VSMPrototypeDynamicPhysicalPage", "_VSMPrototypePageTable",
            "_VSMPrototypePageMetadata", "_VSMReceiverDebugMode", "_CSMInvViewProjMatrix",
            "_VSMReceiverParameters", "_CSMOutputWidth", "_CSMOutputHeight", "_VSMPrototypeEnabled",
            "_VSMPrototypeVirtualResolution", "_VSMPrototypePageSize", "_VSMPrototypePagesPerAxis", "_VSMPrototypePhysicalPagesPerRow", "_CSMFrameIndex" };
        private static readonly int[] s_Ids = BuildIds();
        private static int[] BuildIds()
        {
            var ids = new int[s_ParameterNames.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID(s_ParameterNames[i]);
            return ids;
        }

        [MenuItem("Tools/VividRP/Diagnostics/Run Game Camera Quality Reproduction")]
        private static void Run() => RunPreset(false, false);

        [MenuItem("Tools/VividRP/Diagnostics/Run Density Off Detailed Capture")]
        private static void RunDensityOff() => RunPreset(true, false);

        [MenuItem("Tools/VividRP/Diagnostics/Run Density On Detailed Capture")]
        private static void RunDensityOn() => RunPreset(true, true);

        [MenuItem("Tools/VividRP/Diagnostics/Run Density Target 2 Detailed Capture")]
        private static void RunDensityTarget2() => RunPreset(true, true, 2);

        [MenuItem("Tools/VividRP/Diagnostics/Run Density Target 4 Detailed Capture")]
        private static void RunDensityTarget4() => RunPreset(true, true, 4);

        private static void RunPreset(bool detailed, bool screenDensity, float targetTexelPixels = 1)
        {
            if (s_Active != null) throw new InvalidOperationException("Quality reproduction is already active.");
            foreach (var recorder in Resources.FindObjectsOfTypeAll<VSMBaselineRecorderWindow>())
                if (recorder.IsRecording) throw new InvalidOperationException("Finish the performance recording before quality capture.");
            var session = new VSMQualityReproduction(Camera.main, detailed, screenDensity, targetTexelPixels);
            try { session.Start(); s_Active = session; }
            catch { session.Dispose(); throw; }
        }

        [MenuItem("Tools/VividRP/Diagnostics/Stop Quality Reproduction")]
        private static void Stop() => s_Active?.Finish("interrupted");

        [Serializable] private struct Pose
        {
            public int step, unityFrame;
            public double wallSeconds;
            public Vector3 position;
            public Quaternion rotation;
            public Matrix4x4 gpuViewProjection;
            public bool vsmActive, settingsMatch, cameraMatches;
            public int captureIndex;
        }

        [Serializable] private sealed class Capture
        {
            public int step, status; // 0 not reached; 1 queued; 2 saved; -1 failed/skipped.
            public uint[] pageCounters = new uint[4];
            public string prefix;
            [NonSerialized] internal Channel[] channels;
            [NonSerialized] internal int pending;
            [NonSerialized] internal bool failed;
            [NonSerialized] internal Action<AsyncGPUReadbackRequest> counterCallback;

            internal void PrepareDetailed(VSMQualityReproduction owner)
            {
                channels = new[] { new Channel(owner, 0, 0, this), new Channel(owner, 3, 0, this),
                    new Channel(owner, 5, 0, this), new Channel(owner, 6, 0, this) };
                // Capture-owned callback created once, before the render loop starts.
                counterCallback = request =>
                {
                    try { if (request.hasError) failed = true; else request.GetData<uint>().CopyTo(pageCounters); }
                    catch (Exception e) { owner.m_Report.error = e.Message; failed = true; }
                    finally { pending--; owner.m_Pending--; }
                };
            }
        }

        [Serializable] private sealed class Report
        {
            public int schemaVersion = 2, renderedFrames, savedCaptures, skippedCaptures;
            public bool roiOnly;
            public int[] captureModes = { 0, 3, 5 };
            public string startedUtc, finishedUtc, status = "recording", error = "", project, unityVersion, gpu, graphicsApi;
            public string qualityStatus = "fixture_only_pending_visual_review";
            public string timingScope = "Diagnostic dispatch/readback/PNG/I/O enabled. Never compare these frame times to performance baselines.";
            public string trajectory = "World-space offset (2,0,2), fixed rotation, 601 poses at 30 logical fps; 60 warmup frames; capture every 20 poses.";
            public string simulation = "Time scale frozen; unscaled scripts and external animation are not frozen. Wall duration is recorded separately.";
            public string dataLayout = "ROI: centered 256x256, GPU pixel coordinates; little-endian float32 RGBA row-major. modes 0=preferred/sampled/transition/requested blend; 3=footprintX/Y/worldTexelSize/valid; 5=missingMask/recomputedShadow/sourceShadow/absDifference.";
            public string cameraSource = "Current MainCamera at invocation; exact pose and projection stored in start.";
            public bool gpuUvStartsAtTop, d3d12DebugStartupFlag;
            public int width, height, roiX, roiY, roiWidth = RoiSize, roiHeight = RoiSize;
            public int physicalPageCapacity, clipmapLevels;
            public VSMBaselineCase settings = new VSMBaselineCase { name = "4K_PCF_Quality", resolution = 4096, pcf = true };
            public VSMBaselineSceneSnapshot start, end;
            public Pose[] poses = new Pose[FrameCount];
            public Capture[] captures = new Capture[(FrameCount - 1) / CaptureStride + 1];
        }

        private sealed class Channel
        {
            internal readonly int Mode;
            internal readonly byte[] Raw, Pixels;
            internal readonly Action<AsyncGPUReadbackRequest> RawCallback, ImageCallback;
            private readonly VSMQualityReproduction m_Owner;
            private readonly Capture m_Capture;
            internal Channel(VSMQualityReproduction owner, int mode, int pixels, Capture capture = null)
            {
                m_Owner = owner; m_Capture = capture; Mode = mode;
                Raw = new byte[RoiSize * RoiSize * 16]; Pixels = new byte[pixels * 4];
                RawCallback = OnRaw; ImageCallback = OnImage;
            }
            private void OnRaw(AsyncGPUReadbackRequest request) => Complete(request, Raw);
            private void OnImage(AsyncGPUReadbackRequest request) => Complete(request, Pixels);
            private void Complete(AsyncGPUReadbackRequest request, byte[] destination)
            {
                try
                {
                    if (request.hasError) { if (m_Capture != null) m_Capture.failed = true; else m_Owner.m_ReadbackError = true; }
                    else request.GetData<byte>().CopyTo(destination);
                }
                catch (Exception e) { m_Owner.m_Report.error = e.Message; if (m_Capture != null) m_Capture.failed = true; else m_Owner.m_ReadbackError = true; }
                finally { m_Owner.m_Pending--; if (m_Capture != null) m_Capture.pending--; }
            }
        }

        private readonly Camera m_Camera;
        private readonly Vector3 m_Position;
        private readonly Quaternion m_Rotation;
        private readonly float m_TimeScale, m_CaptureDelta;
        private readonly int m_FrameRate, m_VSync;
        private readonly bool m_Background;
        private readonly Action<ScriptableRenderContext, Camera> m_Begin, m_End;
        private readonly Action<ComputePassContext, Texture, Texture, Texture> m_Record;
        private readonly EditorApplication.CallbackFunction m_Update;
        private readonly AssemblyReloadEvents.AssemblyReloadCallback m_Reload;
        private readonly Action<PlayModeStateChange> m_PlayMode;
        private readonly Action<AsyncGPUReadbackRequest> m_ShadowCallback, m_CounterCallback;
        private readonly Report m_Report = new Report();
        private readonly string m_Directory;
        private readonly int[] m_CaptureIndices = new int[FrameCount];
        private Channel[] m_Channels;
        private float[] m_Shadow;
        private Color32[] m_Gray;
        private Texture2D m_Image;
        private ComputeShader m_Compute;
        private RenderTexture m_Output, m_Data;
        private VolumeProfile m_Profile;
        private CascadedShadowSettingsVolume m_Settings;
        private GameObject m_Override;
        private int m_Kernel, m_Step = -WarmupFrames, m_LastBegin = -1, m_LastEnd = -1, m_LastRecord = -1, m_Pending, m_Queued = -1;
        private bool m_ReadbackError, m_Disposed, m_Started;
        private double m_StartTime, m_LastCameraTime;
        private int m_Width, m_Height;

        private VSMQualityReproduction(Camera camera, bool detailed, bool screenDensity, float targetTexelPixels)
        {
            if (!EditorApplication.isPlaying || !(RenderPipelineManager.currentPipeline is VividRenderPipeline)
                || camera == null || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game)
                throw new InvalidOperationException("Use an active MainCamera in VividRP Play Mode.");
            if (!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("GPU readback is unavailable.");
            m_Camera = camera; m_Position = camera.transform.position; m_Rotation = camera.transform.rotation;
            m_Report.roiOnly = detailed;
            m_Report.settings.screenDensity = screenDensity;
            m_Report.settings.targetTexelPixels = targetTexelPixels;
            m_Report.settings.name = detailed ? (screenDensity ? "4K_PCF_DensityOn_Detailed" : "4K_PCF_DensityOff_Detailed") : "4K_PCF_Quality";
            if (detailed)
            {
                m_Report.captureModes = new[] { 0, 3, 5, 6 };
                m_Report.trajectory += " Additional per-frame ROI captures at steps 440..480; no full-frame image readbacks.";
                m_Report.dataLayout += " mode 6=density desiredLOD/finest covered level/selected level/footprint-to-target ratio (-1 when disabled).";
                m_Report.dataLayout += " Detailed ROI files use lossless gzip (.rgba32f.gz); the decompressed byte layout is unchanged.";
            }
            m_TimeScale = Time.timeScale; m_CaptureDelta = Time.captureDeltaTime;
            m_FrameRate = Application.targetFrameRate; m_VSync = QualitySettings.vSyncCount; m_Background = Application.runInBackground;
            m_Begin = BeginCamera; m_End = EndCamera; m_Record = Record; m_Update = Update;
            m_Reload = BeforeReload; m_PlayMode = PlayModeChanged; m_ShadowCallback = ShadowReadback; m_CounterCallback = CounterReadback;
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VSMQualityReproduction).Assembly).resolvedPath;
            m_Directory = Path.Combine(package, "Roadmap~/Experiments/Quality",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        private void Start()
        {
            m_Width = m_Camera.scaledPixelWidth; m_Height = m_Camera.scaledPixelHeight;
            if (m_Width < RoiSize || m_Height < RoiSize) throw new InvalidOperationException("Game output must be at least 256x256.");
            Directory.CreateDirectory(m_Directory);
            m_Report.startedUtc = DateTime.UtcNow.ToString("O"); m_Report.project = Directory.GetParent(Application.dataPath).FullName;
            m_Report.unityVersion = Application.unityVersion; m_Report.gpu = SystemInfo.graphicsDeviceName;
            m_Report.graphicsApi = SystemInfo.graphicsDeviceType.ToString(); m_Report.gpuUvStartsAtTop = SystemInfo.graphicsUVStartsAtTop;
            m_Report.d3d12DebugStartupFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-force-d3d12-debug") >= 0;
            m_Report.start = VSMBaselineSceneSnapshot.Capture(m_Camera);
            m_Report.width = m_Width; m_Report.height = m_Height;
            m_Report.roiX = (m_Width - RoiSize) / 2; m_Report.roiY = (m_Height - RoiSize) / 2;
            int captureCount = 0;
            for (int step = 0; step < FrameCount; step++)
                m_CaptureIndices[step] = ShouldCapture(step, m_Report.roiOnly) ? captureCount++ : -1;
            m_Report.captures = new Capture[captureCount];
            for (int step = 0; step < FrameCount; step++)
            {
                int index = m_CaptureIndices[step];
                if (index < 0) continue;
                var capture = new Capture { step = step, prefix = "frame_" + step.ToString("D4") };
                m_Report.captures[index] = capture;
                if (m_Report.roiOnly) capture.PrepareDetailed(this);
            }
            CreateOverride();
            m_Compute = Object.Instantiate(PipelineResourceManager.Get<VividRPCoreResources>().CSMShadowResolveCompute);
            m_Compute.hideFlags = HideFlags.HideAndDontSave; m_Kernel = m_Compute.FindKernel("VSMReceiverDebug");
            m_Output = CreateTexture("VSM Quality Preview", GraphicsFormat.R8G8B8A8_UNorm);
            m_Data = CreateTexture("VSM Quality Data", GraphicsFormat.R32G32B32A32_SFloat);
            if (!m_Report.roiOnly)
            {
                m_Image = new Texture2D(m_Width, m_Height, TextureFormat.RGBA32, false, true);
                m_Image.hideFlags = HideFlags.HideAndDontSave;
                m_Shadow = new float[m_Width * m_Height]; m_Gray = new Color32[m_Shadow.Length];
                m_Channels = new[] { new Channel(this, 0, m_Shadow.Length), new Channel(this, 3, m_Shadow.Length), new Channel(this, 5, m_Shadow.Length) };
            }
            m_Started = true;
            Time.timeScale = 0; Time.captureDeltaTime = 1f / 30;
            Application.targetFrameRate = 30; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
            m_StartTime = m_LastCameraTime = EditorApplication.timeSinceStartup;
            CSMShadowResolvePass.EditorReceiverCapture += m_Record;
            RenderPipelineManager.beginCameraRendering += m_Begin; RenderPipelineManager.endCameraRendering += m_End;
            EditorApplication.update += m_Update; AssemblyReloadEvents.beforeAssemblyReload += m_Reload;
            EditorApplication.playModeStateChanged += m_PlayMode;
            WriteReport();
            Debug.Log("VSM quality reproduction started: " + m_Directory);
        }

        private RenderTexture CreateTexture(string name, GraphicsFormat format)
        {
            var texture = new RenderTexture(m_Width, m_Height, 0) { name = name, graphicsFormat = format,
                enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            if (!texture.Create()) { Object.DestroyImmediate(texture); throw new InvalidOperationException("Cannot allocate quality capture textures."); }
            return texture;
        }

        private void CreateOverride()
        {
            LayerMask mask = VividVolumeManagerUtility.ResolveVolumeLayerMask(m_Camera, m_Camera.GetComponent<VividAdditionalCameraData>());
            int layer = 0;
            while (layer < 32 && (mask.value & (1 << layer)) == 0) layer++;
            if (layer == 32) throw new InvalidOperationException("Camera Volume layer mask is empty.");
            var stack = VolumeManager.instance.CreateStack();
            try
            {
                VolumeManager.instance.Update(stack, m_Camera.transform, mask);
                m_Settings = Object.Instantiate(stack.GetComponent<CascadedShadowSettingsVolume>());
            }
            finally { VolumeManager.instance.DestroyStack(stack); }
            m_Settings.hideFlags = HideFlags.HideAndDontSave; m_Settings.SetAllOverridesTo(true);
            m_Report.settings.Apply(m_Settings);
            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>(); m_Profile.hideFlags = HideFlags.HideAndDontSave;
            m_Profile.components.Add(m_Settings);
            m_Override = new GameObject("VSM Quality Temporary Override") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            m_Override.SetActive(false);
            var volume = m_Override.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = float.MaxValue;
            volume.weight = 1; volume.sharedProfile = m_Profile; m_Override.SetActive(true);
        }

        internal static Vector3 PositionAt(Vector3 start, Vector3 offset, int step) =>
            start + offset * Mathf.Clamp01(step / (float)(FrameCount - 1));

        internal static bool ShouldCapture(int step, bool detailed) => step >= 0 && step < FrameCount
            && (step % CaptureStride == 0 || (detailed && step >= 440 && step <= 480));

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera || m_Disposed || m_Step >= FrameCount || m_LastBegin == Time.frameCount) return;
            m_LastBegin = Time.frameCount; m_LastCameraTime = EditorApplication.timeSinceStartup;
            camera.transform.SetPositionAndRotation(PositionAt(m_Position, s_Offset, m_Step), m_Rotation);
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera || m_Disposed || m_LastBegin != Time.frameCount || m_LastEnd == Time.frameCount || m_Step >= FrameCount) return;
            m_LastEnd = Time.frameCount;
            if (m_Step >= 0 && m_LastRecord != Time.frameCount)
            {
                m_Report.error = "Selected camera did not record the VSM resolve.";
                return;
            }
            m_Step++;
        }

        private void Record(ComputePassContext context, Texture depth, Texture normal, Texture shadow)
        {
            var camera = context.Get<VividCameraData>();
            if (camera.camera != m_Camera || m_Disposed || m_Step < 0 || m_Step >= FrameCount || m_LastRecord == Time.frameCount) return;
            m_LastRecord = Time.frameCount;
            bool active = VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(EntityId.ToULong(m_Camera.GetEntityId()),
                camera.frameIndex >= 0 ? camera.frameIndex : Time.frameCount);
            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (m_Step == 0)
            {
                m_Report.physicalPageCapacity = VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity;
                m_Report.clipmapLevels = VirtualShadowMapPrototypeRuntime.Projections.Count;
            }
            m_Report.poses[m_Step] = new Pose { step = m_Step, unityFrame = Time.frameCount,
                wallSeconds = EditorApplication.timeSinceStartup - m_StartTime, position = m_Camera.transform.position,
                rotation = m_Camera.transform.rotation, gpuViewProjection = camera.GetGPUViewProjectionMatrix(true),
                vsmActive = active, settingsMatch = m_Report.settings.Matches(settings),
                cameraMatches = Vector3.SqrMagnitude(m_Camera.transform.position - PositionAt(m_Position, s_Offset, m_Step)) < 1e-8f
                    && Quaternion.Angle(m_Rotation, m_Camera.transform.rotation) < 0.001f, captureIndex = -1 };
            m_Report.renderedFrames++;
            int captureIndex = m_CaptureIndices[m_Step];
            if (captureIndex < 0) return;
            var capture = m_Report.captures[captureIndex];
            if (!active || m_Queued >= 0 || camera.actualWidth != m_Width || camera.actualHeight != m_Height)
            { capture.status = -1; m_Report.skippedCaptures++; return; }
            capture.status = 1;
            m_Report.poses[m_Step].captureIndex = captureIndex; m_ReadbackError = false;
            // CoreRP explicitly exposes internals to VividRP.Editor. Readbacks must follow
            // their diagnostic dispatches in the same command buffer, before outputs are reused.
            var cmd = context.cmd.m_WrappedCommandBuffer;
            Bind(cmd, camera, context.Get<VividShadowData>(), context.Get<VividLightData>(), settings, depth, normal, shadow);
            if (m_Report.roiOnly)
            {
                // Fixed storage per scheduled capture preserves consecutive frame identity
                // without blocking the GPU, holding a pose, or reusing an in-flight buffer.
                for (int i = 0; i < capture.channels.Length; i++)
                {
                    var channel = capture.channels[i];
                    cmd.SetComputeIntParam(m_Compute, s_Ids[9], channel.Mode);
                    cmd.DispatchCompute(m_Compute, m_Kernel, (m_Width + 7) / 8, (m_Height + 7) / 8, 1);
                    m_Pending++; capture.pending++;
                    cmd.RequestAsyncReadback(m_Data, 0, m_Report.roiX, RoiSize, m_Report.roiY, RoiSize, 0, 1, channel.RawCallback);
                }
                m_Pending++; capture.pending++;
                cmd.RequestAsyncReadback(VirtualShadowMapPrototypeRuntime.AllocatorCounters, capture.counterCallback);
                return;
            }
            m_Queued = captureIndex;
            for (int i = 0; i < m_Channels.Length; i++)
            {
                var channel = m_Channels[i];
                cmd.SetComputeIntParam(m_Compute, s_Ids[9], channel.Mode);
                cmd.DispatchCompute(m_Compute, m_Kernel, (m_Width + 7) / 8, (m_Height + 7) / 8, 1);
                m_Pending += 2;
                cmd.RequestAsyncReadback(m_Data, 0, m_Report.roiX, RoiSize, m_Report.roiY, RoiSize, 0, 1, channel.RawCallback);
                cmd.RequestAsyncReadback(m_Output, 0, channel.ImageCallback);
            }
            m_Pending += 2;
            cmd.RequestAsyncReadback(shadow, 0, TextureFormat.RFloat, m_ShadowCallback);
            cmd.RequestAsyncReadback(VirtualShadowMapPrototypeRuntime.AllocatorCounters, m_CounterCallback);
        }

        private void Bind(CommandBuffer cmd, VividCameraData camera, VividShadowData shadow, VividLightData lightData, CascadedShadowSettingsVolume settings,
            Texture depth, Texture normal, Texture sourceShadow)
        {
            BlueNoise.Instance?.Bind(cmd, m_Compute, m_Kernel);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[0], depth);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[1], normal);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[2], sourceShadow);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[3], m_Output);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[4], m_Data);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[5], VirtualShadowMapPrototypeRuntime.StaticPhysicalPage);
            cmd.SetComputeTextureParam(m_Compute, m_Kernel, s_Ids[6], VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, s_Ids[7], VirtualShadowMapPrototypeRuntime.PageTable);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, s_Ids[8], VirtualShadowMapPrototypeRuntime.PageMetadata);
            cmd.SetComputeBufferParam(m_Compute, m_Kernel, VirtualShadowMapProjectionSet.BufferId, VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            cmd.SetComputeIntParam(m_Compute, VirtualShadowMapProjectionSet.CountId, VirtualShadowMapPrototypeRuntime.Projections.Count);
            Matrix4x4 vp = camera.GetGPUViewProjectionMatrix(true);
            cmd.SetComputeMatrixParam(m_Compute, VirtualShadowMapReceiverQuality.ViewProjectionId, vp);
            cmd.SetComputeMatrixParam(m_Compute, s_Ids[10], vp.inverse);
            cmd.SetComputeVectorParam(m_Compute, VirtualShadowMapReceiverQuality.ParametersId, VirtualShadowMapReceiverQuality.BuildParameters(settings));
            float angle = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
            if (DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out _, out var additional)
                && additional != null) angle = additional.angularDiameter;
            cmd.SetComputeVectorParam(m_Compute, VirtualShadowMapReceiverQuality.SMRTParametersId,
                VirtualShadowMapReceiverQuality.BuildSMRTParameters(settings, angle));
            cmd.SetComputeVectorParam(m_Compute, s_Ids[11], new Vector4(settings.virtualShadowMapPCF.value ? 1 : 0, shadow.depthBias, shadow.slopeScaleDepthBias, settings.virtualShadowMapStochasticFiltering.value ? 1 : 0));
            cmd.SetComputeIntParam(m_Compute, s_Ids[12], m_Width); cmd.SetComputeIntParam(m_Compute, s_Ids[13], m_Height);
            cmd.SetComputeIntParam(m_Compute, s_Ids[14], 1);
            cmd.SetComputeIntParam(m_Compute, s_Ids[15], VirtualShadowMapPrototypeRuntime.VirtualResolution);
            cmd.SetComputeIntParam(m_Compute, s_Ids[16], VirtualShadowMapPrototypeRuntime.PageSize);
            cmd.SetComputeIntParam(m_Compute, s_Ids[17], VirtualShadowMapPrototypeRuntime.PagesPerAxis);
            cmd.SetComputeIntParam(m_Compute, s_Ids[18], VirtualShadowMapPrototypeRuntime.PhysicalPagesPerRow);
            cmd.SetComputeIntParam(m_Compute, s_Ids[19], camera.frameIndex >= 0 ? camera.frameIndex : Time.frameCount);
        }

        private void ShadowReadback(AsyncGPUReadbackRequest request)
        {
            try { if (request.hasError) m_ReadbackError = true; else request.GetData<float>().CopyTo(m_Shadow); }
            catch (Exception e) { m_Report.error = e.Message; m_ReadbackError = true; }
            finally { m_Pending--; }
        }
        private void CounterReadback(AsyncGPUReadbackRequest request)
        {
            try { if (request.hasError) m_ReadbackError = true; else request.GetData<uint>().CopyTo(m_Report.captures[m_Queued].pageCounters); }
            catch (Exception e) { m_Report.error = e.Message; m_ReadbackError = true; }
            finally { m_Pending--; }
        }

        private void Update()
        {
            if (m_Disposed) return;
            try
            {
                if (m_Report.roiOnly) SaveDetailedCaptures();
                if (m_Queued >= 0 && m_Pending == 0) SaveCapture();
                if (!string.IsNullOrEmpty(m_Report.error)) { Finish("failed"); return; }
                if (m_Step >= FrameCount && m_Queued < 0 && m_Pending == 0) { Finish("completed"); return; }
                if (EditorApplication.timeSinceStartup - m_LastCameraTime > 10) Finish("camera_not_rendering");
            }
            catch (Exception e) { m_Report.error = e.ToString(); Finish("failed"); }
        }

        private void SaveCapture()
        {
            var capture = m_Report.captures[m_Queued];
            if (m_ReadbackError) { capture.status = -1; m_Report.skippedCaptures++; m_Report.error = "GPU readback failed."; }
            else
            {
                foreach (var channel in m_Channels)
                {
                    string prefix = Path.Combine(m_Directory, capture.prefix + "_mode" + channel.Mode);
                    File.WriteAllBytes(prefix + ".rgba32f", channel.Raw);
                    m_Image.LoadRawTextureData(channel.Pixels);
                    File.WriteAllBytes(prefix + ".png", m_Image.EncodeToPNG());
                }
                for (int i = 0; i < m_Shadow.Length; i++)
                {
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(m_Shadow[i]) * 255);
                    m_Gray[i] = new Color32(value, value, value, 255);
                }
                m_Image.SetPixels32(m_Gray);
                File.WriteAllBytes(Path.Combine(m_Directory, capture.prefix + "_shadow.png"), m_Image.EncodeToPNG());
                capture.status = 2; m_Report.savedCaptures++;
            }
            m_Queued = -1;
            WriteReport();
        }

        private void SaveDetailedCaptures()
        {
            bool changed = false;
            foreach (var capture in m_Report.captures)
            {
                if (capture.status != 1 || capture.pending != 0) continue;
                if (capture.failed)
                {
                    capture.status = -1; m_Report.skippedCaptures++; m_Report.error = "GPU readback failed.";
                }
                else
                {
                    foreach (var channel in capture.channels)
                    {
                        using var file = File.Create(Path.Combine(m_Directory, capture.prefix + "_mode" + channel.Mode + ".rgba32f.gz"));
                        using var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
                        gzip.Write(channel.Raw, 0, channel.Raw.Length);
                    }
                    capture.status = 2; m_Report.savedCaptures++;
                }
                changed = true;
            }
            if (changed) WriteReport();
        }

        private void BeforeReload() => Finish("assembly_reload");
        private void PlayModeChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingPlayMode) Finish("play_mode_exited"); }

        private void Finish(string status)
        {
            if (m_Disposed) return;
            try
            {
                if (m_Pending > 0) AsyncGPUReadback.WaitAllRequests();
                if (m_Report.roiOnly) SaveDetailedCaptures();
                if (m_Queued >= 0 && m_Pending == 0) SaveCapture();
                if (status == "completed" && (m_Report.skippedCaptures > 0 || m_Report.savedCaptures != m_Report.captures.Length))
                    status = "completed_with_warnings";
                m_Report.status = status; m_Report.finishedUtc = DateTime.UtcNow.ToString("O");
                m_Report.end = VSMBaselineSceneSnapshot.Capture(m_Camera); WriteReport();
                Debug.Log("VSM quality reproduction " + status + ": " + m_Directory);
            }
            finally { Dispose(); }
        }

        private void WriteReport() => File.WriteAllText(Path.Combine(m_Directory, "run.json"), JsonUtility.ToJson(m_Report, true));

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            CSMShadowResolvePass.EditorReceiverCapture -= m_Record;
            RenderPipelineManager.beginCameraRendering -= m_Begin; RenderPipelineManager.endCameraRendering -= m_End;
            EditorApplication.update -= m_Update; AssemblyReloadEvents.beforeAssemblyReload -= m_Reload; EditorApplication.playModeStateChanged -= m_PlayMode;
            if (m_Pending > 0) AsyncGPUReadback.WaitAllRequests();
            if (m_Started)
            {
                if (m_Camera != null) m_Camera.transform.SetPositionAndRotation(m_Position, m_Rotation);
                Time.timeScale = m_TimeScale; Time.captureDeltaTime = m_CaptureDelta;
                Application.targetFrameRate = m_FrameRate; QualitySettings.vSyncCount = m_VSync; Application.runInBackground = m_Background;
            }
            if (m_Override != null) Object.DestroyImmediate(m_Override);
            if (m_Profile != null) Object.DestroyImmediate(m_Profile);
            if (m_Settings != null) Object.DestroyImmediate(m_Settings);
            if (m_Output != null) Object.DestroyImmediate(m_Output);
            if (m_Data != null) Object.DestroyImmediate(m_Data);
            if (m_Image != null) Object.DestroyImmediate(m_Image);
            if (m_Compute != null) Object.DestroyImmediate(m_Compute);
            if (s_Active == this) s_Active = null;
        }
    }
}
