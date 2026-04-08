using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
    internal sealed class DDGISceneCache
    {
        public readonly List<MeshRenderer> Renderers = new();
        public readonly List<DDGIInstanceData> Instances = new();
        public readonly List<DDGISubMeshData> SubMeshes = new();
        public readonly List<DDGIMaterialData> Materials = new();
        public readonly List<DDGIVertexData> Vertices = new();
        public readonly List<uint> Indices = new();

        public int SceneHash { get; set; }

        public void Clear()
        {
            Renderers.Clear();
            Instances.Clear();
            SubMeshes.Clear();
            Materials.Clear();
            Vertices.Clear();
            Indices.Clear();
            SceneHash = 0;
        }
    }
}
