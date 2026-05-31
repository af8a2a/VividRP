using NUnit.Framework;
using UnityEngine;
using VividRP.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class ColorCheckerToolTests
    {
        private GameObject m_GameObject;
        private ColorCheckerTool m_Tool;

        [SetUp]
        public void SetUp()
        {
            m_GameObject = new GameObject("Color Checker Test");
            m_Tool = m_GameObject.AddComponent<ColorCheckerTool>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_GameObject != null)
                Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void Refresh_CreatesEditorOnlyGeometryChild()
        {
            m_Tool.Refresh();

            Assert.That(m_GameObject.tag, Is.EqualTo("EditorOnly"));
            Assert.That(m_Tool.colorCheckerObject, Is.Not.Null);
            Assert.That(m_Tool.colorCheckerObject.name, Is.EqualTo(ColorCheckerTool.GeometryName));
            Assert.That(m_Tool.colorCheckerObject.TryGetComponent<MeshRenderer>(out _), Is.True);
            Assert.That(m_Tool.colorCheckerObject.TryGetComponent<MeshFilter>(out var meshFilter), Is.True);
            Assert.That(meshFilter.sharedMesh, Is.Not.Null);
        }

        [Test]
        public void UpdateMaterial_ConfiguresMaterialModeRowsAndMetalFlags()
        {
            m_Tool.materialFieldsCount = 3;
            m_Tool.addGradient = true;
            m_Tool.unlitCompare = true;
            m_Tool.isMetalBools[0] = true;
            m_Tool.isMetalBools[1] = false;

            m_Tool.Mode = ColorCheckerTool.ColorCheckerMode.Materials;

            Assert.That(m_Tool.fieldsToDisplay, Is.EqualTo(18));
            Assert.That(m_Tool.fieldsPerRowToDisplay, Is.EqualTo(ColorCheckerTool.SmoothnessColumns));
            Assert.That(m_Tool.sphereModeToDisplay, Is.True);
            Assert.That(m_Tool.gradientToDisplay, Is.False);
            Assert.That(m_Tool.customMaterials[0].a, Is.EqualTo(byte.MaxValue));
            Assert.That(m_Tool.customMaterials[1].a, Is.EqualTo(byte.MinValue));
        }

        [Test]
        public void ResetColors_RestoresMaterialPaletteAndMetalFlags()
        {
            m_Tool.Mode = ColorCheckerTool.ColorCheckerMode.Materials;
            m_Tool.customMaterials[2] = new Color32(1, 2, 3, 0);
            m_Tool.isMetalBools[2] = false;

            m_Tool.ResetColors();

            Assert.That(m_Tool.customMaterials[2].r, Is.EqualTo(193));
            Assert.That(m_Tool.customMaterials[2].g, Is.EqualTo(190));
            Assert.That(m_Tool.customMaterials[2].b, Is.EqualTo(187));
            Assert.That(m_Tool.customMaterials[2].a, Is.EqualTo(byte.MaxValue));
            Assert.That(m_Tool.isMetalBools[2], Is.True);
        }

        [Test]
        public void TextureMode_BindsExternalTextureAndRawComparison()
        {
            var litTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var rawTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                m_Tool.userTexture = litTexture;
                m_Tool.userTextureRaw = rawTexture;
                m_Tool.textureSlice = 0.25f;
                m_Tool.unlitTextureExposure = false;
                m_Tool.Mode = ColorCheckerTool.ColorCheckerMode.Texture;

                var propertyBlock = new MaterialPropertyBlock();
                m_Tool.colorCheckerRenderer.GetPropertyBlock(propertyBlock);

                Assert.That(m_Tool.fieldsToDisplay, Is.EqualTo(1));
                Assert.That(m_Tool.fieldsPerRowToDisplay, Is.EqualTo(1));
                Assert.That(m_Tool.sphereModeToDisplay, Is.False);
                Assert.That(propertyBlock.GetTexture("_CheckerTexture"), Is.SameAs(litTexture));
                Assert.That(propertyBlock.GetTexture("_rawTexture"), Is.SameAs(rawTexture));
                Assert.That(propertyBlock.GetFloat("_rawTextureAvailable"), Is.EqualTo(1f));
                Assert.That(propertyBlock.GetFloat("_rawTexturePreExposure"), Is.EqualTo(0f));
                Assert.That(propertyBlock.GetFloat("_textureSlice"), Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(litTexture);
                Object.DestroyImmediate(rawTexture);
            }
        }

        [Test]
        public void CreateColorCheckerGameObject_AddsToolAndEditorOnlyTag()
        {
            var created = ColorCheckerToolMenuItems.CreateColorCheckerGameObject(null);

            try
            {
                Assert.That(created, Is.Not.Null);
                Assert.That(created.tag, Is.EqualTo("EditorOnly"));
                Assert.That(created.TryGetComponent<ColorCheckerTool>(out var colorChecker), Is.True);
                Assert.That(colorChecker.colorCheckerObject, Is.Not.Null);
            }
            finally
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
        }
    }
}
