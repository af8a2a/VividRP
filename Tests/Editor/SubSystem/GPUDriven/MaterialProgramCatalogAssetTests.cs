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
                Assert.That(asset.Slots,
                    Has.Count.EqualTo(catalog.RuntimeTableLength));
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

            Assert.That(asset, Is.Not.Null, MaterialProgramCatalogBaker.AssetPath);
            Assert.That(
                asset.Matches(
                    GPUDrivenMaterialCompiler.ProgramCatalog,
                    out string failure),
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
