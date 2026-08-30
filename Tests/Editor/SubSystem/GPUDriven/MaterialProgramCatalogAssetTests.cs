using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests.GPUDriven
{
    internal sealed class MaterialProgramCatalogAssetTests
    {
        private const string TempFolder =
            "Assets/Temp/VividRPMaterialProgramCatalogAssetTests";
        private const string AssetPath = TempFolder + "/Catalog.asset";
        private const string SurfacePath = TempFolder + "/Surface.generated.hlsl";
        private const string CoveragePath = TempFolder + "/Coverage.generated.hlsl";
        private const string StampPath =
            TempFolder + "/VividMaterialProgramCatalogStamp.generated.hlsl";

        [Test]
        public void GeneratedArtifactCompilerErrorFilter_OnlyAcceptsGeneratedFiles()
        {
            string[] generatedPaths =
            {
                "Packages/Custom_URP/Shaders/Core/Public/GPUDriven/" +
                "VividMaterialSurfaceAOT.generated.hlsl",
                "Packages/Custom_URP/Shaders/Core/Public/GPUDriven/" +
                "VividMaterialCoverageAOT.generated.hlsl",
                "Packages/Custom_URP/Shaders/Core/Public/GPUDriven/" +
                "VividMaterialProgramCatalogStamp.generated.hlsl",
            };

            Assert.That(
                MaterialProgramCatalogBaker.IsGeneratedArtifactCompilerError(
                    generatedPaths[0],
                    isError: true,
                    generatedPaths),
                Is.True);
            Assert.That(
                MaterialProgramCatalogBaker.IsGeneratedArtifactCompilerError(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Public/" +
                    "GPUDriven/VividMaterialCoverageAOT.generated.hlsl",
                    isError: true,
                    generatedPaths),
                Is.True);
            Assert.That(
                MaterialProgramCatalogBaker.IsGeneratedArtifactCompilerError(
                    "Assets/Unrelated/Broken.shader",
                    isError: true,
                    generatedPaths),
                Is.False);
            Assert.That(
                MaterialProgramCatalogBaker.IsGeneratedArtifactCompilerError(
                    generatedPaths[2],
                    isError: false,
                    generatedPaths),
                Is.False);
        }

        [Test]
        public void Apply_RoundTripsFrozenManifestAndRuntimeTable()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(catalog);

                Assert.That(asset.Matches(catalog, out string failure),
                    Is.True,
                    failure);
                Assert.That(asset.SchemaVersion,
                    Is.EqualTo(MaterialProgramCatalogAsset.AssetSchemaVersion));
                Assert.That(asset.ProgramCatalogVersion,
                    Is.EqualTo(MaterialProgramContract.ProgramCatalogVersion));
                Assert.That(asset.ManifestVersion,
                    Is.EqualTo(MaterialProgramContract.ProgramCatalogManifestVersion));
                Assert.That(asset.RuntimeAbiVersion,
                    Is.EqualTo(MaterialProgramContract.RuntimeAbiVersion));
                Assert.That(asset.ManifestHash, Is.EqualTo(catalog.ManifestHash));
                Assert.That(asset.IsCommitted, Is.True);
                Assert.That(asset.ArtifactSetHash.IsValid, Is.True);
                Assert.That(asset.CatalogPayloadSeal.IsValid, Is.True);
                Assert.That(
                    asset.ArtifactSetHash,
                    Is.EqualTo(
                        MaterialProgramArtifactSetHashBuilder.Compute(catalog)));
                Assert.That(
                    asset.PublishedGeneration,
                    Is.EqualTo(asset.ArtifactSetHash));
                Assert.That(
                    asset.Slots.Count,
                    Is.EqualTo(catalog.RuntimeTableLength));
                for (int programIndex = 0;
                     programIndex < catalog.RuntimeTableLength;
                     programIndex++)
                {
                    MaterialProgramCatalog.ManifestEntry entry =
                        catalog.Slots[programIndex];
                    if (entry == null)
                        continue;
                    Assert.That(
                        asset.Slots[programIndex].ParameterStrideInWords,
                        Is.EqualTo((uint) entry.Program.Lowering.GenericLayout
                            .ParameterStrideInWords),
                        $"Program {programIndex} parameter stride");
                    Assert.That(
                        asset.Slots[programIndex].ResourceCount,
                        Is.EqualTo((uint) entry.Program.Lowering.GenericLayout
                            .ResourceCount),
                        $"Program {programIndex} resource count");
                }

                AssertRuntimeTablesEqual(
                    catalog.CreateRuntimeProgramTable(),
                    asset.CreateRuntimeProgramTable());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Apply_PreservesReservedProgramIDHoles()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion);
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.Reserved("P0.Reserved"),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P1.Standard",
                    standard));
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(catalog);
                VividMaterialProgramData[] runtimeTable =
                    asset.CreateRuntimeProgramTable();

                Assert.That(asset.Slots[0].IsReserved, Is.True);
                Assert.That((uint) asset.Slots[0].ProgramID, Is.EqualTo(0u));
                Assert.That(asset.Slots[0].StableName, Is.EqualTo("P0.Reserved"));
                Assert.That(asset.Slots[1].IsReserved, Is.False);
                Assert.That((uint) asset.Slots[1].ProgramID, Is.EqualTo(1u));
                Assert.That(runtimeTable, Has.Length.EqualTo(2));
                Assert.That(runtimeTable[0].Version, Is.EqualTo(0u));
                AssertRuntimeDataEqual(runtimeTable[1], standard.RuntimeData, 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Apply_CustomDeclarationsRoundTripAndExtendBuiltinCatalog()
        {
            MaterialProgramCatalog builtin =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            CompiledMaterialProgram customProgram =
                CompiledMaterialProgram.Compile(
                    BuildCustomDeclarationModule(),
                    MaterialProgramContract.RuntimeAbiVersion,
                    builtin.Templates);
            MaterialProgramCatalog extended = BuildExtendedCatalog(
                builtin,
                "Graph.CustomDeclarations",
                customProgram);
            var source = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            var roundTripped = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                source.Apply(extended);
                EditorJsonUtility.FromJsonOverwrite(
                    EditorJsonUtility.ToJson(source),
                    roundTripped);

                Assert.That(
                    customProgram.Lowering.Template.LayoutSchema.Matches(
                        customProgram.Lowering.Requirements),
                    Is.False,
                    "The custom program must not rely on an exact native semantic schema match.");
                Assert.That(
                    roundTripped.ExtendsBuiltinCatalog(
                        builtin,
                        out string extensionFailure),
                    Is.True,
                    extensionFailure);

                int dynamicSlotIndex = builtin.RuntimeTableLength;
                MaterialProgramCatalogAsset.Slot slot =
                    roundTripped.Slots[dynamicSlotIndex];
                Assert.That(
                    slot.ValidateRuntimeDescriptor(out string descriptorFailure),
                    Is.True,
                    descriptorFailure);

                var runtimeBinding = new MaterialProgramRuntimeBinding(slot);
                var tint = new MaterialParameterDeclaration(
                    "UserTint",
                    MaterialValueType.Float4);
                Assert.That(
                    customProgram.Lowering.GenericLayout.TryGetParameterBinding(
                        tint,
                        out MaterialGenericParameterBinding expectedTint),
                    Is.True);
                MaterialRuntimeParameterBindingDescriptor actualTint =
                    FindParameterBinding(runtimeBinding, tint.Symbol);
                Assert.That(actualTint.Symbol, Is.EqualTo(tint.Symbol));
                Assert.That(actualTint.Type, Is.EqualTo(tint.Type));
                Assert.That(actualTint.WordOffset,
                    Is.EqualTo(expectedTint.WordOffset));
                Assert.That(actualTint.WordCount,
                    Is.EqualTo(expectedTint.WordCount));

                var texture = new MaterialResourceDeclaration(
                    "DetailTexture",
                    MaterialValueType.Texture2D,
                    MaterialTextureSampleClass.Raw);
                Assert.That(
                    customProgram.Lowering.GenericLayout.TryGetResourceBinding(
                        texture,
                        out MaterialGenericResourceBinding expectedTexture),
                    Is.True);
                MaterialRuntimeResourceBindingDescriptor actualTexture =
                    FindResourceBinding(runtimeBinding, texture.Symbol);
                Assert.That(actualTexture.Symbol, Is.EqualTo(texture.Symbol));
                Assert.That(actualTexture.Type, Is.EqualTo(texture.Type));
                Assert.That(actualTexture.SampleClass,
                    Is.EqualTo(texture.SampleClass));
                Assert.That(actualTexture.Slot,
                    Is.EqualTo(expectedTexture.Slot));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(roundTripped);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void RuntimeDescriptor_RejectsDeclarationsThatDoNotMatchFingerprint()
        {
            MaterialProgramCatalog builtin =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            CompiledMaterialProgram customProgram =
                CompiledMaterialProgram.Compile(
                    BuildCustomDeclarationModule(),
                    MaterialProgramContract.RuntimeAbiVersion,
                    builtin.Templates);
            MaterialProgramCatalog extended = BuildExtendedCatalog(
                builtin,
                "Graph.CustomDeclarations",
                customProgram);
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(extended);
                MaterialProgramCatalogAsset.Slot slot =
                    asset.Slots[builtin.RuntimeTableLength];
                var bindingsField = typeof(MaterialProgramCatalogAsset.Slot)
                    .GetField(
                        "m_ResourceBindings",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                var symbolField = typeof(
                        MaterialRuntimeResourceBindingDescriptor)
                    .GetField(
                        "m_Symbol",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(bindingsField, Is.Not.Null);
                Assert.That(symbolField, Is.Not.Null);
                var bindings = (MaterialRuntimeResourceBindingDescriptor[])
                    bindingsField.GetValue(slot);
                object tampered = bindings[0];
                symbolField.SetValue(tampered, "RenamedDetailTexture");
                bindings[0] = (MaterialRuntimeResourceBindingDescriptor) tampered;

                Assert.That(
                    slot.ValidateRuntimeDescriptor(out string failure),
                    Is.False);
                Assert.That(failure, Does.Contain("fingerprint"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RuntimeTable_RejectsStaleFrozenAsset()
        {
            var stale = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                InvalidOperationException exception = Assert.Throws<
                    InvalidOperationException>(() =>
                        GPUDrivenMaterialCompiler.CreateRuntimeProgramTable(
                            stale));
                Assert.That(exception.Message, Does.Contain("stale"));

                stale.Apply(GPUDrivenMaterialCompiler.ProgramCatalog);
                AssertRuntimeTablesEqual(
                    GPUDrivenMaterialCompiler.ProgramCatalog
                        .CreateRuntimeProgramTable(),
                    GPUDrivenMaterialCompiler.CreateRuntimeProgramTable(stale));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }
        }

        [Test]
        public void Apply_UncommittedArtifactSetFailsClosedUntilSeal()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                asset.Apply(catalog, committed: false);

                Assert.That(asset.IsCommitted, Is.False);
                Assert.That(asset.ArtifactSetHash.IsValid, Is.True);
                Assert.That(
                    asset.ArtifactSetHash,
                    Is.EqualTo(
                        MaterialProgramArtifactSetHashBuilder.Compute(catalog)));
                Assert.That(
                    asset.Matches(catalog, out string matchFailure),
                    Is.False);
                Assert.That(matchFailure, Does.Contain("not committed"));
                Assert.That(
                    asset.ExtendsBuiltinCatalog(catalog, out string extensionFailure),
                    Is.False);
                Assert.That(extensionFailure, Does.Contain("not committed"));
                Assert.Throws<InvalidOperationException>(
                    () => asset.CreateRuntimeProgramTable());
                Assert.Throws<InvalidOperationException>(
                    () => GPUDrivenMaterialCompiler.CreateRuntimeProgramTable(
                        asset));
                Assert.Throws<InvalidOperationException>(
                    () => GPUDrivenMaterialCompiler.GetRuntimeProgramBinding(
                        VividMaterialProgramID.StandardSingleSlab,
                        asset));
                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        proxy,
                        asset,
                        out string validationMessage),
                    Is.False);
                Assert.That(validationMessage, Does.Contain("not committed"));

                asset.Seal(catalog);

                Assert.That(asset.IsCommitted, Is.True);
                Assert.That(
                    asset.Matches(catalog, out matchFailure),
                    Is.True,
                    matchFailure);
                AssertRuntimeTablesEqual(
                    catalog.CreateRuntimeProgramTable(),
                    asset.CreateRuntimeProgramTable());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void PublishedCatalog_RejectsTamperedArtifactSetSeal()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(catalog);
                var sealField = typeof(MaterialProgramCatalogAsset).GetField(
                    "m_ArtifactSetHash",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
                Assert.That(sealField, Is.Not.Null);
                sealField.SetValue(asset, asset.ArtifactSetHash.Value ^ 1ul);

                Assert.That(asset.IsCommitted, Is.True);
                Assert.That(
                    asset.Matches(catalog, out string matchFailure),
                    Is.False);
                Assert.That(matchFailure, Does.Contain("seal"));
                Assert.That(
                    asset.ExtendsBuiltinCatalog(catalog, out string extensionFailure),
                    Is.False);
                Assert.That(extensionFailure, Does.Contain("seal"));
                Assert.Throws<InvalidOperationException>(
                    () => asset.CreateRuntimeProgramTable());
                Assert.Throws<InvalidOperationException>(
                    () => GPUDrivenMaterialCompiler.CreateRuntimeProgramTable(
                        asset));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void PublishedCatalog_RejectsSlotPayloadTamperedAfterSeal()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(catalog);
                MaterialProgramCatalogPayloadSeal publishedSeal =
                    asset.CatalogPayloadSeal;
                MaterialProgramCatalogAsset.Slot slot = asset.Slots[0];
                var stableNameField = typeof(MaterialProgramCatalogAsset.Slot)
                    .GetField(
                        "m_StableName",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(stableNameField, Is.Not.Null);
                stableNameField.SetValue(
                    slot,
                    slot.StableName + ".Tampered");

                Assert.That(asset.IsCommitted, Is.True);
                Assert.That(asset.CatalogPayloadSeal, Is.EqualTo(publishedSeal));
                Assert.That(
                    asset.ValidatePublication(out string publicationFailure),
                    Is.False);
                Assert.That(
                    publicationFailure,
                    Does.Contain("serialized payload seal"));
                Assert.That(
                    asset.Matches(catalog, out string matchFailure),
                    Is.False);
                Assert.That(
                    matchFailure,
                    Does.Contain("serialized payload seal"));
                Assert.That(
                    asset.ExtendsBuiltinCatalog(catalog, out string extensionFailure),
                    Is.False);
                Assert.That(
                    extensionFailure,
                    Does.Contain("serialized payload seal"));
                Assert.Throws<InvalidOperationException>(
                    () => asset.CreateRuntimeProgramTable());
                Assert.Throws<InvalidOperationException>(
                    () => GPUDrivenMaterialCompiler.CreateRuntimeProgramTable(
                        asset));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void InvalidatePublication_PreservesLastGoodPayloadButFailsClosed()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var asset = ScriptableObject.CreateInstance<
                MaterialProgramCatalogAsset>();
            try
            {
                asset.Apply(catalog);
                MaterialProgramArtifactSetHash lastGood =
                    asset.ArtifactSetHash;
                MaterialProgramCatalogManifestHash manifest =
                    asset.ManifestHash;

                asset.InvalidatePublication();

                Assert.That(asset.IsCommitted, Is.False);
                Assert.That(asset.ArtifactSetHash, Is.EqualTo(lastGood));
                Assert.That(asset.ManifestHash, Is.EqualTo(manifest));
                Assert.That(
                    asset.Slots.Count,
                    Is.EqualTo(catalog.RuntimeTableLength));
                Assert.That(
                    asset.ValidatePublication(out string failure),
                    Is.False);
                Assert.That(failure, Does.Contain("not committed"));
                Assert.Throws<InvalidOperationException>(
                    () => asset.CreateRuntimeProgramTable());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Bake_OneCatalogSynchronizesAssetAndBothDispatchers()
        {
            EnsureTempFolder();
            try
            {
                MaterialProgramCatalog catalog =
                    GPUDrivenMaterialCompiler.ProgramCatalog;
                MaterialProgramCatalogAsset asset =
                    MaterialProgramCatalogBaker.Bake(
                        catalog,
                        AssetPath,
                        SurfacePath,
                        CoveragePath);

                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.Matches(catalog, out string failure),
                    Is.True,
                    failure);
                Assert.That(File.ReadAllText(SurfacePath),
                    Is.EqualTo(MaterialSurfaceHlslSourceBuilder.BuildSource(catalog)));
                Assert.That(File.ReadAllText(CoveragePath),
                    Is.EqualTo(MaterialCoverageHlslSourceBuilder.BuildSource(catalog)));
                Assert.That(File.ReadAllText(StampPath),
                    Is.EqualTo(
                        MaterialProgramCatalogHlslStampSourceBuilder.BuildSource(
                            catalog)));
                Assert.That(
                    MaterialSurfaceHlslGenerator.IsSynchronized(
                        catalog,
                        SurfacePath),
                    Is.True);
                Assert.That(
                    MaterialCoverageHlslGenerator.IsSynchronized(
                        catalog,
                        CoveragePath),
                    Is.True);

                File.WriteAllText(StampPath, "// stale published generation\n");
                Assert.That(
                    MaterialSurfaceHlslGenerator.IsSynchronized(
                        catalog,
                        SurfacePath),
                    Is.False);
                Assert.That(
                    MaterialCoverageHlslGenerator.IsSynchronized(
                        catalog,
                        CoveragePath),
                    Is.False);

                MonoScript script = MonoScript.FromScriptableObject(asset);
                Assert.That(script, Is.Not.Null);
                Assert.That(script.GetClass(),
                    Is.EqualTo(typeof(MaterialProgramCatalogAsset)));
                Assert.That(
                    Path.GetFileNameWithoutExtension(
                        AssetDatabase.GetAssetPath(script)),
                    Is.EqualTo(nameof(MaterialProgramCatalogAsset)));
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [Test]
        public void DefaultFrozenCatalogAsset_IsSynchronized()
        {
            MaterialProgramCatalogAsset asset =
                AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                    MaterialProgramCatalogBaker.AssetPath);
            MaterialProgramCatalog catalog =
                MaterialProgramCatalogBaker.BuildCatalog(
                    MaterialProgramCatalogBaker.DiscoverGraphPaths(),
                    asset);

            Assert.That(asset, Is.Not.Null, MaterialProgramCatalogBaker.AssetPath);
            Assert.That(
                asset.Matches(catalog, out string failure),
                Is.True,
                failure);
            Assert.That(MaterialProgramCatalogAsset.LoadDefault(),
                Is.EqualTo(asset));
            AssertRuntimeTablesEqual(
                asset.CreateRuntimeProgramTable(),
                GPUDrivenMaterialCompiler.CreateRuntimeProgramTable());
            Assert.That(MaterialProgramCatalogBaker.IsSynchronized(), Is.True);
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
                AssetDatabase.CreateFolder("Assets", "Temp");
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets/Temp",
                    "VividRPMaterialProgramCatalogAssetTests");
            }
        }

        private static MaterialProgramCatalog BuildExtendedCatalog(
            MaterialProgramCatalog builtin,
            string stableName,
            CompiledMaterialProgram customProgram)
        {
            var slots = new MaterialProgramCatalogBakeSlot[
                builtin.RuntimeTableLength + 1];
            for (int slotIndex = 0;
                 slotIndex < builtin.RuntimeTableLength;
                 slotIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry =
                    builtin.Slots[slotIndex];
                slots[slotIndex] = entry != null
                    ? MaterialProgramCatalogBakeSlot.ForProgram(
                        builtin.SlotNames[slotIndex],
                        entry.Program)
                    : MaterialProgramCatalogBakeSlot.Reserved(
                        builtin.SlotNames[slotIndex]);
            }
            slots[builtin.RuntimeTableLength] =
                MaterialProgramCatalogBakeSlot.ForProgram(
                    stableName,
                    customProgram);
            return MaterialProgramCatalog.Bake(builtin.Templates, slots);
        }

        private static MaterialIRModule BuildCustomDeclarationModule()
        {
            var values = new MaterialValueIR();
            MaterialValue uv = values.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue texture = values.TextureResource(
                new MaterialResourceDeclaration(
                    "DetailTexture",
                    MaterialValueType.Texture2D,
                    MaterialTextureSampleClass.Raw));
            MaterialValue sample = values.TextureSampleGrad(
                texture,
                uv,
                values.Ddx(uv),
                values.Ddy(uv));
            MaterialValue baseColor = values.Multiply(
                sample,
                values.Parameter(new MaterialParameterDeclaration(
                    "UserTint",
                    MaterialValueType.Float4)));
            MaterialValue roughness = values.Parameter(
                new MaterialParameterDeclaration(
                    "UserRoughness",
                    MaterialValueType.Float));
            MaterialValue metallic = values.Parameter(
                new MaterialParameterDeclaration(
                    "UserMetallic",
                    MaterialValueType.Float));
            MaterialValue alphaClipThreshold = values.Parameter(
                new MaterialParameterDeclaration(
                    "UserAlphaClipThreshold",
                    MaterialValueType.Float));
            MaterialValue emission = values.Parameter(
                new MaterialParameterDeclaration(
                    "UserEmission",
                    MaterialValueType.Float3));
            MaterialValue coverage = values.Swizzle(
                baseColor,
                MaterialSwizzleMask.W);
            var topology = new ClosureTopology(
                values,
                new[]
                {
                    new ClosureNormalBasis(
                        values.ExternalInput(
                            MaterialExternalInput.GeometryNormalWS),
                        values.ExternalInput(
                            MaterialExternalInput.GeometryTangentWS)),
                },
                new[]
                {
                    new ClosureSlab(
                        baseColor,
                        roughness,
                        metallic,
                        normalBasisIndex: 0,
                        features: ClosureFeatureMask.BaseColorTexture,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            ClosureExpressionGraph closureGraph =
                ClosureExpressionGraph.FromTopology(
                    topology,
                    out MaterialClosure surfaceClosure);
            return new MaterialIRModule(
                values,
                new MaterialOutputRoots(
                    coverage,
                    alphaClipThreshold,
                    emission),
                closureGraph,
                surfaceClosure,
                topology.Budget,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialRuntimeParameterBindingDescriptor
            FindParameterBinding(
                MaterialProgramRuntimeBinding runtimeBinding,
                string symbol)
        {
            for (int bindingIndex = 0;
                 bindingIndex < runtimeBinding.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialRuntimeParameterBindingDescriptor binding =
                    runtimeBinding.ParameterBindings[bindingIndex];
                if (string.Equals(
                        binding.Symbol,
                        symbol,
                        StringComparison.Ordinal))
                {
                    return binding;
                }
            }
            Assert.Fail($"Runtime parameter binding '{symbol}' is missing.");
            return default;
        }

        private static MaterialRuntimeResourceBindingDescriptor
            FindResourceBinding(
                MaterialProgramRuntimeBinding runtimeBinding,
                string symbol)
        {
            for (int bindingIndex = 0;
                 bindingIndex < runtimeBinding.ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialRuntimeResourceBindingDescriptor binding =
                    runtimeBinding.ResourceBindings[bindingIndex];
                if (string.Equals(
                        binding.Symbol,
                        symbol,
                        StringComparison.Ordinal))
                {
                    return binding;
                }
            }
            Assert.Fail($"Runtime resource binding '{symbol}' is missing.");
            return default;
        }

        private static void AssertRuntimeTablesEqual(
            VividMaterialProgramData[] actual,
            VividMaterialProgramData[] expected)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int programIndex = 0;
                 programIndex < actual.Length;
                 programIndex++)
            {
                AssertRuntimeDataEqual(
                    actual[programIndex],
                    expected[programIndex],
                    programIndex);
            }
        }

        private static void AssertRuntimeDataEqual(
            in VividMaterialProgramData actual,
            in VividMaterialProgramData expected,
            int programIndex)
        {
            Assert.That(actual.Version, Is.EqualTo(expected.Version),
                $"Program {programIndex} Version");
            Assert.That(actual.CoverageProgramID,
                Is.EqualTo(expected.CoverageProgramID),
                $"Program {programIndex} CoverageProgramID");
            Assert.That(actual.SurfaceProgramID,
                Is.EqualTo(expected.SurfaceProgramID),
                $"Program {programIndex} SurfaceProgramID");
            Assert.That(actual.TransportProgramID,
                Is.EqualTo(expected.TransportProgramID),
                $"Program {programIndex} TransportProgramID");
            Assert.That(actual.ParameterLayoutID,
                Is.EqualTo(expected.ParameterLayoutID),
                $"Program {programIndex} ParameterLayoutID");
            Assert.That(actual.ResourceLayoutID,
                Is.EqualTo(expected.ResourceLayoutID),
                $"Program {programIndex} ResourceLayoutID");
            Assert.That(actual.CapabilityFlags,
                Is.EqualTo(expected.CapabilityFlags),
                $"Program {programIndex} CapabilityFlags");
            Assert.That(actual.ExecutionClass,
                Is.EqualTo(expected.ExecutionClass),
                $"Program {programIndex} ExecutionClass");
        }
    }
}
