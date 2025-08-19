using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = System.Object;

namespace UnityEngine.Rendering.Universal
{
    public class OcclusionOutlineDrawSystem
    {
        private static readonly Lazy<OcclusionOutlineDrawSystem> instance = new Lazy<OcclusionOutlineDrawSystem>();

        public static OcclusionOutlineDrawSystem Instance => instance.Value;


        [SerializeField] private Material _material;

        public Material OutlineMaterial
        {
            get
            {
                if (_material == null)
                {
                    _material = new Material(Shader.Find("Unlit/Outline"));
                }

                return _material;
            }
        }

        #region Shader

        class ShaderIDs
        {
            public static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
            public static readonly int Intensity = Shader.PropertyToID("_Intensity");
            public static readonly int OutlineClampScale = Shader.PropertyToID("_OutlineClampScale");

        }

        #endregion

        #region Data

        private Dictionary<OcclusionOutlineController, (Renderer[], Material)> renderData = new();

        #endregion


        public void Register(OcclusionOutlineController obj)
        {
            if (!renderData.TryGetValue(obj, out var value))
            {
                var material = new Material(OutlineMaterial);
                material.SetFloat(ShaderIDs.Intensity, obj.Intensity);
                // material.SetFloat(ShaderIDs.OutlineClampScale, obj.OutlineClampScale);
                material.SetColor(ShaderIDs.OutlineColor, obj.OutlineColor);

                renderData[obj] = (obj.GetComponentsInChildren<Renderer>(), new Material(OutlineMaterial));
            }
            else
            {
                var material = value.Item2;
                material.SetFloat(ShaderIDs.Intensity, obj.Intensity);
                // material.SetFloat(ShaderIDs.OutlineClampScale, obj.OutlineClampScale);
                material.SetColor(ShaderIDs.OutlineColor, obj.OutlineColor);


            }
        }

        public void Unregister(OcclusionOutlineController obj)
        {
            renderData.Remove(obj);
        }

        public void Render(RasterCommandBuffer commandBuffer)
        {
            foreach (var (renderers, material) in renderData.Values)
            {
                foreach (var renderer in renderers)
                {
                    var submesh = renderer.sharedMaterials.Length;
                    for (var i = 0; i < submesh; ++i)
                    {
                        commandBuffer.DrawRenderer(renderer, material,i,0);
                    }
                }
            }
            foreach (var (renderers, material) in renderData.Values)
            {
                foreach (var renderer in renderers)
                {
                    var submesh = renderer.sharedMaterials.Length;
                    for (var i = 0; i < submesh; ++i)
                    {
                        commandBuffer.DrawRenderer(renderer, material,i,1);
                    }
                }
            }

        }
    }
}