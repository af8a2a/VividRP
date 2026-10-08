# Compile the current package with the containing project's real Unity Bee
# response files, then run an explicitly selected set of pure managed NUnit cases.
# This never launches Unity, imports assets, or runs Unity Test Framework.
[CmdletBinding()]
param(
    [string] $OutputDirectory = '',
    [string] $BeeDagDirectory = '',
    [switch] $ProbesOnly
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $packageRoot '../..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $packageRoot 'Temp~/openpbr-compiler-20261007' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (!$BeeDagDirectory) {
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/Bee/artifacts') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'VividRP.Runtime.rsp') } |
        Sort-Object { (Get-Item -LiteralPath (Join-Path $_.FullName 'VividRP.Runtime.rsp')).LastWriteTimeUtc } -Descending)
    if ($candidates.Count -eq 0) { throw 'No imported project Bee VividRP.Runtime.rsp found.' }
    $BeeDagDirectory = $candidates[0].FullName
}
$beeRoot = (Resolve-Path -LiteralPath $BeeDagDirectory).Path
$runtimeText = Get-Content -Raw -LiteralPath (Join-Path $beeRoot 'VividRP.Runtime.rsp')
$editorReference = [regex]::Match($runtimeText,
    '-r:"(?<path>[^"]*/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule\.dll)"')
if (!$editorReference.Success) { throw 'Cannot determine this project Editor SDK from its Bee references.' }
$editorData = $editorReference.Groups['path'].Value -replace '/Managed/UnityEngine/UnityEngine.CoreModule\.dll$', ''
$dotnet = Join-Path $editorData 'DotNetSdk/dotnet.exe'
$sdk = @(Get-ChildItem -LiteralPath (Join-Path $editorData 'DotNetSdk/sdk') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
    Sort-Object { [version] $_.Name } -Descending)[0]
$csc = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
if (!(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath $csc)) {
    throw 'This project Unity DotNetSdk / Roslyn compiler is missing.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
$referencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$addedSources = @{
    'VividRP.Runtime' = @('Runtime/SubSystem/GPUDriven/Material/OpenPBROpaqueContract.cs')
    'VividRP.Editor' = @('Editor/GPUDriven/Material/MaterialOpenPBROpaqueAuthoring.cs')
    'VividRP.Editor.Tests' = @(
        'Tests/Editor/SubSystem/GPUDriven/OpenPBROpaqueContractTests.cs',
        'Tests/Editor/SubSystem/GPUDriven/OpenPBROpaqueMaterialIRTests.cs',
        'Tests/Editor/SubSystem/GPUDriven/MaterialOpenPBROpaqueAuthoringTests.cs',
        'Tests/Editor/SubSystem/GPUDriven/OpenPBROpaqueSurfaceExportTests.cs')
}

function Invoke-Compiler([string] $Name, [string] $ResponsePath) {
    $logPath = Join-Path $outputRoot ($Name + '.compile.log')
    $ErrorActionPreference = 'Continue'
    $messages = @(& $dotnet exec $csc ('@' + $ResponsePath) 2>&1)
    $compilerExitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    [IO.File]::WriteAllLines($logPath, [string[]] $messages, $utf8)
    if ($compilerExitCode -ne 0) {
        $messages | Where-Object { $_ -match 'error [A-Z]+\d+' } | Select-Object -First 30 | Write-Output
        throw "$Name compilation failed ($compilerExitCode); see $logPath"
    }
    Write-Output "$Name compiled with the project Bee response file. Log: $logPath"
}

Push-Location $projectRoot
try {
    foreach ($assembly in @('VividRP.Runtime', 'VividRP.Editor', 'VividRP.Editor.Tests')) {
        $sourceRsp = Join-Path $beeRoot ($assembly + '.rsp')
        if (!(Test-Path -LiteralPath $sourceRsp)) { throw "Bee response file missing: $sourceRsp" }
        $lines = [Collections.Generic.List[string]]::new()
        foreach ($line in Get-Content -LiteralPath $sourceRsp) {
            if ($line -match '^-out:') {
                $lines.Add('-out:"' + (Join-Path $outputRoot ($assembly + '.dll')) + '"')
            } elseif ($line -match '^-refout:') {
                $lines.Add('-refout:"' + (Join-Path $outputRoot ($assembly + '.ref.dll')) + '"')
            } elseif ($line -match '^-r:"(?<path>[^"]+)"$') {
                $path = $Matches['path']
                if (![IO.Path]::IsPathRooted($path)) { $path = Join-Path $projectRoot $path }
                $leaf = [IO.Path]::GetFileName($path)
                if ($leaf -in @('VividRP.Runtime.ref.dll', 'VividRP.Editor.ref.dll')) {
                    $path = Join-Path $outputRoot $leaf
                }
                $lines.Add('-r:"' + $path + '"')
                [void] $referencePaths.Add($path)
            } else { $lines.Add($line) }
        }
        foreach ($source in $addedSources[$assembly]) {
            $path = Join-Path $packageRoot $source
            if (!(Test-Path -LiteralPath $path)) { throw "Requested real source missing: $path" }
            $packageRelative = 'Packages/VividRP/' + $source
            if (!($lines | Where-Object { $_.Trim('"').Replace('\', '/') -eq $packageRelative })) {
                $lines.Add('"' + $path + '"')
            }
        }
        $responsePath = Join-Path $outputRoot ($assembly + '.rsp')
        [IO.File]::WriteAllLines($responsePath, $lines, $utf8)
        Invoke-Compiler $assembly $responsePath
    }
} finally { Pop-Location }

# Resolve package execution assemblies from the imported project, while preferring
# the three newly compiled assemblies. No substitute types or stubs are compiled.
$executionReferences = [Collections.Generic.List[string]]::new()
foreach ($path in $referencePaths) {
    $name = [IO.Path]::GetFileName($path) -replace '\.ref\.dll$', '.dll'
    $temporary = Join-Path $outputRoot $name
    $imported = Join-Path $projectRoot ('Library/ScriptAssemblies/' + $name)
    $implementation = $path -replace '\.ref\.dll$', '.dll'
    if (Test-Path -LiteralPath $temporary) { $executionReferences.Add($temporary) }
    elseif (Test-Path -LiteralPath $imported) { $executionReferences.Add($imported) }
    elseif (Test-Path -LiteralPath $implementation) { $executionReferences.Add($implementation) }
    else { $executionReferences.Add($path) }
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'references.json'),
    (ConvertTo-Json -InputObject @($executionReferences) -Compress), $utf8)
$nunit = @($executionReferences | Where-Object { [IO.Path]::GetFileName($_) -eq 'nunit.framework.dll' })[0]
if (!$nunit) { throw 'The real project NUnit reference was not found.' }

$runnerSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Dictionary<string, string> References = new(StringComparer.OrdinalIgnoreCase);
    private static int passed, failed, skipped;

    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        foreach (string path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(output, "references.json"))))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            References.TryAdd(name, path);
        }
        AssemblyLoadContext.Default.Resolving += (_, name) => References.TryGetValue(name.Name, out string path)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        Assembly tests = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "VividRP.Editor.Tests.dll"));
        if (args.Length > 1 && args[1] == "--probes-only")
        {
            EmitRealBackendSources(tests, output);
            return 0;
        }
        return RunManagedTests(tests, output);
    }

    // Keep NUnit-dependent JIT work after the real assembly resolver is installed.
    private static int RunManagedTests(Assembly tests, string output)
    {
        var fixtures = new Dictionary<string, int>
        {
            ["VividRP.Editor.Tests.OpenPBROpaqueContractTests"] = 21,
            ["VividRP.Editor.Tests.GPUDriven.OpenPBROpaqueMaterialIRTests"] = 12,
            ["VividRP.Editor.Tests.GPUDriven.MaterialOpenPBROpaqueAuthoringTests"] = 14,
            ["VividRP.Editor.Tests.OpenPBROpaqueSurfaceExportTests"] = 12,
            ["VividRP.Editor.Tests.GPUDriven.MaterialGraphCompilerTests"] = 0,
            ["VividRP.Editor.Tests.MaterialCoverageHlslBackendTests"] = 0,
            ["VividRP.Editor.Tests.MaterialSurfaceHlslBackendTests"] = 0,
            ["VividRP.Editor.Tests.MaterialProgramPrototypeTests"] = 0,
        };
        foreach (var pair in fixtures)
        {
            Type type = tests.GetType(pair.Key, throwOnError: true);
            object fixture = Activator.CreateInstance(type, nonPublic: true);
            int beforePassed = passed, beforeFailed = failed, beforeSkipped = skipped;
            foreach (MethodInfo method in type.GetMethods(All).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                TestCaseAttribute[] cases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();
                bool isTest = method.GetCustomAttribute<TestAttribute>() != null || cases.Length != 0;
                if (!isTest) continue;
                if (RequiresUnityNative(method))
                {
                    skipped += Math.Max(1, cases.Length);
                    Console.WriteLine("SKIP Unity native/asset integration: " + type.Name + "." + method.Name);
                    continue;
                }
                if (cases.Length == 0) Run(fixture, method, Array.Empty<object>());
                else foreach (TestCaseAttribute testCase in cases) Run(fixture, method, testCase.Arguments);
            }
            int fixturePassed = passed - beforePassed, fixtureFailed = failed - beforeFailed;
            Console.WriteLine($"{type.Name}: {fixturePassed} passed, {fixtureFailed} failed, {skipped - beforeSkipped} skipped.");
            if (pair.Value != 0 && fixturePassed + fixtureFailed != pair.Value)
            {
                failed++;
                Console.WriteLine($"FAIL expected {pair.Value} cases, discovered {fixturePassed + fixtureFailed}: {type.Name}");
            }
        }
        try { EmitRealBackendSources(tests, output); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL real backend probe: " + Unwrap(exception)); }
        Console.WriteLine($"Pure managed cases: {passed} passed, {failed} failed, {skipped} skipped. No Unity Test Framework execution.");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed, failed, skipped }), new UTF8Encoding(false));
        return failed == 0 ? 0 : 1;
    }

    private static bool RequiresUnityNative(MethodInfo method)
    {
        return method.Name == "GeneratedInclude_IsSynchronizedWithFrozenProgramCatalog"
            || method.Name == "CompileStandardSingleSlab_ProducesSingleClosureProgramPrototype"
            || method.Name == "CompileDualSlab_AssignsTopologySpecificStableProgramID";
    }

    private static void Run(object fixture, MethodInfo method, object[] args)
    {
        try
        {
            method.Invoke(fixture, args);
            passed++;
        }
        catch (Exception exception)
        {
            failed++;
            Console.WriteLine("FAIL " + fixture.GetType().Name + "." + method.Name + "(" + string.Join(", ", args) + "): " + Unwrap(exception));
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
        return exception;
    }

    private static void EmitRealBackendSources(Assembly tests, string output)
    {
        Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "VividRP.Runtime.dll"));
        Type productionCompiler = runtime.GetType("VividRP.Runtime.GPUDriven.GPUDrivenMaterialCompiler", true);
        object builtin = productionCompiler.GetProperty("ProgramCatalog", All).GetValue(null);
        var pins = new StringBuilder();
        foreach (object entry in (System.Collections.IEnumerable) builtin.GetType().GetProperty("Entries", All).GetValue(builtin))
        {
            Type entryType = entry.GetType();
            object program = entryType.GetProperty("Program", All).GetValue(entry);
            object semantic = program.GetType().GetProperty("SemanticHash", All).GetValue(program);
            ulong value = (ulong) semantic.GetType().GetProperty("Value", All).GetValue(semantic);
            string line = entryType.GetProperty("StableName", All).GetValue(entry) + " 0x" + value.ToString("X16") + "ul";
            pins.AppendLine(line);
            Console.WriteLine("Actual builtin semantic hash: " + line);
        }
        File.WriteAllText(Path.Combine(output, "builtin-semantic-pins.txt"), pins.ToString(), new UTF8Encoding(false));
        Type contract = runtime.GetType("VividRP.Runtime.GPUDriven.MaterialProgramContract", true);
        uint version = (uint) contract.GetField("RuntimeAbiVersion", All).GetRawConstantValue();
        Type builder = runtime.GetType("VividRP.Runtime.GPUDriven.MaterialProgramPrototypeBuilder", true);
        object legacy = builder.GetMethod("BuildStandardSingleSlab", All).Invoke(null, new object[] { version });
        Type fixture = tests.GetType("VividRP.Editor.Tests.OpenPBROpaqueSurfaceExportTests", true);
        object native = fixture.GetMethod("BuildNative", All).Invoke(null, new object[] { true, 0.0f });
        Array programs = Array.CreateInstance(native.GetType(), 2);
        programs.SetValue(legacy, 0);
        programs.SetValue(native, 1);
        object catalog = fixture.GetMethod("Bake", All).Invoke(null, new object[] { programs });
        EmitSources(runtime, catalog, output);
        object nativeDefault = fixture.GetMethod("BuildNative", All).Invoke(null, new object[] { false, 0.0f });
        programs.SetValue(nativeDefault, 0);
        programs.SetValue(native, 1);
        object nativeCatalog = fixture.GetMethod("Bake", All).Invoke(null, new object[] { programs });
        string nativeDirectory = Path.Combine(output, "native-only");
        Directory.CreateDirectory(nativeDirectory);
        EmitSources(runtime, nativeCatalog, nativeDirectory);
    }

    private static void EmitSources(Assembly runtime, object catalog, string output)
    {
        foreach (var item in new[]
        {
            ("MaterialSurfaceHlslSourceBuilder", "VividMaterialSurfaceAOT.generated.hlsl"),
            ("MaterialCoverageHlslSourceBuilder", "VividMaterialCoverageAOT.generated.hlsl"),
            ("MaterialProgramCatalogHlslStampSourceBuilder", "VividMaterialProgramCatalogStamp.generated.hlsl"),
        })
        {
            Type sourceBuilder = runtime.GetType("VividRP.Runtime.GPUDriven." + item.Item1, true);
            string source = (string) sourceBuilder.GetMethod("BuildSource", All).Invoke(null, new[] { catalog });
            File.WriteAllText(Path.Combine(output, item.Item2), source, new UTF8Encoding(false));
            Console.WriteLine("Real backend catalog source: " + Path.Combine(output, item.Item2));
        }
        object manifest = catalog.GetType().GetProperty("ManifestHash", All).GetValue(catalog);
        File.WriteAllText(Path.Combine(output, "catalog-manifest.txt"), manifest.ToString() + Environment.NewLine, new UTF8Encoding(false));
    }
}
'@
[IO.File]::WriteAllText((Join-Path $outputRoot 'Program.cs'), $runnerSource, $utf8)
$targetingPack = @(Get-ChildItem -LiteralPath (Join-Path $editorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ref/net10.0/System.Runtime.dll') } |
    Sort-Object { [version] $_.Name } -Descending)[0]
if (!$targetingPack) { throw 'The project Unity SDK does not contain the .NET 10 reference pack.' }
$runnerRsp = [Collections.Generic.List[string]]::new()
$runnerRsp.Add('-nologo')
$runnerRsp.Add('-target:exe')
$runnerRsp.Add('-out:"' + (Join-Path $outputRoot 'Validate.dll') + '"')
foreach ($reference in Get-ChildItem -LiteralPath (Join-Path $targetingPack.FullName 'ref/net10.0') -Filter '*.dll') {
    $runnerRsp.Add('-r:"' + $reference.FullName + '"')
}
$runnerRsp.Add('-r:"' + $nunit + '"')
$runnerRsp.Add('"' + (Join-Path $outputRoot 'Program.cs') + '"')
$runnerRspPath = Join-Path $outputRoot 'Validate.rsp'
[IO.File]::WriteAllLines($runnerRspPath, $runnerRsp, $utf8)
Invoke-Compiler 'Validate' $runnerRspPath
[IO.File]::WriteAllText((Join-Path $outputRoot 'Validate.runtimeconfig.json'),
    '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}', $utf8)
$ErrorActionPreference = 'Continue'
$runnerArguments = @((Join-Path $outputRoot 'Validate.dll'), $outputRoot)
if ($ProbesOnly) { $runnerArguments += '--probes-only' }
$testOutput = @(& $dotnet @runnerArguments 2>&1)
$testExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$runLogName = if ($ProbesOnly) { 'probes.log' } else { 'tests.log' }
[IO.File]::WriteAllLines((Join-Path $outputRoot $runLogName), [string[]] $testOutput, $utf8)
$testOutput | Write-Output
if ($testExitCode -ne 0) { throw "Pure managed material compiler checks failed ($testExitCode)." }
