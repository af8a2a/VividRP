using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class MeshletShadowPassTests
    {
        [Test]
        public void MeshletShadowPass_UsesMainCameraLODContext_ForCascadeCulling()
        {
            string source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "MeshletShadowPass.cs"));

            Assert.That(source, Does.Contain("private Camera m_LODCamera;"));
            Assert.That(source, Does.Contain("m_LODCamera = cameraData.camera;"));
            Assert.That(source, Does.Contain("VividGPUDrivenCullingContextUtility.BuildLODSelectionContext("));
            Assert.That(source, Does.Contain("m_LODCamera,"));
            Assert.That(source, Does.Contain("BuildShadowCullingContext(cascadeIndex, out var cullingContext);"));
            Assert.That(source, Does.Contain("lodContext,"));
            Assert.That(source, Does.Contain("out _);"));
        }

        [Test]
        public void MeshletShadowPass_BindsShadowCasterState_ForMeshletDraws()
        {
            string meshletSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "MeshletShadowPass.cs"));
            string csmSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "CSMShadowPass.cs"));

            Assert.That(meshletSource, Does.Contain("private static readonly int ShadowBiasId = Shader.PropertyToID(\"_ShadowBias\");"));
            Assert.That(meshletSource, Does.Contain("private Vector4 m_ShadowCasterState;"));
            Assert.That(meshletSource, Does.Contain("m_ShadowCasterState = CSMShadowPass.BuildShadowCasterState(lightData.mainVisibleLight);"));
            Assert.That(meshletSource, Does.Contain("nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowCasterState);"));
            Assert.That(csmSource, Does.Contain("internal static Vector4 BuildShadowCasterState(in VisibleLight shadowLight)"));
        }

        [Test]
        public void MeshletShadowPass_UsesCascadeReceiverSphere_ForShadowCulling()
        {
            string source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "MeshletShadowPass.cs"));

            Assert.That(source, Does.Contain("var cullingSphereWS = m_ShadowData.cascadeSpheres[cascadeIndex];"));
            Assert.That(source, Does.Contain("cullingSphereWS.w = Mathf.Sqrt(Mathf.Max(0.0f, cullingSphereWS.w));"));
            Assert.That(source, Does.Contain("cullingSphereWS,"));
        }

        [Test]
        public void MeshletShadowPass_BindsCascadeRequestBufferPerDrawWithoutMutatingSharedMaterials()
        {
            string source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "MeshletShadowPass.cs"));

            Assert.That(source, Does.Contain("private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();"));
            Assert.That(source, Does.Contain("m_DrawProperties.Clear();"));
            Assert.That(source, Does.Contain("m_DrawProperties.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);"));
            Assert.That(source, Does.Contain("m_DrawProperties.SetBuffer(s_UnityIndirectDrawArgsId, argsBuffer);"));
            Assert.That(source, Does.Contain("m_DrawProperties.SetInteger(s_UnityBaseCommandIdId, rendererListIndex);"));
            Assert.That(source, Does.Contain("m_DrawProperties);"));
            Assert.That(source, Does.Not.Contain("material.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);"));
        }

        [Test]
        public void MeshletShadowCasterShader_ClampsDirectionalShadowDepthLikeStandardCaster()
        {
            string source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "GPUDriven", "VisibilityBufferShadowCasterPass.shader"));

            Assert.That(source, Does.Contain("float4 _ShadowBias;"));
            Assert.That(source, Does.Contain("float4 ApplyVividShadowClamping(float4 positionCS)"));
            Assert.That(source, Does.Contain("UNITY_REVERSED_Z"));
            Assert.That(source, Does.Contain("round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0"));
            Assert.That(source, Does.Contain("output.positionCS = ApplyVividShadowClamping(output.positionCS);"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
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
