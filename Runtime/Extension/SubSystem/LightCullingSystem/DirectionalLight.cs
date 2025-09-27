using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class DirectionalLighting : ScriptableRenderPass
    {
        private static int g_DirectionalLightDatas = Shader.PropertyToID("g_DirectionalLightDatas");
        private static int _DirectionalLightCount = Shader.PropertyToID("_DirectionalLightCount");

        public DirectionalLighting()
        {
            m_DirectionalLightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_MaxDirectionalLightsOnScreen,
                Marshal.SizeOf(typeof(DirectionalLightData)));

            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }

        #region Resoucre

        private GraphicsBuffer m_DirectionalLightBuffer;
        private NativeArray<DirectionalLightData> m_DirectionalLightsData;
        private int m_DirectionalLightCapacity = 0;
        private int m_DirectionalLightCount = 0;
        private const int m_MaxDirectionalLightsOnScreen = 16;

        #endregion


        #region Directional Lights

        void AllocateDirectionalLightBuffer(int directionalLightCount)
        {
            int requestedDurectinalCount = Mathf.Max(1, directionalLightCount);
            if (requestedDurectinalCount > m_DirectionalLightCapacity)
            {
                m_DirectionalLightCapacity = Mathf.Max(Mathf.Max(m_DirectionalLightCapacity * 2, requestedDurectinalCount), m_MaxDirectionalLightsOnScreen);
                m_DirectionalLightsData.ResizeArray(m_DirectionalLightCapacity);
            }

            m_DirectionalLightCount = directionalLightCount;
        }


        internal void BuildGPUDirectionalLightData(in UniversalLightData lightData)
        {
            var visibleLights = lightData.visibleLights;
            int targetIndex = 0;
            for (int visLightIndex = 0; visLightIndex < visibleLights.Length; visLightIndex++)
            {
                var light = visibleLights[visLightIndex].light;
                if (visibleLights[visLightIndex].lightType == LightType.Directional)
                {
                    var additionalLightData = light.GetUniversalAdditionalLightData();

                    var directionalLightData = new DirectionalLightData();

                    Vector4 lightPos, lightColor, lightAttenuation, lightSpotDir, lightOcclusionChannel;
                    // Directional lightPos is direction
                    UniversalRenderPipeline.InitializeLightConstants_Common(visibleLights, visLightIndex, out lightPos, out lightColor, out lightAttenuation,
                        out lightSpotDir, out lightOcclusionChannel);
                    uint lightLayerMask = RenderingLayerUtils.ToValidRenderingLayers(additionalLightData.renderingLayers);

                    int lightFlags = 0;
                    if (light.bakingOutput.lightmapBakeType == LightmapBakeType.Mixed)
                        lightFlags |= (int)LightFlag.SubtractiveMixedLighting;

                    // As we said before.
                    directionalLightData.positionWS = visibleLights[visLightIndex].GetPosition();
                    directionalLightData.dir = lightPos;
                    directionalLightData.color = lightColor;
                    directionalLightData.lightAttenuation = lightAttenuation;
                    // directionalLightData.lightOcclusionProbInfo = lightOcclusionChannel;
                    directionalLightData.lightFlags = lightFlags;
                    //directionalLightData.shadowlightIndex = shadowLightIndex;
                    directionalLightData.lightLayerMask = lightLayerMask;

                    //Value of max smoothness is derived from AngularDiameter. Formula results from eyeballing. Angular diameter of 0 results in 1 and angular diameter of 80 results in 0.
                    float maxSmoothness = Mathf.Clamp01(1.35f / (1.0f + Mathf.Pow(1.15f * (0.0315f * additionalLightData.angularDiameter + 0.4f), 2f)) - 0.11f);
                    // Value of max smoothness is from artists point of view, need to convert from perceptual smoothness to roughness
                    directionalLightData.minRoughness = (1.0f - maxSmoothness) * (1.0f - maxSmoothness);
                    directionalLightData.lightDimmer = 1;
                    directionalLightData.diffuseDimmer = 1;
                    directionalLightData.specularDimmer = 1;
                    directionalLightData.volumetricLightDimmer = additionalLightData.volumetricDimmer;


                    m_DirectionalLightsData[targetIndex] = directionalLightData;
                    targetIndex += 1;
                }
            }
        }

        #endregion


        class PrepareDirectionalLightingPassData
        {
            public BufferHandle DirectionalLightBuffer;
            public int DirectionalLightCount;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lightData = frameData.Get<UniversalLightData>();
            AllocateDirectionalLightBuffer(lightData.directionalLightsCount);
            BuildGPUDirectionalLightData(lightData);
            using (var builder = renderGraph.AddUnsafePass<PrepareDirectionalLightingPassData>("Prepare DirectionalLight", out var passData))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                m_DirectionalLightBuffer.SetData(m_DirectionalLightsData);
                passData.DirectionalLightBuffer = renderGraph.ImportBuffer(m_DirectionalLightBuffer);
                passData.DirectionalLightCount = m_DirectionalLightCount;
                builder.UseBuffer(passData.DirectionalLightBuffer, AccessFlags.Write);

                builder.SetRenderFunc<PrepareDirectionalLightingPassData>((data, ctx) =>
                {
                    ctx.cmd.SetGlobalBuffer(g_DirectionalLightDatas, data.DirectionalLightBuffer);
                    ctx.cmd.SetGlobalInt(_DirectionalLightCount, data.DirectionalLightCount);
                });
            }
        }
    }
}