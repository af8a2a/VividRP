using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Editor.AdaptiveProbeVolume;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class MeshletAPVBakingTests
    {
        private Scene m_Scene;
        private string m_Path;
        private Material m_Material;
        private ProbeVolumeBakingSet m_BakingSet;

        [SetUp]
        public void SetUp()
        {
            m_Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            m_Path = AssetDatabase.GenerateUniqueAssetPath("Assets/MeshletAPVBakingTest.unity");
            EditorSceneManager.SaveScene(m_Scene, m_Path);
            m_Material = new Material(Shader.Find("VividRP/Material/StandardLit"));
            m_BakingSet = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            m_BakingSet.TryAddScene(AssetDatabase.GUIDFromAssetPath(m_Path));
        }

        [TearDown]
        public void TearDown()
        {
            MeshletAPVBaking.DestroyProxies();
            m_BakingSet.RemoveScene(AssetDatabase.GUIDFromAssetPath(m_Path));
            Object.DestroyImmediate(m_BakingSet);
            EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(m_Path);
            Object.DestroyImmediate(m_Material);
        }

        [Test]
        public void Proxy_PreservesSourceAndTransform_AndCleanupRemovesOnlyOwnedObjects()
        {
            var source = Source("Source");
            source.transform.localScale = new Vector3(-2, 3, 0.5f);
            source.transform.rotation = Quaternion.Euler(11, 29, 7);
            var unrelated = new GameObject("Unrelated");
            SceneManager.MoveGameObjectToScene(unrelated, m_Scene);
            MeshletAPVBaking.CreateProxies(m_BakingSet);
            try
            {
                var marker = source.GetComponentInChildren<MeshletAPVBakeProxy>();
                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.owner, Is.SameAs(source));
                Assert.That(marker.transform.localToWorldMatrix, Is.EqualTo(source.transform.localToWorldMatrix));
                Assert.That(marker.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(source.sourceMesh));
                Assert.That(marker.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(m_Material));
                Assert.That(marker.GetComponent<MeshRenderer>().receiveGI, Is.EqualTo(ReceiveGI.LightProbes));
            }
            finally
            {
                MeshletAPVBaking.DestroyProxies();
            }
            Assert.That(source.GetComponentInChildren<MeshletAPVBakeProxy>(), Is.Null);
            Assert.That(unrelated, Is.Not.Null);
            Assert.That(source.sourceMesh, Is.Not.Null);
        }

        [Test]
        public void Export_SkipsNonContributorsAndExistingNativeGeometry()
        {
            var nonStatic = Source("Dynamic");
            GameObjectUtility.SetStaticEditorFlags(nonStatic.gameObject, 0);
            var native = Source("Already native");
            native.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m_Material;
            MeshletAPVBaking.CreateProxies(m_BakingSet);
            Assert.That(nonStatic.GetComponentInChildren<MeshletAPVBakeProxy>(), Is.Null);
            Assert.That(native.GetComponentInChildren<MeshletAPVBakeProxy>(), Is.Null);
        }

        [Test]
        public void InvalidMaterial_RollsBackAllTemporaryGeometry()
        {
            var valid = Source("Valid");
            var invalid = Source("Invalid");
            var renderer = invalid.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = null;
            invalid.CaptureSourceFromRenderer(renderer);
            Object.DestroyImmediate(renderer);
            Assert.Throws<InvalidOperationException>(() => MeshletAPVBaking.CreateProxies(m_BakingSet));
            Assert.That(valid.GetComponentInChildren<MeshletAPVBakeProxy>(), Is.Null);
            Assert.That(invalid.GetComponentInChildren<MeshletAPVBakeProxy>(), Is.Null);
            Assert.That(MeshletAPVBaking.ProxyCount, Is.Zero);
        }

        private MeshletRenderer Source(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            SceneManager.MoveGameObjectToScene(go, m_Scene);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Material;
            var source = go.AddComponent<MeshletRenderer>();
            source.CaptureSourceFromRenderer(renderer);
            Object.DestroyImmediate(renderer);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            return source;
        }
    }
}
