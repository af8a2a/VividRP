using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividSceneViewCameraSettingsTests
    {
        private SceneView m_View;

        [SetUp]
        public void SetUp()
        {
            m_View = ScriptableObject.CreateInstance<SceneView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_View);
            VividAntialiasingRuntimeUtility.Clear();
        }

        [Test]
        public void Settings_InitializeSceneCameraWithTaa_AndReuseItsOwnData()
        {
            var settings = VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            var data = m_View.camera.GetComponent<VividAdditionalCameraData>();

            Assert.That(data, Is.Not.Null);
            Assert.That(data.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
            Assert.That(data.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View), Is.SameAs(settings));
            Assert.That(((SceneView.IAdditionalSettings)settings).linkedComponent, Is.SameAs(data));

            data.ConsumePostProcessingHistoryResetRequest();
            VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            Assert.That(data.ConsumePostProcessingHistoryResetRequest(), Is.False);
        }

        [Test]
        public void Settings_SurviveSerialization_AndApplyToAnotherCameraIndependently()
        {
            var settings = VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            settings.antialiasing.mode = VividAntialiasingMode.TemporalSuperResolution;
            settings.antialiasing.tsrQuality = VividTsrQualityMode.Quality;
            settings.antialiasing.tsrSharpness = 0.7f;
            settings.Apply();
            var restored = JsonUtility.FromJson<VividSceneViewCameraSettings>(JsonUtility.ToJson(settings));
            var other = new GameObject("Scene View AA Other Camera");
            try
            {
                restored.Bind(other.AddComponent<Camera>());
                var otherData = other.GetComponent<VividAdditionalCameraData>();
                Assert.That(otherData.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalSuperResolution));
                Assert.That(otherData.tsrQuality, Is.EqualTo(VividTsrQualityMode.Quality));
                Assert.That(otherData.tsrSharpness, Is.EqualTo(0.7f));

                restored.Reset();
                restored.Apply();
                Assert.That(otherData.antialiasing, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(m_View.camera.GetComponent<VividAdditionalCameraData>().antialiasing,
                    Is.EqualTo(VividAntialiasingMode.TemporalSuperResolution));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ClonedSettings_RebindToNewSceneCamera_WithoutSharingRuntimeState()
        {
            var settings = VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            var clone = (VividSceneViewCameraSettings)((ICloneable)settings).Clone();
            var other = new GameObject("Cloned Scene Camera");
            try
            {
                clone.Bind(other.AddComponent<Camera>());
                clone.antialiasing.mode = VividAntialiasingMode.None;
                clone.Apply();
                Assert.That(m_View.camera.GetComponent<VividAdditionalCameraData>().antialiasing,
                    Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(other.GetComponent<VividAdditionalCameraData>().antialiasing,
                    Is.EqualTo(VividAntialiasingMode.None));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void SceneCamera_TemporalJitterKeepsOriginalProjection_AndStopsWhenDisabled()
        {
            var camera = m_View.camera;
            var settings = VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            var additionalData = camera.GetComponent<VividAdditionalCameraData>();
            var target = new RenderTexture(640, 360, 0);
            var original = Matrix4x4.Frustum(-0.2f, 0.3f, -0.15f, 0.25f, 0.5f, 100f);
            var data = new VividAntialiasingData();
            try
            {
                camera.targetTexture = target;
                camera.SetProjectionMatrices(original, original);
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, true, data);
                VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalData, data, 1);
                additionalData.UpdateCameraMatrices(true);

                Assert.That(data.usesTemporalJitter, Is.True);
                Assert.That(additionalData.jitter.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(additionalData.nonJitteredProjectionMatrix, Is.EqualTo(original));
                Assert.That(additionalData.projectionMatrix.m02, Is.EqualTo(camera.projectionMatrix.m02).Within(0.00001f));
                Assert.That(additionalData.gpuProjectionMatrixNoJitter, Is.EqualTo(GL.GetGPUProjectionMatrix(original, true)));

                // The pipeline restores the editor's original projection after every render.
                camera.SetProjectionMatrices(original, original);
                settings.antialiasing.mode = VividAntialiasingMode.None;
                settings.Apply();
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, true, data);
                VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalData, data, 2);
                additionalData.UpdateCameraMatrices(true);
                Assert.That(data.usesTemporalJitter, Is.False);
                Assert.That(additionalData.jitter.sqrMagnitude, Is.LessThan(0.00000001f));
                Assert.That(camera.projectionMatrix, Is.EqualTo(original));
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void StableSceneCameraSettingsAndMatrices_DoNotAllocateAfterWarmup()
        {
            var camera = m_View.camera;
            VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
            var additionalData = camera.GetComponent<VividAdditionalCameraData>();
            var data = new VividAntialiasingData();
            var original = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 1000f);
            for (var index = 0; index < 8; index++)
            {
                VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
                camera.SetProjectionMatrices(original, original);
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, true, data);
                VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalData, data, index);
                additionalData.UpdateCameraMatrices(true);
            }

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
            {
                VividSceneViewCameraSettingsUI.GetOrCreateSettings(m_View);
                camera.SetProjectionMatrices(original, original);
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, true, data);
                VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalData, data, index);
                additionalData.UpdateCameraMatrices(true);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, Is.Zero);
        }
    }
}
