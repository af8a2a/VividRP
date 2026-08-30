# VividRP source generators

`VividRP.RenderPassNodeGenerator` is an independent Roslyn incremental source
generator. It targets .NET Standard 2.0 and builds against
`Microsoft.CodeAnalysis.CSharp` 4.3.0, matching Unity's supported generator
contract.

Run the pure .NET generator tests with:

```powershell
dotnet test SourceGenerators~/VividRP.RenderPassNodeGenerator.Tests/VividRP.RenderPassNodeGenerator.Tests.csproj
```

Build and deploy the generator DLL into the Unity package with:

```powershell
dotnet build SourceGenerators~/VividRP.RenderPassNodeGenerator/VividRP.RenderPassNodeGenerator.csproj -c Release -t:DeployToUnity
```

The checked-in Unity `.meta` file keeps the deployed DLL labeled as a
`RoslynAnalyzer` and disabled for all runtime platforms. The generator emits
dedicated nodes only for eligible pass types in `VividRP.Runtime`; pass types in
consumer assemblies continue to use the generic node's `PassScript` option.
