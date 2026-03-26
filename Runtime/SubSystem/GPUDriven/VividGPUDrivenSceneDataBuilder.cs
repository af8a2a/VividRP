using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenSceneDataBuilder
    {
        private static readonly int s_BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int s_BumpMapPropertyId = Shader.PropertyToID("_BumpMap");
        private static readonly int s_BumpScalePropertyId = Shader.PropertyToID("_BumpScale");
        private static readonly int s_MetallicPropertyId = Shader.PropertyToID("_Metallic");
        private static readonly int s_SmoothnessPropertyId = Shader.PropertyToID("_Smoothness");
        private static readonly int s_MetallicGlossMapPropertyId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int s_RoughnessMapPropertyId = Shader.PropertyToID("_RoughnessMap");
        private static readonly int s_EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_AlphaClipPropertyId = Shader.PropertyToID("_AlphaClip");
        private static readonly int s_CutoffPropertyId = Shader.PropertyToID("_Cutoff");
        private static readonly int s_CullPropertyId = Shader.PropertyToID("_Cull");

        private readonly Dictionary<int, int> m_MaterialIndexByObjectId = new();
        private readonly Dictionary<int, MeshletAssetMetadata> m_MeshMetadataByObjectId = new();
        private readonly HashSet<int> m_MissingProxyWarningKeys = new();
        private bool m_HasBuiltStaticData;

        public bool Build(
            VividGPUDrivenSceneData sceneData,
            VividMeshletRendererDatabase database,
            BindlessTextureContainer bindlessTextureContainer
        )
        {
            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (bindlessTextureContainer == null)
            {
                throw new ArgumentNullException(nameof(bindlessTextureContainer));
            }

            bool staticDataChanged = !m_HasBuiltStaticData;

            sceneData.ClearDynamic();
            m_MaterialIndexByObjectId.Clear();

            IReadOnlyList<VividMeshletRendererRenderData> rendererData = database.rendererData;
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = Mathf.Min(rendererData.Count, rendererResources.Count);

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                AppendRendererSceneData(
                    sceneData,
                    rendererData[rendererIndex],
                    rendererResources[rendererIndex],
                    bindlessTextureContainer,
                    ref staticDataChanged
                );
            }

            m_HasBuiltStaticData = true;
            return staticDataChanged;
        }

        private void AppendRendererSceneData(
            VividGPUDrivenSceneData sceneData,
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources,
            BindlessTextureContainer bindlessTextureContainer,
            ref bool staticDataChanged
        )
        {
            if (!IsRenderable(trackedData, trackedResources))
            {
                return;
            }

            int subMeshCount = trackedResources.MeshletCollections.Length;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                VividMeshletCollectionAsset meshletCollection = trackedResources.MeshletCollections[subMeshIndex];
                if (meshletCollection == null)
                {
                    continue;
                }

                MeshletAssetMetadata meshMetadata = GetOrAppendMeshletAsset(sceneData, meshletCollection, ref staticDataChanged);
                Material material = GetMaterialForSubMesh(trackedResources.SharedMaterials, subMeshIndex);
                GPUDrivenMaterialProxy materialProxy = GetMaterialProxyForSubMesh(trackedResources.MaterialProxies, subMeshIndex);
                int materialIndex = GetOrAppendMaterial(
                    sceneData,
                    trackedResources.MeshletRenderer,
                    materialProxy,
                    material,
                    subMeshIndex,
                    bindlessTextureContainer
                );

                sceneData.MutableInstances.Add(CreateInstanceData(trackedData, materialIndex, meshMetadata));
            }
        }

        private static bool IsRenderable(
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources
        )
        {
            if ((trackedData.flags & VividMeshletRendererFlags.Valid) == 0)
            {
                return false;
            }

            if (trackedResources.SourceMesh == null)
            {
                return false;
            }

            if (trackedResources.MeshletCollections == null || trackedResources.MeshletCollections.Length == 0)
            {
                return false;
            }

            return true;
        }

        private MeshletAssetMetadata GetOrAppendMeshletAsset(
            VividGPUDrivenSceneData sceneData,
            VividMeshletCollectionAsset meshletCollection,
            ref bool staticDataChanged
        )
        {
            int objectId = meshletCollection.GetInstanceID();
            if (m_MeshMetadataByObjectId.TryGetValue(objectId, out MeshletAssetMetadata metadata))
            {
                if (metadata.Matches(meshletCollection))
                {
                    return metadata;
                }
            }

            uint meshletBaseOffset = (uint) sceneData.MeshletCount;
            uint vertexBaseOffset = (uint) sceneData.VertexCount;
            uint indexBaseOffset = (uint) sceneData.IndexCount;
            uint meshLODStartIndex = (uint) sceneData.MeshLODNodeCount;

            VividMeshlet[] sourceMeshlets = meshletCollection.Meshlets ?? Array.Empty<VividMeshlet>();
            for (int meshletIndex = 0; meshletIndex < sourceMeshlets.Length; meshletIndex++)
            {
                VividMeshlet meshlet = sourceMeshlets[meshletIndex];
                meshlet.VertexOffset += vertexBaseOffset;
                meshlet.TriangleOffset += indexBaseOffset;
                sceneData.MutableMeshlets.Add(meshlet);
            }

            VividMeshLODNode[] sourceMeshLODNodes = meshletCollection.MeshLODNodes ?? Array.Empty<VividMeshLODNode>();
            for (int nodeIndex = 0; nodeIndex < sourceMeshLODNodes.Length; nodeIndex++)
            {
                VividMeshLODNode node = sourceMeshLODNodes[nodeIndex];
                node.MeshletStartIndex += meshletBaseOffset;
                sceneData.MutableMeshLODNodes.Add(node);
            }

            sceneData.MutableVertices.AddRange(meshletCollection.VertexBuffer ?? Array.Empty<VividMeshletVertex>());
            sceneData.MutableIndices.AddRange(meshletCollection.IndexBuffer ?? Array.Empty<byte>());

            metadata = new MeshletAssetMetadata(
                meshLODStartIndex,
                (uint) sourceMeshLODNodes.Length,
                (uint) Mathf.Max(1, meshletCollection.MeshLODLevelCount),
                sourceMeshLODNodes,
                sourceMeshlets,
                meshletCollection.VertexBuffer,
                meshletCollection.IndexBuffer
            );

            m_MeshMetadataByObjectId[objectId] = metadata;
            staticDataChanged = true;
            return metadata;
        }

        private int GetOrAppendMaterial(
            VividGPUDrivenSceneData sceneData,
            MeshletRenderer meshletRenderer,
            GPUDrivenMaterialProxy materialProxy,
            Material material,
            int subMeshIndex,
            BindlessTextureContainer bindlessTextureContainer
        )
        {
            int objectId = materialProxy != null
                ? materialProxy.GetInstanceID()
                : material != null
                    ? material.GetInstanceID()
                    : 0;
            if (m_MaterialIndexByObjectId.TryGetValue(objectId, out int materialIndex))
            {
                return materialIndex;
            }

            VividMaterialData materialData;
            if (materialProxy != null)
            {
                materialData = CreateMaterialData(materialProxy, bindlessTextureContainer);
            }
            else
            {
                WarnMissingMaterialProxy(meshletRenderer, material, subMeshIndex);
                materialData = CreateMaterialData(material, bindlessTextureContainer);
            }

            materialIndex = sceneData.MaterialCount;
            sceneData.MutableMaterials.Add(materialData);
            m_MaterialIndexByObjectId.Add(objectId, materialIndex);
            return materialIndex;
        }

        private void WarnMissingMaterialProxy(
            MeshletRenderer meshletRenderer,
            Material material,
            int subMeshIndex
        )
        {
            int warningKey = material != null
                ? material.GetInstanceID()
                : unchecked(((meshletRenderer != null ? meshletRenderer.GetInstanceID() : 0) * 397) ^ subMeshIndex);

            if (!m_MissingProxyWarningKeys.Add(warningKey))
            {
                return;
            }

            string rendererName = meshletRenderer != null ? meshletRenderer.name : "<unknown>";
            string materialName = material != null ? material.name : "<null>";
            Debug.LogWarning(
                $"[VividRP] MeshletRenderer '{rendererName}' submesh {subMeshIndex} is missing a GPUDriven material proxy. Falling back to source Material '{materialName}'.",
                meshletRenderer
            );
        }

        private static VividMaterialData CreateMaterialData(
            GPUDrivenMaterialProxy materialProxy,
            BindlessTextureContainer bindlessTextureContainer
        )
        {
            return new VividMaterialData
            {
                AlbedoColor = ToFloat4(materialProxy != null ? materialProxy.BaseColor : Color.white),
                TextureTilingOffset = ToFloat4(materialProxy != null ? materialProxy.TextureTilingOffset : new Vector4(1.0f, 1.0f, 0.0f, 0.0f)),
                Emission = ToFloat4(materialProxy != null ? materialProxy.EmissionColor : Color.black),
                AlbedoIndex = GetTextureIndex(bindlessTextureContainer, materialProxy != null ? materialProxy.BaseMap : null),
                NormalsIndex = GetTextureIndex(bindlessTextureContainer, materialProxy != null ? materialProxy.BumpMap : null),
                NormalsStrength = materialProxy != null ? materialProxy.BumpScale : 1.0f,
                MasksIndex = VividMaterialData.NoTextureIndex,
                Roughness = materialProxy != null ? materialProxy.Roughness : 1.0f,
                Metallic = materialProxy != null ? materialProxy.Metallic : 0.0f,
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = GetMaterialFlags(materialProxy),
                RendererListID = GetRendererListId(materialProxy),
                AlphaClipThreshold = GetAlphaClipThreshold(materialProxy),
            };
        }

        private static VividMaterialData CreateMaterialData(
            Material material,
            BindlessTextureContainer bindlessTextureContainer
        )
        {
            Texture albedoTexture = GetTexture(material, s_BaseMapPropertyId) ?? GetTexture(material, s_MainTexPropertyId);
            Texture masksTexture = GetTexture(material, s_MetallicGlossMapPropertyId) ?? GetTexture(material, s_RoughnessMapPropertyId);

            return new VividMaterialData
            {
                AlbedoColor = GetColor(material, s_BaseColorPropertyId, Color.white),
                TextureTilingOffset = GetTilingOffset(material),
                Emission = GetColor(material, s_EmissionColorPropertyId, Color.black),
                AlbedoIndex = GetTextureIndex(bindlessTextureContainer, albedoTexture),
                NormalsIndex = GetTextureIndex(bindlessTextureContainer, GetTexture(material, s_BumpMapPropertyId)),
                NormalsStrength = GetFloat(material, s_BumpScalePropertyId, 1.0f),
                MasksIndex = GetTextureIndex(bindlessTextureContainer, masksTexture),
                Roughness = GetRoughness(material),
                Metallic = GetFloat(material, s_MetallicPropertyId, 0.0f),
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = GetMaterialFlags(material),
                RendererListID = GetRendererListId(material),
                AlphaClipThreshold = GetAlphaClipThreshold(material),
            };
        }

        private static VividInstanceData CreateInstanceData(
            in VividMeshletRendererRenderData trackedData,
            int materialIndex,
            in MeshletAssetMetadata meshMetadata
        )
        {
            Bounds localBounds = trackedData.localBounds;

            return new VividInstanceData
            {
                ObjectToWorldMatrix = ToFloat4x4(trackedData.objectToWorldMatrix),
                WorldToObjectMatrix = ToFloat4x4(trackedData.worldToObjectMatrix),
                AABBMin = ToFloat4(localBounds.min),
                AABBMax = ToFloat4(localBounds.max),
                TopMeshLODStartIndex = meshMetadata.TopMeshLODStartIndex,
                TotalMeshLODCount = meshMetadata.TotalMeshLODCount,
                MaterialIndex = (uint) materialIndex,
                MeshLODLevelCount = meshMetadata.MeshLODLevelCount,
                LODErrorScale = 1.0f,
                PassMask = ExtractPassMask(trackedData.shadowCastingMode),
                Flags = ExtractInstanceFlags(trackedData),
            };
        }

        private static VividInstancePassMask ExtractPassMask(ShadowCastingMode shadowCastingMode)
        {
            return shadowCastingMode switch
            {
                ShadowCastingMode.Off => VividInstancePassMask.Main,
                ShadowCastingMode.On => VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                ShadowCastingMode.TwoSided => VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                ShadowCastingMode.ShadowsOnly => VividInstancePassMask.Shadows,
                _ => VividInstancePassMask.Main,
            };
        }

        private static VividInstanceFlags ExtractInstanceFlags(in VividMeshletRendererRenderData trackedData)
        {
            VividInstanceFlags flags = VividInstanceFlags.None;
            VividMeshletRendererFlags rendererFlags = trackedData.flags;

            bool isDisabled = (rendererFlags & VividMeshletRendererFlags.ActiveInHierarchy) == 0
                || (rendererFlags & VividMeshletRendererFlags.Enabled) == 0
                || (rendererFlags & VividMeshletRendererFlags.SourceRendererEnabled) == 0;

            if (isDisabled)
            {
                flags |= VividInstanceFlags.Disabled;
            }

            if (trackedData.objectToWorldMatrix.determinant < 0.0f)
            {
                flags |= VividInstanceFlags.FlipWindingOrder;
            }

            return flags;
        }

        private static Material GetMaterialForSubMesh(Material[] sharedMaterials, int subMeshIndex)
        {
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                return null;
            }

            int materialIndex = Mathf.Clamp(subMeshIndex, 0, sharedMaterials.Length - 1);
            return sharedMaterials[materialIndex];
        }

        private static GPUDrivenMaterialProxy GetMaterialProxyForSubMesh(
            GPUDrivenMaterialProxy[] materialProxies,
            int subMeshIndex
        )
        {
            if (materialProxies == null || materialProxies.Length == 0)
            {
                return null;
            }

            int materialIndex = Mathf.Clamp(subMeshIndex, 0, materialProxies.Length - 1);
            return materialProxies[materialIndex];
        }

        private static uint GetTextureIndex(BindlessTextureContainer bindlessTextureContainer, Texture texture)
        {
            return bindlessTextureContainer.TryGetOrCreateIndex(texture, out uint textureIndex)
                ? textureIndex
                : VividMaterialData.NoTextureIndex;
        }

        private static Texture GetTexture(Material material, int propertyId)
        {
            return material != null && material.HasProperty(propertyId) ? material.GetTexture(propertyId) : null;
        }

        private static float4 GetColor(Material material, int propertyId, Color fallback)
        {
            Color color = material != null && material.HasProperty(propertyId)
                ? material.GetColor(propertyId)
                : fallback;
            return new float4(color.r, color.g, color.b, color.a);
        }

        private static float4 GetTilingOffset(Material material)
        {
            int texturePropertyId = material != null && material.HasProperty(s_BaseMapPropertyId)
                ? s_BaseMapPropertyId
                : s_MainTexPropertyId;

            if (material == null || !material.HasProperty(texturePropertyId))
            {
                return new float4(1.0f, 1.0f, 0.0f, 0.0f);
            }

            Vector2 scale = material.GetTextureScale(texturePropertyId);
            Vector2 offset = material.GetTextureOffset(texturePropertyId);
            return new float4(scale.x, scale.y, offset.x, offset.y);
        }

        private static float GetFloat(Material material, int propertyId, float fallback)
        {
            return material != null && material.HasProperty(propertyId) ? material.GetFloat(propertyId) : fallback;
        }

        private static float GetRoughness(Material material)
        {
            if (material == null)
            {
                return 1.0f;
            }

            if (material.HasProperty(s_SmoothnessPropertyId))
            {
                return 1.0f - Mathf.Clamp01(material.GetFloat(s_SmoothnessPropertyId));
            }

            return 1.0f;
        }

        private static VividMaterialFlags GetMaterialFlags(GPUDrivenMaterialProxy materialProxy)
        {
            return materialProxy is { DisableLighting: true }
                ? VividMaterialFlags.Unlit
                : VividMaterialFlags.None;
        }

        private static VividMaterialFlags GetMaterialFlags(Material material)
        {
            if (material?.shader != null &&
                string.Equals(material.shader.name, "VividRP/Material/SimpleForward", StringComparison.Ordinal))
            {
                return VividMaterialFlags.Unlit;
            }

            return VividMaterialFlags.None;
        }

        private static VividRendererListID GetRendererListId(GPUDrivenMaterialProxy materialProxy)
        {
            VividRendererListID rendererListId = VividRendererListID.Default;

            if (materialProxy == null)
            {
                return rendererListId;
            }

            if (materialProxy.CullMode == CullMode.Front)
            {
                rendererListId |= VividRendererListID.CullFront;
            }
            else if (materialProxy.CullMode == CullMode.Off)
            {
                rendererListId |= VividRendererListID.CullOff;
            }

            if (materialProxy.AlphaClip)
            {
                rendererListId |= VividRendererListID.AlphaTest;
            }

            return rendererListId;
        }

        private static VividRendererListID GetRendererListId(Material material)
        {
            VividRendererListID rendererListId = VividRendererListID.Default;

            if (material != null)
            {
                int cullMode = material.HasProperty(s_CullPropertyId)
                    ? Mathf.RoundToInt(material.GetFloat(s_CullPropertyId))
                    : (int) CullMode.Back;

                if (cullMode == (int) CullMode.Front)
                {
                    rendererListId |= VividRendererListID.CullFront;
                }
                else if (cullMode == (int) CullMode.Off)
                {
                    rendererListId |= VividRendererListID.CullOff;
                }

                if (IsAlphaClipEnabled(material))
                {
                    rendererListId |= VividRendererListID.AlphaTest;
                }
            }

            return rendererListId;
        }

        private static bool IsAlphaClipEnabled(Material material)
        {
            return material != null &&
                   ((material.HasProperty(s_AlphaClipPropertyId) && material.GetFloat(s_AlphaClipPropertyId) > 0.5f) ||
                    material.IsKeywordEnabled("_ALPHATEST_ON"));
        }

        private static float GetAlphaClipThreshold(Material material)
        {
            return IsAlphaClipEnabled(material) && material.HasProperty(s_CutoffPropertyId)
                ? material.GetFloat(s_CutoffPropertyId)
                : 0.0f;
        }

        private static float GetAlphaClipThreshold(GPUDrivenMaterialProxy materialProxy)
        {
            return materialProxy is { AlphaClip: true } ? materialProxy.Cutoff : 0.0f;
        }

        private static float4 ToFloat4(Vector3 value)
        {
            return new float4(value.x, value.y, value.z, 0.0f);
        }

        private static float4 ToFloat4(Vector4 value)
        {
            return new float4(value.x, value.y, value.z, value.w);
        }

        private static float4 ToFloat4(Color value)
        {
            return new float4(value.r, value.g, value.b, value.a);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33)
            );
        }

        private readonly struct MeshletAssetMetadata
        {
            public MeshletAssetMetadata(
                uint topMeshLODStartIndex,
                uint totalMeshLODCount,
                uint meshLODLevelCount,
                VividMeshLODNode[] meshLODNodes,
                VividMeshlet[] meshlets,
                VividMeshletVertex[] vertexBuffer,
                byte[] indexBuffer
            )
            {
                TopMeshLODStartIndex = topMeshLODStartIndex;
                TotalMeshLODCount = totalMeshLODCount;
                MeshLODLevelCount = meshLODLevelCount;
                MeshLODNodes = meshLODNodes;
                Meshlets = meshlets;
                VertexBuffer = vertexBuffer;
                IndexBuffer = indexBuffer;
            }

            public uint TopMeshLODStartIndex { get; }

            public uint TotalMeshLODCount { get; }

            public uint MeshLODLevelCount { get; }

            private VividMeshLODNode[] MeshLODNodes { get; }

            private VividMeshlet[] Meshlets { get; }

            private VividMeshletVertex[] VertexBuffer { get; }

            private byte[] IndexBuffer { get; }

            public bool Matches(VividMeshletCollectionAsset meshletCollection)
            {
                return meshletCollection != null &&
                       MeshLODLevelCount == (uint) Mathf.Max(1, meshletCollection.MeshLODLevelCount) &&
                       ReferenceEquals(MeshLODNodes, meshletCollection.MeshLODNodes) &&
                       ReferenceEquals(Meshlets, meshletCollection.Meshlets) &&
                       ReferenceEquals(VertexBuffer, meshletCollection.VertexBuffer) &&
                       ReferenceEquals(IndexBuffer, meshletCollection.IndexBuffer);
            }
        }
    }
}
