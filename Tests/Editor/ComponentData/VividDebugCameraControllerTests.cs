using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VividDebugCameraControllerTests
    {
        private GameObject m_ControllerObject;
        private GameObject m_TargetObject;

        [UnityTest]
        public IEnumerator LateUpdate_SynchronizesWithSceneCamera_InPlayMode()
        {
            yield return new EnterPlayMode();

            m_ControllerObject = new GameObject("VividDebugCameraControllerTests_Controller");
            var localCamera = m_ControllerObject.AddComponent<Camera>();
            var controller = m_ControllerObject.AddComponent<VividDebugCameraController>();

            m_TargetObject = new GameObject("VividDebugCameraControllerTests_Target");
            var targetCamera = m_TargetObject.AddComponent<Camera>();
            m_TargetObject.transform.SetPositionAndRotation(
                new Vector3(10f, 5f, -2f),
                Quaternion.Euler(10f, 20f, 0f));
            localCamera.fieldOfView = 40f;
            targetCamera.fieldOfView = 80f;

            SetPrivateField(controller, "_targetCamera", m_TargetObject.transform);
            SetPrivateField(controller, "_lastEditorTime", EditorApplication.timeSinceStartup - 1d);
            InvokePrivateMethod(controller, "LateUpdate");

            Assert.That(controller.transform.position, Is.Not.EqualTo(Vector3.zero));
            Assert.That(controller.transform.rotation, Is.Not.EqualTo(Quaternion.identity));
            Assert.That(localCamera.fieldOfView, Is.GreaterThan(40f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_ControllerObject != null)
            {
                Object.DestroyImmediate(m_ControllerObject);
            }

            if (m_TargetObject != null)
            {
                Object.DestroyImmediate(m_TargetObject);
            }

            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        private static void SetPrivateField<T>(VividDebugCameraController controller, string fieldName, T value)
        {
            var field = typeof(VividDebugCameraController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' to exist.");
            field.SetValue(controller, value);
        }

        private static void InvokePrivateMethod(VividDebugCameraController controller, string methodName)
        {
            var method = typeof(VividDebugCameraController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected method '{methodName}' to exist.");
            method.Invoke(controller, null);
        }
    }
}
