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
                Object.DestroyImmediate(gameObject);
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

                ((ISerializationCallbackReceiver)additionalData).OnAfterDeserialize();

                Assert.That(additionalData.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(additionalData.enableTAA, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
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
                Object.DestroyImmediate(gameObject);
            }
        }

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
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found.");
            field.SetValue(target, value);
        }
    }
}
