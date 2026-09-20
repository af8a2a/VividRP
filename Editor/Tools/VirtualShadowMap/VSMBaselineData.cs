using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal enum VSMBaselineRepaintMode { FourHz, OneHz, Manual }

    [Serializable]
    internal sealed class VSMBaselineCase
    {
        public string name;
        [Range(512, 16384)] public int resolution = 2048;
        public bool pcf;
        public bool stochasticFiltering;
        [Range(-4, 12)] public int firstLevel = 1;
        [Min(0.01f)] public float maxDistance = 150;
        [Range(0, 0.5f)] public float transition = 0.2f;
        public bool screenDensity;
        [Range(0.25f, 8)] public float targetTexelPixels = 1;
        [Range(-4, 4)] public float lodBias;
        public bool screenSpaceDenoise;
        public VSMBaselineRepaintMode recorderRepaint = VSMBaselineRepaintMode.Manual;

        internal VSMBaselineCase Copy() => (VSMBaselineCase)MemberwiseClone();

        internal static VSMBaselineCase[] CreateDefaults()
        {
            var cases = new VSMBaselineCase[6];
            for (int i = 0; i < cases.Length; i++)
                cases[i] = new VSMBaselineCase
                {
                    name = (2 << (i / 2)) + "K_" + (i % 2 == 0 ? "Hard" : "PCF"),
                    resolution = 2048 << (i / 2), pcf = i % 2 != 0,
                };
            return cases;
        }

        internal static VSMBaselineCase[] CreateRepaintComparison()
        {
            var baseline = new VSMBaselineCase
            { name = "4K_Hard_FourHz", resolution = 4096, recorderRepaint = VSMBaselineRepaintMode.FourHz };
            var manual = baseline.Copy();
            manual.name = "4K_Hard_Manual"; manual.recorderRepaint = VSMBaselineRepaintMode.Manual;
            return new[] { baseline, manual };
        }

        internal static VSMBaselineCase[] CreateTimingExperiment(int repetitions)
        {
            if (repetitions < 1 || repetitions > 10) throw new ArgumentOutOfRangeException(nameof(repetitions));
            var cases = new VSMBaselineCase[repetitions * 3];
            for (int repeat = 0; repeat < repetitions; repeat++)
                for (int step = 0; step < 3; step++)
                {
                    var mode = (VSMBaselineRepaintMode)((repeat + step) % 3);
                    cases[repeat * 3 + step] = new VSMBaselineCase
                    { name = "4K_Hard_" + mode + "_R" + (repeat + 1), resolution = 4096, recorderRepaint = mode };
                }
            return cases;
        }

        internal void Validate()
        {
            if (resolution < 512 || resolution > 16384 || resolution % 128 != 0
                || firstLevel < -4 || firstLevel > 12 || !float.IsFinite(maxDistance) || maxDistance <= 0
                || !float.IsFinite(transition) || transition < 0 || transition > 0.5f
                || !float.IsFinite(targetTexelPixels) || targetTexelPixels < 0.25f || targetTexelPixels > 8
                || !float.IsFinite(lodBias) || lodBias < -4 || lodBias > 4)
                throw new ArgumentException("Invalid VSM case. Resolution must be a multiple of 128 in [512, 16384]; check the other parameter ranges.");
        }

        internal void Apply(CascadedShadowSettingsVolume settings)
        {
            settings.enableCSM.Override(true);
            settings.enableVirtualShadowMapPrototype.Override(true);
            settings.virtualShadowMapResolution.Override(resolution);
            settings.virtualShadowMapPCF.Override(pcf);
            settings.virtualShadowMapStochasticFiltering.Override(stochasticFiltering);
            settings.virtualShadowMapFirstLevel.Override(firstLevel);
            settings.maxShadowDistance.Override(maxDistance);
            settings.virtualShadowMapTransition.Override(transition);
            settings.virtualShadowMapScreenDensity.Override(screenDensity);
            settings.virtualShadowMapTargetTexelPixels.Override(targetTexelPixels);
            settings.virtualShadowMapResolutionLodBias.Override(lodBias);
            settings.screenSpaceShadowDenoise.Override(screenSpaceDenoise);
        }

        internal bool Matches(CascadedShadowSettingsVolume settings)
            => settings != null && settings.enableCSM.value && settings.enableVirtualShadowMapPrototype.value
                && settings.virtualShadowMapResolution.value == resolution && settings.virtualShadowMapPCF.value == pcf
                && settings.virtualShadowMapStochasticFiltering.value == stochasticFiltering
                && settings.virtualShadowMapFirstLevel.value == firstLevel && settings.maxShadowDistance.value == maxDistance
                && settings.virtualShadowMapTransition.value == transition && settings.virtualShadowMapScreenDensity.value == screenDensity
                && settings.virtualShadowMapTargetTexelPixels.value == targetTexelPixels && settings.virtualShadowMapResolutionLodBias.value == lodBias
                && settings.screenSpaceShadowDenoise.value == screenSpaceDenoise;
    }

    // Time is accumulated only within an active sampling segment. Pauses,
    // camera waits, snapshot I/O and re-warmup must not advance the measurement.
    internal sealed class VSMBaselineSamplingClock
    {
        private double m_LastTime = -1;
        internal double ElapsedSeconds { get; private set; }
        internal int Segment { get; private set; }
        internal void Reset() { ElapsedSeconds = 0; Segment = 0; m_LastTime = -1; }
        internal void BeginSegment() { Segment++; m_LastTime = -1; }
        internal void Observe(double time)
        {
            if (m_LastTime >= 0 && time > m_LastTime) ElapsedSeconds += time - m_LastTime;
            m_LastTime = time;
        }
    }

    // Fixed storage: all formatting, sorting, scene enumeration and file I/O happen
    // at case boundaries, never while collecting a stable frame.
    internal sealed class VSMBaselineSamples
    {
        internal readonly double[] Values;
        internal readonly int[] Frames;
        internal readonly int[] Segments;
        internal readonly int[] ValidCounts;
        internal readonly int[] CameraFrames, CameraSerials, WindowRepaints;
        internal readonly double[] SecondsSinceRepaint;
        internal readonly double[] Times;
        internal readonly ulong[] TimingTimestamps;
        private readonly double[] m_Scratch;
        internal int Count { get; private set; }
        internal int Capacity => Frames.Length;
        internal int MetricCount { get; }

        internal VSMBaselineSamples(int capacity, int metricCount)
        {
            MetricCount = metricCount;
            Values = new double[checked(capacity * metricCount)];
            Frames = new int[capacity]; Segments = new int[capacity]; ValidCounts = new int[metricCount]; Times = new double[capacity];
            TimingTimestamps = new ulong[capacity]; m_Scratch = new double[capacity];
            CameraFrames = new int[capacity]; CameraSerials = new int[capacity]; WindowRepaints = new int[capacity];
            SecondsSinceRepaint = new double[capacity];
        }

        internal void Clear() { Count = 0; Array.Clear(ValidCounts, 0, ValidCounts.Length); }

        internal void Add(int frame, double time, ulong timestamp, double[] values, int segment = 0,
            int cameraFrame = -1, int cameraSerial = 0, int windowRepaints = 0, double secondsSinceRepaint = double.NaN)
        {
            if (Count == Capacity) return;
            CameraFrames[Count] = cameraFrame; CameraSerials[Count] = cameraSerial;
            WindowRepaints[Count] = windowRepaints; SecondsSinceRepaint[Count] = secondsSinceRepaint;
            Frames[Count] = frame; Segments[Count] = segment; Times[Count] = time; TimingTimestamps[Count] = timestamp;
            for (int i = 0; i < MetricCount; i++)
                if (double.IsFinite(values[i])) ValidCounts[i]++;
            Array.Copy(values, 0, Values, Count * MetricCount, MetricCount);
            Count++;
        }

        internal int Statistics(int metric, out double median, out double p95, out double min, out double max)
        {
            int valid = 0;
            for (int i = 0; i < Count; i++)
            {
                double value = Values[i * MetricCount + metric];
                if (!double.IsNaN(value) && !double.IsInfinity(value)) m_Scratch[valid++] = value;
            }
            median = p95 = min = max = double.NaN;
            if (valid == 0) return 0;
            Array.Sort(m_Scratch, 0, valid);
            min = m_Scratch[0]; max = m_Scratch[valid - 1];
            median = (m_Scratch[(valid - 1) / 2] + m_Scratch[valid / 2]) * 0.5;
            p95 = m_Scratch[(int)Math.Ceiling(valid * 0.95) - 1];
            return valid;
        }

        internal static string Number(double value)
            => double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("R", CultureInfo.InvariantCulture);

        internal static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        internal void Write(string folder, int index, VSMBaselineCase settings, string status, string[] metrics)
        {
            using (var writer = new StreamWriter(Path.Combine(folder, "case_" + index.ToString("D3") + "_samples.csv")))
            {
                writer.Write("observation_frame,unscaled_time,frame_timing_timestamp,segment");
                foreach (string metric in metrics) { writer.Write(','); writer.Write(Csv(metric)); }
                writer.WriteLine(",observed_camera_frame,observed_camera_serial,window_repaint_count,seconds_since_window_repaint");
                for (int row = 0; row < Count; row++)
                {
                    writer.Write(Frames[row].ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                    writer.Write(Number(Times[row])); writer.Write(',');
                    writer.Write(TimingTimestamps[row].ToString(CultureInfo.InvariantCulture)); writer.Write(',');
                    writer.Write(Segments[row].ToString(CultureInfo.InvariantCulture));
                    for (int column = 0; column < MetricCount; column++)
                    { writer.Write(','); writer.Write(Number(Values[row * MetricCount + column])); }
                    writer.Write(','); writer.Write(CameraFrames[row].ToString(CultureInfo.InvariantCulture));
                    writer.Write(','); writer.Write(CameraSerials[row].ToString(CultureInfo.InvariantCulture));
                    writer.Write(','); writer.Write(WindowRepaints[row].ToString(CultureInfo.InvariantCulture));
                    writer.Write(','); writer.Write(Number(SecondsSinceRepaint[row]));
                    writer.WriteLine();
                }
            }
            string summary = Path.Combine(folder, "case_" + index.ToString("D3") + "_summary.csv");
            using (var writer = new StreamWriter(summary))
            {
                writer.WriteLine("case_index,case_name,status,metric,observations,valid_samples,median,p95,min,max");
                for (int metric = 0; metric < MetricCount; metric++)
                {
                    int valid = Statistics(metric, out double median, out double p95, out double min, out double max);
                    writer.WriteLine(string.Join(",", index.ToString(CultureInfo.InvariantCulture), Csv(settings.name),
                        Csv(status), Csv(metrics[metric]), Count.ToString(CultureInfo.InvariantCulture),
                        valid.ToString(CultureInfo.InvariantCulture), Number(median), Number(p95), Number(min), Number(max)));
                }
            }
            RebuildSummary(folder);
        }

        // Checkpoints replace their case rows, rather than appending duplicates.
        internal static void RebuildSummary(string folder)
        {
            string[] files = Directory.GetFiles(folder, "case_*_summary.csv", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            string target = Path.Combine(folder, "summary.csv");
            using (var writer = new StreamWriter(target + ".tmp"))
            {
                bool first = true;
                foreach (string file in files)
                {
                    using (var reader = new StreamReader(file))
                    {
                        string header = reader.ReadLine();
                        if (first) { writer.WriteLine(header); first = false; }
                        writer.Write(reader.ReadToEnd());
                    }
                }
            }
            if (File.Exists(target)) File.Replace(target + ".tmp", target, null);
            else File.Move(target + ".tmp", target);
        }
    }

    [Serializable]
    internal sealed class VSMBaselineSceneSnapshot
    {
        [Serializable] internal struct SceneInfo { public string name, path; public bool active, loaded, dirty; }
        [Serializable] internal struct CameraInfo
        {
            public string name, scenePath;
            public Vector3 position, eulerAngles;
            public Quaternion rotation;
            public Matrix4x4 worldToCamera, projection;
            public float fieldOfView, nearClip, farClip, orthographicSize, aspect, focalLength;
            public bool orthographic, physicalCamera, dynamicResolution;
            public Vector2 sensorSize, lensShift;
            public Rect viewport;
            public int pixelWidth, pixelHeight, scaledPixelWidth, scaledPixelHeight, cullingMask;
        }
        [Serializable] internal struct LightInfo
        {
            public string name, scenePath, type, shadows;
            public bool enabled, active, sun;
            public Vector3 position, eulerAngles, direction;
            public Quaternion rotation;
            public Color color;
            public float intensity, range, spotAngle, shadowStrength, depthBias, normalBias, slopeBias, angularDiameter;
        }
        public SceneInfo[] scenes;
        public CameraInfo camera;
        public LightInfo[] lights;
        public int enabledGameCameras;
        public double scaledTime, unscaledTime;
        public float timeScale;

        internal static VSMBaselineSceneSnapshot Capture(Camera camera)
        {
            var result = new VSMBaselineSceneSnapshot { scenes = new SceneInfo[SceneManager.sceneCount],
                scaledTime = Time.timeAsDouble, unscaledTime = Time.unscaledTimeAsDouble, timeScale = Time.timeScale };
            for (int i = 0; i < result.scenes.Length; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                result.scenes[i] = new SceneInfo { name = scene.name, path = scene.path, loaded = scene.isLoaded,
                    dirty = scene.isDirty, active = scene == SceneManager.GetActiveScene() };
            }
            if (camera != null)
                result.camera = new CameraInfo
                {
                    name = camera.name, scenePath = camera.gameObject.scene.path,
                    position = camera.transform.position, rotation = camera.transform.rotation, eulerAngles = camera.transform.eulerAngles,
                    worldToCamera = camera.worldToCameraMatrix, projection = camera.nonJitteredProjectionMatrix,
                    fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane,
                    orthographic = camera.orthographic, orthographicSize = camera.orthographicSize, aspect = camera.aspect,
                    physicalCamera = camera.usePhysicalProperties, focalLength = camera.focalLength,
                    sensorSize = camera.sensorSize, lensShift = camera.lensShift, viewport = camera.rect,
                    dynamicResolution = camera.allowDynamicResolution, pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight,
                    scaledPixelWidth = camera.scaledPixelWidth, scaledPixelHeight = camera.scaledPixelHeight, cullingMask = camera.cullingMask,
                };
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            result.lights = new LightInfo[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                light.TryGetComponent<VividAdditionalLightData>(out var additional);
                result.lights[i] = new LightInfo
                {
                    name = light.name, scenePath = light.gameObject.scene.path, type = light.type.ToString(), shadows = light.shadows.ToString(),
                    enabled = light.enabled, active = light.gameObject.activeInHierarchy, sun = light == RenderSettings.sun,
                    position = light.transform.position, rotation = light.transform.rotation, eulerAngles = light.transform.eulerAngles,
                    direction = -light.transform.forward, color = light.color, intensity = light.intensity, range = light.range,
                    spotAngle = light.spotAngle, shadowStrength = light.shadowStrength,
                    depthBias = additional != null ? additional.depthBias : light.shadowBias,
                    normalBias = additional != null ? additional.normalBias : light.shadowNormalBias,
                    slopeBias = additional != null ? additional.slopeBias : 0,
                    angularDiameter = additional != null ? additional.angularDiameter : 0,
                };
            }
            foreach (Camera item in Camera.allCameras)
                if (item.cameraType == CameraType.Game) result.enabledGameCameras++;
            return result;
        }
    }
}
