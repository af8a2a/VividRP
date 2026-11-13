// Live GPU conformance, not a Unity Test Framework test. No scene changes.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
{
    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    const string root = "Packages/com.vivid.render-pipelines/";
    if (typeof(VividRP.Runtime.RenderPass.Core.SdfDiffuseGatherPass).GetField("ProposalHistoryId",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic) == null)
        throw new InvalidOperationException("Wait for the sampling proposal assembly reload first.");
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
        foreach (string mode in new[] {"white", "sobol", "blocked"})
        {
            cmd.Clear();
            cmd.SetComputeBufferParam(shader, kernel, "_TemporalValidation", buffer);
            cmd.SetComputeIntParam(shader, "_SdfGatherHistoryDiagnostics", mode == "white" ? 0 : mode == "sobol" ? 262144 : 8388608);
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
                    if (mode == "white")
                    {
                        uint seed = Hash(3u + 7u * 65537u + frame * 0x9e3779b9u) ^ (0xb5297a4du * (ray+1));
                        sx = Hash(seed); sy = Hash(seed ^ 0x68e31da4u);
                    }
                    else
                    {
                        uint proposalFrame = frame + (Hash(3u + 7u * 65537u) & 31u);
                        uint sampleFrame = mode == "blocked" ? proposalFrame & 31u : frame;
                        uint index = sampleFrame ^ (sampleFrame >> 1), second = 0, direction = 0x80000000u;
                        for (uint bits = index; bits != 0; bits >>= 1) { if ((bits & 1) != 0) second ^= direction; direction ^= direction >> 1; }
                        uint scramble = Hash(3u + 7u * 65537u) ^ (0xb5297a4du * (ray+1));
                        if (mode == "blocked") scramble ^= Hash((proposalFrame >> 5) ^ 0x27d4eb2du);
                        sx = Reverse(index) ^ Hash(scramble); sy = second ^ Hash(scramble ^ 0x68e31da4u);
                    }
                }
                var expected = new Vector2((sx >> 8) * (1f/16777216f), (sy >> 8) * (1f/16777216f));
                if (data[frame*8+ray].x != expected.x || data[frame*8+ray].y != expected.y)
                    throw new InvalidOperationException("GPU sample mismatch at " + mode + "/" + frame + "/" + ray);
            }
            values[mode] = data;
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
        int phase = (int)(Hash(3u + 7u * 65537u) & 31u);
        for (int start = (32 - phase) & 31; start + 32 <= 256; start += 32) for (int ray = 0; ray < 8; ray++)
        {
            Array.Clear(cells, 0, cells.Length);
            for (int frame = start; frame < start + 32; frame++)
            {
                var uv = values["blocked"][frame * 8 + ray];
                int cell = (int)(uv.x * 8) + 8 * (int)(uv.y * 4);
                if (cells[cell]) throw new InvalidOperationException("Blocked Sobol joint strata repeated.");
                cells[cell] = true;
            }
        }
        if (values["blocked"].Take(256).SequenceEqual(values["blocked"].Skip(256).Take(256)))
            throw new InvalidOperationException("Block digital shifts repeat.");
        string evidenceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..",
            "VividRP_GI_Evidence", "BlockedTemporalValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        System.IO.Directory.CreateDirectory(evidenceRoot);
        var report = new {evidenceRoot, gpuSamplesChecked = 6144, cpuReference = "iterative Sobol direction numbers", joint16x16Strata = true, blockedJoint8x4Strata = true,
            whiteSecond = values["white"][8].ToString("R"), sobolSecond = values["sobol"][8].ToString("R"),
            shaderMessages = ShaderUtil.GetComputeShaderMessages(shader).Select(m => new {m.message, severity=m.severity.ToString()}).ToArray()};
        System.IO.File.WriteAllText(evidenceRoot + "/gpu-validation.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    finally {cmd.Dispose();buffer.Dispose();}
}
