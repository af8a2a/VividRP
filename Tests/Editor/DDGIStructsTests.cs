using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIStructsTests
    {
        [Test]
        public void DDGIVolumeDescGPUPacked_Create_PacksBalancedProfileAndProbeCounts()
        {
            var volumeObject = new GameObject("DDGI Packed Volume Test");
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                volume.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
                volume.SetBoundProxyShape(new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Box,
                    size = new Vector3(10.0f, 6.0f, 14.0f),
                });

                var serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                serializedObject.FindProperty("m_ProbeSpacing").vector3Value = new Vector3(2.0f, 2.0f, 2.0f);
                serializedObject.FindProperty("m_ProbeNormalBias").floatValue = 0.35f;
                serializedObject.FindProperty("m_ProbeViewBias").floatValue = 0.15f;
                serializedObject.FindProperty("m_ProbeMaxRayDistance").floatValue = 42.0f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");

                DDGIProfile profile = DDGIProfileTable.GetProfile(DDGIProfileId.Balanced);
                DDGIVolumeDescGPUPacked packed = DDGIVolumeDescGPUPacked.Create(volume, profile);

                uint expectedPacked0 = 6u | (4u << 10) | (8u << 20);
                uint expectedPacked2 = 144u | (6u << 16) | (14u << 24);
                uint expectedPacked4 = ((uint)DDGIVolumeTextureFormat.F32x2) << 17;

                Assert.That(packed.origin, Is.EqualTo(new Vector3(3.0f, 4.0f, 5.0f)));
                Assert.That(packed.probeSpacing, Is.EqualTo(new Vector3(2.0f, 2.0f, 2.0f)));
                Assert.That(packed.probeHysteresis, Is.EqualTo(0.97f).Within(0.0001f));
                Assert.That(packed.probeMaxRayDistance, Is.EqualTo(42.0f));
                Assert.That(packed.probeNormalBias, Is.EqualTo(0.35f));
                Assert.That(packed.probeViewBias, Is.EqualTo(0.15f));
                Assert.That(packed.rotation, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
                Assert.That(packed.probeRayRotation, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
                Assert.That(packed.packed0, Is.EqualTo(expectedPacked0));
                Assert.That(packed.packed2, Is.EqualTo(expectedPacked2));
                Assert.That(packed.packed4, Is.EqualTo(expectedPacked4));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void ShaderVariablesDDGI_CreateDisabled_TurnsOffRuntimeQueryState()
        {
            ShaderVariablesDDGI shaderVariables = ShaderVariablesDDGI.CreateDisabled();

            Assert.That(shaderVariables._DDGIWorldAabbMin_BlendDistance, Is.EqualTo(Vector4.zero));
            Assert.That(shaderVariables._DDGIWorldAabbMax_Enabled, Is.EqualTo(Vector4.zero));
            Assert.That(shaderVariables._DDGIVolumeOrigin_ProbeNormalBias, Is.EqualTo(Vector4.zero));
            Assert.That(shaderVariables._DDGIVolumeRotation, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
            Assert.That(shaderVariables._DDGIProbeSpacing_ProbeViewBias, Is.EqualTo(new Vector4(1.0f, 1.0f, 1.0f, 0.0f)));
            Assert.That(shaderVariables._DDGIProbeCounts_IrradianceInteriorTexels, Is.EqualTo(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));
            Assert.That(
                shaderVariables._DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat,
                Is.EqualTo(new Vector4(1.0f, 1.0f, (float)DDGIVolumeTextureFormat.U32, 0.0f)));
        }

        [Test]
        public void ShaderVariablesDDGI_Create_UsesVolumeBoundsAndBalancedProfile()
        {
            var volumeObject = new GameObject("DDGI Shader Variables Test");
            var volume = volumeObject.AddComponent<DDGIVolume>();

            try
            {
                volume.transform.position = new Vector3(7.0f, 2.0f, -3.0f);
                volume.SetBoundProxyShape(new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Box,
                    size = new Vector3(8.0f, 4.0f, 12.0f),
                });

                var serializedObject = new SerializedObject(volume);
                serializedObject.Update();
                serializedObject.FindProperty("m_BlendDistance").floatValue = 1.5f;
                serializedObject.FindProperty("m_ProbeSpacing").vector3Value = new Vector3(2.0f, 1.0f, 3.0f);
                serializedObject.FindProperty("m_ProbeNormalBias").floatValue = 0.3f;
                serializedObject.FindProperty("m_ProbeViewBias").floatValue = 0.45f;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                volume.SendMessage("OnValidate");

                DDGIProfile profile = DDGIProfileTable.GetProfile(DDGIProfileId.Balanced);
                ShaderVariablesDDGI shaderVariables = ShaderVariablesDDGI.Create(volume, profile);

                Assert.That(shaderVariables._DDGIWorldAabbMin_BlendDistance, Is.EqualTo(new Vector4(3.0f, 0.0f, -9.0f, 1.5f)));
                Assert.That(shaderVariables._DDGIWorldAabbMax_Enabled, Is.EqualTo(new Vector4(11.0f, 4.0f, 3.0f, 1.0f)));
                Assert.That(shaderVariables._DDGIVolumeOrigin_ProbeNormalBias, Is.EqualTo(new Vector4(7.0f, 2.0f, -3.0f, 0.3f)));
                Assert.That(shaderVariables._DDGIProbeSpacing_ProbeViewBias, Is.EqualTo(new Vector4(2.0f, 1.0f, 3.0f, 0.45f)));
                Assert.That(shaderVariables._DDGIProbeCounts_IrradianceInteriorTexels, Is.EqualTo(new Vector4(5.0f, 5.0f, 5.0f, 6.0f)));
                Assert.That(
                    shaderVariables._DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat,
                    Is.EqualTo(new Vector4(14.0f, 5.0f, (float)DDGIVolumeTextureFormat.U32, 0.0f)));
            }
            finally
            {
                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void DDGIRuntimeData_Reset_ClearsFrameStateAndDisablesQueries()
        {
            var runtimeData = new DDGIRuntimeData
            {
                supportsRayTracing = true,
                hasActiveVolume = true,
                isRuntimeReady = true,
                clearProbeTextures = true,
                probesPerPlane = 32,
                profileId = DDGIProfileId.Balanced,
            };

            runtimeData.Reset();

            Assert.That(runtimeData.supportsRayTracing, Is.False);
            Assert.That(runtimeData.hasActiveVolume, Is.False);
            Assert.That(runtimeData.isRuntimeReady, Is.False);
            Assert.That(runtimeData.clearProbeTextures, Is.False);
            Assert.That(runtimeData.activeVolume, Is.Null);
            Assert.That(runtimeData.probesPerPlane, Is.EqualTo(0));
            Assert.That(runtimeData.profileId, Is.EqualTo(DDGIProfileId.Balanced));
            Assert.That(runtimeData.shaderVariables._DDGIWorldAabbMax_Enabled, Is.EqualTo(Vector4.zero));
            Assert.That(runtimeData.volumeConstantsBuffer, Is.Null);
            Assert.That(runtimeData.instanceBuffer, Is.Null);
            Assert.That(runtimeData.materialBuffer, Is.Null);
        }
    }
}
