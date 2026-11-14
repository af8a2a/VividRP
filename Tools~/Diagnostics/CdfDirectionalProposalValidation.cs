// Production proposal and sampling GPU conformance. No scene or asset changes.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
{
    const int count = 128, frames = 64;
    const string root = "Packages/com.vivid.render-pipelines/";
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    if (typeof(VividRP.Runtime.RenderPass.Core.SdfDiffuseGatherPass).GetField("m_CdfGuideActive",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) == null)
        throw new InvalidOperationException("CDF prototype did not pass its quality gates. Apply Prototypes/CdfDirectional-RowMajor.patch with --unidiff-zero and wait for assembly reload before replaying.");
    string[] paths = { root + "Shaders/Core/Private/GlobalIllumination/SdfScreenProbeImportanceSampling.compute",
        root + "Editor/Resources/SdfTemporalSamplingValidation.compute" };
    foreach (string path in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    var producer = AssetDatabase.LoadAssetAtPath<ComputeShader>(paths[0]);
    var sampler = AssetDatabase.LoadAssetAtPath<ComputeShader>(paths[1]);
    int make = producer.FindKernel("InspectSdfDirectionalCdfGuideCS"), sample = sampler.FindKernel("ValidateSdfCdfSampling");
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
                0 => 0,
                1 => 1,
                2 => i == c / 8 ? 200 : 0,
                3 => i == c / 8 ? 65000 : .001f,
                4 => i % 4 == c / 32 ? 1e-6f : 1e-9f,
                5 => (Hash((uint)(c * 16 + i)) & 65535) / 100f,
                6 => i / 4 == c / 32 ? 200 : 1,
                _ => .01f + i * i
            };
            density[c][i] = value;
            inputs[4 * c + i / 4][i & 3] = value;
        }
    }
    double Boundary(float[] values, int k)
    {
        if (k == 0) return 0;
        if (k == 8) return 16;
        double total = values.Sum(v => (double)v);
        if (total == 0) return 2 * k;
        double target = 2 * total * k / 8, cdf = 0;
        for (int i = 0; i < 16; i++)
        {
            double mass = values[i] + total / 16;
            if (target <= cdf + mass) return i + (target - cdf) / mass;
            cdf += mass;
        }
        return 16;
    }
    var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
    var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 8 * frames, 16);
    var output = new RenderTexture(new RenderTextureDescriptor(count * 8, 1)
    { graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat, depthBufferBits = 0, enableRandomWrite = true, msaaSamples = 1 });
    var cmd = new CommandBuffer { name = "Validate cosine CDF strata" };
    void CheckShaders()
    {
        var errors = new[] { producer, sampler }.SelectMany(s => ShaderUtil.GetComputeShaderMessages(s))
            .Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
    try
    {
        input.SetData(inputs); output.Create();
        producer.SetBuffer(make, "_SdfCdfValidationInput", input);
        producer.SetTexture(make, "_SdfDirectionalGuideOutput", output);
        producer.SetInt("_SdfCdfValidationCount", count);
        producer.Dispatch(make, count / 8, 1, 1); CheckShaders();
        var readback = AsyncGPUReadback.Request(output); readback.WaitForCompletion();
        if (readback.hasError) throw new InvalidOperationException("CDF descriptor readback failed.");
        var raw = readback.GetData<ushort>();
        var descriptors = new float[count * 8][];
        double maxBoundaryError = 0, maxMassError = 0, maxDirectionError = 0;
        for (int c = 0; c < count; c++)
        {
            double mass = 0;
            for (int ray = 0; ray < 8; ray++)
            {
                int index = c * 8 + ray;
                var v = descriptors[index] = Enumerable.Range(0, 4).Select(k => Unity.Mathematics.math.f16tof32(raw[4 * index + k])).ToArray();
                if (v.Any(x => !float.IsFinite(x)) || v[3] != -7 || v[2] != -3 || v[1] <= v[0]
                    || v[1] - v[0] > 4.015625 || (ray == 0 ? v[0] != 0 : v[0] != descriptors[index - 1][1])
                    || (ray == 7 && v[1] != 16)) throw new InvalidOperationException("CDF hemisphere partition failed at " + c + "/" + ray);
                maxBoundaryError = Math.Max(maxBoundaryError, Math.Max(Math.Abs(v[0] - Boundary(density[c], ray)), Math.Abs(v[1] - Boundary(density[c], ray + 1))));
                mass += .5 * (v[1] - v[0]);
            }
            maxMassError = Math.Max(maxMassError, Math.Abs(mass - 8));
        }
        if (maxBoundaryError > .008 || maxMassError > 1e-6) throw new InvalidOperationException("CDF quantile or PDF mass mismatch.");
        cmd.SetComputeBufferParam(sampler, sample, "_CdfSamplingValidation", result);
        cmd.SetComputeTextureParam(sampler, sample, "_SdfDirectionalGuide", output);
        cmd.SetComputeIntParam(sampler, "_CdfValidationCount", count);
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
            double u = Math.Clamp((Hash(seed) >> 8) / 16777216.0, 1e-5, 1 - 1e-5), v = (Hash(seed ^ 0x68e31da4u) >> 8) / 16777216.0;
            var interval = descriptors[c * 8 + ray];
            double t = interval[0] + (interval[1] - interval[0]) * u;
            int cell = Math.Min((int)t, 15);
            double x = 2 * ((cell % 4 + t - Math.Floor(t)) / 4) - 1, y = 2 * ((cell / 4 + v) / 4) - 1;
            bool horizontal = Math.Abs(x) > Math.Abs(y); double r = horizontal ? x : y;
            double slope = r == 0 ? 0 : (horizontal ? y : x) / r;
            double phi = Math.PI / 4 * (horizontal ? slope : 2 - slope);
            var expected = new Vector3((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)Math.Sqrt(Math.Max(0, 1 - r * r)));
            var a = actual[(frame * count + c) * 8 + ray];
            if (!float.IsFinite(a.x) || !float.IsFinite(a.y) || !float.IsFinite(a.z) || a.w != .5f * (interval[1] - interval[0]))
                throw new InvalidOperationException("Invalid CDF sampling output.");
            maxDirectionError = Math.Max(maxDirectionError, Vector3.Distance(new Vector3(a.x, a.y, a.z), expected));
        }
        if (maxDirectionError > .001) throw new InvalidOperationException("CDF CPU/GPU direction mismatch: " + maxDirectionError);
        string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
            "CdfProposalValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        System.IO.Directory.CreateDirectory(evidenceRoot);
        var report = new { evidenceRoot, casesChecked = count, descriptorsChecked = count * 8, samplesChecked = actual.Length,
            maxBoundaryError, maxMassError, maxDirectionError, density, descriptors,
            shaderMessages = new[] { producer, sampler }.SelectMany(s => ShaderUtil.GetComputeShaderMessages(s)).Select(m => new { m.message, severity = m.severity.ToString() }).ToArray() };
        System.IO.File.WriteAllText(evidenceRoot + "/gpu-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { evidenceRoot, report.casesChecked, report.descriptorsChecked, report.samplesChecked, maxBoundaryError, maxMassError, maxDirectionError };
    }
    finally { cmd.Dispose(); input.Dispose(); result.Dispose(); output.Release(); UnityEngine.Object.DestroyImmediate(output); }
}
