// Constant covered energy must survive feedback independently of packed coverage.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
{
    var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute");
    int kernel = shader.FindKernel("InspectSdfCoveredHistoryEnergyCS");
    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 16);
    var rows = new List<object>();
    float legacyMaximumError = 0f;
    try
    {
        foreach (bool legacy in new[] {true, false})
        {
            shader.SetInt("_SdfGatherHistoryDiagnostics", legacy ? 2097152 : 0);
            shader.SetBuffer(kernel, "_SdfCoveredHistoryValidation", buffer);
            shader.Dispatch(kernel, 1, 1, 1);
            var actual = new Vector4[8]; buffer.GetData(actual);
            for (int i = 0; i < 8; i++)
            {
                var expected = i == 7 ? Vector3.zero : new Vector3(1f, 2f, .5f);
                var value = new Vector3(actual[i].x, actual[i].y, actual[i].z);
                float error = (value - expected).magnitude;
                if (!legacy && error > .0001f) throw new InvalidOperationException("Covered history lost energy at case " + i + ": " + value);
                if (actual[i].w <= 0f) throw new InvalidOperationException("Black history lost coverage.");
                if (legacy) legacyMaximumError = Mathf.Max(legacyMaximumError, error);
            }
            rows.Add(new {legacy, values = actual.Select(v => new[] {v.x, v.y, v.z, v.w}).ToArray()});
        }
        if (legacyMaximumError < .001f) throw new InvalidOperationException("Conformance did not exercise coverage feedback bias.");
        var report = new {steps = 4096, cases = 8, legacyMaximumError, rows};
        System.IO.File.WriteAllText("E:/VividRP_Reborn/Logs/PixelTemporalEnergy-20261113/gpu-conservation.json",
            Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    finally {buffer.Dispose();shader.SetInt("_SdfGatherHistoryDiagnostics", 0);}
}
