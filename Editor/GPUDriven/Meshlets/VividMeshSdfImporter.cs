using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    [ScriptedImporter(1, Extension)]
    internal sealed class VividMeshSdfImporter : ScriptedImporter
    {
        private const string Extension = "vmeshsdf";
        private const string CreateMenu = "Assets/Create/VividRP/Mesh SDF";

        [Min(0.0001f)] public float TargetVoxelSize = 0.1f;
        [Range(16, 256)] public int MaxAxisSamples = 256;
        public VividMeshSdfSignMode SignMode;
        [Min(0f), Tooltip("Total thickness in mesh-local units. Zero uses two voxels.")]
        public float ShellThickness;
        [Tooltip("Optional bake geometry authored in the source mesh's local space.")]
        public Mesh ReplacementMesh;

        [Serializable]
        private sealed class Source
        {
            public string Guid;
            public long LocalFileId;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<VividMeshSdfAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("MeshSdf", asset);
            ctx.SetMainObject(asset);
            try
            {
                var source = JsonUtility.FromJson<Source>(File.ReadAllText(ctx.assetPath));
                string meshPath = AssetDatabase.GUIDToAssetPath(source?.Guid ?? string.Empty);
                if (string.IsNullOrEmpty(meshPath) || meshPath == ctx.assetPath)
                    throw new ArgumentException("Mesh SDF source asset could not be located.");

                ctx.DependsOnArtifact(meshPath);
                Mesh mesh = null;
                foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(meshPath))
                {
                    if (candidate is Mesh sourceMesh
                        && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sourceMesh, out _, out long localFileId)
                        && localFileId == source.LocalFileId)
                    {
                        mesh = sourceMesh;
                        break;
                    }
                }
                if (mesh == null)
                    throw new ArgumentException("Mesh SDF source Mesh could not be located in the imported asset.");

                if (ReplacementMesh != null)
                    ctx.DependsOnArtifact(AssetDatabase.GetAssetPath(ReplacementMesh));

                VividMeshSdfBaker.Bake(asset, mesh, ReplacementMesh != null ? ReplacementMesh : mesh,
                    TargetVoxelSize, MaxAxisSamples, SignMode, ShellThickness);
                asset.name = mesh.name + " Mesh SDF";
                ctx.AddObjectToAsset("DistanceField", asset.DistanceField);
            }
            catch (ArgumentException exception)
            {
                ctx.LogImportError(exception.Message);
            }
        }

        [MenuItem(CreateMenu, true)]
        private static bool CanCreate()
        {
            return Selection.activeObject != null
                && VividMeshletCollectionAssetImporter.ResolveMeshesFromSelection(Selection.activeObject).Length > 0;
        }

        [MenuItem(CreateMenu)]
        private static void CreateSelected()
        {
            var meshes = new HashSet<Mesh>();
            var assets = new List<Object>();
            foreach (Object selected in Selection.objects)
            {
                foreach (Mesh mesh in VividMeshletCollectionAssetImporter.ResolveMeshesFromSelection(selected))
                {
                    if (!meshes.Add(mesh))
                        continue;
                    VividMeshSdfAsset asset = CreateForMesh(mesh);
                    if (asset != null)
                        assets.Add(asset);
                }
            }
            if (assets.Count > 0)
                Selection.objects = assets.ToArray();
        }

        internal static VividMeshSdfAsset CreateForMesh(Mesh mesh)
        {
            if (mesh == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localFileId))
                return null;

            string sourcePath = AssetDatabase.GetAssetPath(mesh);
            string sourceFolder = sourcePath.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.GetDirectoryName(sourcePath)?.Replace('\\', '/')
                : "Assets";
            string folder = GPUDrivenGeneratedAssetPathUtility.EnsureMeshSdfFolder(sourceFolder);
            string path = FormattableString.Invariant($"{folder}/MeshSDF_{guid}_{localFileId}.{Extension}");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonUtility.ToJson(new Source { Guid = guid, LocalFileId = localFileId }, true));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var asset = AssetDatabase.LoadAssetAtPath<VividMeshSdfAsset>(path);
            return asset != null && asset.SourceMesh == mesh && asset.DistanceField != null ? asset : null;
        }
    }
}
