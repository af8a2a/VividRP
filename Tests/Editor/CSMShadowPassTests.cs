using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class CSMShadowPassTests
    {
        [Test]
        public void CSMShadowPass_PushesRedirectedShaderGlobalsForEachCascadeAndRestoresCameraGlobals()
        {
            var source = File.ReadAllText(GetPassSourcePath());

            Assert.That(source, Does.Contain("m_ShadowAtlas.desc.IsShadowMap = true;"));
            Assert.That(source, Does.Contain("BatchCullingProjectionType.Orthographic"));
            Assert.That(source, Does.Contain("settings.splitIndex = i;"));
            Assert.That(source, Does.Contain("var gpuProjMatrix = GL.GetGPUProjectionMatrix(m_ProjMatrices[cascadeIndex], true);"));
            Assert.That(source, Does.Contain("var cascadeShaderGlobals = BuildCascadeShaderGlobals(cascadeIndex, gpuProjMatrix);"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(nativeCmd, cascadeShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);"));
            Assert.That(source, Does.Contain("shadowData.viewProjMatrices[i] = BuildWorldToShadowMatrix(m_ProjMatrices[i], m_ViewMatrices[i]);"));
            Assert.That(source, Does.Contain("private static Matrix4x4 BuildWorldToShadowMatrix(Matrix4x4 projMatrix, Matrix4x4 viewMatrix)"));
            Assert.That(source, Does.Contain("textureScaleAndBias.m22 = 0.5f;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividMatrixVP = viewProjMatrix;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividWorldToCamera = viewMatrix;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividGlstateMatrixProjection = gpuProjMatrix;"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(nativeCmd, m_CameraShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);"));
            Assert.That(source, Does.Not.Contain("SetViewProjectionMatrices("));
        }

        private static string GetPassSourcePath()
        {
            var passPath = GetPackageFilePath("Runtime", "RenderPass", "Core", "CSMShadowPass.cs");

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
