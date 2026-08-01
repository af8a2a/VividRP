using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class HairMaterialUtilityTests
    {
        [Test]
        public void HairShader_ExposesExpectedDefaults()
        {
            Material material = CreateMaterial();
            try
            {
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.AbsorptionModelProperty),
                    Is.EqualTo(HairMaterialUtility.PhysicalAbsorption));
                Assert.That(
                    material.GetFloat(HairMaterialUtility.MelaninProperty),
                    Is.EqualTo(0.805f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.MelaninRednessProperty),
                    Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.LongitudinalRoughnessProperty),
                    Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.AzimuthalRoughnessProperty),
                    Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(HairMaterialUtility.IorProperty),
                    Is.EqualTo(1.55f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.CuticleAngleProperty),
                    Is.EqualTo(3.0f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.FresnelApproximationProperty),
                    Is.EqualTo(1.0f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_ClampsAndRepairsChiangInputs()
        {
            Material material = CreateMaterial();
            try
            {
                material.SetFloat(
                    HairMaterialUtility.AbsorptionModelProperty,
                    1.75f);
                material.SetColor(
                    HairMaterialUtility.BaseColorProperty,
                    new Color(float.NaN, 2.0f, -1.0f, float.PositiveInfinity));
                material.SetFloat(HairMaterialUtility.MelaninProperty, -2.0f);
                material.SetFloat(
                    HairMaterialUtility.MelaninRednessProperty,
                    2.0f);
                material.SetFloat(
                    HairMaterialUtility.LongitudinalRoughnessProperty,
                    0.0f);
                material.SetFloat(
                    HairMaterialUtility.AzimuthalRoughnessProperty,
                    float.NaN);
                material.SetFloat(HairMaterialUtility.IorProperty, 8.0f);
                material.SetFloat(
                    HairMaterialUtility.CuticleAngleProperty,
                    -4.0f);
                material.SetFloat(
                    HairMaterialUtility.FresnelApproximationProperty,
                    0.25f);

                HairMaterialUtility.SetupMaterial(material);

                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.AbsorptionModelProperty),
                    Is.EqualTo(
                        HairMaterialUtility.NormalizedPhysicalAbsorption));
                Color baseColor = material.GetColor(
                    HairMaterialUtility.BaseColorProperty);
                Assert.That(baseColor.r, Is.EqualTo(0.227f).Within(0.0001f));
                Assert.That(baseColor.g, Is.EqualTo(1.0f));
                Assert.That(baseColor.b, Is.Zero);
                Assert.That(baseColor.a, Is.EqualTo(1.0f));
                Assert.That(
                    material.GetFloat(HairMaterialUtility.MelaninProperty),
                    Is.Zero);
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.MelaninRednessProperty),
                    Is.EqualTo(1.0f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.LongitudinalRoughnessProperty),
                    Is.EqualTo(0.001f).Within(0.00001f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.AzimuthalRoughnessProperty),
                    Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(
                    material.GetFloat(HairMaterialUtility.IorProperty),
                    Is.EqualTo(3.0f));
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.CuticleAngleProperty),
                    Is.Zero);
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.FresnelApproximationProperty),
                    Is.Zero);
                Assert.That(
                    material.GetFloat(
                        HairMaterialUtility.MaterialVersionProperty),
                    Is.EqualTo(HairMaterialUtility.CurrentMaterialVersion));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SynchronizesEmissionGiFlag()
        {
            Material material = CreateMaterial();
            try
            {
                material.SetColor(
                    HairMaterialUtility.EmissionColorProperty,
                    new Color(2.0f, 0.5f, 0.0f, 1.0f));
                HairMaterialUtility.SetupMaterial(material);
                Assert.That(
                    material.globalIlluminationFlags
                    & MaterialGlobalIlluminationFlags.EmissiveIsBlack,
                    Is.Zero);

                material.SetColor(
                    HairMaterialUtility.EmissionColorProperty,
                    Color.black);
                HairMaterialUtility.SetupMaterial(material);
                Assert.That(
                    material.globalIlluminationFlags
                    & MaterialGlobalIlluminationFlags.EmissiveIsBlack,
                    Is.Not.Zero);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(HairMaterialUtility.ShaderName);
            Assert.That(shader, Is.Not.Null);
            return new Material(shader);
        }
    }
}
