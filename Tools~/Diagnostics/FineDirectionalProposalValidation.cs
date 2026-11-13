// GPU/CPU conformance for the archived FourFaces prototype, not a production feature.
// Apply Prototypes/FineDirectional-FourFaces.patch in an isolated checkout first.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
{
    const int probes = 512, frames = 32, rays = 8;
    const string path = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeImportanceSampling.compute";
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
    if (!shader.HasKernel("InspectSdfFineDirectionalProposalsCS") || !shader.HasKernel("InspectSdfFineDirectionalSamplesCS"))
        throw new InvalidOperationException("Fine prototype is not installed; apply the archived FineDirectional-FourFaces.patch to replay this experiment.");
    int build = shader.FindKernel("InspectSdfFineDirectionalProposalsCS"), sample = shader.FindKernel("InspectSdfFineDirectionalSamplesCS");
    void CheckShader()
    {
        var errors = ShaderUtil.GetComputeShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
    uint Hash(uint v) { unchecked { v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); } }
    uint Reverse(uint v) { uint r = 0; for (int i = 0; i < 32; i++) { r = (r << 1) | (v & 1); v >>= 1; } return r; }
    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, probes * rays * frames, 16);
    var guide = new Texture2D(probes * rays, 1, TextureFormat.RGBAHalf, false, true);
    var cmd = new CommandBuffer { name = "Validate fine directional proposals" };
    try
    {
        shader.SetBuffer(build, "_SdfFineProposalValidation", buffer);
        shader.Dispatch(build, 1, 1, 1);
        CheckShader();
        var descriptors = new Vector4[32]; buffer.GetData(descriptors, 0, 0, descriptors.Length);
        var rectangles = new int[32][];
        var cases = new List<object>();
        for (int c = 0; c < 4; c++)
        {
            var occupancy = new int[64];
            int minimumArea = 64;
            float mass = 0;
            for (int ray = 0; ray < rays; ray++)
            {
                var d = descriptors[c * rays + ray];
                if (d.w != -5 || !float.IsFinite(d.x) || !float.IsFinite(d.y) || d.x != Mathf.Floor(d.x) || d.y != Mathf.Floor(d.y))
                    throw new InvalidOperationException("Invalid descriptor at " + c + "/" + ray);
                int a = (int)d.x, b = (int)d.y;
                int x = a & 7, y = a >> 3, w = (b & 7) + 1, h = (b >> 3) + 1;
                if (x + w > 8 || y + h > 8 || x < 0 || y < 0 || w < 1 || h < 1)
                    throw new InvalidOperationException("Rectangle escaped the chart.");
                if (x / 4 != (x + w - 1) / 4 || y / 4 != (y + h - 1) / 4 || w * h > 16)
                    throw new InvalidOperationException("A stratum crossed a face or exceeded inverse PDF two.");
                rectangles[c * rays + ray] = new[] { x, y, w, h };
                for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) occupancy[j * 8 + i]++;
                minimumArea = Math.Min(minimumArea, w * h); mass += w * h / 8f;
            }
            if (mass != 8 || occupancy.Any(n => n != 1) || ((c == 1 || c == 3) && minimumArea >= 4))
                throw new InvalidOperationException("Partition lost support, PDF mass, or fine refinement in case " + c);
            cases.Add(new { c, minimumArea, inversePdfMass = mass, rectangles = rectangles.Skip(c * rays).Take(rays).ToArray() });
        }
        var pixels = new Color[probes * rays];
        for (int probe = 0; probe < probes; probe++) for (int ray = 0; ray < rays; ray++)
        {
            var d = descriptors[(probe / 128) * rays + ray];
            pixels[probe * rays + ray] = new Color(d.x, d.y, d.z, d.w);
        }
        guide.SetPixels(pixels); guide.Apply(false, false);
        if (!guide.GetPixels().SequenceEqual(pixels)) throw new InvalidOperationException("Descriptor changed in FP16 storage.");
        cmd.SetComputeBufferParam(shader, sample, "_SdfFineProposalValidation", buffer);
        cmd.SetComputeTextureParam(shader, sample, "_SdfDirectionalGuide", guide);
        cmd.SetComputeIntParam(shader, "_SdfFineValidationProbeCount", probes);
        cmd.SetComputeIntParam(shader, "_SdfDirectionalCacheEnabled", 1);
        cmd.SetComputeIntParam(shader, "_SdfDirectionalGuideEnabled", 2);
        cmd.SetComputeIntParam(shader, "_SdfGatherHistoryDiagnostics", 8388608);
        for (int frame = 0; frame < frames; frame++)
        {
            cmd.SetComputeIntParam(shader, "_SdfDiffuseFrame", frame);
            cmd.SetComputeIntParam(shader, "_SdfFineValidationOffset", frame * probes * rays);
            cmd.DispatchCompute(shader, sample, probes / 8, 1, 1);
        }
        Graphics.ExecuteCommandBuffer(cmd); CheckShader();
        var actual = new Vector4[probes * rays * frames]; buffer.GetData(actual);
        double maxError = 0;
        var integrals = new double[4, 3];
        for (uint frame = 0; frame < frames; frame++) for (uint probe = 0; probe < probes; probe++) for (uint ray = 0; ray < rays; ray++)
        {
            uint sx, sy;
            unchecked
            {
                uint phase = frame + (Hash(probe) & 31u), index = (phase & 31u) ^ ((phase & 31u) >> 1);
                uint second = 0, direction = 0x80000000u;
                for (uint bits = index; bits != 0; bits >>= 1) { if ((bits & 1) != 0) second ^= direction; direction ^= direction >> 1; }
                uint scramble = Hash(probe) ^ (0xb5297a4du * (ray + 1)) ^ Hash((phase >> 5) ^ 0x27d4eb2du);
                sx = Reverse(index) ^ Hash(scramble); sy = second ^ Hash(scramble ^ 0x68e31da4u);
            }
            var rect = rectangles[(probe / 128) * rays + ray];
            double u = Math.Clamp((sx >> 8) / 16777216.0, 1e-5, 1 - 1e-5), v = Math.Clamp((sy >> 8) / 16777216.0, 1e-5, 1 - 1e-5);
            double px = 2 * (rect[0] + rect[2] * u) / 8 - 1, py = 2 * (rect[1] + rect[3] * v) / 8 - 1;
            bool horizontal = Math.Abs(px) > Math.Abs(py);
            double radius = horizontal ? px : py, slope = radius == 0 ? 0 : (horizontal ? py : px) / radius;
            double phi = Math.PI / 4 * (horizontal ? slope : 2 - slope);
            var expected = new[] { radius * Math.Cos(phi), radius * Math.Sin(phi), Math.Sqrt(Math.Max(0, 1 - radius * radius)) };
            var value = actual[(frame * probes + probe) * rays + ray];
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || value.z < 0 || value.w != rect[2] * rect[3] / 8f)
                throw new InvalidOperationException("Non-finite sample or incorrect PDF.");
            for (int component = 0; component < 3; component++)
            {
                double error = Math.Abs(value[component] - expected[component]); maxError = Math.Max(maxError, error);
                if (error > 2e-5) throw new InvalidOperationException("GPU/CPU sample mismatch: " + frame + "/" + probe + "/" + ray + ", error " + error);
                integrals[probe / 128, component] += value[component] * value.w / (frames * 128.0 * rays);
            }
        }
        for (int c = 0; c < 4; c++)
            if (Math.Abs(integrals[c, 0]) > .004 || Math.Abs(integrals[c, 1]) > .004 || Math.Abs(integrals[c, 2] - 2.0 / 3) > .004)
                throw new InvalidOperationException("Analytic cosine integral drift in case " + c);
        string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
            "FineDirectionalValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        System.IO.Directory.CreateDirectory(evidenceRoot);
        var report = new { evidenceRoot, samplesChecked = actual.Length, maxDirectionError = maxError, cases,
            analyticCosineIntegrals = integrals, fullSupport = true, faceSupport = true, maximumInversePdf = 2, fp16PayloadExact = true,
            shaderMessages = ShaderUtil.GetComputeShaderMessages(shader).Select(m => new { m.message, severity = m.severity.ToString() }).ToArray() };
        System.IO.File.WriteAllText(evidenceRoot + "/gpu-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    finally { cmd.Dispose(); buffer.Dispose(); UnityEngine.Object.DestroyImmediate(guide); }
}
