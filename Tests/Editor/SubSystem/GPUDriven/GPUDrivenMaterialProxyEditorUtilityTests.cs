using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class GPUDrivenMaterialProxyEditorUtilityTests
    {
        private const string TempFolder = "Assets/VividRP_Temp_GPUDrivenMaterialProxyEditorUtilityTests";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
            EnsureTempFolderExists();
            VividMeshletRendererDatabase.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividMeshletRendererDatabase.instance.Clear();
            DeleteTempFolder();
        }

        [Test]
        public void CreateOrBindMaterialProxies_CreatesAssetNextToPersistentMaterial_WhenMaterialAssetExists()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/PersistentMesh.asset";
            string materialPath = TempFolder + "/PersistentMaterial.mat";

            Mesh mesh = CreateSingleSubMeshMesh("PersistentMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var persistentMaterial = new Material(shader);
            AssetDatabase.CreateAsset(persistentMaterial, materialPath);
            persistentMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            GameObject gameObject = CreateMeshletRendererObject("PersistentMaterialRenderer", mesh, persistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo($"{TempFolder}/PersistentMaterial_GPUDriven.asset")
                );
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxy_BindsOnlyRequestedSubMesh_WhenMeshHasMultipleSubMeshes()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/MultiSubMesh.asset";
            Mesh mesh = CreateTwoSubMeshMesh("MultiSubMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            Material material0 = new Material(shader);
            Material material1 = new Material(shader);
            GameObject gameObject = CreateMeshletRendererObject(
                "MultiSubMeshRenderer",
                mesh,
                new[] { material0, material1 },
                out MeshletRenderer meshletRenderer
            );

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxy(meshletRenderer, 1);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Null);
                Assert.That(meshletRenderer.GetMaterialProxy(1), Is.Not.Null);
                Assert.That(meshletRenderer.GetMaterialProxy(1).SourceMaterial, Is.SameAs(material1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material0);
                Object.DestroyImmediate(material1);
            }
        }

        [Test]
        public void SyncMaterialProxyFromSourceMaterial_UpdatesOnlyRequestedSubMesh()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            Mesh mesh = CreateSingleSubMeshMesh("SyncSingleSlot");
            Material material = new Material(shader);
            material.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.6f, 1.0f));
            material.SetFloat("_Metallic", 0.7f);
            material.SetFloat("_Smoothness", 0.1f);

            GameObject gameObject = CreateMeshletRendererObject("SyncRenderer", mesh, material, out MeshletRenderer meshletRenderer);
            GPUDrivenMaterialProxy materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                GPUDrivenMaterialProxySyncResult result =
                    GPUDrivenMaterialProxyEditorUtility.SyncMaterialProxyFromSourceMaterial(meshletRenderer, 0);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material));
                Assert.That(materialProxy.BaseColor.r, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.9f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AutoSync_SynchronizesIndexedProxyAndSkipsUnchangedMaterial()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            string materialPath = TempFolder + "/AutoSyncSource.mat";
            string proxyPath = TempFolder + "/AutoSyncProxy.asset";
            var sourceMaterial = new Material(shader);
            AssetDatabase.CreateAsset(sourceMaterial, materialPath);
            sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.SourceMaterial = sourceMaterial;
            AssetDatabase.CreateAsset(materialProxy, proxyPath);
            materialProxy = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);

            GPUDrivenMaterialProxyAutoSyncService.ResetForTests();
            try
            {
                GPUDrivenMaterialProxyAutoSyncService.IndexProxyForTests(materialProxy);
                sourceMaterial.SetColor("_BaseColor", new Color(0.15f, 0.35f, 0.55f, 1.0f));
                sourceMaterial.SetFloat("_Metallic", 0.65f);
                sourceMaterial.SetFloat("_Smoothness", 0.2f);
                sourceMaterial.SetFloat("_MetallicRemapMin", 0.1f);
                sourceMaterial.SetFloat("_MetallicRemapMax", 0.8f);
                sourceMaterial.SetFloat("_SmoothnessRemapMin", 0.2f);
                sourceMaterial.SetFloat("_SmoothnessRemapMax", 0.9f);
                sourceMaterial.SetFloat("_AORemapMin", 0.3f);
                sourceMaterial.SetFloat("_AORemapMax", 0.7f);

                int changedProxyCount = GPUDrivenMaterialProxyAutoSyncService.SynchronizeMaterialNowForTests(
                    sourceMaterial,
                    GPUDrivenMaterialProxyTextureMode.Bindless);
                uint synchronizedRevision = materialProxy.Revision;

                Assert.That(changedProxyCount, Is.EqualTo(1));
                Assert.That(materialProxy.BaseColor.r, Is.EqualTo(0.15f).Within(0.0001f));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(materialProxy.MetallicRemap, Is.EqualTo(new Vector2(0.1f, 0.8f)));
                Assert.That(materialProxy.SmoothnessRemap, Is.EqualTo(new Vector2(0.2f, 0.9f)));
                Assert.That(materialProxy.AmbientOcclusionRemap, Is.EqualTo(new Vector2(0.3f, 0.7f)));
                Assert.That(EditorUtility.IsDirty(materialProxy), Is.True);

                changedProxyCount = GPUDrivenMaterialProxyAutoSyncService.SynchronizeMaterialNowForTests(
                    sourceMaterial,
                    GPUDrivenMaterialProxyTextureMode.Bindless);

                Assert.That(changedProxyCount, Is.Zero);
                Assert.That(materialProxy.Revision, Is.EqualTo(synchronizedRevision));
            }
            finally
            {
                GPUDrivenMaterialProxyAutoSyncService.ResetForTests(requestIndexRebuild: true);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_CreatesAssetNextToPersistentMesh_WhenMaterialIsNonPersistent()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/FallbackMesh.asset";

            Mesh mesh = CreateSingleSubMeshMesh("FallbackMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            Material nonPersistentMaterial = new Material(shader);
            GameObject gameObject = CreateMeshletRendererObject("FallbackMaterialRenderer", mesh, nonPersistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo($"{TempFolder}/FallbackMesh_SubMesh0_GPUDriven.asset")
                );
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(nonPersistentMaterial);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_RebindsExistingProxyWhenSourceMaterialChanges()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/ReboundMesh.asset";
            Mesh mesh = CreateSingleSubMeshMesh("ReboundMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var firstMaterial = new Material(shader);
            var secondMaterial = new Material(shader);
            GameObject gameObject = CreateMeshletRendererObject(
                "ReboundMaterialRenderer",
                mesh,
                firstMaterial,
                out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult firstResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);
                GPUDrivenMaterialProxy materialProxy = meshletRenderer.GetMaterialProxy(0);
                Assert.That(firstResult.Success, Is.True, firstResult.ErrorMessage);
                Assert.That(materialProxy, Is.Not.Null);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(firstMaterial));

                meshletRenderer.SetSourceMaterials(new[] { secondMaterial });
                GPUDrivenMaterialProxyBindingResult secondResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(secondResult.Success, Is.True, secondResult.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.SameAs(materialProxy));
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(secondMaterial));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(firstMaterial);
                Object.DestroyImmediate(secondMaterial);
            }
        }

        [Test]
        public void BuildOrRefreshStreamedVirtualTexture_CreatesGpuSurfaceAssetAndBindsProxy()
        {
            string texturePath = TempFolder + "/SurfaceTexture.asset";
            string sourceMaterialPath = TempFolder + "/SurfaceSource.mat";
            string proxyPath = TempFolder + "/SurfaceProxy.asset";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "SurfaceTexture",
                wrapMode = TextureWrapMode.Repeat,
            };
            texture.SetPixels32(new[]
            {
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255),
                new Color32(0, 0, 255, 255),
                new Color32(255, 255, 255, 255),
            });
            texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, texturePath);

            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            var sourceMaterial = new Material(shader);
            AssetDatabase.CreateAsset(sourceMaterial, sourceMaterialPath);

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.BaseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            materialProxy.SourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterialPath);
            AssetDatabase.CreateAsset(materialProxy, proxyPath);

            bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out string assetPath,
                out bool wasCreated,
                out string errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.True);
            Assert.That(assetPath, Is.EqualTo(TempFolder + "/SurfaceProxy_Surface.vividvt"));
            Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.VirtualTexture));
            Assert.That(materialProxy.StreamedVirtualTexture, Is.Not.Null);
            Assert.That(materialProxy.BaseMap, Is.Null);
            Assert.That(materialProxy.BumpMap, Is.Null);
            Assert.That(materialProxy.MaskMap, Is.Null);
            Assert.That(materialProxy.StreamedVirtualTexture.BuildProfile, Is.EqualTo(VividVirtualTextureBuildProfile.GPUDrivenSurface));
            Assert.That(materialProxy.StreamedVirtualTexture.AddressMode, Is.EqualTo(VividVirtualTextureAddressMode.Repeat));
            Assert.That(materialProxy.StreamedVirtualTexture.StorageProfile, Is.EqualTo(VividVirtualTextureStorageProfile.DesktopBCn));
            var importer = (VividVirtualTextureAssetImporter) AssetImporter.GetAtPath(assetPath);
            Assert.That(importer.StreamCompression, Is.EqualTo(VividVirtualTextureStreamCompression.Zstd));
            Assert.That(importer.SourceTexture, Is.SameAs(texture));
            Assert.That(File.ReadAllText(assetPath).Trim(), Is.EqualTo(VividVirtualTextureAssetImporter.Version3Marker));
            Assert.That(materialProxy.StreamedVirtualTexture.ContentLayerMask, Is.EqualTo(1));
            Assert.That(materialProxy.StreamedVirtualTexture.BuiltData.HasStreamData, Is.True);
            Assert.That(materialProxy.StreamedVirtualTexture.BuiltData.RuntimeStreamDataPath, Is.Not.Empty);
            Assert.That(File.Exists(materialProxy.StreamedVirtualTexture.BuiltData.StreamDataPath), Is.True);

            materialProxy.SourceMaterial = null;
            VividVirtualTextureAsset initialStreamedAsset = materialProxy.StreamedVirtualTexture;
            string streamDataPath = initialStreamedAsset.BuiltData.StreamDataPath;
            var sentinelWriteTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(streamDataPath, sentinelWriteTime);

            success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out _,
                out wasCreated,
                out errorMessage,
                skipIfUpToDate: true);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.False);
            Assert.That(materialProxy.StreamedVirtualTexture, Is.SameAs(initialStreamedAsset));
            Assert.That(materialProxy.BaseMap, Is.Null);
            Assert.That(materialProxy.BumpMap, Is.Null);
            Assert.That(materialProxy.MaskMap, Is.Null);
            Assert.That(File.GetLastWriteTimeUtc(streamDataPath), Is.EqualTo(sentinelWriteTime));

            File.Delete(streamDataPath);
            success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out _,
                out wasCreated,
                out errorMessage,
                skipIfUpToDate: true);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.False);
            Assert.That(materialProxy.StreamedVirtualTexture, Is.SameAs(initialStreamedAsset));
            Assert.That(materialProxy.BaseMap, Is.Null);
            Assert.That(materialProxy.BumpMap, Is.Null);
            Assert.That(materialProxy.MaskMap, Is.Null);
            Assert.That(File.Exists(streamDataPath), Is.True);

            File.SetLastWriteTimeUtc(streamDataPath, sentinelWriteTime);
            success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out _,
                out wasCreated,
                out errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.False);
            Assert.That(materialProxy.StreamedVirtualTexture, Is.SameAs(initialStreamedAsset));
            Assert.That(materialProxy.BaseMap, Is.Null);
            Assert.That(materialProxy.BumpMap, Is.Null);
            Assert.That(materialProxy.MaskMap, Is.Null);
            Assert.That(File.GetLastWriteTimeUtc(streamDataPath), Is.Not.EqualTo(sentinelWriteTime));
        }

        [Test]
        public void BuildOrRefreshStreamedVirtualTexture_SourceMaterialWithoutTexturesClearsStaleBinding()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            string texturePath = TempFolder + "/RemovedSourceTexture.asset";
            string materialPath = TempFolder + "/RemovedSourceMaterial.mat";
            string proxyPath = TempFolder + "/RemovedSourceProxy.asset";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, texturePath);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var sourceMaterial = new Material(shader);
            sourceMaterial.SetTexture("_BaseMap", texture);
            AssetDatabase.CreateAsset(sourceMaterial, materialPath);
            sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.SourceMaterial = sourceMaterial;
            AssetDatabase.CreateAsset(materialProxy, proxyPath);

            bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out _,
                out _,
                out string errorMessage);
            Assert.That(success, Is.True, errorMessage);
            Assert.That(materialProxy.StreamedVirtualTexture, Is.Not.Null);

            sourceMaterial.SetTexture("_BaseMap", null);
            EditorUtility.SetDirty(sourceMaterial);
            AssetDatabase.SaveAssetIfDirty(sourceMaterial);

            success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out string assetPath,
                out bool wasCreated,
                out errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.False);
            Assert.That(assetPath, Is.Empty);
            Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.VirtualTexture));
            Assert.That(materialProxy.StreamedVirtualTexture, Is.Null);
            Assert.That(materialProxy.BaseMap, Is.Null);
            Assert.That(materialProxy.BumpMap, Is.Null);
            Assert.That(materialProxy.MaskMap, Is.Null);
        }

        [Test]
        public void BuildOrRefreshStreamedVirtualTexture_BuildsMaskOnlyTerrainControlAsset()
        {
            string texturePath = TempFolder + "/TerrainControl.asset";
            string virtualTexturePath = TempFolder + "/TerrainControl.vividvt";
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, true, linear: true)
            {
                name = "TerrainControl",
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[8 * 8];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                byte weight = (byte)(pixelIndex * byte.MaxValue / (pixels.Length - 1));
                pixels[pixelIndex] = new Color32((byte)(byte.MaxValue - weight), weight, 0, 0);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            AssetDatabase.CreateAsset(texture, texturePath);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                virtualTexturePath,
                null,
                null,
                texture,
                GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness,
                VividVirtualTextureAddressMode.Clamp,
                out VividVirtualTextureAsset streamedAsset,
                out bool wasCreated,
                out string errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.True);
            Assert.That(streamedAsset, Is.Not.Null);
            Assert.That(streamedAsset.ContentLayerMask, Is.EqualTo(4));
            Assert.That(streamedAsset.StorageProfile, Is.EqualTo(VividVirtualTextureStorageProfile.DesktopBCn));
            var importer = (VividVirtualTextureAssetImporter)AssetImporter.GetAtPath(virtualTexturePath);
            Assert.That(importer.SourceTexture, Is.Null);
            Assert.That(importer.NormalTexture, Is.Null);
            Assert.That(importer.MaskTexture, Is.SameAs(texture));
        }

        private static GameObject CreateMeshletRendererObject(
            string name,
            Mesh mesh,
            Material material,
            out MeshletRenderer meshletRenderer
        )
        {
            return CreateMeshletRendererObject(name, mesh, new[] { material }, out meshletRenderer);
        }

        private static GameObject CreateMeshletRendererObject(
            string name,
            Mesh mesh,
            Material[] materials,
            out MeshletRenderer meshletRenderer
        )
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;
            meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
            return gameObject;
        }

        private static Mesh CreateSingleSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                vertices = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                },
            };

            mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTwoSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                vertices = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, 1.0f, 0.0f),
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                },
                subMeshCount = 2,
            };

            mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            mesh.SetTriangles(new[] { 1, 2, 3 }, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureTempFolderExists()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
            }
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }
    }
}
