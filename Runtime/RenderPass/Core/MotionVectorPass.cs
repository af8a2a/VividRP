using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class MotionVectorPass : RasterPass
    {
        internal const string CameraMotionVectorsShaderName = "Hidden/VividRP/CameraMotionVectors";
        internal const string ObjectMotionVectorFallbackShaderName = "Hidden/VividRP/ObjectMotionVectorFallback";

        private static readonly string[] s_DefaultShaderTagNames =
        {
            "VividGBuffer",
            RenderGraphRenderListDesc.ForwardShaderTagName,
            RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
        };

        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepthTexture;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_MotionVectorTexture;

        [RenderGraphResource(Name = "MotionVectorDepth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_MotionVectorDepthTexture;

        private Material m_CameraMotionMaterial;
        private Material m_ObjectMotionVectorMaterial;
        private Camera m_Camera;
        private MotionVectorsPersistentData m_PersistentData;

        public MotionVectorPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = (string[])s_DefaultShaderTagNames.Clone(),
                    RenderQueueRange = RenderGraphRenderQueueRange.Opaque,
                    SortingCriteria = SortingCriteria.CommonOpaque,
                    RendererConfiguration = PerObjectData.MotionVectors,
                }
            };

            m_CameraDepthTexture = CreateInputDepthTexture("CameraDepth");
            m_MotionVectorTexture = CreateMotionVectorTexture("MotionVectors");
            m_MotionVectorDepthTexture = CreateDepthOutputTexture("MotionVectorDepth");
        }

        public override void Create()
        {
            EnsureMaterialsCreated();
        }

        public override void Prepare(ContextContainer frameData)
        {
            EnsureMaterialsCreated();

            var cameraData = frameData.Get<VividCameraData>();
            m_Camera = cameraData.camera;

            if (m_Camera != null)
            {
                m_Camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                m_PersistentData = MotionVectorsPersistentDataRegistry.GetOrCreate(m_Camera);
                m_PersistentData.Update(cameraData);
            }
            else
            {
                m_PersistentData = null;
            }

            ConfigureRenderList();
            ConfigureTargets(cameraData);
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Camera == null || m_Camera.cameraType == CameraType.Preview)
                return;

            if (!m_CameraDepthTexture.innerHandle.IsValid()
                || !m_MotionVectorTexture.innerHandle.IsValid()
                || !m_MotionVectorDepthTexture.innerHandle.IsValid())
            {
                return;
            }

            m_PersistentData?.SetGlobalMotionMatrices(context.cmd);

            if (m_CameraMotionMaterial != null)
            {
                m_CameraMotionMaterial.SetTexture(CameraDepthTextureId, m_CameraDepthTexture.innerHandle);
                context.cmd.DrawProcedural(Matrix4x4.identity, m_CameraMotionMaterial, 0, MeshTopology.Triangles, 3, 1);
            }

            if (m_ObjectMotionVectorMaterial != null && m_RenderList != null && m_RenderList.IsValid)
                context.cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
            if (m_CameraMotionMaterial != null)
            {
                CoreUtils.Destroy(m_CameraMotionMaterial);
                m_CameraMotionMaterial = null;
            }

            if (m_ObjectMotionVectorMaterial != null)
            {
                CoreUtils.Destroy(m_ObjectMotionVectorMaterial);
                m_ObjectMotionVectorMaterial = null;
            }

            m_Camera = null;
            m_PersistentData = null;
        }

        private void EnsureMaterialsCreated()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            if (m_CameraMotionMaterial == null)
            {
                var cameraShader = resources?.CameraMotionVectorsShader;
                cameraShader ??= Shader.Find(CameraMotionVectorsShaderName);
                if (cameraShader != null)
                    m_CameraMotionMaterial = CoreUtils.CreateEngineMaterial(cameraShader);
            }

            if (m_ObjectMotionVectorMaterial == null)
            {
                var objectShader = resources?.ObjectMotionVectorFallbackShader;
                objectShader ??= Shader.Find(ObjectMotionVectorFallbackShaderName);
                if (objectShader != null)
                    m_ObjectMotionVectorMaterial = CoreUtils.CreateEngineMaterial(objectShader);
            }
        }

        private void ConfigureRenderList()
        {
            m_RenderList ??= new RenderGraphRenderList();
            m_RenderList.desc ??= new RenderGraphRenderListDesc();

            if (m_RenderList.desc.ShaderTagNames == null || m_RenderList.desc.ShaderTagNames.Length == 0)
                m_RenderList.desc.ShaderTagNames = (string[])s_DefaultShaderTagNames.Clone();

            m_RenderList.desc.RendererConfiguration |= PerObjectData.MotionVectors;
            m_RenderList.desc.ExcludeObjectMotionVectors = false;
            m_RenderList.desc.OverrideMaterial = m_ObjectMotionVectorMaterial;
            m_RenderList.desc.OverrideMaterialPassIndex = 0;
        }

        private void ConfigureTargets(VividCameraData cameraData)
        {
            var sourceDescriptor = m_CameraDepthTexture?.desc;
            var hasExplicitSourceSize = HasExplicitSize(sourceDescriptor);

            var width = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Width)
                : ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            var height = hasExplicitSourceSize
                ? Mathf.Max(1, sourceDescriptor.Height)
                : ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);

            ConfigureMotionVectorTexture(width, height, sourceDescriptor);
            ConfigureDepthTexture(width, height, sourceDescriptor);
        }

        private void ConfigureMotionVectorTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_MotionVectorTexture?.desc == null)
                return;

            m_MotionVectorTexture.desc.Width = width;
            m_MotionVectorTexture.desc.Height = height;
            m_MotionVectorTexture.desc.ColorFormat = GraphicsFormat.R16G16_SFloat;
            m_MotionVectorTexture.desc.DepthBufferBits = DepthBits.None;
            m_MotionVectorTexture.desc.MsaaSamples = MSAASamples.None;
            m_MotionVectorTexture.desc.FilterMode = FilterMode.Point;
            m_MotionVectorTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_MotionVectorTexture.desc.ClearBuffer = false;
            m_MotionVectorTexture.desc.ClearColor = Color.clear;
            m_MotionVectorTexture.desc.UseMipMap = false;
            m_MotionVectorTexture.desc.AutoGenerateMips = false;
            m_MotionVectorTexture.desc.MipCount = 1;
            m_MotionVectorTexture.desc.EnableRandomWrite = false;
            m_MotionVectorTexture.desc.BindTextureMS = false;
            m_MotionVectorTexture.desc.Name = "MotionVectors";

            if (sourceDescriptor == null)
                return;

            m_MotionVectorTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_MotionVectorTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_MotionVectorTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_MotionVectorTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_MotionVectorTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private void ConfigureDepthTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_MotionVectorDepthTexture?.desc == null)
                return;

            m_MotionVectorDepthTexture.desc.Width = width;
            m_MotionVectorDepthTexture.desc.Height = height;
            m_MotionVectorDepthTexture.desc.ColorFormat = GraphicsFormat.None;
            m_MotionVectorDepthTexture.desc.DepthBufferBits = sourceDescriptor != null && sourceDescriptor.DepthBufferBits != DepthBits.None
                ? sourceDescriptor.DepthBufferBits
                : DepthBits.Depth32;
            m_MotionVectorDepthTexture.desc.MsaaSamples = sourceDescriptor?.MsaaSamples ?? MSAASamples.None;
            m_MotionVectorDepthTexture.desc.ClearBuffer = false;
            m_MotionVectorDepthTexture.desc.Name = "MotionVectorDepth";

            if (sourceDescriptor == null)
                return;

            m_MotionVectorDepthTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_MotionVectorDepthTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_MotionVectorDepthTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_MotionVectorDepthTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_MotionVectorDepthTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

        private static int ResolveCameraDimension(int actualCameraDimension, int cameraDimension, int screenDimension)
        {
            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static RenderGraphTexture CreateInputDepthTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateMotionVectorTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    ColorFormat = GraphicsFormat.R16G16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Point,
                    WrapMode = TextureWrapMode.Clamp,
                    ClearBuffer = false,
                    Name = name
                }
            };
        }

        private static RenderGraphTexture CreateDepthOutputTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }
    }

    internal sealed class MotionVectorsPersistentData
    {
        private static readonly int PreviousViewProjectionNoJitterId = Shader.PropertyToID("_PrevViewProjMatrix");
        private static readonly int ViewProjectionNoJitterId = Shader.PropertyToID("_NonJitteredViewProjMatrix");

        private Matrix4x4 m_ViewProjection = Matrix4x4.identity;
        private Matrix4x4 m_PreviousViewProjection = Matrix4x4.identity;
        private int m_LastFrameIndex = -1;
        private float m_LastAspectRatio = -1f;

        public void Reset()
        {
            m_ViewProjection = Matrix4x4.identity;
            m_PreviousViewProjection = Matrix4x4.identity;
            m_LastFrameIndex = -1;
            m_LastAspectRatio = -1f;
        }

        public void Update(VividCameraData cameraData)
        {
            if (cameraData?.camera == null)
            {
                Reset();
                return;
            }

            var frameIndex = Time.frameCount;
            var aspectRatio = ResolveAspectRatio(cameraData);
            var currentViewProjection = cameraData.GetGPUProjectionMatrixNoJitter(true) * cameraData.GetViewMatrix();
            var hasValidHistory = m_LastFrameIndex >= 0 && Mathf.Abs(m_LastAspectRatio - aspectRatio) < 0.0001f;

            if (!hasValidHistory)
            {
                m_PreviousViewProjection = currentViewProjection;
                m_ViewProjection = currentViewProjection;
            }
            else if (m_LastFrameIndex != frameIndex)
            {
                m_PreviousViewProjection = m_ViewProjection;
                m_ViewProjection = currentViewProjection;
            }
            else
            {
                m_ViewProjection = currentViewProjection;
            }

            m_LastFrameIndex = frameIndex;
            m_LastAspectRatio = aspectRatio;
        }

        public void SetGlobalMotionMatrices(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalMatrix(PreviousViewProjectionNoJitterId, m_PreviousViewProjection);
            cmd.SetGlobalMatrix(ViewProjectionNoJitterId, m_ViewProjection);
        }

        private static float ResolveAspectRatio(VividCameraData cameraData)
        {
            var camera = cameraData.camera;
            if (camera != null && camera.aspect > 0f)
                return camera.aspect;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;
            if (width > 0 && height > 0)
                return width / (float)height;

            return 1f;
        }
    }

    internal static class MotionVectorsPersistentDataRegistry
    {
        private static readonly Dictionary<Camera, MotionVectorsPersistentData> s_DataByCamera = new();
        private static readonly List<Camera> s_DestroyedCameras = new();

        public static MotionVectorsPersistentData GetOrCreate(Camera camera)
        {
            if (camera == null)
                return null;

            PruneDestroyedCameras();

            if (!s_DataByCamera.TryGetValue(camera, out var data))
            {
                data = new MotionVectorsPersistentData();
                s_DataByCamera[camera] = data;
            }

            return data;
        }

        private static void PruneDestroyedCameras()
        {
            if (s_DataByCamera.Count == 0)
                return;

            s_DestroyedCameras.Clear();
            foreach (var pair in s_DataByCamera)
            {
                if (pair.Key == null)
                    s_DestroyedCameras.Add(pair.Key);
            }

            for (var i = 0; i < s_DestroyedCameras.Count; i++)
                s_DataByCamera.Remove(s_DestroyedCameras[i]);
        }
    }
}
