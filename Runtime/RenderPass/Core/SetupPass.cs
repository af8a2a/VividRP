using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class SetupPass : UnsafePass
    {
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");

        private static readonly VividLightData.DirectionalLightData[] s_EmptyDirectionalLights =
        {
            default
        };

        private GraphicsBuffer m_DirectionalLightBuffer;
        private int m_DirectionalLightCount;
        private int m_MainDirectionalLightIndex;

        public override void Prepare(ContextContainer frameData)
        {
            var lightData = frameData.GetOrCreate<VividLightData>();

            m_DirectionalLightCount = lightData.directionalLightCount;
            m_MainDirectionalLightIndex = lightData.mainDirectionalLightIndex;

            var requiredBufferCount = Mathf.Max(lightData.directionalLightCount, 1);
            EnsureDirectionalLightBuffer(requiredBufferCount);

            if (m_DirectionalLightCount > 0)
            {
                m_DirectionalLightBuffer.SetData(lightData.directionalLights, 0, 0, m_DirectionalLightCount);
                return;
            }

            m_DirectionalLightBuffer.SetData(s_EmptyDirectionalLights);
        }

        public override void Create()
        {
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DirectionalLightBuffer == null)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            cmd.SetGlobalInt(DirectionalLightCountId, m_DirectionalLightCount);
            cmd.SetGlobalInt(MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
            cmd.SetGlobalBuffer(DirectionalLightsId, m_DirectionalLightBuffer);
        }

        public override void Dispose()
        {
            m_DirectionalLightBuffer?.Dispose();
            m_DirectionalLightBuffer = null;
            m_DirectionalLightCount = 0;
            m_MainDirectionalLightIndex = -1;
        }

        private void EnsureDirectionalLightBuffer(int requiredBufferCount)
        {
            if (m_DirectionalLightBuffer != null
                && m_DirectionalLightBuffer.count >= requiredBufferCount
                && m_DirectionalLightBuffer.stride == VividLightData.DirectionalLightData.Stride)
                return;

            m_DirectionalLightBuffer?.Dispose();
            m_DirectionalLightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.DirectionalLightData.Stride);
        }
    }
}
