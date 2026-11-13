// Live GPU conformance, not a Unity Test Framework test. No scene changes.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
{
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    const string root = "Packages/com.vivid.render-pipelines/";
    AssetDatabase.ImportAsset(root + "Shaders/Core/Private/GlobalIllumination/SdfScreenProbeTracing.compute", ImportAssetOptions.ForceUpdate);
    var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(root + "Editor/Resources/SdfTemporalSamplingValidation.compute");
    int kernel = shader.FindKernel("ValidateSdfTemporalSampling");
    uint Hash(uint v) { unchecked { v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); } }
    uint Reverse(uint v) {
        uint r = 0; for (int i = 0; i < 32; i++) { r = (r << 1) | (v & 1); v >>= 1; } return r;
    }
    var values = new Dictionary<string, Vector2[]>();
    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256 * 8, 8);
    var cmd = new CommandBuffer {name = "Validate native probe temporal samples"};
    try
    {
        foreach (bool legacy in new[] {true, false})
        {
            cmd.Clear();
            cmd.SetComputeBufferParam(shader, kernel, "_TemporalValidation", buffer);
            cmd.SetComputeIntParam(shader, "_SdfGatherHistoryDiagnostics", legacy ? 0 : 262144);
            for (int frame = 0; frame < 256; frame++)
            {
                cmd.SetComputeIntParam(shader, "_SdfDiffuseFrame", frame);
                cmd.SetComputeIntParam(shader, "_TemporalValidationOffset", frame * 8);
                cmd.DispatchCompute(shader, kernel, 1, 1, 1);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            var data = new Vector2[256 * 8]; buffer.GetData(data);
            for (uint frame = 0; frame < 256; frame++) for (uint ray = 0; ray < 8; ray++)
            {
                uint sx, sy;
                unchecked
                {
                    if (legacy)
                    {
                        uint seed = Hash(3u + 7u * 65537u + frame * 0x9e3779b9u) ^ (0xb5297a4du * (ray+1));
                        sx = Hash(seed); sy = Hash(seed ^ 0x68e31da4u);
                    }
                    else
                    {
                        uint index = frame ^ (frame >> 1), second = 0, direction = 0x80000000u;
                        for (uint bits = index; bits != 0; bits >>= 1) { if ((bits & 1) != 0) second ^= direction; direction ^= direction >> 1; }
                        uint scramble = Hash(3u + 7u * 65537u) ^ (0xb5297a4du * (ray+1));
                        sx = Reverse(index) ^ Hash(scramble); sy = second ^ Hash(scramble ^ 0x68e31da4u);
                    }
                }
                var expected = new Vector2((sx >> 8) * (1f/16777216f), (sy >> 8) * (1f/16777216f));
                if (data[frame*8+ray].x != expected.x || data[frame*8+ray].y != expected.y)
                    throw new InvalidOperationException("GPU sample mismatch at " + legacy + "/" + frame + "/" + ray);
            }
            values[legacy ? "white" : "sobol"] = data;
        }
        var cells = new bool[256];
        for (int ray = 0; ray < 8; ray++)
        {
            Array.Clear(cells, 0, cells.Length);
            for (int frame = 0; frame < 256; frame++)
            {
                var uv = values["sobol"][frame*8+ray];
                int cell = (int)(uv.x * 16) + 16*(int)(uv.y * 16);
                if (cells[cell]) throw new InvalidOperationException("Sobol joint strata repeated.");
                cells[cell] = true;
            }
        }
        if (values["white"].SequenceEqual(values["sobol"])) throw new InvalidOperationException("A/B control has no effect.");
        var report = new {gpuSamplesChecked = 4096, cpuReference = "iterative Sobol direction numbers", joint16x16Strata = true,
            whiteSecond = values["white"][8].ToString("R"), sobolSecond = values["sobol"][8].ToString("R"),
            shaderMessages = ShaderUtil.GetComputeShaderMessages(shader).Select(m => new {m.message, severity=m.severity.ToString()}).ToArray()};
        System.IO.File.WriteAllText("E:/VividRP_Reborn/Logs/TemporalSampling-20261113/gpu-validation.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    finally {cmd.Dispose();buffer.Dispose();}
}
