using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingOpenPBRNEETests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CombinedEvaluation_MatchesIndependentBsdfAndPdf(bool baseOnly)
        {
            Assume.That(SystemInfo.supportsRayTracing, Is.True);
            var shader = AssetDatabase.LoadAssetAtPath<RayTracingShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/GlobalIllumination/ReferencedPathtracing/ReferencedPathTracingOpenPBRNEETests.raytrace");
            Assert.That(shader, Is.Not.Null);
            const int caseCount = 24 * 1024;
            using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, caseCount * 2, 16);
            using var command = new CommandBuffer { name = "OpenPBR NEE equivalence" };
            var reference = new Vector4[caseCount * 2];
            var combined = new Vector4[caseCount * 2];
            var cleared = new Vector4[caseCount * 2];
            var keyword = new LocalKeyword(shader, "VIVIDRP_NEE_BASE_ONLY");
            try
            {
                results.SetData(cleared);
                command.SetKeyword(shader, keyword, baseOnly);
                command.SetRayTracingBufferParam(shader, "_Results", results);
                command.DispatchRays(shader, "EvaluateReference", caseCount, 1, 1);
                Graphics.ExecuteCommandBuffer(command);
                results.GetData(reference);
                command.Clear();
                results.SetData(cleared);
                command.SetKeyword(shader, keyword, baseOnly);
                command.SetRayTracingBufferParam(shader, "_Results", results);
                command.DispatchRays(shader, "EvaluateCombined", caseCount, 1, 1);
                Graphics.ExecuteCommandBuffer(command);
                results.GetData(combined);

                for (int row = 0; row < reference.Length; row++)
                for (int channel = 0; channel < 4; channel++)
                {
                    float expected = reference[row][channel];
                    float actual = combined[row][channel];
                    string context = $"BaseOnly={baseOnly}: case {row / 2}, output {row % 2}, channel {channel}";
                    Assert.That(float.IsNaN(expected) || float.IsInfinity(expected), Is.False, context);
                    Assert.That(float.IsNaN(actual) || float.IsInfinity(actual), Is.False, context);
                    if (row % 2 == 0 && channel == 3)
                    {
                        Assert.That(expected, Is.EqualTo(row / 2 + 1), "Reference dispatch sentinel: " + context);
                        Assert.That(actual, Is.EqualTo(row / 2 + 1), "Combined dispatch sentinel: " + context);
                    }
                    else
                    {
                        Assert.That(actual, Is.EqualTo(expected).Within(1e-5f + 1e-4f * Mathf.Abs(expected)), context);
                        if (row % 2 == 1 && channel == 3)
                            Assert.That(actual == 0.0f, Is.EqualTo(expected == 0.0f), "Zero PDF: " + context);
                    }
                }
            }
            finally
            {
                shader.SetKeyword(keyword, false);
            }
        }
    }
}
