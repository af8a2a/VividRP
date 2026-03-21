using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Debug = UnityEngine.Debug;
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
                ctx.LogImportError("Mesh reference is missing.");
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
            Mesh mesh = Selection.objects.OfType<Mesh>().FirstOrDefault();
            if (mesh != null)
            {
                string path = AssetDatabase.GetAssetPath(mesh);
                string folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                string fileName = mesh.name + "_Meshlets." + Extension;
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder ?? "Assets", fileName));

                File.WriteAllText(assetPath, string.Empty);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                if (AssetImporter.GetAtPath(assetPath) is VividMeshletCollectionAssetImporter importer)
                {
                    importer.Mesh = mesh;
                    Save(assetPath, importer);
                }
            }
            else
            {
                ProjectWindowUtil.CreateAssetWithTextContent("New Meshlet Collection." + Extension, string.Empty);
            }
        }

        private static async void Save(string assetPath, VividMeshletCollectionAssetImporter importer)
        {
            EditorUtility.SetDirty(importer);
            AssetDatabase.SaveAssetIfDirty(importer);

            await Task.Yield();

            importer.SaveAndReimport();
            VividMeshletCollectionAsset meshletCollection = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(assetPath);
            Selection.activeObject = meshletCollection;
        }
    }
}
