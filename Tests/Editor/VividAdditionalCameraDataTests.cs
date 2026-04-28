using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividAdditionalCameraDataTests
    {
        [Test]
        public void Antialiasing_Setter_KeepsLegacyTaaFlagInSync()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                additionalData.antialiasing = VividAntialiasingMode.CMAA2;

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.CMAA2));
                Assert.That(additionalData.enableTAA, Is.False);

                additionalData.enableTAA = true;

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(additionalData.enableTAA, Is.True);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OnAfterDeserialize_MigratesLegacyEnableTaaFlagToAntialiasingMode()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_Migration");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                SetPrivateField(additionalData, "m_Antialiasing", VividAntialiasingMode.None);
                SetPrivateField(additionalData, "m_EnableTAA", true);
                SetPrivateField(additionalData, "m_LegacyAntialiasingMigrated", false);

                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(additionalData.enableTAA, Is.True);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OnAfterDeserialize_DoesNotRestoreTaaAfterLegacyMigrationCompleted()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_NoRestoreTAA");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                SetPrivateField(additionalData, "m_Antialiasing", VividAntialiasingMode.None);
                SetPrivateField(additionalData, "m_EnableTAA", true);
                SetPrivateField(additionalData, "m_LegacyAntialiasingMigrated", false);
                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                SetPrivateField(additionalData, "m_Antialiasing", VividAntialiasingMode.None);
                SetPrivateField(additionalData, "m_EnableTAA", true);
                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.None));
                Assert.That(additionalData.enableTAA, Is.False);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Antialiasing_Setter_TracksStpModeAsTemporalAntialiasing()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_STP");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                additionalData.antialiasing = VividAntialiasingMode.SpatialTemporalPostProcessing;

                Assert.That(additionalData.enableTAA, Is.False);
                Assert.That(additionalData.enableSTP, Is.True);
                Assert.That(additionalData.usesTemporalAntialiasing, Is.True);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AntialiasingMode_Fsr3KeepsExplicitValueFive()
        {
            Assert.That((int)VividAntialiasingMode.FidelityFXSuperResolution3, Is.EqualTo(5));
        }

        [Test]
        public void Antialiasing_Setter_TracksFsr3ModeAsTemporalAntialiasing()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_FSR3");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                additionalData.enableFSR3 = true;

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.FidelityFXSuperResolution3));
                Assert.That(additionalData.enableFSR3, Is.True);
                Assert.That(additionalData.enableTAA, Is.False);
                Assert.That(additionalData.enableSTP, Is.False);
                Assert.That(additionalData.usesTemporalAntialiasing, Is.True);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Fsr3Settings_DefaultsAndClampsSharpness()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_FSR3Settings");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                Assert.That(additionalData.fsr3Quality, Is.EqualTo(VividFsr3QualityMode.Balanced));
                Assert.That(additionalData.fsr3EnableSharpening, Is.True);
                Assert.That(additionalData.fsr3Sharpness, Is.EqualTo(0.2f).Within(0.0001f));

                additionalData.fsr3Sharpness = -1.0f;
                Assert.That(additionalData.fsr3Sharpness, Is.EqualTo(0.0f));

                additionalData.fsr3Sharpness = 2.0f;
                Assert.That(additionalData.fsr3Sharpness, Is.EqualTo(1.0f));
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

#if DLSS_PLUGIN_INTEGRATE
        [Test]
        public void Antialiasing_Setter_TracksDlssModeAsTemporalAntialiasing()
        {
            var gameObject = new GameObject("VividAdditionalCameraDataTests_DLSS");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                additionalData.enableDLSS = true;
                additionalData.dlssQuality = DLSSQuality.MaxQuality;

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.DeepLearningSuperSampling));
                Assert.That(additionalData.enableDLSS, Is.True);
                Assert.That(additionalData.enableTAA, Is.False);
                Assert.That(additionalData.usesTemporalAntialiasing, Is.True);
                Assert.That(additionalData.dlssQuality, Is.EqualTo(DLSSQuality.MaxQuality));
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }
#else
        [Test]
        public void DLSSOptions_AreNotExposed_WhenDlssPluginIsNotIntegrated()
        {
            Assert.That(Enum.GetNames(typeof(VividAntialiasingMode)), Does.Not.Contain("DeepLearningSuperSampling"));
            Assert.That(typeof(VividAdditionalCameraData).GetProperty("enableDLSS"), Is.Null);
            Assert.That(typeof(VividAdditionalCameraData).GetProperty("dlssQuality"), Is.Null);
            Assert.That(
                typeof(VividAdditionalCameraData).GetField("m_DLSSQuality", BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);

            var runtimeAssembly = typeof(VividAdditionalCameraData).Assembly;
            Assert.That(runtimeAssembly.GetType("VividRP.Runtime.DLSSQuality"), Is.Null);
            Assert.That(runtimeAssembly.GetType("VividRP.Runtime.DLSSExtension"), Is.Null);
            Assert.That(runtimeAssembly.GetType("VividRP.Runtime.RenderPass.Core.DLSSPass"), Is.Null);

            var gameObject = new GameObject("VividAdditionalCameraDataTests_NoDLSS");
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();

            try
            {
                SetPrivateField(additionalData, "m_Antialiasing", (VividAntialiasingMode)4);
                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.None));

                SetPrivateField(additionalData, "m_Antialiasing", VividAntialiasingMode.FidelityFXSuperResolution3);
                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.FidelityFXSuperResolution3));
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }
#endif

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found.");
            field.SetValue(target, value);
        }
    }
}
