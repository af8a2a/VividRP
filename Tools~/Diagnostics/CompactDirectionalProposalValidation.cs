// Production compact partition/sampler GPU conformance; no scene or asset saves.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
{
    const int count = 128, frames = 64;
    const string root = "Packages/com.vivid.render-pipelines/";
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    if (typeof(VividRP.Runtime.RenderPass.Core.SdfDiffuseGatherPass).GetField("m_CompactGuideActive",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) == null)
        throw new InvalidOperationException("Compact prototypes were withdrawn after quality checks. Apply the matching CompactDirectional-White.patch or CompactDirectional-Frozen.patch and wait for assembly reload to replay.");
    string[] paths = { root + "Shaders/Core/Private/GlobalIllumination/SdfScreenProbeImportanceSampling.compute",
        root + "Editor/Resources/SdfTemporalSamplingValidation.compute" };
    foreach (string path in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    var producer = AssetDatabase.LoadAssetAtPath<ComputeShader>(paths[0]);
    var sampler = AssetDatabase.LoadAssetAtPath<ComputeShader>(paths[1]);
    int make = producer.FindKernel("InspectSdfDirectionalCompactGuideCS"), sample = sampler.FindKernel("ValidateSdfCompactSampling");
    uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); } }
    var density = new float[count][];
    var inputs = new Vector4[count * 4];
    for (int c = 0; c < count; c++)
    {
        density[c] = new float[16];
        for (int i = 0; i < 16; i++)
        {
            float value = (c % 8) switch
            {
                0 => 0, 1 => 1, 2 => i == c / 8 ? 200 : 0, 3 => i == c / 8 ? 65000 : .001f,
                4 => i % 4 == c / 32 ? 1e-6f : 1e-9f,
                5 => (Hash((uint)(c * 16 + i)) & 65535) / 100f,
                6 => i / 4 == c / 32 ? 200 : 1, _ => .01f + i * i
            };
            density[c][i] = value; inputs[4 * c + i / 4][i & 3] = value;
        }
    }
    double Median(float[] values, double[] rect, int axis)
    {
        double exploration = values.Sum(v => (double)v) / 16;
        if (exploration == 0) exploration = 1;
        var masses = new double[4];
        for (int cell = 0; cell < 16; cell++)
        {
            int x = cell % 4, y = cell / 4, band = axis == 0 ? x : y;
            double dx = Math.Max(0, Math.Min(rect[2], x + 1) - Math.Max(rect[0], x));
            double dy = Math.Max(0, Math.Min(rect[3], y + 1) - Math.Max(rect[1], y));
            masses[band] += (values[cell] + exploration) * dx * dy;
        }
        double target = masses.Sum() / 2, prefix = 0, cut = (rect[axis] + rect[axis + 2]) / 2;
        for (int band = 0; band < 4; band++)
        {
            if (masses[band] > 0 && target <= prefix + masses[band])
            {
                double lower = Math.Max(rect[axis], band), upper = Math.Min(rect[axis + 2], band + 1);
                cut = lower + (upper - lower) * (target - prefix) / masses[band]; break;
            }
            prefix += masses[band];
        }
        return Math.Clamp(Math.Round(cut * 16, MidpointRounding.ToEven) / 16, rect[axis] + .0625, rect[axis + 2] - .0625);
    }
    var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
    var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 8 * frames, 16);
    var output = new RenderTexture(new RenderTextureDescriptor(count * 8, 1)
    { graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat, depthBufferBits = 0, enableRandomWrite = true, msaaSamples = 1 });
    var cmd = new CommandBuffer { name = "Validate compact cosine strata" };
    void CheckShaders()
    {
        var errors = new[] { producer, sampler }.SelectMany(s => ShaderUtil.GetComputeShaderMessages(s))
            .Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
    try
    {
        input.SetData(inputs); output.Create();
        producer.SetBuffer(make, "_SdfCompactValidationInput", input);
        producer.SetTexture(make, "_SdfDirectionalGuideOutput", output);
        producer.SetInt("_SdfCompactValidationCount", count);
        producer.Dispatch(make, count / 8, 1, 1); CheckShaders();
        var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
        if (readback.hasError) throw new InvalidOperationException("Compact descriptor readback failed.");
        var raw = readback.GetData<ushort>();
        var descriptors = new float[count * 8][];
        double maxBoundaryError = 0, maxMassError = 0, maxDirectionError = 0, maxMass = 0;
        for (int c = 0; c < count; c++)
        {
            var occupancy = new int[64 * 64]; double massSum = 0;
            for (int ray = 0; ray < 8; ray++)
            {
                int index = c * 8 + ray;
                var v = Enumerable.Range(0, 4).Select(k => Unity.Mathematics.math.f16tof32(raw[4 * index + k])).ToArray();
                if (v[3] >= -8) throw new InvalidOperationException("Missing compact descriptor tag.");
                v[3] = -v[3] - 8; descriptors[index] = v;
                if (v.Any(x => !float.IsFinite(x) || x < 0 || x > 4 || x * 16 != Math.Floor(x * 16))
                    || v[2] <= v[0] || v[3] <= v[1]) throw new InvalidOperationException("Invalid compact rectangle.");
                float mass = .5f * (v[2] - v[0]) * (v[3] - v[1]); massSum += mass; maxMass = Math.Max(maxMass, mass);
                if (mass > 4 || Unity.Mathematics.math.f16tof32(Unity.Mathematics.math.f32tof16(mass)) != mass)
                    throw new InvalidOperationException("Ray metadata cannot represent compact inverse PDF exactly.");
                for (int y = (int)(v[1] * 16); y < (int)(v[3] * 16); y++)
                    for (int x = (int)(v[0] * 16); x < (int)(v[2] * 16); x++) occupancy[y * 64 + x]++;
            }
            if (occupancy.Any(n => n != 1)) throw new InvalidOperationException("Compact hemisphere overlap or gap.");
            maxMassError = Math.Max(maxMassError, Math.Abs(massSum - 8));
            double[] Bounds(int first, int length) => new[] {
                Enumerable.Range(first, length).Min(i => (double)descriptors[c * 8 + i][0]),
                Enumerable.Range(first, length).Min(i => (double)descriptors[c * 8 + i][1]),
                Enumerable.Range(first, length).Max(i => (double)descriptors[c * 8 + i][2]),
                Enumerable.Range(first, length).Max(i => (double)descriptors[c * 8 + i][3]) };
            for (int level = 0; level < 3; level++) for (int parent = 0; parent < (1 << level); parent++)
            {
                int length = 8 >> level, first = parent * length;
                var rect = Bounds(first, length); var left = Bounds(first, length / 2); var right = Bounds(first + length / 2, length / 2);
                int axis = rect[2] - rect[0] >= rect[3] - rect[1] ? 0 : 1, other = 1 - axis;
                if (left[axis] != rect[axis] || right[axis + 2] != rect[axis + 2] || left[axis + 2] != right[axis]
                    || left[other] != rect[other] || right[other] != rect[other]
                    || left[other + 2] != rect[other + 2] || right[other + 2] != rect[other + 2])
                    throw new InvalidOperationException("Compact median tree did not partition its parent.");
                maxBoundaryError = Math.Max(maxBoundaryError, Math.Abs(left[axis + 2] - Median(density[c], rect, axis)));
            }
        }
        // A floating-point median exactly on a grid midpoint may choose either adjacent coordinate.
        if (maxBoundaryError > .062501 || maxMassError != 0) throw new InvalidOperationException("Compact median or PDF mismatch.");
        cmd.SetComputeBufferParam(sampler, sample, "_CompactSamplingValidation", result);
        cmd.SetComputeTextureParam(sampler, sample, "_SdfDirectionalGuide", output);
        cmd.SetComputeIntParam(sampler, "_CompactValidationCount", count);
        cmd.SetComputeIntParam(sampler, "_SdfDirectionalCacheEnabled", 1);
        cmd.SetComputeIntParam(sampler, "_SdfDirectionalGuideEnabled", 2);
        cmd.SetComputeIntParam(sampler, "_SdfGatherHistoryDiagnostics", 0);
        for (int frame = 0; frame < frames; frame++)
        {
            cmd.SetComputeIntParam(sampler, "_SdfDiffuseFrame", frame);
            cmd.SetComputeIntParam(sampler, "_TemporalValidationOffset", frame * count * 8);
            cmd.DispatchCompute(sampler, sample, count, 1, 1);
        }
        Graphics.ExecuteCommandBuffer(cmd); CheckShaders();
        var actual = new Vector4[count * 8 * frames]; result.GetData(actual);
        for (int frame = 0; frame < frames; frame++) for (int c = 0; c < count; c++) for (int ray = 0; ray < 8; ray++)
        {
            uint seed; unchecked { seed = Hash((uint)c + (uint)frame * 0x9e3779b9u) ^ (0xb5297a4du * ((uint)ray + 1)); }
            double u = Math.Clamp((Hash(seed) >> 8) / 16777216.0, 1e-5, 1 - 1e-5);
            double v = Math.Clamp((Hash(seed ^ 0x68e31da4u) >> 8) / 16777216.0, 1e-5, 1 - 1e-5);
            var rect = descriptors[c * 8 + ray];
            double x = 2 * (rect[0] + (rect[2] - rect[0]) * u) / 4 - 1;
            double y = 2 * (rect[1] + (rect[3] - rect[1]) * v) / 4 - 1;
            bool horizontal = Math.Abs(x) > Math.Abs(y); double r = horizontal ? x : y;
            double slope = r == 0 ? 0 : (horizontal ? y : x) / r;
            double phi = Math.PI / 4 * (horizontal ? slope : 2 - slope);
            var expected = new Vector3((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)Math.Sqrt(Math.Max(0, 1 - r * r)));
            var a = actual[(frame * count + c) * 8 + ray];
            if (!float.IsFinite(a.x) || !float.IsFinite(a.y) || !float.IsFinite(a.z)
                || a.w != .5f * (rect[2] - rect[0]) * (rect[3] - rect[1])) throw new InvalidOperationException("Invalid compact sampling output.");
            maxDirectionError = Math.Max(maxDirectionError, Vector3.Distance(new Vector3(a.x, a.y, a.z), expected));
        }
        if (maxDirectionError > .001) throw new InvalidOperationException("Compact CPU/GPU direction mismatch: " + maxDirectionError);
        string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
            "CompactProposalValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        System.IO.Directory.CreateDirectory(evidenceRoot);
        var report = new { evidenceRoot, casesChecked = count, descriptorsChecked = count * 8, samplesChecked = actual.Length,
            maxBoundaryError, maxMassError, maxDirectionError, maxMass, density, descriptors, exactHalfPdf = true,
            shaderMessages = new[] { producer, sampler }.SelectMany(s => ShaderUtil.GetComputeShaderMessages(s)).Select(m => new { m.message, severity = m.severity.ToString() }).ToArray() };
        System.IO.File.WriteAllText(evidenceRoot + "/gpu-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { evidenceRoot, report.casesChecked, report.descriptorsChecked, report.samplesChecked, maxBoundaryError, maxMassError, maxDirectionError, maxMass };
    }
    finally { cmd.Dispose(); input.Dispose(); result.Dispose(); output.Release(); UnityEngine.Object.DestroyImmediate(output); }
}
