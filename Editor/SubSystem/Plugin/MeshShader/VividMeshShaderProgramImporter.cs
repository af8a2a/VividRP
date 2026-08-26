using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.MeshShader;

namespace VividRP.Editor.MeshShader
{
    [ScriptedImporter(1, Extension)]
    internal sealed class VividMeshShaderProgramImporter : ScriptedImporter
    {
        internal const string Extension = "vms";

        private const string VividPackageName = "com.vivid.render-pipelines";
        private const string CorePackageName = "com.unity.render-pipelines.core";
        private const string CompilerPluginDirectory =
            "Editor/SubSystem/Plugin/MeshShader/Plugins/x86_64";

        private static readonly Regex s_IncludePattern = new Regex(
            @"^\s*#\s*include(?:_with_pragmas)?\s*[<""](?<path>[^>""]+)[>""]",
            RegexOptions.Compiled | RegexOptions.Multiline);

        [Serializable]
        internal sealed class Manifest
        {
            public string source = string.Empty;
            public string amplificationEntry = "AmplificationMain";
            public string amplificationProfile = "as_6_5";
            public string meshEntry = "MeshMain";
            public string meshProfile = "ms_6_5";
            public string pixelEntry = "PixelMain";
            public string pixelProfile = "ps_6_5";
            public uint rootLayoutVersion = VividMeshShaderProgramAsset.CurrentRootLayoutVersion;
            public bool debug;
            public bool disableOptimizations;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                ctx.LogImportError("Vivid mesh shaders currently require the Windows DXC compiler plugin.");
                return;
            }

            if (!TryReadManifest(ctx, out Manifest manifest))
                return;
            if (!ValidateManifest(ctx, manifest))
                return;
            if (!TryResolvePhysicalAssetPath(manifest.source, out string sourcePhysicalPath))
            {
                ctx.LogImportError($"Could not resolve mesh shader source '{manifest.source}'.");
                return;
            }
            if (!File.Exists(sourcePhysicalPath))
            {
                ctx.LogImportError($"Mesh shader source does not exist: '{manifest.source}'.");
                return;
            }

            ctx.DependsOnSourceAsset(GetAssetDatabasePath(manifest.source));
            RegisterCompilerDependencies(ctx);
            RegisterIncludeDependencies(ctx, manifest.source, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            if (!TryGetPackageRoot(VividPackageName, out string vividPackageRoot)
                || !TryGetPackageRoot(CorePackageName, out string corePackageRoot))
            {
                ctx.LogImportError("Could not resolve the VividRP or CoreRP package root for DXC includes.");
                return;
            }

            var includeRoots = new[]
            {
                new VividMeshShaderCompiler.IncludeRoot(
                    $"Packages/{VividPackageName}/",
                    vividPackageRoot),
                new VividMeshShaderCompiler.IncludeRoot(
                    $"Packages/{CorePackageName}/",
                    corePackageRoot),
                new VividMeshShaderCompiler.IncludeRoot(
                    string.Empty,
                    Path.GetDirectoryName(sourcePhysicalPath)),
            };

            if (!VividMeshShaderCompiler.TryGetCompilerVersion(
                    out string compilerVersion,
                    out string compilerError))
            {
                ctx.LogImportError($"Could not load VividMeshShaderCompiler: {compilerError}");
                return;
            }

            var compileFlags = VividMeshShaderCompiler.CompileFlags.None;
            if (manifest.debug)
                compileFlags |= VividMeshShaderCompiler.CompileFlags.Debug;
            if (manifest.disableOptimizations)
                compileFlags |= VividMeshShaderCompiler.CompileFlags.DisableOptimizations;

            string source = File.ReadAllText(sourcePhysicalPath);
            if (!TryCompileStage(
                    ctx,
                    source,
                    manifest.source,
                    manifest.amplificationEntry,
                    manifest.amplificationProfile,
                    "amplification",
                    includeRoots,
                    compileFlags,
                    out byte[] amplificationDxil)
                || !TryCompileStage(
                    ctx,
                    source,
                    manifest.source,
                    manifest.meshEntry,
                    manifest.meshProfile,
                    "mesh",
                    includeRoots,
                    compileFlags,
                    out byte[] meshDxil)
                || !TryCompileStage(
                    ctx,
                    source,
                    manifest.source,
                    manifest.pixelEntry,
                    manifest.pixelProfile,
                    "pixel",
                    includeRoots,
                    compileFlags,
                    out byte[] pixelDxil))
            {
                return;
            }

            var program = ScriptableObject.CreateInstance<VividMeshShaderProgramAsset>();
            program.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            program.Initialize(
                amplificationDxil,
                meshDxil,
                pixelDxil,
                manifest.source,
                $"{compilerVersion} (VMSC ABI {VividMeshShaderCompiler.AbiVersion})",
                VividMeshShaderCompiler.AbiVersion,
                manifest.rootLayoutVersion);

            ctx.AddObjectToAsset(nameof(VividMeshShaderProgramAsset), program);
            ctx.SetMainObject(program);
        }

        private static void RegisterCompilerDependencies(AssetImportContext ctx)
        {
            string packagePrefix = $"Packages/{VividPackageName}/{CompilerPluginDirectory}";
            ctx.DependsOnSourceAsset(
                GetAssetDatabasePath($"{packagePrefix}/VividMeshShaderCompiler.dll"));
            ctx.DependsOnSourceAsset(
                GetAssetDatabasePath($"{packagePrefix}/dxcompiler.dll"));
            ctx.DependsOnSourceAsset(
                GetAssetDatabasePath($"{packagePrefix}/dxil.dll"));
        }

        private static bool TryReadManifest(AssetImportContext ctx, out Manifest manifest)
        {
            manifest = null;
            if (!TryResolvePhysicalAssetPath(ctx.assetPath, out string physicalPath)
                || !File.Exists(physicalPath))
            {
                ctx.LogImportError($"Could not read mesh shader manifest '{ctx.assetPath}'.");
                return false;
            }

            try
            {
                manifest = new Manifest();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(physicalPath), manifest);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ctx.LogImportError($"Could not parse mesh shader manifest '{ctx.assetPath}': {exception.Message}");
                return false;
            }
        }

        private static bool ValidateManifest(AssetImportContext ctx, Manifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.source))
            {
                ctx.LogImportError("The mesh shader manifest must define a source asset path.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.amplificationEntry)
                || string.IsNullOrWhiteSpace(manifest.amplificationProfile)
                || string.IsNullOrWhiteSpace(manifest.meshEntry)
                || string.IsNullOrWhiteSpace(manifest.meshProfile)
                || string.IsNullOrWhiteSpace(manifest.pixelEntry)
                || string.IsNullOrWhiteSpace(manifest.pixelProfile))
            {
                ctx.LogImportError("The mesh shader manifest must define all stage entry points and profiles.");
                return false;
            }

            if (manifest.rootLayoutVersion != VividMeshShaderProgramAsset.CurrentRootLayoutVersion)
            {
                ctx.LogImportError(
                    $"Unsupported mesh shader root layout version {manifest.rootLayoutVersion}; "
                    + $"expected {VividMeshShaderProgramAsset.CurrentRootLayoutVersion}.");
                return false;
            }

            return true;
        }

        private static bool TryCompileStage(
            AssetImportContext ctx,
            string source,
            string sourceName,
            string entryPoint,
            string profile,
            string stageName,
            VividMeshShaderCompiler.IncludeRoot[] includeRoots,
            VividMeshShaderCompiler.CompileFlags flags,
            out byte[] dxil)
        {
            dxil = null;
            VividMeshShaderCompiler.CompilationResult result;
            try
            {
                result = VividMeshShaderCompiler.Compile(
                    source,
                    sourceName,
                    entryPoint,
                    profile,
                    includeRoots,
                    flags);
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException
                                              or InvalidOperationException
                                              or ArgumentException)
            {
                ctx.LogImportError($"Could not compile the {stageName} mesh-shader stage: {exception.Message}");
                return false;
            }

            if (!result.Success || result.Dxil.Length == 0)
            {
                string diagnostics = string.IsNullOrWhiteSpace(result.Diagnostics)
                    ? "DXC produced no diagnostics."
                    : result.Diagnostics.Trim();
                ctx.LogImportError(
                    $"DXC failed for {entryPoint}/{profile} ({stageName} stage):\n{diagnostics}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(result.Diagnostics))
            {
                ctx.LogImportWarning(
                    $"DXC diagnostics for {entryPoint}/{profile} ({stageName} stage):\n"
                    + result.Diagnostics.Trim());
            }

            dxil = result.Dxil;
            return true;
        }

        private static void RegisterIncludeDependencies(
            AssetImportContext ctx,
            string sourceAssetPath,
            HashSet<string> visitedAssetPaths)
        {
            sourceAssetPath = NormalizeAssetPath(sourceAssetPath);
            if (!visitedAssetPaths.Add(sourceAssetPath))
                return;
            if (!TryResolvePhysicalAssetPath(sourceAssetPath, out string physicalPath)
                || !File.Exists(physicalPath))
            {
                return;
            }

            string source = File.ReadAllText(physicalPath);
            MatchCollection matches = s_IncludePattern.Matches(source);
            for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                string includePath = matches[matchIndex].Groups["path"].Value;
                if (!TryResolveIncludeAssetPath(sourceAssetPath, includePath, out string includeAssetPath))
                    continue;

                ctx.DependsOnSourceAsset(GetAssetDatabasePath(includeAssetPath));
                RegisterIncludeDependencies(ctx, includeAssetPath, visitedAssetPaths);
            }
        }

        private static bool TryResolveIncludeAssetPath(
            string includingAssetPath,
            string includePath,
            out string includeAssetPath)
        {
            includeAssetPath = string.Empty;
            if (string.IsNullOrWhiteSpace(includePath))
                return false;

            includePath = NormalizeAssetPath(includePath);
            if (includePath.StartsWith("Packages/", StringComparison.Ordinal)
                || includePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                includeAssetPath = includePath;
                return true;
            }

            string includingDirectory = Path.GetDirectoryName(NormalizeAssetPath(includingAssetPath))
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(includingDirectory))
                return false;

            includeAssetPath = CollapseAssetPath($"{includingDirectory}/{includePath}");
            return includeAssetPath.StartsWith("Packages/", StringComparison.Ordinal)
                   || includeAssetPath.StartsWith("Assets/", StringComparison.Ordinal);
        }

        internal static bool TryResolvePhysicalAssetPath(string assetPath, out string physicalPath)
        {
            physicalPath = string.Empty;
            assetPath = NormalizeAssetPath(assetPath);
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || string.Equals(assetPath, "Assets", StringComparison.Ordinal))
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    return false;
                physicalPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                return true;
            }

            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return false;

            if (TryResolveKnownPackageAssetPath(
                    assetPath,
                    VividPackageName,
                    out PackageInfo knownPackage,
                    out string knownPackageRelativePath)
                || TryResolveKnownPackageAssetPath(
                    assetPath,
                    CorePackageName,
                    out knownPackage,
                    out knownPackageRelativePath))
            {
                physicalPath = Path.GetFullPath(
                    Path.Combine(knownPackage.resolvedPath, knownPackageRelativePath));
                return true;
            }

            int packageNameEnd = assetPath.IndexOf('/', "Packages/".Length);
            string packageName = packageNameEnd >= 0
                ? assetPath.Substring("Packages/".Length, packageNameEnd - "Packages/".Length)
                : assetPath.Substring("Packages/".Length);
            PackageInfo packageInfo = PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null && string.Equals(packageName, VividPackageName, StringComparison.Ordinal))
                packageInfo = PackageInfo.FindForAssembly(typeof(VividMeshShaderProgramAsset).Assembly);
            if (string.IsNullOrEmpty(packageInfo?.resolvedPath))
                return false;

            string relativePath = packageNameEnd >= 0
                ? assetPath.Substring(packageNameEnd + 1)
                : string.Empty;
            physicalPath = Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relativePath));
            return true;
        }

        internal static string GetAssetDatabasePath(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (TryResolveKnownPackageAssetPath(
                    assetPath,
                    VividPackageName,
                    out PackageInfo packageInfo,
                    out string packageRelativePath)
                || TryResolveKnownPackageAssetPath(
                    assetPath,
                    CorePackageName,
                    out packageInfo,
                    out packageRelativePath))
            {
                string packageAssetPath = NormalizeAssetPath(packageInfo.assetPath).TrimEnd('/');
                return string.IsNullOrEmpty(packageRelativePath)
                    ? packageAssetPath
                    : $"{packageAssetPath}/{NormalizeAssetPath(packageRelativePath)}";
            }

            return assetPath;
        }

        private static bool TryResolveKnownPackageAssetPath(
            string assetPath,
            string packageName,
            out PackageInfo packageInfo,
            out string packageRelativePath)
        {
            packageInfo = PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null && string.Equals(packageName, VividPackageName, StringComparison.Ordinal))
                packageInfo = PackageInfo.FindForAssembly(typeof(VividMeshShaderProgramAsset).Assembly);

            packageRelativePath = string.Empty;
            if (string.IsNullOrEmpty(packageInfo?.resolvedPath))
                return false;

            string logicalPackagePath = $"Packages/{packageName}";
            if (TryGetPackageRelativePath(assetPath, logicalPackagePath, out packageRelativePath))
                return true;

            return TryGetPackageRelativePath(
                assetPath,
                NormalizeAssetPath(packageInfo.assetPath),
                out packageRelativePath);
        }

        private static bool TryGetPackageRelativePath(
            string assetPath,
            string packageAssetPath,
            out string packageRelativePath)
        {
            packageRelativePath = string.Empty;
            packageAssetPath = NormalizeAssetPath(packageAssetPath).TrimEnd('/');
            if (string.Equals(assetPath, packageAssetPath, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!assetPath.StartsWith(packageAssetPath + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            packageRelativePath = assetPath.Substring(packageAssetPath.Length + 1);
            return true;
        }

        private static bool TryGetPackageRoot(string packageName, out string packageRoot)
        {
            PackageInfo packageInfo = PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null && string.Equals(packageName, VividPackageName, StringComparison.Ordinal))
                packageInfo = PackageInfo.FindForAssembly(typeof(VividMeshShaderProgramAsset).Assembly);

            packageRoot = packageInfo?.resolvedPath;
            return !string.IsNullOrEmpty(packageRoot) && Directory.Exists(packageRoot);
        }

        private static string CollapseAssetPath(string path)
        {
            var collapsed = new List<string>();
            string[] segments = NormalizeAssetPath(path).Split('/');
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                string segment = segments[segmentIndex];
                if (string.IsNullOrEmpty(segment) || string.Equals(segment, ".", StringComparison.Ordinal))
                    continue;
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    if (collapsed.Count > 0)
                        collapsed.RemoveAt(collapsed.Count - 1);
                    continue;
                }

                collapsed.Add(segment);
            }

            return string.Join("/", collapsed);
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
