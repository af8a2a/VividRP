using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class FrameContextSystemTests
    {
        private GameObject m_CameraGameObject;
        private Camera m_Camera;

        [SetUp]
        public void SetUp()
        {
            m_CameraGameObject = new GameObject("TestCamera");
            m_Camera = m_CameraGameObject.AddComponent<Camera>();
            FrameContextSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FrameContextSystem.Clear();
            if (m_CameraGameObject != null)
                Object.DestroyImmediate(m_CameraGameObject);
        }

        [Test]
        public void GetOrCreate_ReturnsSameInstance_ForSameCamera()
        {
            var data1 = FrameContextSystem.GetOrCreate(m_Camera);
            var data2 = FrameContextSystem.GetOrCreate(m_Camera);

            Assert.That(data1, Is.SameAs(data2));
        }

        [Test]
        public void GetOrCreate_ReturnsNull_WhenCameraIsNull()
        {
            var data = FrameContextSystem.GetOrCreate(null);

            Assert.That(data, Is.Null);
        }

        [Test]
        public void GetOrCreate_ReturnsDifferentInstances_ForDifferentCameras()
        {
            var otherGo = new GameObject("OtherCamera");
            try
            {
                var otherCamera = otherGo.AddComponent<Camera>();
                var data1 = FrameContextSystem.GetOrCreate(m_Camera);
                var data2 = FrameContextSystem.GetOrCreate(otherCamera);

                Assert.That(data1, Is.Not.SameAs(data2));
            }
            finally
            {
                Object.DestroyImmediate(otherGo);
            }
        }

        [Test]
        public void Clear_RemovesAllTemporalData()
        {
            var data1 = FrameContextSystem.GetOrCreate(m_Camera);
            FrameContextSystem.Clear();
            var data2 = FrameContextSystem.GetOrCreate(m_Camera);

            Assert.That(data1, Is.Not.SameAs(data2));
        }

        [Test]
        public void Tick_PopulatesVividTemporalData_InContextContainer()
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = m_Camera;
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            cameraData.pixelWidth = 1920;
            cameraData.pixelHeight = 1080;
            cameraData.frameIndex = 1;
            cameraData.additionalData = m_Camera.GetVividAdditionalCameraData();
            cameraData.additionalData.UpdateCameraMatrices(false);

            var cmd = new CommandBuffer();
            FrameContextSystem.Update(frameData, cmd);
            cmd.Dispose();

            var temporalData = frameData.Get<VividTemporalData>();
            Assert.That(temporalData, Is.Not.Null);
            Assert.That(temporalData.nonJitteredViewProjectionMatrix, Is.Not.EqualTo(default(Matrix4x4)));
        }

        [Test]
        public void ExecutePostRender_InvokesSubsystemPostRenderCallbacks()
        {
            var frameData = new ContextContainer();
            using var cmd = new CommandBuffer();
            var invoked = false;

            void Handler(ContextContainer context, CommandBuffer commandBuffer)
            {
                invoked = ReferenceEquals(context, frameData) && ReferenceEquals(commandBuffer, cmd);
            }

            try
            {
                FrameContextSystem.SubsystemPostRender += Handler;

                FrameContextSystem.ExecutePostRender(frameData, cmd);

                Assert.That(invoked, Is.True);
            }
            finally
            {
                FrameContextSystem.SubsystemPostRender -= Handler;
            }
        }

        [Test]
        public void Clear_InvokesSubsystemDisposeCallbacks()
        {
            var invoked = false;

            void Handler()
            {
                invoked = true;
            }

            try
            {
                FrameContextSystem.SubsystemDispose += Handler;

                FrameContextSystem.Clear();

                Assert.That(invoked, Is.True);
            }
            finally
            {
                FrameContextSystem.SubsystemDispose -= Handler;
            }
        }
    }

    public class CameraTemporalDataTests
    {
        private GameObject m_CameraGameObject;
        private Camera m_Camera;

        [SetUp]
        public void SetUp()
        {
            m_CameraGameObject = new GameObject("TestCamera");
            m_Camera = m_CameraGameObject.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_CameraGameObject != null)
                Object.DestroyImmediate(m_CameraGameObject);
        }

        [Test]
        public void Update_FirstFrame_SetsPreviousEqualToCurrent()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);

            data.Update(cameraData);

            Assert.That(data.PreviousViewProjection, Is.EqualTo(data.ViewProjection));
            Assert.That(data.IsFirstFrame, Is.False);
        }

        [Test]
        public void Update_SubsequentFrame_AdvancesPreviousMatrix()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);

            var firstFrameVP = data.ViewProjection;

            // Move camera and advance frame
            m_Camera.transform.position = new Vector3(10, 0, 0);
            cameraData.additionalData.UpdateCameraMatrices(false);
            cameraData.frameIndex = 1;
            data.Update(cameraData);

            Assert.That(data.PreviousViewProjection, Is.EqualTo(firstFrameVP));
            Assert.That(data.ViewProjection, Is.Not.EqualTo(firstFrameVP));
        }

        [Test]
        public void Update_ThirdFrame_AdvancesPreviousPreviousViewMatrix()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);
            var firstViewMatrix = data.ViewMatrix;

            m_Camera.transform.position = new Vector3(2f, 0f, 0f);
            cameraData.additionalData.UpdateCameraMatrices(false);
            cameraData.frameIndex = 1;
            data.Update(cameraData);
            var secondViewMatrix = data.ViewMatrix;

            m_Camera.transform.position = new Vector3(4f, 0f, 0f);
            cameraData.additionalData.UpdateCameraMatrices(false);
            cameraData.frameIndex = 2;
            data.Update(cameraData);

            Assert.That(data.PreviousViewMatrix, Is.EqualTo(secondViewMatrix));
            Assert.That(data.PreviousPreviousViewMatrix, Is.EqualTo(firstViewMatrix));
        }

        [Test]
        public void Update_SameFrame_DoesNotAdvancePrevious()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);

            var firstVP = data.ViewProjection;
            var firstPrevVP = data.PreviousViewProjection;

            // Same frame index, different position
            m_Camera.transform.position = new Vector3(5, 0, 0);
            cameraData.additionalData.UpdateCameraMatrices(false);
            data.Update(cameraData);

            // Previous should not have advanced
            Assert.That(data.PreviousViewProjection, Is.EqualTo(firstPrevVP));
        }

        [Test]
        public void Update_AspectRatioChange_ResetsHistory()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);

            // Change aspect ratio by changing dimensions
            cameraData.actualWidth = 800;
            cameraData.actualHeight = 800;
            cameraData.pixelWidth = 800;
            cameraData.pixelHeight = 800;
            cameraData.frameIndex = 1;
            cameraData.additionalData.UpdateCameraMatrices(false);
            data.Update(cameraData);

            // After aspect ratio change, previous should equal current (reset)
            Assert.That(data.PreviousViewProjection, Is.EqualTo(data.ViewProjection));
        }

        [Test]
        public void Update_NullCamera_ResetsState()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);

            // Now update with null camera
            cameraData.camera = null;
            data.Update(cameraData);

            Assert.That(data.IsFirstFrame, Is.True);
            Assert.That(data.ViewProjection, Is.EqualTo(Matrix4x4.identity));
        }

        [Test]
        public void Reset_SetsAllFieldsToDefaults()
        {
            var data = new CameraTemporalData();
            var cameraData = CreateCameraData(frameIndex: 0);
            data.Update(cameraData);

            data.Reset();

            Assert.That(data.IsFirstFrame, Is.True);
            Assert.That(data.ViewProjection, Is.EqualTo(Matrix4x4.identity));
            Assert.That(data.PreviousViewProjection, Is.EqualTo(Matrix4x4.identity));
            Assert.That(data.Jitter, Is.EqualTo(Vector2.zero));
            Assert.That(data.PreviousJitter, Is.EqualTo(Vector2.zero));
            Assert.That(data.PreviousPreviousViewMatrix, Is.EqualTo(Matrix4x4.identity));
            Assert.That(data.PreviousPreviousProjectionMatrix, Is.EqualTo(Matrix4x4.identity));
            Assert.That(data.Width, Is.Zero);
            Assert.That(data.Height, Is.Zero);
        }

        private VividCameraData CreateCameraData(int frameIndex)
        {
            var additionalData = m_Camera.GetVividAdditionalCameraData();
            additionalData.UpdateCameraMatrices(false);

            var cameraData = new VividCameraData
            {
                camera = m_Camera,
                additionalData = additionalData,
                actualWidth = 1920,
                actualHeight = 1080,
                pixelWidth = 1920,
                pixelHeight = 1080,
                frameIndex = frameIndex,
            };
            return cameraData;
        }
    }
}
