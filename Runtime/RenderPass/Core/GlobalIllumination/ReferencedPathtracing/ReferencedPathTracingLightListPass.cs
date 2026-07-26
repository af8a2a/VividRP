using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Builds the deterministic, camera-independent light inventory used by the
    /// reference path tracer. Sampling and visibility are integrated separately.
    /// </summary>
    public sealed class ReferencedPathTracingLightListPass : ComputePass
    {
        private static readonly ReferencedPathTracingLightRecord[]
            s_EmptyLightUpload = new ReferencedPathTracingLightRecord[1];

        [RenderGraphResource(Name = "ReferenceLightList", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReferenceLightList;

        [RenderGraphResource(
            Name = "ReferenceLightListParameters",
            Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReferenceLightListParameters;

        private ReferencedPathTracingLightListBuildResult m_BuildResult;

        public ReferencedPathTracingLightListPass()
        {
            profilingSampler =
                new ProfilingSampler(nameof(ReferencedPathTracingLightListPass));
            m_ReferenceLightList = RenderGraphBuffer.CreateStructured(
                "ReferenceLightList",
                1,
                ReferencedPathTracingLightRecord.Stride);
            m_ReferenceLightListParameters = RenderGraphBuffer.CreateStructured(
                "ReferenceLightListParameters",
                ReferencedPathTracingLightSpatialIndexBuilder
                    .EmptyStorageBlockCount,
                ReferencedPathTracingLightListStorageBlock.Stride);
        }

        public override void Create()
        {
            VividSceneLightSystem.EnsureInitialized();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var lightDatabase = VividLightRenderDatabase.instance;
            lightDatabase.CompleteSceneLightPrepare();
            m_BuildResult =
                ReferencedPathTracingLightListBuilder.Build(
                    lightDatabase.sceneLightData);

            ConfigureBuffer(
                m_ReferenceLightList,
                Mathf.Max(m_BuildResult.records.Length, 1),
                ReferencedPathTracingLightRecord.Stride,
                "ReferenceLightList");
            ConfigureBuffer(
                m_ReferenceLightListParameters,
                Mathf.Max(m_BuildResult.storageBlocks.Length, 1),
                ReferencedPathTracingLightListStorageBlock.Stride,
                "ReferenceLightListParameters");
            m_ReferenceLightList.EnsureImportedBuffer();
            m_ReferenceLightListParameters.EnsureImportedBuffer();
            m_ReferenceLightList.SetData(
                m_BuildResult.records.Length > 0
                    ? m_BuildResult.records
                    : s_EmptyLightUpload);
            m_ReferenceLightListParameters.SetData(
                m_BuildResult.storageBlocks);
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
            m_ReferenceLightList?.ClearImportedBuffer();
            m_ReferenceLightListParameters?.ClearImportedBuffer();
            m_BuildResult = default;
        }

        private static void ConfigureBuffer(
            RenderGraphBuffer buffer,
            int count,
            int stride,
            string name)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(count, 1);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = name;
        }
    }
}
