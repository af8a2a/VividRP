using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public enum SimulationResolution
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }


    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class NSFluidPlane : FluidPlane
    {
        #region Private

        MaterialPropertyBlock m_MaterialPropertyBlock;
        Renderer m_Renderer;
        RenderTextureDescriptor m_RenderTextureDescriptor;
        RTHandle m_NSFluidTexture;
        List<FluidInteractor> m_Interactors = new List<FluidInteractor>();
        private int m_InteractorsCount = 0;
        private int m_InteractorCapacity = 1;
        GraphicsBuffer m_InteractorData;

        static readonly Rect unitRect = new Rect(0, 0, 1, 1);

        #endregion

        #region Public

        [Range(1, 30)] public int diffusionTimes = 4;
        [Range(1, 30)] public int pressureTimes = 4;

        public SimulationResolution resolution = SimulationResolution._512;
        [Min(0.00001f)] public float Viscosity = 0.05f;
        public int interactorsCount => m_InteractorsCount;
        [Min(0.001f)] public float advectSpeed = 1;

        #endregion

        #region Interface

        public void RegisterInteractor(FluidInteractor interactor)
        {
            m_Interactors.Add(interactor);
        }

        public void UnregisterInteractor(FluidInteractor interactor)
        {
            m_Interactors.Remove(interactor);
        }


        public void ApplyFluid()
        {
            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();
            if (m_MaterialPropertyBlock is not null && m_Renderer is not null && m_NSFluidTexture is not null)
            {
                m_Renderer.GetPropertyBlock(m_MaterialPropertyBlock);
                var worldToFluid = Matrix4x4.TRS(new Vector3(0.5f, 0.0f, 0.5f), Quaternion.identity, new Vector3(1.0f / areaSize.x, 1.0f, 1.0f / areaSize.y));
                worldToFluid *= transform.worldToLocalMatrix;
                m_MaterialPropertyBlock.SetTexture("_FluidTex", m_NSFluidTexture);
                m_MaterialPropertyBlock.SetMatrix("_WorldToFluid", worldToFluid);
                m_Renderer.SetPropertyBlock(m_MaterialPropertyBlock);
            }
        }

        public GraphicsBuffer interactorData => m_InteractorData;

        public RTHandle nsFluidTexture => m_NSFluidTexture;

        #endregion

        #region Util

        struct InteractorData
        {
            public Vector2 PositionOS;
            public Vector2 Force;
            public float Radius;
        }


        /// <summary>
        /// Convert WorldSpace Positon into Mesh UV space
        /// </summary>
        /// <param name="worldPos"></param>
        /// <returns></returns>
        Vector2 TransformWorldToPlaneSpace(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);

            var areaExtents = areaSize * 0.5f;
            var uv = new Vector2(local.x / areaExtents.x, local.z / areaExtents.y);
            uv = (uv + Vector2.one) * 0.5f;

            // float u = (local.x / areaSize.x) + 0.5f;
            // float v = (local.z / areaSize.y) + 0.5f;
            return uv;
        }

        bool Inside(Vector2 localPos)
        {
            bool inPlane = unitRect.Contains(localPos);
            return inPlane;
        }

        void UpdateTextureIfNeed()
        {
            var res = (int)resolution;
            if (m_RenderTextureDescriptor.width != res)
            {
                m_RenderTextureDescriptor = new RenderTextureDescriptor(res, res)
                {
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    useDynamicScale = true,
                    depthStencilFormat = GraphicsFormat.None,
                };
                m_NSFluidTexture?.Release();
                m_NSFluidTexture = RTHandles.Alloc(m_RenderTextureDescriptor, name: $"{gameObject.name}_FluidTexture");
            }
        }

        #endregion


        private void OnEnable()
        {
            m_Renderer ??= GetComponent<Renderer>();
            m_InteractorData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_InteractorCapacity, Marshal.SizeOf<InteractorData>());
            NSFluidPlaneManager.instance.Add(this);
            UpdateTextureIfNeed();
        }


        private void OnDisable()
        {
            NSFluidPlaneManager.instance.Remove(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_Interactors.Count(item => item is null) > 0)
            {
                m_Interactors = new List<FluidInteractor>();
            }
        }
#endif

        private void OnDestroy()
        {
            m_NSFluidTexture?.Release();
        }

        private void Update()
        {
            if (m_InteractorsCount != m_Interactors.Count)
            {
                m_InteractorsCount = m_Interactors.Count;
                bool needResize = m_InteractorCapacity <= m_Interactors.Count;
                m_InteractorCapacity = Math.Max(m_Interactors.Count, m_InteractorCapacity);
                if (needResize)
                {
                    m_InteractorData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_InteractorCapacity, Marshal.SizeOf<InteractorData>());
                }
            }

            UpdateTextureIfNeed();


            var datas = m_Interactors.Where(item => item is not null && item.isActiveAndEnabled)
                .Select(item =>
                {
                    var currentPositionOS = TransformWorldToPlaneSpace(item.CurrentPosition);
                    //todo:consider inside necessary?
                    var inside = Inside(currentPositionOS) ? 1f : 0f;
                    var previousPositionOS = TransformWorldToPlaneSpace(item.PreviousPosition);


                    return new InteractorData()
                    {
                        PositionOS = currentPositionOS,
                        Radius = item.radius,
                        Force = (currentPositionOS - previousPositionOS) * item.forceScale
                    };
                })
                .ToList();

            m_InteractorData.SetData(datas);
        }
    }
}