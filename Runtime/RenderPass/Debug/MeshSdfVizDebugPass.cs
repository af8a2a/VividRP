using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class MeshSdfVizDebugPass : RasterPass
    {
        private static readonly int DistanceFieldId = Shader.PropertyToID("_SdfDistanceField");
        private static readonly int SampleOriginId = Shader.PropertyToID("_SdfSampleOrigin");
        private static readonly int GridId = Shader.PropertyToID("_SdfGrid");
        private static readonly int LocalToClipId = Shader.PropertyToID("_SdfLocalToClip");
        private static readonly int ClipToLocalId = Shader.PropertyToID("_SdfClipToLocal");
        private static readonly int ScreenRectId = Shader.PropertyToID("_SdfScreenRect");

        private const VividMeshletRendererFlags VisibleFlags =
            VividMeshletRendererFlags.ActiveInHierarchy
            | VividMeshletRendererFlags.Enabled
            | VividMeshletRendererFlags.SourceRendererEnabled;

        [RenderGraphResource(Name = "OutputTexture", Access = AccessFlags.Write, AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [TransientResource]
        [RenderGraphResource(Name = "SdfDepth", Access = AccessFlags.Write, IsDepthAttachment = true)]
        private RenderGraphTexture m_Depth;

        private Material m_Material;
        private Matrix4x4 m_ViewProjection;
        private Matrix4x4 m_InverseViewProjection;
        private uint m_CullingMask;

        public MeshSdfVizDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(MeshSdfVizDebugPass));
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("MeshSdfViz", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_Depth = RenderGraphTexture.CreateDepthTarget("MeshSdfVizDepth");
        }

        public override void Create()
        {
            var shader = PipelineResourceManager.Get<VividRPCoreResources>()?.MeshSdfVizDebugShader;
            if (shader != null && shader.isSupported)
                m_Material = CoreUtils.CreateEngineMaterial(shader);
            else
                Debug.LogWarning("[VividRP] Mesh SDF visualization shader is missing or unsupported.");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            int width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            int height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_OutputTexture.Resize(width, height);
            m_Depth.Resize(width, height);

            var camera = cameraData.camera;
            m_CullingMask = camera == null || DebugPassCameraUtility.ShouldSkipExecution(camera)
                ? 0u
                : unchecked((uint)camera.cullingMask);
            m_ViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            m_InverseViewProjection = m_ViewProjection.inverse;
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Material == null || m_CullingMask == 0u)
                return;

            var database = VividMeshletRendererDatabase.instance;
            var rendererData = database.rendererData;
            var rendererResources = database.rendererResources;
            var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
            for (int i = 0; i < rendererData.Count; i++)
            {
                var data = rendererData[i];
                if ((data.flags & VisibleFlags) != VisibleFlags || (data.cameraLayerMask & m_CullingMask) == 0)
                    continue;

                var renderer = rendererResources[i].MeshletRenderer;
                var sdf = renderer != null ? renderer.meshSdf : null;
                if (sdf == null || !(sdf.VoxelSize > 0f) || data.objectToWorldMatrix.determinant == 0f)
                    continue;

                var field = sdf.DistanceField;
                var origin = sdf.SampleOrigin;
                var size = new Vector3(field.width - 1, field.height - 1, field.depth - 1) * sdf.VoxelSize;
                var localToClip = m_ViewProjection * data.objectToWorldMatrix;
                if (!TryGetScreenRect(localToClip, origin, origin + size, out var screenRect))
                    continue;

                properties.SetTexture(DistanceFieldId, field);
                properties.SetVector(SampleOriginId, new Vector4(origin.x, origin.y, origin.z,
                    sdf.SignMode == VividMeshSdfSignMode.ThinShell ? 1f : 0f));
                properties.SetVector(GridId, new Vector4(field.width, field.height, field.depth, sdf.VoxelSize));
                properties.SetMatrix(LocalToClipId, localToClip);
                properties.SetMatrix(ClipToLocalId, data.worldToObjectMatrix * m_InverseViewProjection);
                properties.SetVector(ScreenRectId, screenRect);
                context.cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 6, 1, properties);
            }
        }

        public override void Dispose()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }

        private static bool TryGetScreenRect(Matrix4x4 localToClip, Vector3 min, Vector3 max, out Vector4 rect)
        {
            var minNdc = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maxNdc = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            int frontCorners = 0;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector4(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z,
                    1f);
                var clip = localToClip * corner;
                if (clip.w <= 0f)
                    continue;

                frontCorners++;
                var ndc = new Vector2(clip.x, clip.y) / clip.w;
                minNdc = Vector2.Min(minNdc, ndc);
                maxNdc = Vector2.Max(maxNdc, ndc);
            }

            rect = default;
            if (frontCorners == 0)
                return false;

            // A box crossing the eye plane can project outside the bounds of its front corners.
            if (frontCorners != 8)
            {
                rect = new Vector4(-1f, -1f, 1f, 1f);
                return true;
            }

            minNdc = Vector2.Max(minNdc, -Vector2.one);
            maxNdc = Vector2.Min(maxNdc, Vector2.one);
            rect = new Vector4(minNdc.x, minNdc.y, maxNdc.x, maxNdc.y);
            return maxNdc.x > minNdc.x && maxNdc.y > minNdc.y;
        }
    }
}
