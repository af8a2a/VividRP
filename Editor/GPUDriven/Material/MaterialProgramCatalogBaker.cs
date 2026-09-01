using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialProgramCatalogBaker
    {
        internal const string CatalogDependencyName =
            "VividRP/MaterialProgramCatalog/PublishedGeneration/v2";

        private const string AssetRelativePath =
            "Runtime/Resources/VividMaterialProgramCatalog.asset";

        private const string StampRelativePath =
            "Shaders/Core/Public/GPUDriven/" +
            "VividMaterialProgramCatalogStamp.generated.hlsl";

        [NoAutoStaticsCleanup]
        private static bool s_IsBaking;

        [NoAutoStaticsCleanup]
        private static bool s_BakeScheduled;

        [NoAutoStaticsCleanup]
        private static MaterialProgramCatalog s_CurrentCatalog;

        [NoAutoStaticsCleanup]
        private static int s_AutoBakeSuppressionCount;

        [NoAutoStaticsCleanup]
        private static uint s_InvalidationRevision;

        internal static string AssetPath =>
            VividPackagePathUtility.GetPreferredAssetPath(AssetRelativePath);

        internal static string StampPath =>
            VividPackagePathUtility.GetPreferredAssetPath(StampRelativePath);

        internal static MaterialProgramCatalog CurrentCatalog =>
            s_CurrentCatalog ?? GPUDrivenMaterialCompiler.ProgramCatalog;

        internal static MaterialProgramCatalogAsset BakeAll()
        {
            return BakeAll(synchronizeGraphImports: true);
        }

        internal static MaterialProgramCatalogAsset BakeAll(
            bool synchronizeGraphImports)
        {
            if (s_IsBaking)
            {
                throw new InvalidOperationException(
                    "A Frozen Material Program Catalog bake is already in progress.");
            }

            s_IsBaking = true;
            try
            {
                MaterialProgramCatalogAsset previous =
                    AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                        AssetPath);
                IReadOnlyList<string> graphPaths = DiscoverGraphPaths();
                var graphDiagnostics = new List<string>();
                MaterialProgramCatalog catalog = BuildCatalog(
                    graphPaths,
                    previous,
                    graphDiagnostics);
                MaterialProgramCatalogAsset asset = BakeCore(
                    catalog,
                    AssetPath,
                    MaterialSurfaceHlslGenerator.GeneratedPath,
                    MaterialCoverageHlslGenerator.GeneratedPath,
                    StampPath);

                PublishCatalogDependency(catalog);
                if (synchronizeGraphImports)
                    SynchronizeGraphImports(graphPaths, catalog);
                for (int diagnosticIndex = 0;
                     diagnosticIndex < graphDiagnostics.Count;
                     diagnosticIndex++)
                {
                    Debug.LogWarning(graphDiagnostics[diagnosticIndex]);
                }
                // CurrentCatalog is only published after every graph importer
                // has bound to the same committed artifact-set stamp.
                s_CurrentCatalog = catalog;
                return asset;
            }
            catch (Exception exception)
            {
                s_CurrentCatalog = null;
                Exception invalidationFailure =
                    TryInvalidatePublishedGeneration();
                if (invalidationFailure != null)
                {
                    throw new AggregateException(
                        "Frozen Material Program Catalog bake failed and its published generation could not be fully invalidated.",
                        exception,
                        invalidationFailure);
                }
                throw;
            }
            finally
            {
                s_IsBaking = false;
            }
        }

        internal static MaterialProgramCatalogAsset Bake(
            MaterialProgramCatalog catalog,
            string assetPath,
            string surfaceHlslPath,
            string coverageHlslPath)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (s_IsBaking)
            {
                throw new InvalidOperationException(
                    "A Frozen Material Program Catalog bake is already in progress.");
            }

            s_IsBaking = true;
            try
            {
                return BakeCore(
                    catalog,
                    assetPath,
                    surfaceHlslPath,
                    coverageHlslPath,
                    GetSiblingStampPath(surfaceHlslPath, coverageHlslPath));
            }
            finally
            {
                s_IsBaking = false;
            }
        }

        internal static MaterialProgramCatalog BuildCatalog(
            IEnumerable<string> graphPaths,
            MaterialProgramCatalogAsset previous,
            ICollection<string> diagnostics = null)
        {
            if (graphPaths == null)
                throw new ArgumentNullException(nameof(graphPaths));

            MaterialProgramCatalog builtin =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var uniquePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string graphPath in graphPaths)
            {
                if (!string.IsNullOrEmpty(graphPath)
                    && IsMaterialGraphPath(graphPath))
                {
                    uniquePaths.Add(graphPath);
                }
            }
            var sortedPaths = new List<string>(uniquePaths);
            sortedPaths.Sort(StringComparer.Ordinal);

            var dynamicPrograms = new Dictionary<string, CompiledMaterialProgram>(
                StringComparer.Ordinal);
            for (int pathIndex = 0; pathIndex < sortedPaths.Count; pathIndex++)
            {
                string graphPath = sortedPaths[pathIndex];
                MaterialGraphEditorGraph graph;
                try
                {
                    graph = GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(
                        graphPath);
                }
                catch (Exception exception)
                {
                    diagnostics?.Add(
                        $"Material Graph '{graphPath}' was excluded from the Frozen Catalog because it could not be loaded: " +
                        $"{exception.GetType().Name}: {exception.Message}");
                    continue;
                }
                if (graph == null)
                {
                    diagnostics?.Add(
                        $"Material Graph '{graphPath}' was excluded from the Frozen Catalog because it could not be loaded.");
                    continue;
                }

                MaterialGraphCompilationResult result =
                    MaterialGraphEditorCompiler.Compile(graph);
                if (!result.Succeeded || result.Program == null)
                {
                    diagnostics?.Add(
                        FormatGraphCompilationFailure(graphPath, result));
                    continue;
                }
                if (builtin.TryGetCatalogedProgram(result.Program, out _))
                    continue;

                bool duplicate = false;
                foreach (CompiledMaterialProgram existing in dynamicPrograms.Values)
                {
                    if (!MaterialProgramCatalog.AreExactlyEquivalent(
                            existing,
                            result.Program))
                    {
                        continue;
                    }
                    duplicate = true;
                    break;
                }
                if (duplicate)
                    continue;

                string stableName = GetDynamicStableName(result.Program);
                if (dynamicPrograms.TryGetValue(
                        stableName,
                        out CompiledMaterialProgram collision))
                {
                    if (!MaterialProgramCatalog.AreExactlyEquivalent(
                            collision,
                            result.Program))
                    {
                        throw new InvalidOperationException(
                            $"Material program stable-name collision '{stableName}'.");
                    }
                    continue;
                }
                dynamicPrograms.Add(stableName, result.Program);
            }

            var slots = new List<MaterialProgramCatalogBakeSlot>();
            for (int slotIndex = 0;
                 slotIndex < builtin.RuntimeTableLength;
                 slotIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry =
                    builtin.Slots[slotIndex];
                slots.Add(entry != null
                    ? MaterialProgramCatalogBakeSlot.ForProgram(
                        builtin.SlotNames[slotIndex],
                        entry.Program)
                    : MaterialProgramCatalogBakeSlot.Reserved(
                        builtin.SlotNames[slotIndex]));
            }

            if (CanPreserveDynamicSlots(previous, builtin))
            {
                for (int slotIndex = builtin.RuntimeTableLength;
                     slotIndex < previous.Slots.Count;
                     slotIndex++)
                {
                    MaterialProgramCatalogAsset.Slot oldSlot =
                        previous.Slots[slotIndex];
                    string stableName = oldSlot.StableName;
                    if (dynamicPrograms.TryGetValue(
                            stableName,
                            out CompiledMaterialProgram program))
                    {
                        ValidatePreservedIdentity(oldSlot, program);
                        slots.Add(MaterialProgramCatalogBakeSlot.ForProgram(
                            stableName,
                            program));
                        dynamicPrograms.Remove(stableName);
                    }
                    else
                    {
                        slots.Add(MaterialProgramCatalogBakeSlot.Reserved(
                            stableName));
                    }
                }
            }

            var newStableNames = new List<string>(dynamicPrograms.Keys);
            newStableNames.Sort(StringComparer.Ordinal);
            for (int nameIndex = 0;
                 nameIndex < newStableNames.Count;
                 nameIndex++)
            {
                string stableName = newStableNames[nameIndex];
                slots.Add(MaterialProgramCatalogBakeSlot.ForProgram(
                    stableName,
                    dynamicPrograms[stableName]));
            }

            return MaterialProgramCatalog.Bake(
                builtin.Templates,
                slots.ToArray());
        }

        internal static IReadOnlyList<string> DiscoverGraphPaths()
        {
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            var graphPaths = new List<string>();
            for (int pathIndex = 0;
                 pathIndex < assetPaths.Length;
                 pathIndex++)
            {
                if (IsMaterialGraphPath(assetPaths[pathIndex]))
                    graphPaths.Add(assetPaths[pathIndex]);
            }
            graphPaths.Sort(StringComparer.Ordinal);
            return graphPaths;
        }

        internal static void ScheduleBake()
        {
            if (s_IsBaking
                || s_BakeScheduled
                || s_AutoBakeSuppressionCount > 0)
                return;
            s_BakeScheduled = true;
            EditorApplication.delayCall += RunScheduledBake;
        }

        internal static IDisposable SuppressAutoBake()
        {
            s_AutoBakeSuppressionCount++;
            return new AutoBakeSuppressionScope();
        }

        internal static bool IsSynchronized()
        {
            MaterialProgramCatalogAsset asset =
                AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                    AssetPath);
            MaterialProgramCatalog catalog;
            try
            {
                catalog = BuildCatalog(DiscoverGraphPaths(), asset);
            }
            catch (Exception)
            {
                return false;
            }
            return asset != null
                && asset.Matches(catalog, out _)
                && MaterialSurfaceHlslGenerator.IsSynchronized(
                    catalog,
                    MaterialSurfaceHlslGenerator.GeneratedPath)
                && MaterialCoverageHlslGenerator.IsSynchronized(
                    catalog,
                    MaterialCoverageHlslGenerator.GeneratedPath)
                && IsStampSynchronized(catalog, StampPath);
        }

        private static MaterialProgramCatalogAsset BakeCore(
            MaterialProgramCatalog catalog,
            string assetPath,
            string surfaceHlslPath,
            string coverageHlslPath,
            string stampPath)
        {
            ValidateOutputPaths(
                assetPath,
                surfaceHlslPath,
                coverageHlslPath,
                stampPath);

            // Build both dispatchers before touching disk so a backend failure
            // cannot leave the catalog in a split Surface/Coverage state.
            string surfaceSource =
                MaterialSurfaceHlslGenerator.BuildSource(catalog);
            string coverageSource =
                MaterialCoverageHlslGenerator.BuildSource(catalog);
            string stampSource =
                MaterialProgramCatalogHlslStampSourceBuilder.BuildSource(catalog);
            GeneratedFileSnapshot surfaceSnapshot =
                GeneratedFileSnapshot.Capture(surfaceHlslPath);
            GeneratedFileSnapshot coverageSnapshot =
                GeneratedFileSnapshot.Capture(coverageHlslPath);
            GeneratedFileSnapshot stampSnapshot =
                GeneratedFileSnapshot.Capture(stampPath);
            bool surfaceChanged =
                surfaceSnapshot.DiffersFrom(surfaceSource);
            bool coverageChanged =
                coverageSnapshot.DiffersFrom(coverageSource);
            bool stampChanged = stampSnapshot.DiffersFrom(stampSource);

            MaterialProgramCatalogAsset asset =
                AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                    assetPath);
            if (asset != null
                && asset.Matches(catalog, out _)
                && !surfaceChanged
                && !coverageChanged
                && !stampChanged)
            {
                ValidateDependentShaderCompilation(
                    surfaceHlslPath,
                    coverageHlslPath,
                    stampPath);
                return asset;
            }

            bool assetCreated = false;
            string assetSnapshot = asset != null
                ? EditorJsonUtility.ToJson(asset)
                : string.Empty;
            try
            {
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<
                        MaterialProgramCatalogAsset>();
                    AssetDatabase.CreateAsset(asset, assetPath);
                    assetCreated = true;
                }

                // The serialized catalog is deliberately unavailable while
                // candidate dispatchers are exchanged and imported.
                asset.Apply(catalog, committed: false);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);

                AssetDatabase.StartAssetEditing();
                try
                {
                    if (surfaceChanged)
                    {
                        WriteGeneratedSourceAtomic(
                            surfaceHlslPath,
                            surfaceSource);
                    }
                    if (coverageChanged)
                    {
                        WriteGeneratedSourceAtomic(
                            coverageHlslPath,
                            coverageSource);
                    }
                    if (stampChanged)
                        WriteGeneratedSourceAtomic(stampPath, stampSource);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                ImportGeneratedSource(surfaceHlslPath, surfaceChanged);
                ImportGeneratedSource(coverageHlslPath, coverageChanged);
                ImportGeneratedSource(stampPath, stampChanged);

                ValidateGeneratedSource(surfaceHlslPath, surfaceSource);
                ValidateGeneratedSource(coverageHlslPath, coverageSource);
                ValidateGeneratedSource(stampPath, stampSource);
                ValidateDependentShaderCompilation(
                    surfaceHlslPath,
                    coverageHlslPath,
                    stampPath);

                // This is the only transition that makes the CPU catalog
                // consumable. Both shader dispatchers already validate the
                // same published stamp independently in their own TUs.
                asset.Seal(catalog);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                if (!asset.Matches(catalog, out string failure))
                {
                    throw new InvalidOperationException(
                        $"Frozen Catalog failed post-commit validation: {failure}");
                }
                return asset;
            }
            catch (Exception exception)
            {
                Exception rollbackFailure = RestoreBakeSnapshot(
                    asset,
                    assetPath,
                    assetSnapshot,
                    assetCreated,
                    surfaceHlslPath,
                    surfaceSnapshot,
                    surfaceChanged,
                    coverageHlslPath,
                    coverageSnapshot,
                    coverageChanged,
                    stampPath,
                    stampSnapshot,
                    stampChanged);
                if (rollbackFailure != null)
                {
                    throw new AggregateException(
                        "Frozen Catalog artifact-set commit and rollback both failed.",
                        exception,
                        rollbackFailure);
                }
                throw;
            }
        }

        private static void WriteGeneratedSourceAtomic(
            string path,
            string source)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string stagingPath = path + ".vivid-staging-" +
                Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(
                    stagingPath,
                    source,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (File.Exists(path))
                    File.Replace(stagingPath, path, null);
                else
                    File.Move(stagingPath, path);
            }
            finally
            {
                if (File.Exists(stagingPath))
                    File.Delete(stagingPath);
            }
        }

        private static void ImportGeneratedSource(
            string path,
            bool changed)
        {
            if (!changed)
                return;
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);
        }

        private static void ValidateGeneratedSource(
            string path,
            string expected)
        {
            if (!File.Exists(path)
                || !string.Equals(
                    File.ReadAllText(path),
                    expected,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated artifact '{path}' did not persist its candidate payload.");
            }
        }

        private static void ValidateDependentShaderCompilation(
            string surfaceHlslPath,
            string coverageHlslPath,
            string stampPath)
        {
            string[] generatedPaths =
            {
                surfaceHlslPath,
                coverageHlslPath,
                stampPath,
            };
            string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
            var failures = new List<string>();
            var visitedShaderPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int guidIndex = 0;
                 guidIndex < shaderGuids.Length;
                 guidIndex++)
            {
                string shaderPath = AssetDatabase.GUIDToAssetPath(
                    shaderGuids[guidIndex]);
                if (string.IsNullOrEmpty(shaderPath)
                    || !visitedShaderPaths.Add(shaderPath)
                    || !ShaderDependsOnGeneratedArtifact(
                        shaderPath,
                        generatedPaths))
                {
                    continue;
                }

                AssetDatabase.ImportAsset(
                    shaderPath,
                    ImportAssetOptions.ForceSynchronousImport
                    | ImportAssetOptions.ForceUpdate);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                    shaderPath);
                if (shader == null)
                    continue;

                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                for (int messageIndex = 0;
                     messageIndex < messages.Length;
                     messageIndex++)
                {
                    ShaderMessage message = messages[messageIndex];
                    if (!IsGeneratedArtifactCompilerError(
                            message.file,
                            string.Equals(
                                message.severity.ToString(),
                                "Error",
                                StringComparison.Ordinal),
                            generatedPaths))
                    {
                        continue;
                    }
                    failures.Add(
                        $"{shaderPath}: {message.file}:{message.line}: " +
                        message.message);
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Generated Material Program HLSL failed Shader compilation:\n" +
                    string.Join("\n", failures));
            }
        }

        internal static bool IsGeneratedArtifactCompilerError(
            string messageFile,
            bool isError,
            IReadOnlyList<string> generatedPaths)
        {
            if (!isError
                || string.IsNullOrEmpty(messageFile)
                || generatedPaths == null)
            {
                return false;
            }
            for (int pathIndex = 0;
                 pathIndex < generatedPaths.Count;
                 pathIndex++)
            {
                if (PathsReferToSameGeneratedArtifact(
                        messageFile,
                        generatedPaths[pathIndex]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ShaderDependsOnGeneratedArtifact(
            string shaderPath,
            IReadOnlyList<string> generatedPaths)
        {
            string[] dependencies = AssetDatabase.GetDependencies(
                shaderPath,
                recursive: true);
            for (int dependencyIndex = 0;
                 dependencyIndex < dependencies.Length;
                 dependencyIndex++)
            {
                string dependencyPath = dependencies[dependencyIndex];
                for (int generatedIndex = 0;
                     generatedIndex < generatedPaths.Count;
                     generatedIndex++)
                {
                    if (PathsReferToSameAsset(
                            dependencyPath,
                            generatedPaths[generatedIndex]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool PathsReferToSameAsset(
            string left,
            string right)
        {
            string normalizedLeft = NormalizeAssetPath(left);
            string normalizedRight = NormalizeAssetPath(right);
            if (string.Equals(
                    normalizedLeft,
                    normalizedRight,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string leftGuid = AssetDatabase.AssetPathToGUID(normalizedLeft);
            string rightGuid = AssetDatabase.AssetPathToGUID(normalizedRight);
            return !string.IsNullOrEmpty(leftGuid)
                && string.Equals(
                    leftGuid,
                    rightGuid,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsReferToSameGeneratedArtifact(
            string messageFile,
            string generatedPath)
        {
            if (string.IsNullOrEmpty(generatedPath))
                return false;
            if (PathsReferToSameAsset(messageFile, generatedPath))
                return true;

            // Unity may report an include using its package alias or only its
            // file name. Dependency filtering already proved that the Shader
            // consumes the actual generated artifact, so this final name
            // comparison cannot admit an unrelated Shader error.
            return string.Equals(
                Path.GetFileName(NormalizeAssetPath(messageFile)),
                Path.GetFileName(NormalizeAssetPath(generatedPath)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }

        private static Exception RestoreBakeSnapshot(
            MaterialProgramCatalogAsset asset,
            string assetPath,
            string assetSnapshot,
            bool assetCreated,
            string surfacePath,
            in GeneratedFileSnapshot surfaceSnapshot,
            bool surfaceChanged,
            string coveragePath,
            in GeneratedFileSnapshot coverageSnapshot,
            bool coverageChanged,
            string stampPath,
            in GeneratedFileSnapshot stampSnapshot,
            bool stampChanged)
        {
            var failures = new List<Exception>();

            // Rollback is itself a publication transaction. Persist an
            // unavailable Catalog before exchanging any generated files so a
            // crash cannot expose old CPU state with candidate dispatchers.
            try
            {
                if (asset != null
                    && AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    asset.InvalidatePublication();
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                    if (asset.IsCommitted)
                    {
                        throw new InvalidOperationException(
                            "Frozen Catalog remained committed while rollback was starting.");
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count == 0)
            {
                TryRestoreGeneratedSource(
                    surfacePath,
                    surfaceSnapshot,
                    surfaceChanged,
                    failures);
                TryRestoreGeneratedSource(
                    coveragePath,
                    coverageSnapshot,
                    coverageChanged,
                    failures);
                TryRestoreGeneratedSource(
                    stampPath,
                    stampSnapshot,
                    stampChanged,
                    failures);
            }

            if (failures.Count == 0)
            {
                try
                {
                    ValidateDependentShaderCompilation(
                        surfacePath,
                        coveragePath,
                        stampPath);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            // Restoring (or deleting) the Catalog is the rollback commit point.
            // Never make the old CPU payload consumable unless every generated
            // artifact has already been restored and compiled successfully.
            if (failures.Count == 0)
            {
                try
                {
                    if (assetCreated)
                    {
                        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null
                            && !AssetDatabase.DeleteAsset(assetPath))
                        {
                            throw new IOException(
                                $"Failed to delete newly created Catalog asset '{assetPath}'.");
                        }
                    }
                    else if (asset != null
                        && !string.IsNullOrEmpty(assetSnapshot))
                    {
                        EditorJsonUtility.FromJsonOverwrite(assetSnapshot, asset);
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                        if (!string.Equals(
                                EditorJsonUtility.ToJson(asset),
                                assetSnapshot,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Existing Frozen Catalog asset did not restore its serialized snapshot.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 0)
                return null;
            return failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "One or more Frozen Catalog artifact snapshots could not be restored.",
                    failures);
        }

        private static void TryRestoreGeneratedSource(
            string path,
            in GeneratedFileSnapshot snapshot,
            bool changed,
            List<Exception> failures)
        {
            try
            {
                RestoreGeneratedSource(path, snapshot, changed);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void RestoreGeneratedSource(
            string path,
            in GeneratedFileSnapshot snapshot,
            bool changed)
        {
            if (!changed)
                return;
            if (!snapshot.Existed)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    if (!AssetDatabase.DeleteAsset(path))
                    {
                        throw new IOException(
                            $"Failed to remove generated artifact '{path}'.");
                    }
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }

            WriteGeneratedSourceAtomic(path, snapshot.Source);
            ImportGeneratedSource(path, changed: true);
            ValidateGeneratedSource(path, snapshot.Source);
        }

        private static void ValidateOutputPaths(
            string assetPath,
            string surfaceHlslPath,
            string coverageHlslPath,
            string stampPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("A catalog asset path is required.", nameof(assetPath));
            if (string.IsNullOrEmpty(surfaceHlslPath))
                throw new ArgumentException("A Surface HLSL path is required.", nameof(surfaceHlslPath));
            if (string.IsNullOrEmpty(coverageHlslPath))
                throw new ArgumentException("A Coverage HLSL path is required.", nameof(coverageHlslPath));
            if (string.IsNullOrEmpty(stampPath))
                throw new ArgumentException("A published stamp path is required.", nameof(stampPath));

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(assetPath),
                Path.GetFullPath(surfaceHlslPath),
                Path.GetFullPath(coverageHlslPath),
                Path.GetFullPath(stampPath),
            };
            if (paths.Count != 4)
            {
                throw new ArgumentException(
                    "Catalog, Surface, Coverage, and published stamp outputs must use distinct paths.");
            }
        }

        private static string GetSiblingStampPath(
            string surfaceHlslPath,
            string coverageHlslPath)
        {
            string surfaceDirectory =
                Path.GetDirectoryName(surfaceHlslPath) ?? string.Empty;
            string coverageDirectory =
                Path.GetDirectoryName(coverageHlslPath) ?? string.Empty;
            if (!string.Equals(
                    Path.GetFullPath(surfaceDirectory),
                    Path.GetFullPath(coverageDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Surface and Coverage dispatchers must share a directory with their published stamp.");
            }
            return Path.Combine(
                    surfaceDirectory,
                    MaterialProgramArtifactSetHlslContract
                        .PublishedStampFileName)
                .Replace('\\', '/');
        }

        private static bool IsStampSynchronized(
            MaterialProgramCatalog catalog,
            string stampPath)
        {
            if (catalog == null
                || string.IsNullOrEmpty(stampPath)
                || !File.Exists(stampPath))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    File.ReadAllText(stampPath),
                    MaterialProgramCatalogHlslStampSourceBuilder.BuildSource(
                        catalog),
                    StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void PublishCatalogDependency(
            MaterialProgramCatalog catalog)
        {
            MaterialProgramArtifactSetHash artifactSetHash =
                MaterialProgramArtifactSetHashBuilder.Compute(catalog);
            AssetDatabase.RegisterCustomDependency(
                CatalogDependencyName,
                Hash128.Compute(
                    $"valid:{artifactSetHash.Version}:" +
                    $"{artifactSetHash.Value:X16}"));
        }

        private static Exception TryInvalidatePublishedGeneration()
        {
            var failures = new List<Exception>();
            try
            {
                MaterialProgramCatalogAsset asset =
                    AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                        AssetPath);
                if (asset != null)
                {
                    asset.InvalidatePublication();
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                string invalidStamp = BuildInvalidStampSource();
                InvalidateGeneratedSource(StampPath, invalidStamp);
                InvalidateGeneratedSource(
                    MaterialSurfaceHlslGenerator.GeneratedPath,
                    BuildInvalidDispatcherSource("Surface"));
                InvalidateGeneratedSource(
                    MaterialCoverageHlslGenerator.GeneratedPath,
                    BuildInvalidDispatcherSource("Coverage"));
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                s_InvalidationRevision++;
                AssetDatabase.RegisterCustomDependency(
                    CatalogDependencyName,
                    Hash128.Compute(
                        $"invalid:{s_InvalidationRevision}:" +
                        $"{MaterialProgramCatalogAsset.AssetSchemaVersion}:" +
                        $"{MaterialProgramContract.RuntimeAbiVersion}"));
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count == 0)
                return null;
            return failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "Published Frozen Catalog generation invalidation failed.",
                    failures);
        }

        private static string BuildInvalidStampSource()
        {
            return "// <auto-generated by MaterialProgramCatalogBaker>\n" +
                "// The Material Program artifact set is invalid; do not edit.\n" +
                "#ifndef VIVID_MATERIAL_PROGRAM_CATALOG_STAMP_GENERATED_INCLUDED\n" +
                "#define VIVID_MATERIAL_PROGRAM_CATALOG_STAMP_GENERATED_INCLUDED\n" +
                "#error Frozen Material Program Catalog artifact publication failed. Re-bake the catalog.\n" +
                "#endif\n";
        }

        private static string BuildInvalidDispatcherSource(string stage)
        {
            return "// <auto-generated by MaterialProgramCatalogBaker>\n" +
                $"// The {stage} dispatcher publication is invalid; do not edit.\n" +
                "#include \"" +
                MaterialProgramArtifactSetHlslContract.PublishedStampFileName +
                "\"\n" +
                $"#error Frozen Material Program Catalog {stage} dispatcher is unavailable. Re-bake the catalog.\n";
        }

        private static void InvalidateGeneratedSource(
            string path,
            string invalidSource)
        {
            if (File.Exists(path)
                && string.Equals(
                    File.ReadAllText(path),
                    invalidSource,
                    StringComparison.Ordinal))
            {
                return;
            }
            WriteGeneratedSourceAtomic(path, invalidSource);
            ImportGeneratedSource(path, changed: true);
        }

        private static bool CanPreserveDynamicSlots(
            MaterialProgramCatalogAsset previous,
            MaterialProgramCatalog builtin)
        {
            if (previous == null
                || !previous.ExtendsBuiltinCatalog(builtin, out _)
                || previous.SchemaVersion
                    != MaterialProgramCatalogAsset.AssetSchemaVersion
                || previous.ProgramCatalogVersion
                    != MaterialProgramContract.ProgramCatalogVersion
                || previous.ManifestVersion
                    != MaterialProgramContract.ProgramCatalogManifestVersion
                || previous.RuntimeAbiVersion
                    != MaterialProgramContract.RuntimeAbiVersion
                || previous.Slots == null
                || previous.Slots.Count < builtin.RuntimeTableLength)
            {
                return false;
            }
            for (int slotIndex = 0;
                 slotIndex < builtin.RuntimeTableLength;
                 slotIndex++)
            {
                MaterialProgramCatalogAsset.Slot slot = previous.Slots[slotIndex];
                if (slot == null
                    || !string.Equals(
                        slot.StableName,
                        builtin.SlotNames[slotIndex],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            var stableNames = new HashSet<string>(StringComparer.Ordinal);
            for (int slotIndex = 0;
                 slotIndex < previous.Slots.Count;
                 slotIndex++)
            {
                MaterialProgramCatalogAsset.Slot slot = previous.Slots[slotIndex];
                if (slot == null
                    || string.IsNullOrEmpty(slot.StableName)
                    || !stableNames.Add(slot.StableName))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidatePreservedIdentity(
            MaterialProgramCatalogAsset.Slot previous,
            CompiledMaterialProgram program)
        {
            if (previous.IsReserved)
                return;
            if (previous.CompiledHash != program.CompiledHash
                || previous.LayoutFingerprint
                    != program.Lowering.LayoutFingerprint
                || previous.CoveragePayloadHash
                    != program.CoverageHlsl.PayloadHash
                || previous.SurfacePayloadHash
                    != program.SurfaceHlsl.PayloadHash)
            {
                throw new InvalidOperationException(
                    $"Frozen catalog slot '{previous.StableName}' no longer matches its content-addressed payload.");
            }
        }

        private static string GetDynamicStableName(
            CompiledMaterialProgram program)
        {
            return $"G.{program.CompiledHash.Version}." +
                $"{program.CompiledHash.Value:X16}." +
                $"{program.Lowering.LayoutFingerprint.Value:X16}." +
                $"{program.CoverageHlsl.PayloadHash:X16}." +
                $"{program.SurfaceHlsl.PayloadHash:X16}";
        }

        private static bool IsMaterialGraphPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(
                    $".{MaterialGraphEditorGraph.AssetExtension}",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void RunScheduledBake()
        {
            s_BakeScheduled = false;
            if (s_AutoBakeSuppressionCount > 0)
                return;
            try
            {
                BakeAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void SynchronizeGraphImports(
            IReadOnlyList<string> graphPaths,
            MaterialProgramCatalog catalog)
        {
            for (int pathIndex = 0;
                 pathIndex < graphPaths.Count;
                 pathIndex++)
            {
                string graphPath = graphPaths[pathIndex];
                MaterialGraphCompilationResult result = null;
                try
                {
                    MaterialGraphEditorGraph graph =
                        GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(
                            graphPath);
                    result = graph != null
                        ? MaterialGraphEditorCompiler.Compile(graph)
                        : null;
                }
                catch (Exception)
                {
                    // The importer owns the detailed load diagnostic. The
                    // synchronization contract only requires a failed sentinel.
                }
                if (result == null || !result.Succeeded || result.Program == null)
                {
                    SynchronizeFailedGraphImport(graphPath);
                    continue;
                }
                if (!catalog.TryGetCatalogedProgram(
                        result.Program,
                        out MaterialProgramCatalog.ManifestEntry expected))
                {
                    throw new InvalidOperationException(
                        $"Material Graph '{graphPath}' is missing from the freshly baked Frozen Catalog.");
                }

                MaterialGraphImportAsset imported =
                    AssetDatabase.LoadAssetAtPath<MaterialGraphImportAsset>(
                        graphPath);
                if (!MatchesImportedProgram(imported, expected, catalog))
                {
                    AssetDatabase.ImportAsset(
                        graphPath,
                        ImportAssetOptions.ForceSynchronousImport
                        | ImportAssetOptions.ForceUpdate);
                    imported =
                        AssetDatabase.LoadAssetAtPath<MaterialGraphImportAsset>(
                            graphPath);
                }
                if (!MatchesImportedProgram(imported, expected, catalog))
                {
                    throw new InvalidOperationException(
                        $"Material Graph '{graphPath}' did not bind to the freshly baked Frozen Catalog.");
                }
            }
        }

        private static bool MatchesImportedProgram(
            MaterialGraphImportAsset imported,
            MaterialProgramCatalog.ManifestEntry expected,
            MaterialProgramCatalog catalog)
        {
            return imported != null
                && imported.Succeeded
                && imported.IsCataloged
                && imported.CatalogManifestHash == catalog.ManifestHash
                && imported.ArtifactSetHash
                    == MaterialProgramArtifactSetHashBuilder.Compute(catalog)
                && imported.ProgramID == expected.ProgramID
                && imported.CompiledProgramHash == expected.Program.CompiledHash
                && imported.LayoutFingerprint == expected.LayoutFingerprint;
        }

        private static void SynchronizeFailedGraphImport(string graphPath)
        {
            MaterialGraphImportAsset imported =
                AssetDatabase.LoadAssetAtPath<MaterialGraphImportAsset>(
                    graphPath);
            if (!MatchesFailedImport(imported))
            {
                AssetDatabase.ImportAsset(
                    graphPath,
                    ImportAssetOptions.ForceSynchronousImport
                    | ImportAssetOptions.ForceUpdate);
                imported =
                    AssetDatabase.LoadAssetAtPath<MaterialGraphImportAsset>(
                        graphPath);
            }
            if (!MatchesFailedImport(imported))
            {
                throw new InvalidOperationException(
                    $"Invalid Material Graph '{graphPath}' did not publish a failed import sentinel.");
            }
        }

        private static bool MatchesFailedImport(
            MaterialGraphImportAsset imported)
        {
            return imported != null
                && !imported.Succeeded
                && imported.ProgramVersion
                    == GPUDrivenMaterialCompiler.ProgramVersion
                && !imported.IsCataloged
                && imported.ProgramID == VividMaterialProgramID.Invalid
                && imported.CatalogManifestHash == default
                && imported.CompiledProgramHash == default
                && imported.LayoutFingerprint == default
                && imported.ArtifactSetHash == default
                && imported.Diagnostics != null
                && imported.Diagnostics.Count > 0;
        }

        private static string FormatGraphCompilationFailure(
            string graphPath,
            MaterialGraphCompilationResult result)
        {
            var message = new StringBuilder()
                .Append("Material Graph '")
                .Append(graphPath)
                .Append("' was excluded from the Frozen Catalog because it failed to compile.");
            if (result == null || result.Diagnostics == null)
                return message.ToString();

            for (int diagnosticIndex = 0;
                 diagnosticIndex < result.Diagnostics.Count;
                 diagnosticIndex++)
            {
                MaterialGraphDiagnostic diagnostic =
                    result.Diagnostics[diagnosticIndex];
                message.AppendLine()
                    .Append(diagnostic.Code)
                    .Append(": ")
                    .Append(diagnostic.Message);
            }
            return message.ToString();
        }

        private readonly struct GeneratedFileSnapshot
        {
            private GeneratedFileSnapshot(bool existed, string source)
            {
                Existed = existed;
                Source = source;
            }

            internal bool Existed { get; }

            internal string Source { get; }

            internal static GeneratedFileSnapshot Capture(string path)
            {
                return File.Exists(path)
                    ? new GeneratedFileSnapshot(true, File.ReadAllText(path))
                    : new GeneratedFileSnapshot(false, string.Empty);
            }

            internal bool DiffersFrom(string source)
            {
                return !Existed
                    || !string.Equals(Source, source, StringComparison.Ordinal);
            }
        }

        private sealed class AutoBakeSuppressionScope : IDisposable
        {
            private bool m_Disposed;

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                s_AutoBakeSuppressionCount--;
            }
        }

        [MenuItem("VividRP/GPU Driven/Bake Frozen Material Program Catalog")]
        private static void BakeFromMenu()
        {
            try
            {
                MaterialProgramCatalogAsset asset = BakeAll();
                Debug.Log(
                    $"Baked frozen Material Program Catalog at '{AssetDatabase.GetAssetPath(asset)}'.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    internal sealed class MaterialProgramCatalogGraphPostprocessor :
        AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsMaterialGraph(importedAssets)
                || ContainsMaterialGraph(deletedAssets)
                || ContainsMaterialGraph(movedAssets)
                || ContainsMaterialGraph(movedFromAssetPaths))
            {
                MaterialProgramCatalogBaker.ScheduleBake();
            }
        }

        private static bool ContainsMaterialGraph(string[] paths)
        {
            if (paths == null)
                return false;
            for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
            {
                if (!string.IsNullOrEmpty(paths[pathIndex])
                    && paths[pathIndex].EndsWith(
                        $".{MaterialGraphEditorGraph.AssetExtension}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
