using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UnityEngine.Rendering.Universal
{
    public class UIScratchEffectSystem
    {
        private static UIScratchEffectSystem _instance;

        public static UIScratchEffectSystem instance
        {
            get
            {
                if (null == _instance)
                    _instance = new UIScratchEffectSystem();
                return _instance;
            }
        }


        private Material _markDrawMaterial;

        private Material MarkDrawMaterial
        {
            get { return _markDrawMaterial ??= new Material(Shader.Find("ScratchMask")); }
        }


        #region Property

        int traceRTId = Shader.PropertyToID("_TraceTexture");
        RenderTexture traceRT;

        public class ScratchData
        {
            public Material material;
            public Vector3 scale;
            public bool dirty;
            public bool newRT;
            public RenderTexture traceRT;
        }

        Dictionary<int, ScratchData> dataMap = new();


        private Mesh _quad;

        internal Mesh quad
        {
            get
            {
                if (null == _quad)
                {
                    _quad = new Mesh();

                    _quad.SetVertices(new List<Vector3>
                    {
                        new Vector3(-1.0f, 0.0f, -1.0f),
                        new Vector3(-1.0f, 0.0f, 1.0f),
                        new Vector3(1.0f, 0.0f, 1.0f),
                        new Vector3(1.0f, 0.0f, -1.0f)
                    });

                    _quad.SetUVs(0, new List<Vector2>
                    {
                        new Vector2(0.0f, 0.0f),
                        new Vector2(0.0f, 1.0f),
                        new Vector2(1.0f, 1.0f),
                        new Vector2(1.0f, 0.0f)
                    });

                    _quad.SetIndices(new int[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0, false);
                }

                return _quad;
            }
        }

        #endregion


        int index = 0;

        public int Regist(UIScratch uiScratch)
        {
            var data = new ScratchData
            {
                material = new Material(MarkDrawMaterial),
                scale = new Vector3(uiScratch.scale.x, uiScratch.scale.y, 1f),
                dirty = false,
                traceRT = GetRenderTexture(),
            };


            dataMap[index] = data;
            UpdateData(index, uiScratch);

            return index++;
        }

        public void UnRegist(int id)
        {
            if (!dataMap.ContainsKey(id))
                return;

            if (dataMap[id].material)
            {
                dataMap[id].traceRT.Release();
                Object.DestroyImmediate(dataMap[id].material);
            }

            dataMap.Remove(id);
        }


        public void UpdateData(int id, UIScratch uiScratch, bool dirty = false)
        {
            float scale = 1;
            dataMap[id].material.SetTexture("_TraceTex", uiScratch.traceTex);
            dataMap[id].material.SetFloat("_TraceSize", uiScratch.traceSize * scale);

            dataMap[id].dirty = dirty;
        }

        public RenderTexture GetRenderTextureByID(int id)
        {
            return dataMap[id].traceRT;
        }

        public void UpdateTracePos(int id, Vector2 pos)
        {
            dataMap[id].material.SetVector("_TracePos", pos);
        }


        #region Render

        static RenderTexture GetRenderTexture()
        {
            const int RTWidth = 1024;

            return new RenderTexture(RTWidth, RTWidth, 0, RenderTextureFormat.R8)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = true,
                autoGenerateMips = true,
                name = "TraceRT"
            };
        }


        public void Clear(int id)
        {
            dataMap[id].newRT = true;
            dataMap[id].dirty = true;
        }

        public void RenderMask(CommandBuffer cmd)
        {

            if ((dataMap == null || dataMap.Count == 0))
                return;



            if (dataMap != null && dataMap.Count > 0)
            {

                foreach (var i in dataMap)
                {

                    //draw only dirty
                    ScratchData scratchData = i.Value;
                    if (i.Value.dirty)
                    {
                        i.Value.dirty = false;
                        cmd.SetRenderTarget(scratchData.traceRT, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                        //clear not need draw
                        if (scratchData.newRT)
                        {
                            scratchData.newRT = false;
                            cmd.ClearRenderTarget(false, true, Color.black);
                            return;
                        }


                        scratchData.material.SetTexture(traceRTId, traceRT);
                        var matrix = Matrix4x4.Scale(scratchData.scale);
                        cmd.DrawMesh(quad, matrix, scratchData.material, 0, 0);

                    }
                    
                }
            }
        }

        #endregion
    }
}