using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;

namespace VividRP.Runtime
{
    internal sealed class DDGISceneCacheBuilder
    {
        private static readonly int s_BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int s_BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int s_MetallicPropertyId = Shader.PropertyToID("_Metallic");
        private static readonly int s_EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_SurfacePropertyId = Shader.PropertyToID("_Surface");
        private static readonly int s_AlphaClipPropertyId = Shader.PropertyToID("_AlphaClip");
        private static readonly int s_CutoffPropertyId = Shader.PropertyToID("_Cutoff");

        public bool Build(DDGIVolume volume, BindlessTextureContainer bindlessTextures, DDGISceneCache sceneCache)
        {
            if (sceneCache == null)
            {
                throw new ArgumentNullException(nameof(sceneCache));
            }

            int previousHash = sceneCache.SceneHash;
            sceneCache.Clear();

            if (volume == null || !volume.IsRuntimeSupported)
            {
                return previousHash != 0;
            }

            Bounds searchBounds = volume.ExpandedWorldBounds;
            MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID);
            HashCode sceneHash = new HashCode();

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                if (!IsSupportedRenderer(renderer, searchBounds, out Mesh mesh))
                {
                    continue;
                }

                Material[] sharedMaterials = renderer.sharedMaterials;
                int subMeshCount = Mathf.Min(mesh.subMeshCount, sharedMaterials != null ? sharedMaterials.Length : 0);
                if (subMeshCount <= 0 || !AreAllSubMeshesSupported(sharedMaterials, subMeshCount))
                {
                    continue;
                }

                sceneHash.Add(renderer.GetEntityId());
                sceneHash.Add(mesh.GetEntityId());
                AddMatrixHash(ref sceneHash, renderer.transform.localToWorldMatrix);

                int firstVertexIndex = sceneCache.Vertices.Count;
                AppendVertices(mesh, sceneCache);

                int firstSubMeshIndex = sceneCache.SubMeshes.Count;
                uint primitiveOffset = 0u;
                for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                {
                    Material material = sharedMaterials[subMeshIndex];
                    sceneHash.Add(material.GetEntityId());
                    AddMaterialHash(ref sceneHash, material);

                    sceneCache.Materials.Add(CreateMaterialData(material, bindlessTextures));
                    uint materialIndex = (uint)(sceneCache.Materials.Count - 1);

                    int[] subMeshIndices = mesh.GetIndices(subMeshIndex, applyBaseVertex: false);
                    uint indexOffset = (uint)sceneCache.Indices.Count;
                    for (int index = 0; index < subMeshIndices.Length; index++)
                    {
                        sceneCache.Indices.Add((uint)(firstVertexIndex + subMeshIndices[index]));
                    }

                    uint primitiveCount = (uint)(subMeshIndices.Length / 3);
                    sceneCache.SubMeshes.Add(new DDGISubMeshData
                    {
                        MaterialIndex = materialIndex,
                        PrimitiveOffset = primitiveOffset,
                        PrimitiveCount = primitiveCount,
                        IndexOffset = indexOffset,
                    });
                    primitiveOffset += primitiveCount;
                }

                sceneCache.Renderers.Add(renderer);
                sceneCache.Instances.Add(new DDGIInstanceData
                {
                    ObjectToWorldMatrix = ToFloat4x4(renderer.transform.localToWorldMatrix),
                    WorldToObjectMatrix = ToFloat4x4(renderer.transform.worldToLocalMatrix),
                    FirstSubMeshIndex = (uint)firstSubMeshIndex,
                    SubMeshCount = (uint)subMeshCount,
                });
            }

            sceneCache.SceneHash = sceneHash.ToHashCode();
            return sceneCache.SceneHash != previousHash;
        }

        private static bool IsSupportedRenderer(MeshRenderer renderer, Bounds searchBounds, out Mesh mesh)
        {
            mesh = null;

            if (renderer == null
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
                || !renderer.bounds.Intersects(searchBounds))
            {
                return false;
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            return mesh != null && mesh.vertexCount > 0 && mesh.subMeshCount > 0;
        }

        private static bool AreAllSubMeshesSupported(Material[] sharedMaterials, int subMeshCount)
        {
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                if (!IsSupportedMaterial(sharedMaterials[subMeshIndex]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            if (material.renderQueue > (int)RenderQueue.GeometryLast)
            {
                return false;
            }

            if (material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
            {
                return false;
            }

            if (material.HasProperty(s_SurfacePropertyId) && material.GetFloat(s_SurfacePropertyId) > 0.5f)
            {
                return false;
            }

            if (material.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                return false;
            }

            if (material.HasProperty(s_AlphaClipPropertyId) && material.GetFloat(s_AlphaClipPropertyId) > 0.5f)
            {
                return false;
            }

            if (material.HasProperty(s_CutoffPropertyId) && material.GetFloat(s_CutoffPropertyId) > 0.0f)
            {
                return false;
            }

            return true;
        }

        private static void AppendVertices(Mesh mesh, DDGISceneCache sceneCache)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv0 = mesh.uv;

            for (int vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
            {
                Vector3 position = positions[vertexIndex];
                Vector3 normal = normals != null && vertexIndex < normals.Length
                    ? normals[vertexIndex]
                    : Vector3.up;
                Vector2 texCoord = uv0 != null && vertexIndex < uv0.Length
                    ? uv0[vertexIndex]
                    : Vector2.zero;

                sceneCache.Vertices.Add(new DDGIVertexData
                {
                    PositionOS = new Vector4(position.x, position.y, position.z, 1.0f),
                    NormalOS = new Vector4(normal.x, normal.y, normal.z, 0.0f),
                    TexCoord0 = new Vector4(texCoord.x, texCoord.y, 0.0f, 0.0f),
                });
            }
        }

        private static DDGIMaterialData CreateMaterialData(Material material, BindlessTextureContainer bindlessTextures)
        {
            int baseMapPropertyId = ResolveBaseMapPropertyId(material);
            Texture baseMap = baseMapPropertyId >= 0 ? material.GetTexture(baseMapPropertyId) : null;
            Vector2 tiling = baseMapPropertyId >= 0 ? material.GetTextureScale(baseMapPropertyId) : Vector2.one;
            Vector2 offset = baseMapPropertyId >= 0 ? material.GetTextureOffset(baseMapPropertyId) : Vector2.zero;

            return new DDGIMaterialData
            {
                BaseColor = GetColor(material, s_BaseColorPropertyId, s_ColorPropertyId, Color.white),
                EmissiveColor = GetColor(material, s_EmissionColorPropertyId, fallbackId: -1, Color.black),
                BaseMapST = new Vector4(tiling.x, tiling.y, offset.x, offset.y),
                Metallic = material.HasProperty(s_MetallicPropertyId) ? Mathf.Clamp01(material.GetFloat(s_MetallicPropertyId)) : 0.0f,
                BaseMapIndex = GetTextureIndex(bindlessTextures, baseMap),
            };
        }

        private static Texture GetTexture(Material material, int propertyId)
        {
            return material != null && material.HasProperty(propertyId)
                ? material.GetTexture(propertyId)
                : null;
        }

        private static int ResolveBaseMapPropertyId(Material material)
        {
            if (material == null)
            {
                return -1;
            }

            if (material.HasProperty(s_BaseMapPropertyId))
            {
                return s_BaseMapPropertyId;
            }

            return material.HasProperty(s_MainTexPropertyId)
                ? s_MainTexPropertyId
                : -1;
        }

        private static uint GetTextureIndex(BindlessTextureContainer bindlessTextures, Texture texture)
        {
            if (bindlessTextures != null && bindlessTextures.TryGetOrCreateIndex(texture, out uint index))
            {
                return index;
            }

            return DDGIMaterialData.InvalidTextureIndex;
        }

        private static Vector4 GetColor(Material material, int primaryId, int fallbackId, Color fallback)
        {
            Color value = fallback;
            if (material != null && primaryId >= 0 && material.HasProperty(primaryId))
            {
                value = material.GetColor(primaryId);
            }
            else if (material != null && fallbackId >= 0 && material.HasProperty(fallbackId))
            {
                value = material.GetColor(fallbackId);
            }

            return new Vector4(value.r, value.g, value.b, value.a);
        }

        private static void AddMatrixHash(ref HashCode hash, Matrix4x4 matrix)
        {
            hash.Add(matrix.m00);
            hash.Add(matrix.m01);
            hash.Add(matrix.m02);
            hash.Add(matrix.m03);
            hash.Add(matrix.m10);
            hash.Add(matrix.m11);
            hash.Add(matrix.m12);
            hash.Add(matrix.m13);
            hash.Add(matrix.m20);
            hash.Add(matrix.m21);
            hash.Add(matrix.m22);
            hash.Add(matrix.m23);
            hash.Add(matrix.m30);
            hash.Add(matrix.m31);
            hash.Add(matrix.m32);
            hash.Add(matrix.m33);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }

        private static void AddMaterialHash(ref HashCode hash, Material material)
        {
            if (material == null)
            {
                hash.Add(0);
                return;
            }

            hash.Add(material.renderQueue);
            if (material.HasProperty(s_BaseColorPropertyId))
            {
                hash.Add(material.GetColor(s_BaseColorPropertyId));
            }
            else if (material.HasProperty(s_ColorPropertyId))
            {
                hash.Add(material.GetColor(s_ColorPropertyId));
            }

            if (material.HasProperty(s_EmissionColorPropertyId))
            {
                hash.Add(material.GetColor(s_EmissionColorPropertyId));
            }

            if (material.HasProperty(s_MetallicPropertyId))
            {
                hash.Add(material.GetFloat(s_MetallicPropertyId));
            }

            Texture baseMap = GetTexture(material, s_BaseMapPropertyId) ?? GetTexture(material, s_MainTexPropertyId);
            hash.Add(baseMap != null ? baseMap.GetEntityId() : EntityId.None);
        }
    }
}
