using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Raytracing-based GBuffer pass for DLSS-RR integration.
    /// Outputs GBuffer data directly in DLSS-RR native format, eliminating
    /// the need for DLSSRRResourcePrep.compute transformation pass.
    /// </summary>
    public class RayTracingGBufferPass : ScriptableRenderPass
    {
        private const string kPassName = "RT GBuffer (DLSS-RR)";
        private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler(kPassName);

        // Ray tracing shader
        private RayTracingShader m_GBufferRayTracingShader;

        // Shader property IDs
        private static readonly int s_GBufferMaterialDataId = Shader.PropertyToID("_GBufferMaterialData");
        private static readonly int s_GBufferDiffuseAlbedoId = Shader.PropertyToID("_GBufferDiffuseAlbedo");
        private static readonly int s_GBufferSpecularAlbedoId = Shader.PropertyToID("_GBufferSpecularAlbedo");
        private static readonly int s_GBufferNormalRoughnessId = Shader.PropertyToID("_GBufferNormalRoughness");
        private static readonly int s_GBufferHitDistanceId = Shader.PropertyToID("_GBufferHitDistance");
        private static readonly int s_GBufferEmissionId = Shader.PropertyToID("_GBufferEmission");
        private static readonly int s_InvViewProjMatrixId = Shader.PropertyToID("_InvViewProjMatrix");
        private static readonly int s_CameraPositionWSId = Shader.PropertyToID("_CameraPositionWS");
        private static readonly int s_ScreenSizeId = Shader.PropertyToID("_ScreenSize");

        // Current dimensions
        private int m_CurrentWidth;
        private int m_CurrentHeight;

        public RayTracingGBufferPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
        }

        #region RenderGraph Support

        /// <summary>
        /// Pass data for RenderGraph execution
        /// </summary>
        class RenderGraphPassData
        {
            internal RayTracingShader rayTracingShader;
            internal RayTracingAccelerationStructure rtas;
            // Path Tracing output
            internal TextureHandle materialData;
            // DLSS-RR outputs
            internal TextureHandle diffuseAlbedo;
            internal TextureHandle specularAlbedo;
            internal TextureHandle normalRoughness;
            internal TextureHandle hitDistance;
            internal TextureHandle emission;
            internal Matrix4x4 invViewProjMatrix;
            internal Vector3 cameraPositionWS;
            internal Vector4 screenSize;
            internal uint dispatchWidth;
            internal uint dispatchHeight;
            internal ShaderVariablesRaytracing rayTracingCB;

        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var rtas = RayTracingSystem.instance.RequestAccelerationStructure(cameraData);
            Render(renderGraph, cameraData, rtas,
                out var materialData,
                out var diffuseAlbedo,
                out var specularAlbedo,
                out var normalRoughness,
                out var hitDistance,
                out var emission);

            // Export to resource data for use by Path Tracing and DLSS-RR
            resourceData.materialData = materialData;
            resourceData.diffuseAlbedo = diffuseAlbedo;
            resourceData.specularAlbedo = specularAlbedo;
            resourceData.normalRoughness = normalRoughness;
            resourceData.hitDistance = hitDistance;
            resourceData.emission = emission;
        }

        /// <summary>
        /// Record the pass to RenderGraph
        /// </summary>
        public void Render(
            RenderGraph renderGraph,
            UniversalCameraData cameraData,
            RayTracingAccelerationStructure rtas,
            out TextureHandle materialData,
            out TextureHandle diffuseAlbedo,
            out TextureHandle specularAlbedo,
            out TextureHandle normalRoughness,
            out TextureHandle hitDistance,
            out TextureHandle emission)
        {
            if (!m_GBufferRayTracingShader)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();
                m_GBufferRayTracingShader = runtimeShader.rayTracingGBufferRTShader;
            }

            int width = cameraData.camera.pixelWidth;
            int height = cameraData.camera.pixelHeight;

            // Create texture descriptors
            var materialDataDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                name = "_RTGBufferMaterialData"
            };

            var albedoDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                name = "_RTGBufferDiffuseAlbedo"
            };

            var normalRoughnessDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                name = "_RTGBufferNormalRoughness"
            };

            var hitDistDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R32_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                name = "_RTGBufferHitDistance"
            };

            var emissionDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                name = "_RTGBufferEmission"
            };

            // Create render targets
            materialData = renderGraph.CreateTexture(materialDataDesc);
            diffuseAlbedo = renderGraph.CreateTexture(albedoDesc);
            albedoDesc.name = "_RTGBufferSpecularAlbedo";
            specularAlbedo = renderGraph.CreateTexture(albedoDesc);
            normalRoughness = renderGraph.CreateTexture(normalRoughnessDesc);
            hitDistance = renderGraph.CreateTexture(hitDistDesc);
            emission = renderGraph.CreateTexture(emissionDesc);

            using (var builder = renderGraph.AddComputePass<RenderGraphPassData>(kPassName, out var passData, s_ProfilingSampler))
            {
                // Setup pass data
                passData.rayTracingShader = m_GBufferRayTracingShader;
                passData.rtas = rtas;
                passData.materialData = materialData;
                passData.diffuseAlbedo = diffuseAlbedo;
                passData.specularAlbedo = specularAlbedo;
                passData.normalRoughness = normalRoughness;
                passData.hitDistance = hitDistance;
                passData.emission = emission;

                // Camera parameters
                Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
                Matrix4x4 projMatrix = cameraData.GetGPUProjectionMatrix(true);
                Matrix4x4 viewProj = projMatrix * viewMatrix;
                passData.invViewProjMatrix = viewProj.inverse;
                passData.cameraPositionWS = cameraData.worldSpaceCameraPos;
                passData.screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
                passData.dispatchWidth = (uint)width;
                passData.dispatchHeight = (uint)height;
                passData.rayTracingCB = RayTracingSystem.instance.GetShaderVariablesRaytracingCB(cameraData);

                // Declare texture usage
                builder.UseTexture(materialData, AccessFlags.Write);
                builder.UseTexture(diffuseAlbedo, AccessFlags.Write);
                builder.UseTexture(specularAlbedo, AccessFlags.Write);
                builder.UseTexture(normalRoughness, AccessFlags.Write);
                builder.UseTexture(hitDistance, AccessFlags.Write);
                builder.UseTexture(emission, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (RenderGraphPassData data, ComputeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    // Set shader pass
                    cmd.SetRayTracingShaderPass(data.rayTracingShader, "GBufferDXR");

                    ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                    // Set acceleration structure
                    cmd.SetRayTracingAccelerationStructure(data.rayTracingShader, Shader.PropertyToID("_RaytracingAccelerationStructure"), data.rtas);

                    // Camera parameters
                    cmd.SetRayTracingVectorParam(data.rayTracingShader, s_CameraPositionWSId, data.cameraPositionWS);
                    cmd.SetRayTracingVectorParam(data.rayTracingShader, s_ScreenSizeId, data.screenSize);

                    // Output UAVs - use RenderGraph handles
                    // Path Tracing material data
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferMaterialDataId, data.materialData);
                    // DLSS-RR outputs
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferDiffuseAlbedoId, data.diffuseAlbedo);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferSpecularAlbedoId, data.specularAlbedo);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferNormalRoughnessId, data.normalRoughness);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferHitDistanceId, data.hitDistance);
                    cmd.SetRayTracingTextureParam(data.rayTracingShader, s_GBufferEmissionId, data.emission);

                    // Dispatch rays
                    cmd.DispatchRays(data.rayTracingShader, "GBufferRayGeneration", data.dispatchWidth, data.dispatchHeight, 1);
                });
            }
        }

        #endregion
    }
}