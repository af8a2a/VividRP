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
        private const string GeneratedRoot = TempFolder + "/GPUDrivenGenerated";
        private const string MaterialProxyFolder = GeneratedRoot + "/MaterialProxy";
        private const string StreamedVirtualTextureFolder = GeneratedRoot + "/SVT";
        private const string StreamedVirtualTextureBinaryFolder = StreamedVirtualTextureFolder + "/Bin";

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
        public void CreateOrBindMaterialProxies_CreatesAssetInGeneratedFolder_WhenMaterialAssetExists()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            AssetDatabase.CreateFolder(TempFolder, "Meshes");
            AssetDatabase.CreateFolder(TempFolder, "Materials");
            string meshPath = TempFolder + "/Meshes/PersistentMesh.asset";
            string materialPath = TempFolder + "/Materials/PersistentMaterial.mat";
            string meshGeneratedRoot = TempFolder + "/Meshes/GPUDrivenGenerated";
            string meshGeneratedProxyFolder = meshGeneratedRoot + "/MaterialProxy";

            Mesh mesh = CreateSingleSubMeshMesh("PersistentMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var persistentMaterial = new Material(shader);
            AssetDatabase.CreateAsset(persistentMaterial, materialPath);
            persistentMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    persistentMaterial,
                    out string materialGuid,
                    out long materialLocalFileId),
                Is.True);
            string materialIdentifier = materialLocalFileId != 0L
                ? $"{materialGuid}_{unchecked((ulong)materialLocalFileId):X16}"
                : materialGuid;

            GameObject gameObject = CreateMeshletRendererObject("PersistentMaterialRenderer", mesh, persistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                GPUDrivenMaterialProxy generatedProxy = meshletRenderer.GetMaterialProxy(0);
                Assert.That(generatedProxy, Is.Not.Null);
                Assert.That(
                    generatedProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.StandardLit));
                Assert.That(
                    AssetDatabase.GetAssetPath(generatedProxy),
                    Is.EqualTo(
                        $"{meshGeneratedProxyFolder}/PersistentMaterial_{materialIdentifier}_GPUDriven.asset")
                );
                Assert.That(AssetDatabase.IsValidFolder(meshGeneratedRoot), Is.True);
                Assert.That(AssetDatabase.IsValidFolder(meshGeneratedProxyFolder), Is.True);
                Assert.That(AssetDatabase.IsValidFolder(meshGeneratedRoot + "/MeshletAsset"), Is.True);
                Assert.That(AssetDatabase.IsValidFolder(meshGeneratedRoot + "/SVT"), Is.True);
                Assert.That(AssetDatabase.IsValidFolder(meshGeneratedRoot + "/SVT/Bin"), Is.True);
                Assert.That(
                    AssetDatabase.IsValidFolder(TempFolder + "/Materials/GPUDrivenGenerated"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_PersistsProxyAndSourceMaterialThroughPrefabRoundtrip()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/PrefabMesh.asset";
            string materialPath = TempFolder + "/PrefabMaterial.mat";
            string prefabPath = TempFolder + "/MeshletRenderer.prefab";
            Mesh mesh = CreateSingleSubMeshMesh("PrefabMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            GameObject gameObject = null;
            GameObject prefabContents = null;

            try
            {
                gameObject = CreateMeshletRendererObject(
                    "PrefabMeshletRenderer",
                    mesh,
                    material,
                    out MeshletRenderer meshletRenderer);
                GPUDrivenMaterialProxyBindingResult bindingResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(
                        meshletRenderer);
                GPUDrivenMaterialProxy materialProxy =
                    meshletRenderer.GetMaterialProxy(0);
                Assert.That(bindingResult.Success, Is.True, bindingResult.ErrorMessage);
                Assert.That(materialProxy, Is.Not.Null);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material));

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                    gameObject,
                    prefabPath);
                Assert.That(savedPrefab, Is.Not.Null);
                Object.DestroyImmediate(gameObject);
                gameObject = null;

                prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
                MeshletRenderer reloadedRenderer =
                    prefabContents.GetComponent<MeshletRenderer>();
                Assert.That(reloadedRenderer, Is.Not.Null);
                Assert.That(reloadedRenderer.GetMaterialProxy(0), Is.SameAs(materialProxy));
                Assert.That(
                    reloadedRenderer.GetMaterialProxy(0).SourceMaterial,
                    Is.SameAs(material));
            }
            finally
            {
                if (prefabContents != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
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
                GPUDrivenMaterialProxy materialProxy = meshletRenderer.GetMaterialProxy(1);
                Assert.That(materialProxy, Is.Not.Null);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material1));

                var topSlab = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                AssetDatabase.CreateAsset(topSlab, TempFolder + "/SingleSlotTopSlab.asset");
                var definition =
                    ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
                definition.TopSlab = topSlab;
                AssetDatabase.CreateAsset(
                    definition,
                    TempFolder + "/SingleSlotDualSlabDefinition.asset");
                materialProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                materialProxy.DualSlabDefinition = definition;
                materialProxy.LayerWeight = 0.25f;
                EditorUtility.SetDirty(materialProxy);
                AssetDatabase.SaveAssetIfDirty(materialProxy);

                GPUDrivenMaterialProxyBindingResult reboundResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxy(
                        meshletRenderer,
                        1);

                Assert.That(reboundResult.Success, Is.True, reboundResult.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Null);
                Assert.That(meshletRenderer.GetMaterialProxy(1), Is.SameAs(materialProxy));
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.25f).Within(0.0001f));
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
            GPUDrivenMaterialProxy topSlab =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            GPUDrivenDualSlabMaterialDefinition definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();

            try
            {
                definition.TopSlab = topSlab;
                materialProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                materialProxy.DualSlabDefinition = definition;
                materialProxy.LayerWeight = 0.4f;
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                GPUDrivenMaterialProxySyncResult result =
                    GPUDrivenMaterialProxyEditorUtility.SyncMaterialProxyFromSourceMaterial(meshletRenderer, 0);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material));
                Assert.That(materialProxy.BaseColor.r, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.4f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topSlab);
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AutoSync_PreservesPersistentDualTopologyAcrossForcedReimportAndSkipsUnchangedMaterial()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            string materialPath = TempFolder + "/AutoSyncSource.mat";
            string proxyPath = TempFolder + "/AutoSyncProxy.asset";
            string topSlabPath = TempFolder + "/AutoSyncTopSlab.asset";
            string definitionPath = TempFolder + "/AutoSyncDualSlabDefinition.asset";
            var sourceMaterial = new Material(shader);
            AssetDatabase.CreateAsset(sourceMaterial, materialPath);
            sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            var topSlab = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            AssetDatabase.CreateAsset(topSlab, topSlabPath);
            topSlab = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(topSlabPath);
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            definition.TopSlab = topSlab;
            definition.Operator = VividDualSlabOperator.VerticalLayer;
            AssetDatabase.CreateAsset(definition, definitionPath);
            definition = AssetDatabase.LoadAssetAtPath<GPUDrivenDualSlabMaterialDefinition>(
                definitionPath);
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.SourceMaterial = sourceMaterial;
            materialProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
            materialProxy.DualSlabDefinition = definition;
            materialProxy.LayerWeight = 0.45f;
            AssetDatabase.CreateAsset(materialProxy, proxyPath);
            materialProxy = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);

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
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.45f).Within(0.0001f));

                changedProxyCount = GPUDrivenMaterialProxyAutoSyncService.SynchronizeMaterialNowForTests(
                    sourceMaterial,
                    GPUDrivenMaterialProxyTextureMode.Bindless);

                Assert.That(changedProxyCount, Is.Zero);
                Assert.That(materialProxy.Revision, Is.EqualTo(synchronizedRevision));
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.45f).Within(0.0001f));

                GPUDrivenMaterialProxyAutoSyncService.FlushPendingProxySavesForTests();
                Assert.That(EditorUtility.IsDirty(materialProxy), Is.False);
                AssetDatabase.ImportAsset(
                    topSlabPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(
                    definitionPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(
                    proxyPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                GPUDrivenMaterialProxy reloadedTopSlab =
                    AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(topSlabPath);
                GPUDrivenDualSlabMaterialDefinition reloadedDefinition =
                    AssetDatabase.LoadAssetAtPath<GPUDrivenDualSlabMaterialDefinition>(definitionPath);
                GPUDrivenMaterialProxy reloadedProxy =
                    AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);
                Assert.That(reloadedTopSlab, Is.Not.Null);
                Assert.That(reloadedDefinition, Is.Not.Null);
                Assert.That(reloadedProxy, Is.Not.Null);
                Assert.That(
                    reloadedTopSlab.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.StandardLit));
                Assert.That(reloadedDefinition.TopSlab, Is.SameAs(reloadedTopSlab));
                Assert.That(
                    reloadedDefinition.Operator,
                    Is.EqualTo(VividDualSlabOperator.VerticalLayer));
                Assert.That(
                    reloadedProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(reloadedProxy.DualSlabDefinition, Is.SameAs(reloadedDefinition));
                Assert.That(reloadedProxy.LayerWeight, Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(reloadedProxy.BaseColor.r, Is.EqualTo(0.15f).Within(0.0001f));
            }
            finally
            {
                GPUDrivenMaterialProxyAutoSyncService.FlushPendingProxySavesForTests();
            }
        }

        [Test]
        public void AutoSync_RebuildIndexRecoversPendingMaterialLostAcrossReload()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            string materialPath = TempFolder + "/ReloadSource.mat";
            string proxyPath = TempFolder + "/ReloadProxy.asset";
            var sourceMaterial = new Material(shader);
            AssetDatabase.CreateAsset(sourceMaterial, materialPath);
            sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            materialProxy.SourceMaterial = sourceMaterial;
            AssetDatabase.CreateAsset(materialProxy, proxyPath);
            materialProxy = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);

            try
            {
                var expectedBaseColor = new Color(0.2f, 0.4f, 0.6f, 1.0f);
                sourceMaterial.SetColor("_BaseColor", expectedBaseColor);
                EditorUtility.SetDirty(sourceMaterial);
                AssetDatabase.SaveAssetIfDirty(sourceMaterial);

                GPUDrivenMaterialProxyAutoSyncService.FlushPendingProxySavesForTests();
                GPUDrivenMaterialProxyAutoSyncService.QueueMaterial(sourceMaterial);
                GPUDrivenMaterialProxyAutoSyncService.ResetForTests(
                    requestIndexRebuild: true,
                    requeueAllSourceMaterials: true);
                GPUDrivenMaterialProxyAutoSyncService
                    .RebuildIndexAndSynchronizeForTests(TempFolder);

                Assert.That(
                    materialProxy.BaseColor.r,
                    Is.EqualTo(expectedBaseColor.r).Within(0.0001f));
                Assert.That(
                    materialProxy.BaseColor.g,
                    Is.EqualTo(expectedBaseColor.g).Within(0.0001f));
                Assert.That(
                    materialProxy.BaseColor.b,
                    Is.EqualTo(expectedBaseColor.b).Within(0.0001f));
                GPUDrivenMaterialProxyAutoSyncService.FlushPendingProxySavesForTests();
                Assert.That(EditorUtility.IsDirty(materialProxy), Is.False);

                AssetDatabase.ImportAsset(
                    proxyPath,
                    ImportAssetOptions.ForceSynchronousImport
                    | ImportAssetOptions.ForceUpdate);
                GPUDrivenMaterialProxy reloadedProxy =
                    AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(proxyPath);
                Assert.That(reloadedProxy, Is.Not.Null);
                Assert.That(
                    reloadedProxy.BaseColor.r,
                    Is.EqualTo(expectedBaseColor.r).Within(0.0001f));
                Assert.That(
                    reloadedProxy.BaseColor.g,
                    Is.EqualTo(expectedBaseColor.g).Within(0.0001f));
                Assert.That(
                    reloadedProxy.BaseColor.b,
                    Is.EqualTo(expectedBaseColor.b).Within(0.0001f));
            }
            finally
            {
                GPUDrivenMaterialProxyAutoSyncService.ResetForTests(
                    requestIndexRebuild: true,
                    requeueAllSourceMaterials: true);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_CreatesAssetInGeneratedFolder_WhenMaterialIsNonPersistent()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/FallbackMesh.asset";

            Mesh mesh = CreateSingleSubMeshMesh("FallbackMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    mesh,
                    out string meshGuid,
                    out long meshLocalFileId),
                Is.True);
            string meshIdentifier = meshLocalFileId != 0L
                ? $"{meshGuid}_{unchecked((ulong)meshLocalFileId):X16}"
                : meshGuid;

            Material nonPersistentMaterial = new Material(shader);
            GameObject gameObject = CreateMeshletRendererObject("FallbackMaterialRenderer", mesh, nonPersistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo(
                        $"{MaterialProxyFolder}/FallbackMesh_{meshIdentifier}_SubMesh0_GPUDriven.asset")
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
                var topSlab = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                string topSlabPath = TempFolder + "/ReboundTopSlab.asset";
                AssetDatabase.CreateAsset(topSlab, topSlabPath);
                topSlab = AssetDatabase.LoadAssetAtPath<GPUDrivenMaterialProxy>(topSlabPath);
                var definition =
                    ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
                definition.TopSlab = topSlab;
                string definitionPath = TempFolder + "/ReboundDualSlabDefinition.asset";
                AssetDatabase.CreateAsset(definition, definitionPath);
                definition = AssetDatabase.LoadAssetAtPath<GPUDrivenDualSlabMaterialDefinition>(
                    definitionPath);
                materialProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                materialProxy.DualSlabDefinition = definition;
                materialProxy.LayerWeight = 0.6f;
                EditorUtility.SetDirty(materialProxy);
                AssetDatabase.SaveAssetIfDirty(materialProxy);

                meshletRenderer.SetSourceMaterials(new[] { secondMaterial });
                GPUDrivenMaterialProxyBindingResult secondResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(secondResult.Success, Is.True, secondResult.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.SameAs(materialProxy));
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(secondMaterial));
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.6f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(firstMaterial);
                Object.DestroyImmediate(secondMaterial);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_PreservesMatchingLegacyBoundProxy()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/LegacyProxyMesh.asset";
            string materialPath = TempFolder + "/LegacyProxyMaterial.mat";
            string proxyPath = TempFolder + "/LegacyProxyMaterial_GPUDriven.asset";
            Mesh mesh = CreateSingleSubMeshMesh("LegacyProxyMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var legacyProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            legacyProxy.SourceMaterial = material;
            AssetDatabase.CreateAsset(legacyProxy, proxyPath);
            GameObject gameObject = CreateMeshletRendererObject(
                "LegacyProxyRenderer",
                mesh,
                material,
                out MeshletRenderer meshletRenderer);
            meshletRenderer.SetMaterialProxies(new[] { legacyProxy });

            try
            {
                GPUDrivenMaterialProxyBindingResult result =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.SameAs(legacyProxy));
                Assert.That(AssetDatabase.GetAssetPath(legacyProxy), Is.EqualTo(proxyPath));
                Assert.That(AssetDatabase.IsValidFolder(GeneratedRoot), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_SeparatesSameNameMaterialsFromDifferentAssets()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            AssetDatabase.CreateFolder(TempFolder, "MaterialA");
            AssetDatabase.CreateFolder(TempFolder, "MaterialB");
            string meshPath = TempFolder + "/SameNameMaterialMesh.asset";
            Mesh mesh = CreateTwoSubMeshMesh("SameNameMaterialMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var materialA = new Material(shader) { name = "SharedName" };
            var materialB = new Material(shader) { name = "SharedName" };
            AssetDatabase.CreateAsset(materialA, TempFolder + "/MaterialA/SharedName.mat");
            AssetDatabase.CreateAsset(materialB, TempFolder + "/MaterialB/SharedName.mat");
            materialA = AssetDatabase.LoadAssetAtPath<Material>(
                TempFolder + "/MaterialA/SharedName.mat");
            materialB = AssetDatabase.LoadAssetAtPath<Material>(
                TempFolder + "/MaterialB/SharedName.mat");
            GameObject gameObject = CreateMeshletRendererObject(
                "SameNameMaterialRenderer",
                mesh,
                new[] { materialA, materialB },
                out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult firstResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);
                GPUDrivenMaterialProxy proxyA = meshletRenderer.GetMaterialProxy(0);
                GPUDrivenMaterialProxy proxyB = meshletRenderer.GetMaterialProxy(1);

                Assert.That(firstResult.Success, Is.True, firstResult.ErrorMessage);
                Assert.That(proxyA, Is.Not.Null);
                Assert.That(proxyB, Is.Not.Null);
                Assert.That(proxyA, Is.Not.SameAs(proxyB));
                Assert.That(proxyA.SourceMaterial, Is.SameAs(materialA));
                Assert.That(proxyB.SourceMaterial, Is.SameAs(materialB));
                Assert.That(AssetDatabase.GetAssetPath(proxyA), Does.StartWith(MaterialProxyFolder + "/"));
                Assert.That(AssetDatabase.GetAssetPath(proxyB), Does.StartWith(MaterialProxyFolder + "/"));

                GPUDrivenMaterialProxyBindingResult secondResult =
                    GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);
                Assert.That(secondResult.Success, Is.True, secondResult.ErrorMessage);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.SameAs(proxyA));
                Assert.That(meshletRenderer.GetMaterialProxy(1), Is.SameAs(proxyB));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BuildOrRefreshStreamedVirtualTexture_CreatesGpuSurfaceAssetAndBindsProxy()
        {
            string texturePath = TempFolder + "/SurfaceTexture.asset";
            string sourceMaterialPath = TempFolder + "/SurfaceSource.mat";
            string proxyPath = MaterialProxyFolder + "/SurfaceProxy.asset";
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
            GPUDrivenGeneratedAssetPathUtility.EnsureMaterialProxyFolder(TempFolder);
            AssetDatabase.CreateAsset(materialProxy, proxyPath);

            bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                materialProxy,
                out string assetPath,
                out bool wasCreated,
                out string errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(wasCreated, Is.True);
            Assert.That(
                assetPath,
                Is.EqualTo(StreamedVirtualTextureFolder + "/SurfaceProxy_Surface.vividvt"));
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
            Assert.That(
                streamDataPath.Replace('\\', '/'),
                Is.EqualTo(
                    StreamedVirtualTextureBinaryFolder
                    + "/SurfaceProxy_Surface.vividvt.stream"));
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
        public void ResolveStreamedVirtualTextureFolderForProxy_ManualProxyKeepsAdjacentFolder()
        {
            string proxyPath = TempFolder + "/ManualProxy.asset";

            string streamedVirtualTextureFolder = GPUDrivenGeneratedAssetPathUtility
                .ResolveStreamedVirtualTextureFolderForProxy(proxyPath);

            Assert.That(streamedVirtualTextureFolder, Is.EqualTo(TempFolder));
            Assert.That(AssetDatabase.IsValidFolder(GeneratedRoot), Is.False);
        }

        [Test]
        public void EnsureGeneratedFolder_FromGeneratedSubfolderDoesNotNestAnotherRoot()
        {
            string meshletFolder = GPUDrivenGeneratedAssetPathUtility
                .EnsureMeshletAssetFolder(TempFolder);

            string materialProxyFolder = GPUDrivenGeneratedAssetPathUtility
                .EnsureMaterialProxyFolder(meshletFolder);

            Assert.That(materialProxyFolder, Is.EqualTo(MaterialProxyFolder));
            Assert.That(
                AssetDatabase.IsValidFolder(meshletFolder + "/GPUDrivenGenerated"),
                Is.False);
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
            Assert.That(
                streamedAsset.BuiltData.StreamDataPath.Replace('\\', '/'),
                Is.EqualTo(virtualTexturePath + ".stream"));
            Assert.That(File.Exists(streamedAsset.BuiltData.StreamDataPath), Is.True);
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
