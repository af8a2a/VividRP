using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.Examples;

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
        public void BuildSource_EmitsMultipleLayoutsTypedAccessorsDefaultsAndSelectedAliases()
        {
            VividPerObjectLayout character = VividPerObjectLayoutTests.CreateFullLayout();
            VividPerObjectLayout decal = VividPerObjectLayoutTests.CreateFloatLayout(
                "Decal",
                "_Opacity",
                0.5f);

            string source = VividPerObjectLayoutGenerator.BuildSource(new[] { decal, character });

            Assert.That(source, Does.Contain($"0x{character.Signature:X8}u"));
            Assert.That(source, Does.Contain($"0x{decal.Signature:X8}u"));
            Assert.That(source, Does.Contain("#define VIVID_SETUP_PER_OBJECT_Character()"));
            Assert.That(source, Does.Contain("#define VIVID_SETUP_PER_OBJECT_Decal()"));
            Assert.That(source, Does.Contain("VividPerObjectLoadInt"));
            Assert.That(source, Does.Contain("VividPerObjectLoadFloat4x4"));
            Assert.That(source, Does.Contain("VividPerObjectContext_Character"));
            Assert.That(source, Does.Contain("#if defined(VIVID_PER_OBJECT_LAYOUT_Character)"));
            Assert.That(source, Does.Contain("#define _Dissolve VividPerObject_Character_Get__Dissolve()"));
            Assert.That(source, Does.Contain("#if defined(VIVID_PER_OBJECT_LAYOUT_Decal)"));
            Assert.That(source, Does.Contain("#define _Opacity VividPerObject_Decal_Get__Opacity()"));
            Assert.That(source, Does.Contain("VIVID_PER_OBJECT_LAYOUT_ALIASES_SELECTED"));
            Assert.That(source, Does.Contain("asfloat(0x3E800000u)"));
            Assert.That(source, Does.Contain(", 44u,"));
        }

        [Test]
        public void Generate_WritesDeterministicCentralIncludeAndDetectsStaleness()
        {
            EnsureTemporaryFolder();
            string generatedPath = $"{TemporaryFolder}/PerObjectBufferLayouts.generated.hlsl";
            VividPerObjectLayout layout = VividPerObjectLayoutTests.CreateFloatLayout(
                "GeneratedLayout",
                "_GeneratedValue",
                1.5f);
            VividPerObjectLayout[] layouts = { layout };

            string resultPath = VividPerObjectLayoutGenerator.Generate(layouts, generatedPath);
            Assert.That(resultPath, Is.EqualTo(generatedPath));
            Assert.That(File.Exists(generatedPath), Is.True);
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layouts, generatedPath), Is.True);

            File.AppendAllText(generatedPath, "// stale\n");
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layouts, generatedPath), Is.False);
            VividPerObjectLayoutGenerator.Generate(layouts, generatedPath);
            Assert.That(VividPerObjectLayoutGenerator.IsSynchronized(layouts, generatedPath), Is.True);
        }

        [Test]
        public void GeneratedPath_UsesTheMountedVividPackageRoot()
        {
            string expectedPath = VividPackagePathUtility.GetPreferredAssetPath(
                "Shaders/Core/Public/PerObjectBufferLayouts.generated.hlsl");

            Assert.That(VividPerObjectLayoutGenerator.GeneratedPath, Is.EqualTo(expectedPath));
            Assert.That(VividPerObjectLayoutGenerator.GeneratedPath, Does.EndWith(
                "/Shaders/Core/Public/PerObjectBufferLayouts.generated.hlsl"));
        }

        [Test]
        public void DiscoverLayouts_IncludesConcreteRuntimeLayouts()
        {
            IReadOnlyList<VividPerObjectLayout> layouts =
                VividPerObjectLayoutGenerator.DiscoverLayouts();

            Assert.That(
                layouts,
                Has.Some.TypeOf<VividPerObjectColorExampleLayout>());
        }

        [Test]
        public void BuildSource_ThrowsForDuplicateShaderIdentifiers()
        {
            VividPerObjectLayout first = VividPerObjectLayoutTests.CreateFloatLayout(
                "Duplicate",
                "_First",
                1.0f);
            VividPerObjectLayout second = VividPerObjectLayoutTests.CreateFloatLayout(
                "Duplicate",
                "_Second",
                2.0f);

            Assert.Throws<InvalidOperationException>(() =>
                VividPerObjectLayoutGenerator.BuildSource(new[] { first, second }));
        }

        [Test]
        public void GeneratedCentralInclude_CompilesWithSelectedLayoutAliases()
        {
            EnsureTemporaryFolder();
            string generatedPath = $"{TemporaryFolder}/PerObjectBufferLayouts.generated.hlsl";
            string shaderPath = $"{TemporaryFolder}/GeneratedLayoutTest.shader";
            VividPerObjectLayout layout = VividPerObjectLayoutTests.CreateFloatLayout(
                "GeneratedLayout",
                "_GeneratedValue",
                1.5f);
            VividPerObjectLayoutGenerator.Generate(new[] { layout }, generatedPath);

            File.WriteAllText(shaderPath, $@"Shader ""Hidden/VividRP/Tests/GeneratedCodeLayout""
{{
    SubShader
    {{
        Pass
        {{
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl""
            #define VIVID_PER_OBJECT_LAYOUT_GeneratedLayout
            #include ""{generatedPath}""

            struct Attributes
            {{
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            }};

            struct Varyings
            {{
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            }};

            Varyings Vert(Attributes input)
            {{
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }}

            float4 Frag(Varyings input) : SV_Target
            {{
                UNITY_SETUP_INSTANCE_ID(input);
                VIVID_SETUP_PER_OBJECT_GeneratedLayout();
                return _GeneratedValue.xxxx;
            }}
            ENDHLSL
        }}
    }}
}}");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }

        [Test]
        public void Signature_ChangesWhenDefaultValueChanges()
        {
            VividPerObjectLayout first = VividPerObjectLayoutTests.CreateFloatLayout("Signature", "_Value", 1.0f);
            VividPerObjectLayout second = VividPerObjectLayoutTests.CreateFloatLayout("Signature", "_Value", 2.0f);
            Assert.That(first.Signature, Is.Not.EqualTo(second.Signature));
        }

        private static void EnsureTemporaryFolder()
        {
            if (!AssetDatabase.IsValidFolder(TemporaryFolder))
                AssetDatabase.CreateFolder("Assets", "VividPerObjectBufferTests");
        }
    }
}
