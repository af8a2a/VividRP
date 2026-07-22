using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividPerObjectLayoutGeneratorTests
    {
        private const string TemporaryFolder = "Assets/VividPerObjectBufferTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TemporaryFolder);
        }

        [Test]
        public void BuildSource_EmitsSetupTypedAccessorsDefaultsAndAliases()
        {
            VividPerObjectLayout layout = VividPerObjectLayoutTests.CreateFullLayout();
            try
            {
                string source = VividPerObjectLayoutGenerator.BuildSource(layout, "Assets/Character.asset");

                Assert.That(source, Does.Contain($"0x{layout.Signature:X8}u"));
                Assert.That(source, Does.Contain("#define VIVID_SETUP_PER_OBJECT_Character()"));
                Assert.That(source, Does.Contain("VividPerObjectLoadInt"));
                Assert.That(source, Does.Contain("VividPerObjectLoadFloat4x4"));
                Assert.That(source, Does.Contain("VividPerObjectContext_Character_"));
                Assert.That(source, Does.Contain("#define _Dissolve VividPerObject_Character_"));
                Assert.That(source, Does.Contain("asfloat(0x3E800000u)"));
                Assert.That(source, Does.Contain(", 44u,"));
                Assert.That(source, Does.Contain("#ifndef VIVID_PER_OBJECT_NO_PROPERTY_ALIASES"));
            }
            finally
            {
                Object.DestroyImmediate(layout);
            }
        }

        [Test]
        public void Generate_WritesDeterministicSiblingIncludeAndDetectsStaleness()
        {
            EnsureTemporaryFolder();
            string layoutPath = $"{TemporaryFolder}/GeneratedLayout.asset";
            VividPerObjectLayout layout = VividPerObjectLayoutTests.CreateFloatLayout(
                "GeneratedLayout",
                "_GeneratedValue",
                1.5f);
            AssetDatabase.CreateAsset(layout, layoutPath);

            string generatedPath = VividPerObjectLayoutGenerator.Generate(layout);
            Assert.That(generatedPath, Is.EqualTo($"{TemporaryFolder}/GeneratedLayout.generated.hlsl"));
            Assert.That(File.Exists(generatedPath), Is.True);
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layout), Is.True);

            File.AppendAllText(generatedPath, "// stale\n");
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layout), Is.False);
            VividPerObjectLayoutGenerator.Generate(layout);
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layout), Is.True);
        }

        [Test]
        public void Signature_ChangesWhenDefaultValueChanges()
        {
            VividPerObjectLayout first = VividPerObjectLayoutTests.CreateFloatLayout("Signature", "_Value", 1.0f);
            VividPerObjectLayout second = VividPerObjectLayoutTests.CreateFloatLayout("Signature", "_Value", 2.0f);
            try
            {
                Assert.That(first.Signature, Is.Not.EqualTo(second.Signature));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static void EnsureTemporaryFolder()
        {
            if (!AssetDatabase.IsValidFolder(TemporaryFolder))
                AssetDatabase.CreateFolder("Assets", "VividPerObjectBufferTests");
        }
    }
}
