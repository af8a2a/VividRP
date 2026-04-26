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
            Assert.That(source, Does.Contain("TryResolveVisibleMainDirectionalLight(lightData, out var light, out var additionalLightData)"));
            Assert.That(source, Does.Not.Contain("DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData"));
            Assert.That(source, Does.Not.Contain("FindObjectsByType<Light>"));
            Assert.That(source, Does.Contain("m_AtlasResolution = Mathf.Max(AtlasGridSize, additionalLightData.resolvedShadowAtlasResolution);"));
            Assert.That(source, Does.Contain("m_CascadeResolution = Mathf.Max(1, m_AtlasResolution / AtlasGridSize);"));
            Assert.That(source, Does.Not.Contain("m_DepthBias = Mathf.Max(0.0f, additionalLightData.depthBias);"));
            Assert.That(source, Does.Contain("m_NormalBias = Mathf.Max(0.0f, additionalLightData.normalBias);"));
            Assert.That(source, Does.Contain("m_SlopeScaleDepthBias = Mathf.Max(0.0f, additionalLightData.slopeBias);"));
            Assert.That(source, Does.Contain("var cascadeBorders = csmSettings.GetCascadeBorderRatios();"));
            Assert.That(source, Does.Contain("private const float CascadeBlendCullingFactor = 0.6f;"));
            Assert.That(source, Does.Contain("m_SplitData[i].shadowCascadeBlendCullingFactor = CascadeBlendCullingFactor;"));
            Assert.That(source, Does.Contain("StabilizeCascadeProjection(ref m_ProjMatrices[i], m_ViewMatrices[i], m_CascadeResolution);"));
            Assert.That(source, Does.Contain("m_CascadeWorldTexelSizes[i] = ComputeCascadeWorldTexelSize(m_ProjMatrices[i], m_CascadeResolution);"));
            Assert.That(source, Does.Contain("m_CascadeBorders[i] = cascadeBorders[i];"));
            Assert.That(source, Does.Contain("m_ShadowCasterBiases[i] = BuildShadowCasterState(mainVisibleLight);"));
            Assert.That(source, Does.Contain("m_CameraShaderGlobals = ResolveCameraShaderGlobals(frameData, cameraData);"));
            Assert.That(source, Does.Contain("var cameraShaderData = frameData.Get<VividCameraShaderData>();"));
            Assert.That(source, Does.Not.Contain("m_CameraShaderGlobals = ShaderVariablesGlobal.Create(cameraData.BuildShaderVariables"));
            Assert.That(source, Does.Contain("var gpuProjMatrix = GL.GetGPUProjectionMatrix(m_ProjMatrices[cascadeIndex], true);"));
            Assert.That(source, Does.Contain("var cascadeShaderGlobals = BuildCascadeShaderGlobals(cascadeIndex, gpuProjMatrix);"));
            Assert.That(source, Does.Contain("nativeCmd.SetViewProjectionMatrices(m_ViewMatrices[cascadeIndex], m_ProjMatrices[cascadeIndex]);"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(nativeCmd, cascadeShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);"));
            Assert.That(source, Does.Contain("nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);"));
            Assert.That(source, Does.Contain("nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowCasterBiases[cascadeIndex]);"));
            Assert.That(source, Does.Not.Contain("nativeCmd.SetGlobalVector(LightDirectionId, m_ShadowLightDirection);"));
            Assert.That(source, Does.Not.Contain("nativeCmd.SetGlobalVector(LightPositionId, m_ShadowLightPosition);"));
            Assert.That(source, Does.Contain("shadowData.viewProjMatrices[i] = BuildWorldToShadowMatrix(m_ProjMatrices[i], m_ViewMatrices[i]);"));
            Assert.That(source, Does.Contain("shadowData.cascadeWorldTexelSizes[i] = m_CascadeWorldTexelSizes[i];"));
            Assert.That(source, Does.Contain("shadowData.cascadeBorders[i] = m_CascadeBorders[i];"));
            Assert.That(source, Does.Contain("private static Vector4 BuildShadowCasterState(in VisibleLight shadowLight)"));
            Assert.That(source, Does.Contain("return new Vector4("));
            Assert.That(source, Does.Contain("0.0f,"));
            Assert.That(source, Does.Contain("private static Matrix4x4 BuildWorldToShadowMatrix(Matrix4x4 projMatrix, Matrix4x4 viewMatrix)"));
            Assert.That(source, Does.Contain("private static void StabilizeCascadeProjection(ref Matrix4x4 projMatrix, Matrix4x4 viewMatrix, float cascadeResolution)"));
            Assert.That(source, Does.Contain("projMatrix.m03 -= offsetX;"));
            Assert.That(source, Does.Contain("private static float ComputeCascadeWorldTexelSize(Matrix4x4 lightProjectionMatrix, float shadowResolution)"));
            Assert.That(source, Does.Contain("textureScaleAndBias.m22 = 0.5f;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividMatrixVP = viewProjMatrix;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividWorldToCamera = viewMatrix;"));
            Assert.That(source, Does.Contain("shadowGlobals._VividGlstateMatrixProjection = gpuProjMatrix;"));
            Assert.That(source, Does.Contain("private static bool TryResolveVisibleMainDirectionalLight("));
            Assert.That(source, Does.Contain("light = lightData.visibleLights[lightData.mainLightIndex].light;"));
            Assert.That(source, Does.Contain("!light.GetEntityId().Equals(lightData.mainDirectionalLightEntityId)"));
            Assert.That(source, Does.Contain("nativeCmd.SetViewProjectionMatrices("));
            Assert.That(source, Does.Contain("m_CameraShaderGlobals._VividWorldToCamera,"));
            Assert.That(source, Does.Contain("m_CameraShaderGlobals._VividCameraProjection);"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(nativeCmd, m_CameraShaderGlobals, ShaderVariablesGlobal.ConstantBufferShaderId);"));
        }

        [Test]
        public void FrameContextSystem_CachesShaderGlobalsForPrepareConsumers()
        {
            var source = File.ReadAllText(GetFrameContextSystemSourcePath());

            Assert.That(source, Does.Contain("internal sealed class VividCameraShaderData : ContextItem"));
            Assert.That(source, Does.Contain("public ShaderVariablesGlobal shaderVariablesGlobal;"));
            Assert.That(source, Does.Contain("public bool hasShaderVariablesGlobal;"));
            Assert.That(source, Does.Contain("SetShaderGlobals(cmd, frameData, cameraData, shaderVariables, temporalData, skyData);"));
            Assert.That(source, Does.Contain("var cameraShaderData = frameData.GetOrCreate<VividCameraShaderData>();"));
            Assert.That(source, Does.Contain("cameraShaderData.shaderVariablesGlobal = shaderVariablesGlobal;"));
            Assert.That(source, Does.Contain("cameraShaderData.hasShaderVariablesGlobal = true;"));
        }

        private static string GetPassSourcePath()
        {
            var passPath = GetPackageFilePath("Runtime", "RenderPass", "Core", "CSMShadowPass.cs");

            Assert.That(File.Exists(passPath), Is.True, $"Expected pass source at '{passPath}'.");
            return passPath;
        }

        private static string GetFrameContextSystemSourcePath()
        {
            var sourcePath = GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected frame context source at '{sourcePath}'.");
            return sourcePath;
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
