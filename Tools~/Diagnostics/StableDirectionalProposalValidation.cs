// GPU replay of the rejected VarianceGate prototype; apply its archived patch first.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEditor;
{
    const int count = 64;
    const string path = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeImportanceSampling.compute";
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
    if (!shader.HasKernel("InspectSdfStableDirectionalGuideCS"))
        throw new InvalidOperationException("Stable proposal prototype was withdrawn. Apply Prototypes/StableDirectional-VarianceGate.patch with --unidiff-zero in an isolated checkout to replay.");
    int kernel = shader.FindKernel("InspectSdfStableDirectionalGuideCS");
    var inputs = new Vector4[count * 12];
    var oldPixels = new Color[count * 8];
    var density = new float[count][];
    uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); } }
    int Encode(int x, int y, int w, int h) => x | (y << 2) | ((w - 1) << 4) | ((h - 1) << 5);
    for (int c = 0; c < count; c++)
    {
        density[c] = new float[16];
        for (int i = 0; i < 16; i++)
        {
            int x = i & 3, y = i >> 2;
            float value;
            switch (c % 8)
            {
                case 0: value = 1; break;
                case 1: value = y == 0 ? 200 : 1; break;
                case 2: value = x == 0 ? 200 : 1; break;
                case 3: value = 1 + .85f * x + y; break;
                case 4: value = 1 + .95f * x + y; break;
                case 5: value = (1 + .85f * x + y) * 7000; break;
                case 6: value = 1 + (Hash((uint)(c * 16 + i)) & 1023) / 100f; break;
                default: value = 0; break;
            }
            density[c][i] = value;
            inputs[c * 12 + i / 4][i & 3] = value;
        }
        for (int ray = 0; ray < 8; ray++)
        {
            // Both partitions cover 16 cells exactly once, with inverse PDF one.
            oldPixels[c * 8 + ray] = new Color(Encode(ray & 3, 2 * (ray >> 2), 1, 2), 17, 29, c % 16 == 15 ? 0 : -3);
            inputs[c * 12 + 4 + ray] = new Vector4(Encode(2 * (ray & 1), ray >> 1, 2, 1), 37, 43, -3);
        }
    }
    double Cost(float[] values, bool horizontal)
    {
        double peak = values.Max();
        if (peak == 0) return 0;
        double variance = 0;
        for (int ray = 0; ray < 8; ray++)
        {
            int x = horizontal ? 2 * (ray & 1) : ray & 3;
            int y = horizontal ? ray >> 1 : 2 * (ray >> 2);
            double a = values[y * 4 + x] / peak, b = values[y * 4 + x + (horizontal ? 1 : 4)] / peak;
            // A two-cell stratum has variance proportional to (a-b)^2.
            variance += (a - b) * (a - b);
        }
        return variance;
    }
    var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, inputs.Length, 16);
    var result = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
    var previous = new Texture2D(count * 8, 1, TextureFormat.RGBAHalf, false, true);
    var output = new RenderTexture(new RenderTextureDescriptor(count * 8, 1)
    { graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat, depthBufferBits = 0, enableRandomWrite = true, msaaSamples = 1 });
    try
    {
        input.SetData(inputs); previous.SetPixels(oldPixels); previous.Apply(false, false); output.Create();
        shader.SetBuffer(kernel, "_SdfStableValidationInput", input);
        shader.SetBuffer(kernel, "_SdfStableValidationResult", result);
        shader.SetTexture(kernel, "_SdfDirectionalProposalHistory", previous);
        shader.SetTexture(kernel, "_SdfDirectionalGuideOutput", output);
        shader.SetInt("_SdfStableValidationCount", count);
        double maxError = 0;
        var rows = new List<object>();
        for (int history = 0; history < 2; history++)
        {
            shader.SetInt("_SdfDirectionalHasProposalHistory", history);
            shader.Dispatch(kernel, count / 8, 1, 1);
            var errors = ShaderUtil.GetComputeShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
            var actual = new Vector4[count]; result.GetData(actual);
            var request = AsyncGPUReadback.Request(output); request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("Proposal output readback failed.");
            var pixels = request.GetData<ushort>();
            int retainedCount = 0, replacedCount = 0;
            for (int c = 0; c < count; c++)
            {
                bool valid = history != 0 && c % 16 != 15;
                double before = valid ? Cost(density[c], false) : 0, after = valid ? Cost(density[c], true) : 0;
                bool retain = valid && !(after < .8 * before);
                var value = actual[c];
                if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || value.z != (retain ? 1 : 0) || value.w != 1)
                    throw new InvalidOperationException("Invalid retention decision at " + history + "/" + c);
                maxError = Math.Max(maxError, Math.Max(Math.Abs(value.x - before), Math.Abs(value.y - after)));
                if (maxError > .0002) throw new InvalidOperationException("GPU/CPU variance cost mismatch.");
                if (retain) retainedCount++; else replacedCount++;
                for (int ray = 0; ray < 8; ray++) for (int component = 0; component < 4; component++)
                {
                    float expected = retain ? oldPixels[c * 8 + ray][component] : inputs[c * 12 + 4 + ray][component];
                    float observed = Unity.Mathematics.math.f16tof32(pixels[(c * 8 + ray) * 4 + component]);
                    if (observed != expected) throw new InvalidOperationException("Wrong descriptor publication.");
                }
            }
            if (history == 1 && (retainedCount == 0 || replacedCount == 0)) throw new InvalidOperationException("Both decisions were not exercised.");
            rows.Add(new { history, retainedCount, replacedCount });
        }
        string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
            "StableProposalValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        System.IO.Directory.CreateDirectory(evidenceRoot);
        var report = new { evidenceRoot, casesChecked = count * 2, descriptorsChecked = count * 2 * 8,
            maxCostError = maxError, threshold = .8, rows, invalidHistoryNeverRetained = true, publicationExact = true };
        System.IO.File.WriteAllText(evidenceRoot + "/gpu-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    finally { input.Dispose(); result.Dispose(); output.Release(); UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(previous); }
}
