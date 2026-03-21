using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    [ScriptedImporter(1, Extension)]
    internal sealed class VividMeshletCollectionAssetImporter : ScriptedImporter
    {
        internal const string Extension = "vmeshletcollection";

        public Mesh Mesh;
        public int SubMeshIndex;
        public bool OptimizeVertexCache;

        [Range(0.0f, 0.25f)]
        public float TargetError = 0.01f;

        [Range(0.0f, 0.25f)]
        public float TargetErrorSloppy = 0.001f;

        [Range(0.0f, 1.0f)]
        public float MinTriangleReductionPerStep = 0.8f;

        [Range(0, 10)]
        public int MaxMeshLODLevelCount;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            meshletCollection.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            ctx.AddObjectToAsset(nameof(VividMeshletCollectionAsset), meshletCollection);
            ctx.SetMainObject(meshletCollection);

            if (Mesh == null)
            {
                return;
            }

            var timer = Stopwatch.StartNew();

            int clampedSubMeshIndex = Mathf.Clamp(SubMeshIndex, 0, Mathf.Max(0, Mesh.subMeshCount - 1));
            VividMeshletCollectionBuilder.Generate(meshletCollection, new VividMeshletCollectionBuilder.Parameters
            {
                Mesh = Mesh,
                SourceMeshGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Mesh)),
                SubMeshIndex = clampedSubMeshIndex,
                OptimizeVertexCache = OptimizeVertexCache,
                MaxMeshLODLevelCount = MaxMeshLODLevelCount,
                TargetError = TargetError,
                TargetErrorSloppy = TargetErrorSloppy,
                MinTriangleReductionPerStep = MinTriangleReductionPerStep,
                LogErrorHandler = message => ctx.LogImportError(message),
            });

            timer.Stop();
            Debug.Log($"Building meshlets for {ctx.assetPath} took {timer.Elapsed.TotalMilliseconds:F3} ms.", meshletCollection);
        }

        [MenuItem("Assets/Create/VividRP/Meshlet Collection")]
        private static void CreateNewAsset(MenuCommand menuCommand)
        {
            string[] createdAssetPaths = CreateAssetsForSelection(Selection.objects);
            if (createdAssetPaths.Length > 0)
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(createdAssetPaths[^1]);
                return;
            }

            ProjectWindowUtil.CreateAssetWithTextContent("New Meshlet Collection." + Extension, string.Empty);
        }

        internal static string[] CreateAssetsForSelection(IEnumerable<Object> selection)
        {
            var createdAssetPaths = new List<string>();
            foreach (MeshSourceSelection sourceSelection in CollectMeshSelections(selection))
            {
                string assetPath = CreateAssetForMesh(sourceSelection);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    createdAssetPaths.Add(assetPath);
                }
            }

            return createdAssetPaths.ToArray();
        }

        internal static Mesh[] ResolveMeshesFromSelection(Object selection)
        {
            var meshes = new List<Mesh>();
            var dedupe = new HashSet<string>();
            AppendMeshesFromSelection(selection, meshes, dedupe);
            return meshes.ToArray();
        }

        private static IEnumerable<MeshSourceSelection> CollectMeshSelections(IEnumerable<Object> selection)
        {
            var results = new List<MeshSourceSelection>();
            var dedupe = new HashSet<string>();

            foreach (Object selectedObject in selection ?? Enumerable.Empty<Object>())
            {
                Mesh[] meshes = ResolveMeshesFromSelection(selectedObject);
                if (meshes.Length == 0)
                {
                    continue;
                }

                string selectionName = GetSelectionDisplayName(selectedObject);
                string targetFolder = GetTargetFolder(selectedObject);
                bool prefixWithSelectionName = meshes.Length > 1 && !(selectedObject is Mesh);

                foreach (Mesh mesh in meshes)
                {
                    int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                    string baseName = GetBaseName(selectionName, mesh, prefixWithSelectionName);

                    for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                    {
                        string dedupeKey = GetMeshKey(mesh) + "|" + subMeshIndex;
                        if (!dedupe.Add(dedupeKey))
                        {
                            continue;
                        }

                        string assetBaseName = subMeshCount > 1 ? $"{baseName}_SubMesh{subMeshIndex}" : baseName;
                        results.Add(new MeshSourceSelection(mesh, subMeshIndex, assetBaseName, targetFolder));
                    }
                }
            }

            return results;
        }

        private static string CreateAssetForMesh(in MeshSourceSelection sourceSelection)
        {
            if (sourceSelection.Mesh == null)
            {
                return string.Empty;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceSelection.Mesh);
            string folder = !string.IsNullOrEmpty(sourceSelection.TargetFolder)
                ? sourceSelection.TargetFolder
                : (File.Exists(sourcePath) ? Path.GetDirectoryName(sourcePath) : sourcePath);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder ?? "Assets", sourceSelection.AssetBaseName + "_Meshlets." + Extension));

            File.WriteAllText(assetPath, string.Empty);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(assetPath) is not VividMeshletCollectionAssetImporter importer)
            {
                return string.Empty;
            }

            importer.Mesh = sourceSelection.Mesh;
            importer.SubMeshIndex = sourceSelection.SubMeshIndex;
            Save(assetPath, importer);
            return assetPath;
        }

        private static void Save(string assetPath, VividMeshletCollectionAssetImporter importer)
        {
            EditorUtility.SetDirty(importer);
            AssetDatabase.SaveAssetIfDirty(importer);
            importer.SaveAndReimport();
        }

        private static void AppendMeshesFromSelection(Object selection, List<Mesh> results, HashSet<string> dedupe)
        {
            if (selection == null)
            {
                return;
            }

            if (selection is Mesh mesh)
            {
                AddMesh(mesh, results, dedupe);
                return;
            }

            if (selection is GameObject gameObject)
            {
                AppendMeshesFromGameObject(gameObject, results, dedupe);
                if (results.Count > 0)
                {
                    return;
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(selection);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            GameObject assetGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (assetGameObject != null)
            {
                AppendMeshesFromGameObject(assetGameObject, results, dedupe);
                if (results.Count > 0)
                {
                    return;
                }
            }

            foreach (Mesh embeddedMesh in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>())
            {
                AddMesh(embeddedMesh, results, dedupe);
            }
        }

        private static void AppendMeshesFromGameObject(GameObject gameObject, List<Mesh> results, HashSet<string> dedupe)
        {
            foreach (MeshFilter meshFilter in gameObject.GetComponentsInChildren<MeshFilter>(true))
            {
                AddMesh(meshFilter.sharedMesh, results, dedupe);
            }

            foreach (SkinnedMeshRenderer skinnedMeshRenderer in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                AddMesh(skinnedMeshRenderer.sharedMesh, results, dedupe);
            }
        }

        private static void AddMesh(Mesh mesh, List<Mesh> results, HashSet<string> dedupe)
        {
            if (mesh == null)
            {
                return;
            }

            string meshKey = GetMeshKey(mesh);
            if (!dedupe.Add(meshKey))
            {
                return;
            }

            results.Add(mesh);
        }

        private static string GetSelectionDisplayName(Object selection)
        {
            string path = AssetDatabase.GetAssetPath(selection);
            if (!string.IsNullOrEmpty(path))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return selection != null ? selection.name : "Mesh";
        }

        private static string GetBaseName(string selectionName, Mesh mesh, bool prefixWithSelectionName)
        {
            if (prefixWithSelectionName && !string.Equals(selectionName, mesh.name))
            {
                return selectionName + "_" + mesh.name;
            }

            return mesh.name;
        }

        private static string GetMeshKey(Mesh mesh)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId))
            {
                return guid + ":" + localId;
            }

            string path = AssetDatabase.GetAssetPath(mesh);
            return path + ":" + mesh.name;
        }

        private static string GetTargetFolder(Object selection)
        {
            string path = AssetDatabase.GetAssetPath(selection);
            return File.Exists(path) ? Path.GetDirectoryName(path) : path;
        }

        private readonly struct MeshSourceSelection
        {
            public MeshSourceSelection(Mesh mesh, int subMeshIndex, string assetBaseName, string targetFolder)
            {
                Mesh = mesh;
                SubMeshIndex = subMeshIndex;
                AssetBaseName = assetBaseName;
                TargetFolder = targetFolder;
            }

            public Mesh Mesh { get; }

            public int SubMeshIndex { get; }

            public string AssetBaseName { get; }

            public string TargetFolder { get; }
        }
    }
}
