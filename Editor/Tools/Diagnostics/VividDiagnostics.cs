using VividRP.Runtime.VirtualShadowMap;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor
{
    // One API for menu, Unity CLI and existing MCP/eval clients. No server, PID,
    // project path or scene preset is embedded here. Inactive means no subscriptions.
    public static class VividDiagnostics
    {
        [Serializable]
        public sealed class Request
        {
            public string action = "status", jobId, outputDirectory, cameraId;
            public int warmupFrames = 64, frames = 128;
            public float timeoutSeconds = 120;
            public bool includeDepthPools, repaintViews;
            public string[] markers;
        }
        [Serializable]
        public sealed class Result
        {
            public int schemaVersion = 1;
            public bool success = true;
            public string error, jobId, kind, state, outputDirectory;
            public int observations, warmupObserved, pendingReadbacks;
            public CameraInfo[] cameras;
        }
        [Serializable]
        public sealed class CameraInfo
        {
            public string id, name, type, scene;
            public bool enabled, main;
        }
        [Serializable]
        private sealed class Snapshot
        {
            public int schemaVersion = 1;
            public string utc, unityVersion, projectPath, gpu, graphicsApi, settingsJson;
            public bool playing, compiling, updating, vsmActive;
            public string cameraId;
            public int virtualResolution, physicalPages, depthLayers;
            public VSMBaselineSceneSnapshot scene;
            public string vsmScope = "Runtime state at snapshot time; outside a resolve callback it may belong to the last rendered camera.";
        }
        [Serializable]
        private sealed class Channel
        {
            public string file, sourceFormat, storageFormat, sha256;
            public int width, height, layers, count, stride;
            public long bytes;
        }
        [Serializable]
        private sealed class CaptureManifest
        {
            public int schemaVersion = 1, cameraFrame;
            public string scope = "Post directional-shadow resolve; raw visibility/depth/normal and VSM metadata, not final color or geometric truth. No timing comparison with this capture.";
            public List<Channel> channels = new List<Channel>();
        }

        private static Job s_Active;
        private const string LastResultKey = "VividRP.Diagnostics.LastResult";
        public static bool IsRunning => s_Active != null;

        public static string Execute(string requestJson)
        {
            try
            {
                var request = JsonUtility.FromJson<Request>(requestJson);
                if (request == null) throw new ArgumentException("Request JSON is required.");
                if (request.action == "cameras")
                {
                    var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
                    var entries = new CameraInfo[cameras.Length];
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        var item = cameras[i];
                        entries[i] = new CameraInfo { id = CameraId(item), name = item.name,
                            type = item.cameraType.ToString(), scene = item.gameObject.scene.path,
                            enabled = item.isActiveAndEnabled, main = item == Camera.main };
                    }
                    return JsonUtility.ToJson(new Result { state = "complete", kind = "cameras", cameras = entries });
                }
                if (request.action == "status") return Status(request.jobId);
                if (request.action == "cancel")
                {
                    if (s_Active == null || string.IsNullOrEmpty(request.jobId) || request.jobId != s_Active.Result.jobId)
                        throw new InvalidOperationException("Cancel requires the active jobId.");
                    s_Active.Stop("cancelled"); return Status(request.jobId);
                }
                if (request.action == "snapshot")
                {
                    if (s_Active != null) throw new InvalidOperationException("Finish the active diagnostic before saving a snapshot.");
                    string folder = CreateDirectory(request.outputDirectory);
                    WriteSnapshot(folder, "snapshot.json", FindCamera(request.cameraId, false));
                    return JsonUtility.ToJson(new Result { kind = "snapshot", state = "complete", outputDirectory = folder });
                }
                if (request.action != "profile" && request.action != "capture" && request.action != "smrt-cost") throw new ArgumentException("Unknown action.");
                if (s_Active != null || VSMQualityReproduction.IsRunning) throw new InvalidOperationException("Another diagnostic is active.");
                foreach (var window in Resources.FindObjectsOfTypeAll<VSMBaselineRecorderWindow>())
                    if (window.IsRecording) throw new InvalidOperationException("Baseline Recorder is active.");
                Validate(request);
                var camera = FindCamera(request.cameraId, true);
                s_Active = new Job(request, camera, CreateDirectory(request.outputDirectory));
                try { s_Active.Start(); }
                catch { s_Active.Stop("failed", "Failed to start diagnostic."); throw; }
                return Status(null);
            }
            catch (Exception exception)
            {
                return JsonUtility.ToJson(new Result { success = false, state = "failed", error = exception.Message });
            }
        }

        internal static void Validate(Request request)
        {
            if (request.warmupFrames < 0 || request.warmupFrames > 8192 || request.frames < 1 || request.frames > 8192
                || !float.IsFinite(request.timeoutSeconds) || request.timeoutSeconds < 1 || request.timeoutSeconds > 3600)
                throw new ArgumentException("Invalid warm-up/frame count or timeout (1–3600 seconds).");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation/import to finish.");
            if ((request.action == "capture" || request.action == "smrt-cost") && !SystemInfo.supportsAsyncGPUReadback)
                throw new NotSupportedException("Async GPU readback is unavailable.");
        }

        private static Camera FindCamera(string id, bool required)
        {
            Camera camera;
            if (string.IsNullOrEmpty(id)) camera = Camera.main;
#if UNITY_6000_5_OR_NEWER
            else camera = EditorUtility.EntityIdToObject(EntityId.FromULong(ulong.Parse(id))) as Camera;
#else
            else camera = EditorUtility.InstanceIDToObject(int.Parse(id)) as Camera;
#endif
            if (required && (camera == null || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game))
                throw new ArgumentException("Select an enabled Game camera by cameraId, or tag it MainCamera.");
            if (!string.IsNullOrEmpty(id) && camera == null) throw new ArgumentException("cameraId does not identify a camera.");
            return camera;
        }

        private static string CameraId(Camera camera)
        {
            if (camera == null) return null;
#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(camera.GetEntityId()).ToString();
#else
            return camera.GetInstanceID().ToString();
#endif
        }

        private static string Status(string jobId)
        {
            string json = s_Active != null ? JsonUtility.ToJson(s_Active.Result) : SessionState.GetString(LastResultKey, "");
            var result = string.IsNullOrEmpty(json) ? new Result { state = "idle" } : JsonUtility.FromJson<Result>(json);
            if (!string.IsNullOrEmpty(jobId) && result.jobId != jobId) throw new InvalidOperationException("Unknown jobId in this Editor session.");
            return JsonUtility.ToJson(result);
        }

        private static string CreateDirectory(string parent)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (string.IsNullOrWhiteSpace(parent)) parent = Path.Combine(project, "Temp", "VividRPDiagnostics");
            if (!Path.IsPathRooted(parent)) parent = Path.Combine(project, parent);
            string folder = Path.Combine(Path.GetFullPath(parent), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); return folder;
        }

        private static void WriteSnapshot(string folder, string name, Camera camera)
        {
            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            var snapshot = new Snapshot
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating,
                cameraId = CameraId(camera),
                scene = VSMBaselineSceneSnapshot.Capture(camera),
                settingsJson = settings != null ? JsonUtility.ToJson(settings) : null,
                vsmActive = VirtualShadowMapPrototypeRuntime.IsFrameActive,
                virtualResolution = VirtualShadowMapPrototypeRuntime.VirtualResolution,
                physicalPages = VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity,
                depthLayers = VirtualShadowMapPrototypeRuntime.DepthLayerCount
            };
            File.WriteAllText(Path.Combine(folder, name), JsonUtility.ToJson(snapshot, true));
        }

        [MenuItem("Tools/VividRP/Diagnostics/Save State Snapshot")]
        private static void SnapshotMenu() => Debug.Log(Execute("{\"action\":\"snapshot\"}"));
        [MenuItem("Tools/VividRP/Diagnostics/Profile Current Camera")]
        private static void ProfileMenu() => Debug.Log(Execute("{\"action\":\"profile\"}"));
        [MenuItem("Tools/VividRP/Diagnostics/Capture VSM Buffers")]
        private static void CaptureMenu() => Debug.Log(Execute("{\"action\":\"capture\"}"));
        [MenuItem("Tools/VividRP/Diagnostics/Capture SMRT Cost")]
        private static void SMRTCostMenu() => Debug.Log(Execute("{\"action\":\"smrt-cost\"}"));
        [MenuItem("Tools/VividRP/Diagnostics/Cancel Current Diagnostic")]
        private static void CancelMenu() { if (s_Active != null) s_Active.Stop("cancelled"); }

        private sealed class Job
        {
            internal readonly Result Result;
            private readonly Request m_Request;
            private readonly Camera m_Camera;
            private readonly Action<ScriptableRenderContext, Camera> m_EndCamera;
            private readonly Action<ComputePassContext, Texture, Texture, Texture> m_Resolve;
            private readonly Action<CSMShadowResolvePass, ComputePassContext, int, int> m_SMRTCost;
            private readonly Action<AsyncGPUReadbackRequest> m_SMRTCostReadback;
            private static readonly ProfilingSampler s_SMRTCostReplay = new("VSM.SMRTCostReplay");
            private readonly EditorApplication.CallbackFunction m_Update;
            private readonly AssemblyReloadEvents.AssemblyReloadCallback m_Reload;
            private readonly Action m_Quit;
            private readonly Action<PlayModeStateChange> m_PlayMode;
            private readonly CaptureManifest m_Manifest = new CaptureManifest();
            private readonly double m_Started;
            private RenderStageCapture m_Timing;
            private ComputeShader m_Copy;
            private RenderTexture m_DepthCopy;
            private GraphicsBuffer m_CostBuffer;
            private int m_CostWidth, m_CostHeight;
            private int m_CopyKernel;
            private bool m_Stopped, m_Queued;
            private int m_LastFrame = -1, m_CameraObservations;

            internal Job(Request request, Camera camera, string folder)
            {
                m_Request = request; m_Camera = camera; m_Started = EditorApplication.timeSinceStartup;
                Result = new Result { jobId = Guid.NewGuid().ToString("N"), kind = request.action, state = "warming", outputDirectory = folder };
                m_EndCamera = EndCamera; m_Resolve = Resolve; m_Update = Update;
                m_SMRTCost = CaptureSMRTCost; m_SMRTCostReadback = SaveSMRTCost;
                m_Reload = OnReload; m_Quit = OnQuit; m_PlayMode = OnPlayMode;
            }

            internal void Start()
            {
                File.WriteAllText(Path.Combine(Result.outputDirectory, "request.json"), JsonUtility.ToJson(m_Request, true));
                WriteSnapshot(Result.outputDirectory, "before.json", m_Camera);
                if (m_Request.action == "profile")
                {
                    _ = VSMProfiling.FilterTemporalVertical;
                    m_Timing = new RenderStageCapture(m_Request.markers ?? RenderStageCapture.VsmMarkers, m_Request.frames);
                    RenderPipelineManager.endCameraRendering += m_EndCamera;
                }
                else if (m_Request.action == "smrt-cost")
                    CSMShadowResolvePass.EditorSMRTCostCapture += m_SMRTCost;
                else
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VividDiagnostics).Assembly);
                    m_Copy = AssetDatabase.LoadAssetAtPath<ComputeShader>(package.assetPath + "/Editor/Tools/Diagnostics/VividDiagnosticCopy.compute");
                    if (m_Copy == null) throw new InvalidOperationException("Diagnostic depth-copy shader is not imported.");
                    m_CopyKernel = m_Copy.FindKernel("CopyDepth");
                    CSMShadowResolvePass.EditorReceiverCapture += m_Resolve;
                }
                EditorApplication.update += m_Update;
                AssemblyReloadEvents.beforeAssemblyReload += m_Reload;
                EditorApplication.quitting += m_Quit;
                EditorApplication.playModeStateChanged += m_PlayMode;
                WriteResult();
            }

            private void EndCamera(ScriptableRenderContext context, Camera camera)
            {
                if (m_Stopped || camera != m_Camera || m_Queued) return;
                // Frame count is an observation timestamp, not a GPU frame identity.
                if (!Ready(m_CameraObservations++)) return;
                m_Timing.Observe(Time.frameCount, EditorApplication.timeSinceStartup);
                Result.observations = m_Timing.Count;
                if (m_Timing.Count == m_Request.frames) m_Queued = true;
            }

            private bool Ready(int frame)
            {
                if (m_LastFrame == frame) return false;
                m_LastFrame = frame;
                if (Result.warmupObserved < m_Request.warmupFrames) { Result.warmupObserved++; return false; }
                Result.state = "sampling"; return true;
            }

            private void Resolve(ComputePassContext context, Texture depth, Texture normal, Texture shadow)
            {
                if (m_Stopped || m_Queued) return;
                var camera = context.Get<VividCameraData>();
                if (camera.camera != m_Camera || !Ready(camera.frameIndex)) return;
                m_Queued = true;
                try
                {
                    if (!VirtualShadowMapPrototypeRuntime.IsFrameActive) throw new InvalidOperationException("VSM is inactive at resolve.");
                    m_Manifest.cameraFrame = camera.frameIndex;
                    WriteSnapshot(Result.outputDirectory, "frame.json", m_Camera);
                    var cmd = context.cmd.m_WrappedCommandBuffer;
                    // Depth-only RTs have graphicsFormat=None: copy device depth into
                    // a color staging texture before requesting a converted readback.
                    m_DepthCopy = new RenderTexture(new RenderTextureDescriptor(depth.width, depth.height,
                        UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat, 0)
                        { enableRandomWrite = true, msaaSamples = 1 }) { hideFlags = HideFlags.HideAndDontSave };
                    m_DepthCopy.Create();
                    cmd.SetComputeTextureParam(m_Copy, m_CopyKernel, "_SourceDepth", depth);
                    cmd.SetComputeTextureParam(m_Copy, m_CopyKernel, "_CopiedDepth", m_DepthCopy);
                    cmd.DispatchCompute(m_Copy, m_CopyKernel, (depth.width + 7) / 8, (depth.height + 7) / 8, 1);
                    TextureReadback(cmd, m_DepthCopy, "depth", true,
                        depth is RenderTexture rt ? rt.depthStencilFormat.ToString() : depth.graphicsFormat.ToString());
                    TextureReadback(cmd, normal, "normal", true);
                    TextureReadback(cmd, shadow, "shadow", true);
                    BufferReadback(cmd, VirtualShadowMapPrototypeRuntime.PageTable, "page-table");
                    BufferReadback(cmd, VirtualShadowMapPrototypeRuntime.PageMetadata, "page-metadata");
                    BufferReadback(cmd, VirtualShadowMapPrototypeRuntime.PhysicalPageOwners, "page-owners");
                    BufferReadback(cmd, VirtualShadowMapPrototypeRuntime.AllocatorCounters, "page-counters");
                    BufferReadback(cmd, VirtualShadowMapPrototypeRuntime.Projections.Buffer, "projections");
                    if (m_Request.includeDepthPools)
                    {
                        TextureReadback(cmd, VirtualShadowMapPrototypeRuntime.StaticPhysicalPage.rt, "static-depth-pool", false);
                        TextureReadback(cmd, VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage.rt, "dynamic-depth-pool", false);
                    }
                    Result.observations = 1; Result.state = "readback";
                }
                catch (Exception exception) { Result.error = exception.Message; }
            }

            private void TextureReadback(CommandBuffer cmd, Texture texture, string name, bool float4, string sourceFormat = null)
            {
                if (texture == null) throw new InvalidOperationException("Missing texture: " + name);
                var channel = new Channel { file = name + ".bin.gz", width = texture.width, height = texture.height,
                    sourceFormat = sourceFormat ?? texture.graphicsFormat.ToString(), storageFormat = float4 ? "RGBAFloat" : texture.graphicsFormat.ToString() };
                Action<AsyncGPUReadbackRequest> callback = readback => Save(readback, channel);
                if (float4) cmd.RequestAsyncReadback(texture, 0, TextureFormat.RGBAFloat, callback);
                else cmd.RequestAsyncReadback(texture, 0, callback);
                m_Manifest.channels.Add(channel); Result.pendingReadbacks++;
            }

            private void CaptureSMRTCost(CSMShadowResolvePass pass, ComputePassContext context, int width, int height)
            {
                if (m_Stopped || m_Queued) return;
                var camera = context.Get<VividCameraData>();
                if (camera.camera != m_Camera || !Ready(camera.frameIndex)) return;
                m_Queued = true;
                try
                {
                    m_CostWidth = width; m_CostHeight = height;
                    m_CostBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                        checked(width * height * (SMRTCostCapture.CounterCount / 4)), sizeof(uint) * 4)
                        { name = "VSMSMRTCostCapture" };
                    WriteSnapshot(Result.outputDirectory, "frame.json", m_Camera);
                    var cmd = context.cmd.m_WrappedCommandBuffer;
                    using (new ProfilingScope(cmd, s_SMRTCostReplay)) pass.RecordSMRTCost(context, m_CostBuffer);
                    cmd.RequestAsyncReadback(m_CostBuffer, m_SMRTCostReadback);
                    Result.pendingReadbacks++; Result.observations = 1; Result.state = "readback";
                }
                catch (Exception exception) { Result.error = exception.Message; }
            }

            private void SaveSMRTCost(AsyncGPUReadbackRequest readback)
            {
                try
                {
                    if (m_Stopped) return;
                    if (readback.hasError) throw new InvalidOperationException("SMRT cost readback failed.");
                    var report = SMRTCostCapture.Summarize(readback.GetData<uint>(), m_CostWidth, m_CostHeight);
                    File.WriteAllText(Path.Combine(Result.outputDirectory, "smrt-cost.json"), JsonUtility.ToJson(report, true));
                    if (report.shadowDifferencesOverTolerance != 0) Result.error = "SMRT replay differs from raw production shadow by more than 1e-6.";
                }
                catch (Exception exception) { Result.error = exception.Message; }
                finally { Result.pendingReadbacks--; }
            }

            private void BufferReadback(CommandBuffer cmd, GraphicsBuffer buffer, string name)
            {
                if (buffer == null) throw new InvalidOperationException("Missing buffer: " + name);
                var channel = new Channel { file = name + ".bin.gz", count = buffer.count, stride = buffer.stride, storageFormat = "raw-buffer" };
                cmd.RequestAsyncReadback(buffer, readback => Save(readback, channel));
                m_Manifest.channels.Add(channel); Result.pendingReadbacks++;
            }

            private void Save(AsyncGPUReadbackRequest readback, Channel channel)
            {
                try
                {
                    if (m_Stopped) return;
                    if (readback.hasError) throw new InvalidOperationException("GPU readback failed: " + channel.file);
                    channel.layers = readback.layerCount;
                    using var hash = SHA256.Create();
                    using var file = File.Create(Path.Combine(Result.outputDirectory, channel.file));
                    using var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
                    for (int layer = 0; layer < readback.layerCount; layer++)
                    {
                        byte[] bytes = readback.GetData<byte>(layer).ToArray();
                        zip.Write(bytes, 0, bytes.Length); hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                        channel.bytes += bytes.Length;
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    channel.sha256 = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                }
                catch (Exception exception) { Result.error = exception.Message; }
                finally { Result.pendingReadbacks--; }
            }

            private void Update()
            {
                if (m_Stopped) return;
                if (m_Queued && Result.pendingReadbacks == 0) { Stop(Result.error == null ? "complete" : "failed", Result.error); return; }
                if (m_Camera == null || EditorApplication.timeSinceStartup - m_Started > m_Request.timeoutSeconds)
                { Stop("failed", "Camera unavailable or diagnostic timed out; no render was forced."); return; }
                // Normal Editor tick, with optional existing-view repaint recorded in the request.
                EditorApplication.QueuePlayerLoopUpdate();
                if (m_Request.repaintViews) UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }

            private void OnReload() => Stop("cancelled", "Assembly reload.");
            private void OnQuit() => Stop("cancelled", "Editor quitting.");
            private void OnPlayMode(PlayModeStateChange state) => Stop("cancelled", "Play mode changed.");

            internal void Stop(string state, string error = null)
            {
                if (m_Stopped) return;
                m_Stopped = true;
                RenderPipelineManager.endCameraRendering -= m_EndCamera;
                CSMShadowResolvePass.EditorReceiverCapture -= m_Resolve;
                CSMShadowResolvePass.EditorSMRTCostCapture -= m_SMRTCost;
                EditorApplication.update -= m_Update;
                AssemblyReloadEvents.beforeAssemblyReload -= m_Reload;
                EditorApplication.quitting -= m_Quit;
                EditorApplication.playModeStateChanged -= m_PlayMode;
                Result.state = state; Result.success = state == "complete"; Result.error = error;
                try
                {
                    m_Timing?.WriteCsv(Path.Combine(Result.outputDirectory, "stages.csv"));
                    if (m_Request.action == "capture") File.WriteAllText(Path.Combine(Result.outputDirectory, "capture.json"), JsonUtility.ToJson(m_Manifest, true));
                    WriteSnapshot(Result.outputDirectory, "after.json", m_Camera);
                }
                catch (Exception exception) { Result.success = false; Result.state = "failed"; Result.error = exception.Message; }
                finally
                {
                    // The staging texture must outlive native readback. Normal completion
                    // already has zero pending requests; only early teardown needs a drain.
                    if ((m_DepthCopy != null || m_CostBuffer != null) && Result.pendingReadbacks > 0) AsyncGPUReadback.WaitAllRequests();
                    if (m_DepthCopy != null) UnityEngine.Object.DestroyImmediate(m_DepthCopy);
                    m_CostBuffer?.Dispose();
                    m_Timing?.Dispose(); s_Active = null;
                    // Persist terminal state across domain reload.
                    try { WriteResult(); }
                    catch (Exception exception) { Result.success = false; Result.state = "failed"; Result.error = exception.Message; }
                    SessionState.SetString(LastResultKey, JsonUtility.ToJson(Result));
                }
            }

            private void WriteResult() => File.WriteAllText(Path.Combine(Result.outputDirectory, "result.json"), JsonUtility.ToJson(Result, true));
        }
    }
}
