using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    // The trace shaders use a single pair of material arrays. Borrow a lone source
    // directly and only compose arrays when the set of resident sources changes.
    internal sealed class VividSdfSceneCacheSet : IDisposable
    {
        private sealed class SceneComparer : IEqualityComparer<Scene>
        {
            public bool Equals(Scene left, Scene right) => left == right;
            public int GetHashCode(Scene scene) => scene.GetHashCode();
        }

        private readonly struct Source : IEquatable<Source>
        {
            internal readonly VividSdfSurfaceCache Cache;
            private readonly ulong m_Version;

            internal Source(VividSdfSurfaceCache cache)
            {
                Cache = cache;
                m_Version = cache.CardVersion;
            }

            public bool Equals(Source other) => Cache == other.Cache && m_Version == other.m_Version;
        }

        private readonly Dictionary<Scene, VividSdfSurfaceCache> m_Scenes = new(new SceneComparer());
        private readonly HashSet<VividSdfSurfaceCache> m_UniqueCaches = new();
        private readonly Dictionary<VividSdfSurfaceCache, int> m_FirstCards = new();
        private readonly List<VividSdfSurfaceCache> m_Resident = new();
        private List<Source> m_Sources = new(), m_NextSources = new();
        private VividSdfSurfaceCache m_Composed;

        internal VividSdfSurfaceCache Cache { get; private set; }

        internal VividSdfSurfaceCache Refresh(IReadOnlyList<VividSdfSceneSurfaceCache> bindings)
        {
            m_Scenes.Clear();
            m_UniqueCaches.Clear();
            m_NextSources.Clear();
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding == null)
                    continue;
                var scene = binding.gameObject.scene;
#if UNITY_EDITOR
                if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene))
                    continue;
#endif
                var cache = binding.Cache;
                if (scene.isLoaded && cache != null && cache.IsUsable && m_Scenes.TryAdd(scene, cache)
                    && m_UniqueCaches.Add(cache))
                    m_NextSources.Add(new Source(cache));
            }

            bool same = m_Sources.Count == m_NextSources.Count;
            for (int i = 0; same && i < m_Sources.Count; i++)
                same = m_Sources[i].Equals(m_NextSources[i]);
            if (!same)
            {
                Rebuild();
                (m_Sources, m_NextSources) = (m_NextSources, m_Sources);
            }

            m_NextSources.Clear();
            m_UniqueCaches.Clear();
            return Cache;
        }

        internal bool TryGetCards(Scene scene, Mesh mesh, Material material, out Bounds bounds, out int firstCard,
            out bool hasOriginalNormals)
        {
            if (m_Scenes.TryGetValue(scene, out var source) && m_FirstCards.TryGetValue(source, out int offset)
                && source.TryGetCards(mesh, material, out bounds, out firstCard))
            {
                firstCard += offset;
                hasOriginalNormals = source.HasOriginalNormals;
                return true;
            }
            bounds = default;
            firstCard = 0;
            hasOriginalNormals = false;
            return false;
        }

        private void Rebuild()
        {
            m_FirstCards.Clear();
            m_Resident.Clear();
            ReleaseComposed();
            Cache = null;
            if (m_NextSources.Count == 0)
                return;

            var first = m_NextSources[0].Cache;
            Cache = first;
            m_FirstCards.Add(first, 0);
            int entries = first.Entries.Length;
            int locals = first.LocalCardCount;
            var size = first.CardSize;
            m_Resident.Add(first);
            for (int i = 1; i < m_NextSources.Count; i++)
            {
                var source = m_NextSources[i].Cache;
                long totalSlices = (long)entries * VividSdfSurfaceCache.CardsPerMesh + locals + source.AlbedoDepth.depth;
                if (totalSlices > SystemInfo.maxTextureArraySlices || !CanCompose(first, source))
                {
                    Debug.LogWarning($"[VividRP] Surface Cache '{source.name}' could not join the resident card arrays. Its SDFs still occlude rays.", source);
                    continue;
                }
                m_FirstCards.Add(source, entries * VividSdfSurfaceCache.CardsPerMesh);
                entries += source.Entries.Length;
                locals += source.LocalCardCount;
                size = Vector2Int.Max(size, source.CardSize);
                m_Resident.Add(source);
            }
            if (m_Resident.Count == 1)
                return;

            var mergedEntries = new VividSdfSurfaceCache.Entry[entries];
            var mergedLocals = new VividSdfSurfaceCache.LocalCard[locals];
            int entry = 0, local = 0;
            foreach (var source in m_Resident)
            {
                Array.Copy(source.Entries, 0, mergedEntries, entry, source.Entries.Length);
                for (int i = 0; i < source.LocalCardCount; i++)
                {
                    var card = source.ReadLocalCard(i);
                    card.EntryIndex += entry;
                    mergedLocals[local++] = card;
                }
                entry += source.Entries.Length;
            }

            Texture2DArray albedo = null, normals = null;
            VividSdfSurfaceCache composed = null;
            try
            {
                int baseSlices = entries * VividSdfSurfaceCache.CardsPerMesh;
                albedo = ComposeArray(first.AlbedoDepth.graphicsFormat, size, baseSlices, locals, false);
                normals = ComposeArray(first.NormalCoverage.graphicsFormat, size, baseSlices, locals, true);
                composed = ScriptableObject.CreateInstance<VividSdfSurfaceCache>();
                composed.hideFlags = HideFlags.HideAndDontSave;
                composed.name = "Loaded Scene Surface Caches";
                composed.Initialize(mergedEntries, albedo, normals, mergedLocals);
                Cache = m_Composed = composed;
            }
            catch
            {
                CoreUtils.Destroy(composed);
                CoreUtils.Destroy(albedo);
                CoreUtils.Destroy(normals);
                m_FirstCards.Clear();
                m_FirstCards.Add(first, 0);
                throw;
            }
        }

        private static bool CanCompose(VividSdfSurfaceCache first, VividSdfSurfaceCache source)
        {
            // The baker emits uncompressed linear arrays. Retain raw texel values
            // when different card resolutions share the physical arrays.
            return Compatible(first.AlbedoDepth, source.AlbedoDepth)
                && Compatible(first.NormalCoverage, source.NormalCoverage);

            static bool Compatible(Texture2DArray first, Texture2DArray source) =>
                first.graphicsFormat == source.graphicsFormat && !GraphicsFormatUtility.IsCompressedFormat(first.graphicsFormat)
                && first.isReadable && source.isReadable;
        }

        private Texture2DArray ComposeArray(GraphicsFormat format, Vector2Int size, int baseSlices, int locals, bool normals)
        {
            var atlas = new Texture2DArray(size.x, size.y, baseSlices + locals, format, TextureCreationFlags.None)
            {
                name = normals ? "Scene Surface Cache Normal / Coverage" : "Scene Surface Cache Albedo / Depth",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                if (CanCopyOnGpu(size))
                {
                    atlas.Apply(false, true);
                    CopySlices(atlas, baseSlices, normals, default, 0);
                    return atlas;
                }

                int texelBytes = (int)GraphicsFormatUtility.GetBlockSize(format);
                using var scratch = new NativeArray<byte>(size.x * size.y * texelBytes, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);
                CopySlices(atlas, baseSlices, normals, scratch, texelBytes);
                atlas.Apply(false, true);
                return atlas;
            }
            catch
            {
                CoreUtils.Destroy(atlas);
                throw;
            }
        }

        private bool CanCopyOnGpu(Vector2Int size)
        {
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                return false;
            foreach (var source in m_Resident)
                if (source.CardSize != size)
                    return false;
            return true;
        }

        private void CopySlices(Texture2DArray atlas, int baseSlices, bool normals, NativeArray<byte> scratch, int texelBytes)
        {
            int baseOffset = 0, localOffset = baseSlices;
            foreach (var source in m_Resident)
            {
                var input = normals ? source.NormalCoverage : source.AlbedoDepth;
                int sourceBase = source.Entries.Length * VividSdfSurfaceCache.CardsPerMesh;
                for (int slice = 0; slice < sourceBase; slice++)
                    CopySlice(input, slice, atlas, baseOffset + slice, scratch, texelBytes);
                for (int slice = 0; slice < source.LocalCardCount; slice++)
                    CopySlice(input, sourceBase + slice, atlas, localOffset + slice, scratch, texelBytes);
                baseOffset += sourceBase;
                localOffset += source.LocalCardCount;
            }
        }

        private static void CopySlice(Texture2DArray input, int slice, Texture2DArray atlas, int target,
            NativeArray<byte> scratch, int texelBytes)
        {
            if (!scratch.IsCreated)
            {
                Graphics.CopyTexture(input, slice, 0, atlas, target, 0);
                return;
            }

            var texels = input.GetPixelData<byte>(0, slice);
            if (input.width == atlas.width && input.height == atlas.height)
            {
                atlas.SetPixelData(texels, 0, target);
                return;
            }

            for (int y = 0; y < atlas.height; y++)
            {
                int sourceRow = (2 * y + 1) * input.height / (2 * atlas.height) * input.width;
                for (int x = 0; x < atlas.width; x++)
                {
                    int sourceX = (2 * x + 1) * input.width / (2 * atlas.width);
                    NativeArray<byte>.Copy(texels, (sourceRow + sourceX) * texelBytes, scratch,
                        (y * atlas.width + x) * texelBytes, texelBytes);
                }
            }
            atlas.SetPixelData(scratch, 0, target);
        }

        private void ReleaseComposed()
        {
            if (m_Composed == null)
                return;
            var albedo = m_Composed.AlbedoDepth;
            var normals = m_Composed.NormalCoverage;
            CoreUtils.Destroy(m_Composed);
            CoreUtils.Destroy(albedo);
            CoreUtils.Destroy(normals);
            m_Composed = null;
        }

        public void Dispose()
        {
            ReleaseComposed();
            Cache = null;
            m_Scenes.Clear();
            m_Sources.Clear();
            m_NextSources.Clear();
            m_UniqueCaches.Clear();
            m_FirstCards.Clear();
            m_Resident.Clear();
        }
    }
}
