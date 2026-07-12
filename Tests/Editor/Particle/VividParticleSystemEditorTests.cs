using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.Particle;

namespace VividRP.Editor.Tests
{
    public sealed class VividParticleSystemEditorTests
    {
        private GameObject m_GameObject;
        private VividParticleSystemAsset m_Asset;
        private UnityEditor.Editor m_Editor;

        [SetUp]
        public void SetUp()
        {
            VividParticleSystemManager.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            VividParticleSystemManager.ClearForTests();

            if (m_Editor != null)
                Object.DestroyImmediate(m_Editor);

            if (m_Asset != null)
                Object.DestroyImmediate(m_Asset);

            if (m_GameObject != null)
                Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void CreateEditor_UsesVividParticleSystemEditor_ForComponent()
        {
            VividParticleSystem system = CreateSystem();

            m_Editor = UnityEditor.Editor.CreateEditor(system);

            Assert.That(m_Editor, Is.TypeOf<VividParticleSystemEditor>());
        }

        [Test]
        public void CreateEditor_UsesVividParticleSystemAssetEditor_ForAsset()
        {
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();

            m_Editor = UnityEditor.Editor.CreateEditor(m_Asset);

            Assert.That(m_Editor, Is.TypeOf<VividParticleSystemAssetEditor>());
        }

        [Test]
        public void CreateEditor_UsesVividParticleForceFieldEditor_ForForceField()
        {
            VividParticleSystem system = CreateSystem();
            VividParticleForceField forceField = system.gameObject.AddComponent<VividParticleForceField>();

            m_Editor = UnityEditor.Editor.CreateEditor(forceField);

            Assert.That(m_Editor, Is.TypeOf<VividParticleForceFieldEditor>());
        }

        [Test]
        public void ForceFieldEditor_ResolveRanges_ClampsAndOrdersSceneHandleValues()
        {
            VividParticleForceFieldEditor.ResolveRanges(
                startRange: 3.0f,
                endRange: 1.0f,
                out float startRange,
                out float endRange);

            Assert.That(startRange, Is.EqualTo(3.0f));
            Assert.That(endRange, Is.EqualTo(3.0f));

            VividParticleForceFieldEditor.ResolveRanges(
                startRange: -2.0f,
                endRange: 4.0f,
                out startRange,
                out endRange);

            Assert.That(startRange, Is.EqualTo(0.0f));
            Assert.That(endRange, Is.EqualTo(4.0f));
            Assert.That(VividParticleForceFieldEditor.ResolveLength(-1.0f), Is.EqualTo(0.0f));
            Assert.That(VividParticleForceFieldEditor.ResolveLength(2.5f), Is.EqualTo(2.5f));
        }

        [Test]
        public void MenuItems_CreateVividParticleSystemGameObject_AddsComponentAndParents()
        {
            var parent = new GameObject("Vivid Particle Menu Parent");
            GameObject created = null;
            try
            {
                created = VividParticleSystemMenuItems.CreateVividParticleSystemGameObject(parent);

                Assert.That(VividParticleSystemMenuItems.CreateVividParticleSystemMenuPath, Is.EqualTo("GameObject/Rendering/Vivid Particle System"));
                Assert.That(created, Is.Not.Null);
                Assert.That(created.name, Is.EqualTo("Vivid Particle System"));
                Assert.That(created.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(created.GetComponent<VividParticleSystem>(), Is.Not.Null);
                Assert.That(Selection.activeGameObject, Is.EqualTo(created));
            }
            finally
            {
                if (created != null)
                    Object.DestroyImmediate(created);

                Object.DestroyImmediate(parent);
                VividParticleSystemManager.ClearForTests();
            }
        }

        [Test]
        public void InspectorUtility_FindsModuleRoots_ForComponentAndAsset()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();

            using var systemObject = new SerializedObject(system);
            using var assetObject = new SerializedObject(m_Asset);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindModuleRoots(
                    systemObject,
                    out SerializedProperty systemMain,
                    out SerializedProperty systemEmission,
                    out SerializedProperty systemShape,
                    out SerializedProperty systemRenderer),
                Is.True);
            Assert.That(systemMain, Is.Not.Null);
            Assert.That(systemEmission, Is.Not.Null);
            Assert.That(systemShape, Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindForceOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindExternalForcesProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindCollisionProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindTriggerProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindColorOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindColorBySpeedProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindSizeOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindSizeBySpeedProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindRotationOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindRotationBySpeedProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindVelocityOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindInheritVelocityProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindLifetimeByEmitterSpeedProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindLimitVelocityOverLifetimeProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindNoiseProperty(systemObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindCustomDataProperty(systemObject),
                Is.Not.Null);
            Assert.That(systemRenderer, Is.Not.Null);
            Assert.That(VividParticleSystemEditorUtility.FindAssetProperty(systemObject), Is.Not.Null);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindModuleRoots(
                    assetObject,
                    out SerializedProperty assetMain,
                    out SerializedProperty assetEmission,
                    out SerializedProperty assetShape,
                    out SerializedProperty assetRenderer),
                Is.True);
            Assert.That(assetMain, Is.Not.Null);
            Assert.That(assetEmission, Is.Not.Null);
            Assert.That(assetShape, Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindForceOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindExternalForcesProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindCollisionProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindTriggerProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindColorOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindColorBySpeedProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindSizeOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindSizeBySpeedProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindRotationOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindRotationBySpeedProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindVelocityOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindInheritVelocityProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindLifetimeByEmitterSpeedProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindLimitVelocityOverLifetimeProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindNoiseProperty(assetObject),
                Is.Not.Null);
            Assert.That(
                VividParticleSystemEditorUtility.FindCustomDataProperty(assetObject),
                Is.Not.Null);
            Assert.That(assetRenderer, Is.Not.Null);
        }

        [Test]
        public void InspectorUtility_ApplyAssetTemplate_CopiesModulesThroughPublicProperty()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_Asset.main.startLifetime = 12.0f;
            m_Asset.emission.rateOverTime = 4.0f;
            m_Asset.shape.shapeType = VividParticleShapeType.Sphere;
            m_Asset.shape.radius = 3.0f;
            m_Asset.forceOverLifetime.enabled = true;
            m_Asset.forceOverLifetime.force = Vector3.up;
            m_Asset.colorOverLifetime.enabled = true;
            m_Asset.colorOverLifetime.color = CreateGradient(Color.red, Color.blue);
            m_Asset.sizeOverLifetime.enabled = true;
            m_Asset.sizeOverLifetime.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.5f);
            m_Asset.rotationOverLifetime.enabled = true;
            m_Asset.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 45.0f);
            m_Asset.noise.enabled = true;
            m_Asset.noise.strength = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            m_Asset.noise.frequency = 0.75f;
            m_Asset.noise.quality = VividParticleNoiseQuality.Medium;
            m_Asset.noise.remapEnabled = true;
            m_Asset.noise.remapX = AnimationCurve.Linear(0.0f, -0.25f, 1.0f, 0.25f);
            m_Asset.noise.positionAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);
            m_Asset.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 20.0f);
            m_Asset.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.25f);
            m_Asset.rendererModule.color = Color.cyan;
            system.main.startLifetime = 1.0f;
            system.emission.rateOverTime = 0.0f;
            system.shape.shapeType = VividParticleShapeType.Point;
            system.rendererModule.color = Color.red;

            bool applied = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);

            Assert.That(applied, Is.True);
            Assert.That(system.asset, Is.SameAs(m_Asset));
            Assert.That(system.main.startLifetime, Is.EqualTo(12.0f));
            Assert.That(system.emission.rateOverTime, Is.EqualTo(4.0f));
            Assert.That(system.shape.shapeType, Is.EqualTo(VividParticleShapeType.Sphere));
            Assert.That(system.shape.radius, Is.EqualTo(3.0f));
            Assert.That(system.forceOverLifetime.enabled, Is.True);
            Assert.That(system.forceOverLifetime.force, Is.EqualTo(Vector3.up));
            Assert.That(system.colorOverLifetime.enabled, Is.True);
            Assert.That(system.colorOverLifetime.Evaluate(0.0f), Is.EqualTo(Color.red));
            Assert.That(system.sizeOverLifetime.enabled, Is.True);
            Assert.That(system.sizeOverLifetime.Evaluate(1.0f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(system.rotationOverLifetime.enabled, Is.True);
            Assert.That(system.rotationOverLifetime.EvaluateAngularVelocity(0.5f), Is.EqualTo(45.0f).Within(0.0001f));
            Assert.That(system.noise.enabled, Is.True);
            Assert.That(system.noise.EvaluateStrength(0.5f), Is.EqualTo(Vector3.one * 2.0f));
            Assert.That(system.noise.frequency, Is.EqualTo(0.75f));
            Assert.That(system.noise.quality, Is.EqualTo(VividParticleNoiseQuality.Medium));
            Assert.That(system.noise.remapEnabled, Is.True);
            Assert.That(system.noise.EvaluateRemap(Vector3.zero).x, Is.EqualTo(-0.25f));
            Assert.That(system.noise.EvaluatePositionAmount(0.5f), Is.EqualTo(0.5f));
            Assert.That(system.noise.EvaluateRotationAmount(0.5f), Is.EqualTo(20.0f));
            Assert.That(system.noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.25f));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void InspectorUtility_ApplyAssetTemplate_CanForceReapplySameAsset()
        {
            VividParticleSystem system = CreateSystem();
            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_Asset.main.startSize = 2.0f;
            VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);

            system.main.startSize = 8.0f;
            m_Asset.main.startSize = 3.0f;
            bool appliedWithoutForce = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: false);
            bool appliedWithForce = VividParticleSystemEditorUtility.ApplyAssetTemplate(system, m_Asset, force: true);

            Assert.That(appliedWithoutForce, Is.False);
            Assert.That(appliedWithForce, Is.True);
            Assert.That(system.asset, Is.SameAs(m_Asset));
            Assert.That(system.main.startSize, Is.EqualTo(3.0f));
        }

        [Test]
        public void InspectorUtility_CopyComponentSettingsToAsset_CopiesModulesWithoutLinking()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startLifetime = 9.0f;
            system.main.startColor = Color.green;
            system.emission.rateOverTime = 3.0f;
            system.emission.bursts = new[]
            {
                new VividParticleBurst(0.25f, 4),
            };
            system.shape.shapeType = VividParticleShapeType.Box;
            system.shape.boxSize = new Vector3(1.0f, 2.0f, 3.0f);
            system.forceOverLifetime.enabled = true;
            system.forceOverLifetime.force = new Vector3(3.0f, 2.0f, 1.0f);
            system.forceOverLifetime.space = VividParticleForceSpace.World;
            VividParticleForceField forceField = m_GameObject.AddComponent<VividParticleForceField>();
            system.externalForces.enabled = true;
            system.externalForces.influenceFilter = VividParticleGameObjectFilter.List;
            system.externalForces.multiplier = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);
            system.externalForces.AddInfluence(forceField);
            system.collision.enabled = true;
            system.collision.bounce = 0.75f;
            system.collision.AddPlane(m_GameObject.transform);
            SphereCollider triggerCollider = m_GameObject.AddComponent<SphereCollider>();
            system.trigger.enabled = true;
            system.trigger.enter = VividParticleOverlapAction.Callback;
            system.trigger.AddCollider(triggerCollider);
            system.main.emitterVelocityMode = VividParticleEmitterVelocityMode.Custom;
            system.main.customEmitterVelocity = new Vector3(2.0f, 3.0f, 4.0f);
            system.inheritVelocity.enabled = true;
            system.inheritVelocity.mode = VividParticleInheritVelocityMode.Current;
            system.inheritVelocity.curve = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
            system.limitVelocityOverLifetime.enabled = true;
            system.limitVelocityOverLifetime.limit = AnimationCurve.Constant(0.0f, 1.0f, 4.0f);
            system.limitVelocityOverLifetime.dampen = 0.25f;
            system.colorOverLifetime.enabled = true;
            system.colorOverLifetime.color = CreateGradient(Color.green, Color.blue);
            system.colorBySpeed.enabled = true;
            system.colorBySpeed.color = CreateGradient(Color.red, Color.cyan);
            system.colorBySpeed.range = new Vector2(2.0f, 8.0f);
            system.sizeOverLifetime.enabled = true;
            system.sizeOverLifetime.size = AnimationCurve.Linear(0.0f, 2.0f, 1.0f, 0.25f);
            system.sizeBySpeed.enabled = true;
            system.sizeBySpeed.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 3.0f);
            system.sizeBySpeed.range = new Vector2(1.0f, 5.0f);
            system.rotationOverLifetime.enabled = true;
            system.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 120.0f);
            system.rotationBySpeed.enabled = true;
            system.rotationBySpeed.range = new Vector2(1.0f, 7.0f);
            system.rotationBySpeed.z = AnimationCurve.Constant(0.0f, 1.0f, 60.0f);
            system.noise.enabled = true;
            system.noise.separateAxes = true;
            system.noise.strengthX = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            system.noise.strengthY = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            system.noise.strengthZ = AnimationCurve.Constant(0.0f, 1.0f, 3.0f);
            system.noise.frequency = 0.5f;
            system.noise.quality = VividParticleNoiseQuality.Low;
            system.noise.remapEnabled = true;
            system.noise.remapZ = AnimationCurve.Linear(0.0f, -0.5f, 1.0f, 0.5f);
            system.noise.octaveCount = 3;
            system.noise.positionAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
            system.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 45.0f);
            system.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);
            system.customData.mode1 = VividParticleCustomDataMode.Vector;
            system.customData.numberOfComponents1 = 2;
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 2.0f));
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.color = Color.yellow;
            system.rendererModule.stretchLengthScale = 5.0f;

            m_Asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            bool copied = VividParticleSystemEditorUtility.CopyComponentSettingsToAsset(system, m_Asset);

            Assert.That(copied, Is.True);
            Assert.That(m_Asset.main.startLifetime, Is.EqualTo(9.0f));
            Assert.That(m_Asset.main.startColor, Is.EqualTo(Color.green));
            Assert.That(m_Asset.emission.rateOverTime, Is.EqualTo(3.0f));
            Assert.That(m_Asset.emission.bursts, Has.Length.EqualTo(1));
            Assert.That(m_Asset.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(m_Asset.emission.bursts[0].count, Is.EqualTo(4));
            Assert.That(m_Asset.shape.shapeType, Is.EqualTo(VividParticleShapeType.Box));
            Assert.That(m_Asset.shape.boxSize, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(m_Asset.forceOverLifetime.enabled, Is.True);
            Assert.That(m_Asset.forceOverLifetime.force, Is.EqualTo(new Vector3(3.0f, 2.0f, 1.0f)));
            Assert.That(m_Asset.forceOverLifetime.space, Is.EqualTo(VividParticleForceSpace.World));
            Assert.That(m_Asset.externalForces.enabled, Is.True);
            Assert.That(
                m_Asset.externalForces.influenceFilter,
                Is.EqualTo(VividParticleGameObjectFilter.List));
            Assert.That(m_Asset.externalForces.EvaluateMultiplier(0.5f), Is.EqualTo(0.5f));
            Assert.That(m_Asset.externalForces.GetInfluence(0), Is.EqualTo(forceField));
            Assert.That(m_Asset.collision.enabled, Is.True);
            Assert.That(m_Asset.collision.bounce, Is.EqualTo(0.75f));
            Assert.That(m_Asset.collision.GetPlane(0), Is.EqualTo(m_GameObject.transform));
            Assert.That(m_Asset.trigger.enabled, Is.True);
            Assert.That(m_Asset.trigger.enter, Is.EqualTo(VividParticleOverlapAction.Callback));
            Assert.That(m_Asset.trigger.GetCollider(0), Is.SameAs(triggerCollider));
            Assert.That(m_Asset.main.emitterVelocityMode, Is.EqualTo(VividParticleEmitterVelocityMode.Custom));
            Assert.That(m_Asset.main.customEmitterVelocity, Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(m_Asset.inheritVelocity.enabled, Is.True);
            Assert.That(m_Asset.inheritVelocity.mode, Is.EqualTo(VividParticleInheritVelocityMode.Current));
            Assert.That(m_Asset.inheritVelocity.Evaluate(0.5f), Is.EqualTo(0.75f));
            Assert.That(m_Asset.limitVelocityOverLifetime.enabled, Is.True);
            Assert.That(
                m_Asset.limitVelocityOverLifetime.EvaluateLimit(0.5f),
                Is.EqualTo(Vector3.one * 4.0f));
            Assert.That(m_Asset.limitVelocityOverLifetime.dampen, Is.EqualTo(0.25f));
            Assert.That(m_Asset.colorOverLifetime.enabled, Is.True);
            Assert.That(m_Asset.colorOverLifetime.Evaluate(0.0f), Is.EqualTo(Color.green));
            Assert.That(m_Asset.colorBySpeed.enabled, Is.True);
            Assert.That(m_Asset.colorBySpeed.range, Is.EqualTo(new Vector2(2.0f, 8.0f)));
            Assert.That(m_Asset.colorBySpeed.Evaluate(2.0f), Is.EqualTo(Color.red));
            Assert.That(m_Asset.sizeOverLifetime.enabled, Is.True);
            Assert.That(m_Asset.sizeOverLifetime.Evaluate(1.0f), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(m_Asset.sizeBySpeed.enabled, Is.True);
            Assert.That(m_Asset.sizeBySpeed.range, Is.EqualTo(new Vector2(1.0f, 5.0f)));
            Assert.That(m_Asset.sizeBySpeed.Evaluate(5.0f), Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(m_Asset.rotationOverLifetime.enabled, Is.True);
            Assert.That(m_Asset.rotationOverLifetime.EvaluateAngularVelocity(0.5f), Is.EqualTo(120.0f).Within(0.0001f));
            Assert.That(m_Asset.rotationBySpeed.enabled, Is.True);
            Assert.That(m_Asset.rotationBySpeed.range, Is.EqualTo(new Vector2(1.0f, 7.0f)));
            Assert.That(
                m_Asset.rotationBySpeed.EvaluateAngularVelocity(4.0f).z,
                Is.EqualTo(60.0f).Within(0.0001f));
            Assert.That(m_Asset.noise.enabled, Is.True);
            Assert.That(m_Asset.noise.separateAxes, Is.True);
            Assert.That(m_Asset.noise.EvaluateStrength(0.5f), Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(m_Asset.noise.frequency, Is.EqualTo(0.5f));
            Assert.That(m_Asset.noise.quality, Is.EqualTo(VividParticleNoiseQuality.Low));
            Assert.That(m_Asset.noise.remapEnabled, Is.True);
            Assert.That(m_Asset.noise.EvaluateRemap(Vector3.zero).z, Is.EqualTo(-0.5f));
            Assert.That(m_Asset.noise.octaveCount, Is.EqualTo(3));
            Assert.That(m_Asset.noise.EvaluatePositionAmount(0.5f), Is.EqualTo(0.75f));
            Assert.That(m_Asset.noise.EvaluateRotationAmount(0.5f), Is.EqualTo(45.0f));
            Assert.That(m_Asset.noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.5f));
            Assert.That(m_Asset.customData.mode1, Is.EqualTo(VividParticleCustomDataMode.Vector));
            Assert.That(m_Asset.customData.numberOfComponents1, Is.EqualTo(2));
            Assert.That(
                m_Asset.customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f).x,
                Is.EqualTo(2.0f));
            Assert.That(m_Asset.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(m_Asset.rendererModule.color, Is.EqualTo(Color.yellow));
            Assert.That(m_Asset.rendererModule.stretchLengthScale, Is.EqualTo(5.0f));

            system.emission.bursts[0] = new VividParticleBurst(0.75f, 9);
            system.rendererModule.color = Color.red;
            system.limitVelocityOverLifetime.limit = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            system.colorBySpeed.color = CreateGradient(Color.black, Color.white);
            system.sizeBySpeed.size = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            system.rotationBySpeed.z = AnimationCurve.Constant(0.0f, 1.0f, 180.0f);
            system.noise.strengthX = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            system.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            system.noise.remapZ = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 9.0f));
            system.main.customEmitterVelocity = Vector3.one * 9.0f;
            system.inheritVelocity.curve = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);

            Assert.That(m_Asset.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(m_Asset.emission.bursts[0].count, Is.EqualTo(4));
            Assert.That(m_Asset.rendererModule.color, Is.EqualTo(Color.yellow));
            Assert.That(
                m_Asset.limitVelocityOverLifetime.EvaluateLimit(0.5f),
                Is.EqualTo(Vector3.one * 4.0f));
            Assert.That(m_Asset.colorBySpeed.Evaluate(2.0f), Is.EqualTo(Color.red));
            Assert.That(m_Asset.sizeBySpeed.Evaluate(5.0f), Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(
                m_Asset.rotationBySpeed.EvaluateAngularVelocity(4.0f).z,
                Is.EqualTo(60.0f).Within(0.0001f));
            Assert.That(m_Asset.noise.EvaluateStrength(0.5f), Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(m_Asset.noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.5f));
            Assert.That(m_Asset.noise.EvaluateRemap(Vector3.zero).z, Is.EqualTo(-0.5f));
            Assert.That(
                m_Asset.customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f).x,
                Is.EqualTo(2.0f));
            Assert.That(m_Asset.main.customEmitterVelocity, Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(m_Asset.inheritVelocity.Evaluate(0.5f), Is.EqualTo(0.75f));
        }

        [Test]
        public void ShapeSceneData_ReadsAndWritesSerializedShape()
        {
            VividParticleSystem system = CreateSystem();
            system.shape.shapeType = VividParticleShapeType.Box;
            system.shape.radius = 2.0f;
            system.shape.boxSize = new Vector3(1.0f, 2.0f, 3.0f);
            system.shape.angle = 35.0f;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty shape = serializedSystem.FindProperty("m_Shape");

            Assert.That(
                VividParticleSystemEditorUtility.TryReadShapeSceneData(
                    shape,
                    out VividParticleShapeSceneData data),
                Is.True);
            Assert.That(data.Enabled, Is.True);
            Assert.That(data.ShapeType, Is.EqualTo(VividParticleShapeType.Box));
            Assert.That(data.Radius, Is.EqualTo(2.0f));
            Assert.That(data.BoxSize, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(data.Angle, Is.EqualTo(35.0f));

            data.Radius = -10.0f;
            data.BoxSize = new Vector3(-1.0f, 4.0f, -5.0f);
            data.Angle = 120.0f;
            VividParticleSystemEditorUtility.WriteShapeSceneData(shape, data);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.shape.radius, Is.EqualTo(VividParticleShapeModule.MinimumRadius));
            Assert.That(system.shape.boxSize, Is.EqualTo(new Vector3(0.0f, 4.0f, 0.0f)));
            Assert.That(system.shape.angle, Is.EqualTo(89.0f));
        }

        [Test]
        public void ShapeSceneData_ReadsAndWritesRuntimeModule_ForSceneGui()
        {
            VividParticleSystem system = CreateSystem();
            system.shape.enabled = true;
            system.shape.shapeType = VividParticleShapeType.Cone;
            system.shape.radius = 3.0f;
            system.shape.boxSize = new Vector3(2.0f, 4.0f, 6.0f);
            system.shape.angle = 25.0f;

            Assert.That(
                VividParticleSystemEditorUtility.TryReadShapeSceneData(
                    system.shape,
                    out VividParticleShapeSceneData data),
                Is.True);
            Assert.That(data.Enabled, Is.True);
            Assert.That(data.ShapeType, Is.EqualTo(VividParticleShapeType.Cone));
            Assert.That(data.Radius, Is.EqualTo(3.0f));
            Assert.That(data.BoxSize, Is.EqualTo(new Vector3(2.0f, 4.0f, 6.0f)));
            Assert.That(data.Angle, Is.EqualTo(25.0f));

            data.Radius = -1.0f;
            data.BoxSize = new Vector3(-2.0f, 5.0f, -6.0f);
            data.Angle = 180.0f;
            VividParticleSystemEditorUtility.WriteShapeSceneData(system.shape, data);

            Assert.That(system.shape.radius, Is.EqualTo(VividParticleShapeModule.MinimumRadius));
            Assert.That(system.shape.boxSize, Is.EqualTo(new Vector3(0.0f, 5.0f, 0.0f)));
            Assert.That(system.shape.angle, Is.EqualTo(89.0f));
        }

        [Test]
        public void ShapeSceneData_CalculatesStableConePreviewLength()
        {
            float narrow = VividParticleSystemEditorUtility.GetConePreviewLength(2.0f, 10.0f);
            float wide = VividParticleSystemEditorUtility.GetConePreviewLength(2.0f, 80.0f);

            Assert.That(narrow, Is.GreaterThan(wide));
            Assert.That(narrow, Is.LessThanOrEqualTo(8.0f));
            Assert.That(wide, Is.GreaterThanOrEqualTo(0.25f));
        }

        [Test]
        public void ShapeSceneData_ConvertsConeAngleHandlePosition()
        {
            Vector3 handlePosition = VividParticleSystemEditorUtility.GetConeAngleHandlePosition(2.0f, 35.0f);
            float angle = VividParticleSystemEditorUtility.GetConeAngleFromHandlePosition(handlePosition);

            Assert.That(angle, Is.EqualTo(35.0f).Within(0.0001f));
            Assert.That(
                VividParticleSystemEditorUtility.GetConeAngleFromHandlePosition(new Vector3(1000.0f, 0.0f, 0.00001f)),
                Is.EqualTo(89.0f));
        }

        [Test]
        public void EmissionBurstUtility_FindsAndInitializesSerializedBurst()
        {
            VividParticleSystem system = CreateSystem();

            using var serializedSystem = new SerializedObject(system);

            Assert.That(
                VividParticleSystemEditorUtility.TryFindEmissionBurstsProperty(
                    serializedSystem,
                    out SerializedProperty bursts),
                Is.True);
            Assert.That(bursts, Is.Not.Null);

            bursts.arraySize = 1;
            SerializedProperty burst = bursts.GetArrayElementAtIndex(0);
            Assert.That(
                VividParticleSystemEditorUtility.TryWriteBurstElement(
                    burst,
                    time: -1.0f,
                    count: -3),
                Is.True);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.emission.bursts, Has.Length.EqualTo(1));
            Assert.That(system.emission.bursts[0].time, Is.EqualTo(0.0f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(0));

            serializedSystem.Update();
            Assert.That(
                VividParticleSystemEditorUtility.TryWriteBurstElement(
                    bursts.GetArrayElementAtIndex(0),
                    time: 0.25f,
                    count: 4),
                Is.True);
            serializedSystem.ApplyModifiedProperties();

            Assert.That(system.emission.bursts[0].time, Is.EqualTo(0.25f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(4));
        }

        [Test]
        public void PreviewUtility_ScrubsDeterministically_AndRestartsPlayback()
        {
            VividParticleSystem system = CreateSystem();
            system.main.playOnAwake = false;
            system.main.duration = 2.0f;
            system.main.loop = false;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.emission.enabled = true;
            system.emission.rateOverTime = 4.0f;
            system.shape.enabled = false;

            Assert.That(VividParticleSystemEditorUtility.ScrubPreview(system, 0.5f), Is.True);
            Assert.That(system.isPaused, Is.True);
            Assert.That(system.time, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(system.particleCount, Is.EqualTo(2));

            Assert.That(VividParticleSystemEditorUtility.RestartPreview(system, play: true), Is.True);
            Assert.That(system.isPlaying, Is.True);
            Assert.That(system.isPaused, Is.False);
            Assert.That(system.time, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(system.particleCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemEditorUtility.ScrubPreview(null, 1.0f), Is.False);
            Assert.That(VividParticleSystemEditorUtility.RestartPreview(null, play: true), Is.False);
        }

        [Test]
        public void GpuLayoutPreview_CreatesDescriptorFromSerializedRendererModule()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.rotationDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.velocityDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.uvDataEnabled = true;
            system.rendererModule.customData1Enabled = true;
            system.rendererModule.meshIndexDataEnabled = true;
            system.textureSheetAnimation.enabled = true;
            system.textureSheetAnimation.numTilesX = 4;
            system.textureSheetAnimation.numTilesY = 2;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor),
                Is.True);
            Assert.That(descriptor.RenderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(descriptor.IncludeUV, Is.True);
            Assert.That(descriptor.UVDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
            Assert.That(descriptor.IncludeCustomData1, Is.True);
            Assert.That(descriptor.IncludeCustomData2, Is.False);
            Assert.That(descriptor.IncludeMeshIndex, Is.True);

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor, out var colorInfo),
                Is.True);
            Assert.That(colorInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Rotation, out var rotationInfo),
                Is.True);
            Assert.That(rotationInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch, out var velocityInfo),
                Is.True);
            Assert.That(velocityInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(velocityInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat3));
            Assert.That(
                layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.UV, out var uvInfo),
                Is.True);
            Assert.That(uvInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout.PerInstanceElementByteSize,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 3
                    + VividParticleSystemManager.SizeOfFloat3));
            Assert.That(
                layout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnUVMask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(
                layout.PerSharpValueBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData1)));
        }

        [Test]
        public void GpuLayoutPreview_ReturnsFalse_WhenRendererModuleIsMissing()
        {
            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    null,
                    out _),
                Is.False);
        }

        [Test]
        public void GpuLayoutPreview_FormatsUploadColumnMaskWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(0),
                Is.EqualTo("None (0x0)"));

            int columnMask = VividParticleSystemManager.UploadColumnPositionSizeMask
                | VividParticleSystemManager.UploadColumnBaseColorMask
                | VividParticleSystemManager.UploadColumnScaleMask;
            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(columnMask),
                Is.EqualTo("PositionSize | BaseColor | Scale (0x13)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatUploadColumnMask(1 << 30),
                Is.EqualTo("Unknown(0x40000000) (0x40000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsPerSharpBitsWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(0u),
                Is.EqualTo("None (0x0)"));

            uint bits = VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale);
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(bits),
                Is.EqualTo("SharedData | BaseColor | Rotation | VelocityStretch | Scale (0x79)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataBits(1u << 31),
                Is.EqualTo("Unknown(0x80000000) (0x80000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsRenderJobModuleFlagsWithNames()
        {
            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(0u),
                Is.EqualTo("None (0x0)"));

            uint flags = VividParticleSystemManager.RenderJobTransformUploadFlag
                | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                | VividParticleSystemManager.RenderJobSharedDataFlag;
            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(flags),
                Is.EqualTo("Transform | VelocityStretch | SharedData (0x45)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(
                    VividParticleSystemManager.RenderJobExtraDataUploadFlag),
                Is.EqualTo("UV | CustomData | MeshIndex (0x38)"));

            Assert.That(
                VividParticleSystemEditorUtility.FormatRenderJobModuleFlags(1u << 31),
                Is.EqualTo("Unknown(0x80000000) (0x80000000)"));
        }

        [Test]
        public void GpuLayoutPreview_FormatsDataInfoWithCopyMask()
        {
            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Billboard);

            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[0]),
                Is.EqualTo($"SharedDataBlock / PerSharp / ZeroBlock / {VividParticleSystemManager.SharedDataByteSize} B / Bit SharedData (0x1) / Copy None (0x0) / Job None (0x0)"));
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[2]),
                Is.EqualTo($"PerInstanceValue / PerInstance / PositionSize / {VividParticleSystemManager.SizeOfFloat4} B / Bit PositionSize (0x4) / Copy PositionSize (0x1) / Job Transform (0x1)"));
            Assert.That(
                VividParticleSystemEditorUtility.FormatGpuDataInfo(layout[5]),
                Is.EqualTo($"PerSharpValue / PerSharp / Rotation / {VividParticleSystemManager.SizeOfFloat4} B / Bit Rotation (0x10) / Copy Rotation (0x4) / Job Transform (0x1)"));
        }

        [Test]
        public void GpuLayoutPreview_EstimatesFootprintFromSerializedMaxParticles()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 513;
            system.rendererModule.renderMode = VividParticleRenderMode.Billboard;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            system.rendererModule.customData1Enabled = true;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuLayoutFootprint(
                    renderer,
                    out VividParticleGpuLayoutFootprint footprint),
                Is.True);
            Assert.That(footprint.InstanceCapacity, Is.EqualTo(513));
            Assert.That(footprint.SharpCapacity, Is.EqualTo(1));
            Assert.That(footprint.SpanCapacity, Is.EqualTo(3));

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(
                    VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor.Create(system.rendererModule));
            Assert.That(
                footprint.TotalByteSize,
                Is.EqualTo(layout.CalculateByteSize(513, 1, 3)));
        }

        [Test]
        public void GpuLayoutPreview_LifetimeModulesForceDynamicColumns()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.Shared;
            system.colorOverLifetime.enabled = true;
            system.sizeOverLifetime.enabled = true;
            system.rotationOverLifetime.enabled = true;
            system.velocityOverLifetime.enabled = true;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");
            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor),
                Is.True);
            Assert.That(descriptor.ColorDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
            Assert.That(descriptor.SizeDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
            Assert.That(descriptor.RotationDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
            Assert.That(descriptor.VelocityDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
        }

        [Test]
        public void GpuLayoutPreview_CustomDataModuleForcesPerParticleStream()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.customData1Enabled = false;
            system.customData.mode1 = VividParticleCustomDataMode.Vector;
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f));

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");
            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor),
                Is.True);
            Assert.That(descriptor.IncludeCustomData1, Is.True);
            Assert.That(descriptor.CustomData1Mode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            Assert.That(
                layout.TryGetDataInfo(
                    VividParticleSystemManager.VividParticleGpuDataId.CustomData1,
                    out VividParticleSystemManager.VividParticleGpuDataInfo dataInfo),
                Is.True);
            Assert.That(
                dataInfo.Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout.CustomDataRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnCustomData1Mask));
        }

        [Test]
        public void GpuLayoutPreview_ConstantCustomDataModuleUsesPerSharpStream()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.customData1Enabled = false;
            system.customData.mode1 = VividParticleCustomDataMode.Vector;
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 4.0f));

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");
            Assert.That(
                VividParticleSystemEditorUtility.TryCreateGpuDataLayoutDescriptor(
                    renderer,
                    out VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor descriptor),
                Is.True);
            Assert.That(descriptor.IncludeCustomData1, Is.True);
            Assert.That(descriptor.CustomData1Mode, Is.EqualTo(VividParticleGpuDataMode.Shared));

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(descriptor);
            Assert.That(
                layout.TryGetDataInfo(
                    VividParticleSystemManager.VividParticleGpuDataId.CustomData1,
                    out VividParticleSystemManager.VividParticleGpuDataInfo dataInfo),
                Is.True);
            Assert.That(
                dataInfo.Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(layout.CustomDataRenderJobUploadColumnMask, Is.EqualTo(0));
        }

        [Test]
        public void GpuLayoutPreview_FormatsByteSize()
        {
            Assert.That(VividParticleSystemEditorUtility.FormatByteSize(-1), Is.EqualTo("0 B"));
            Assert.That(VividParticleSystemEditorUtility.FormatByteSize(512), Is.EqualTo("512 B"));
            Assert.That(VividParticleSystemEditorUtility.FormatByteSize(1536), Is.EqualTo("1.5 KiB (1536 B)"));
            Assert.That(
                VividParticleSystemEditorUtility.FormatByteSize(2 * 1024 * 1024),
                Is.EqualTo("2 MiB (2097152 B)"));
        }

        [Test]
        public void RuntimeDebugSummary_FormatsKeyPerformanceCounters()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(2);
            Assert.That(system.particleCount, Is.EqualTo(2));

            Assert.That(
                VividParticleSystemManager.TryGetRuntimeStats(
                    system,
                    out VividParticleSystemManager.VividParticleSystemRuntimeStats runtimeStats),
                Is.True);
            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();

            string summary = VividParticleSystemEditorUtility.FormatPerformanceSummary(runtimeStats, rendererStats);

            Assert.That(summary, Does.Contain("Particles 2"));
            Assert.That(summary, Does.Contain("Storage"));
            Assert.That(summary, Does.Contain("PendingSim 0"));
            Assert.That(summary, Does.Contain("EmitInit"));
            Assert.That(summary, Does.Contain("Upload"));
            Assert.That(summary, Does.Contain("CopyOps"));
            Assert.That(summary, Does.Contain("Sorts"));
            Assert.That(summary, Does.Contain("RenderJobs"));
            Assert.That(summary, Does.Contain("Draw"));
            Assert.That(summary, Does.Contain("Cull"));
            Assert.That(summary, Does.Contain("CullingBuild 1"));
            Assert.That(summary, Does.Contain("MeshCount"));
            Assert.That(summary, Does.Contain("Reduce"));
            Assert.That(summary, Does.Contain("PickBuild"));
            Assert.That(summary, Does.Contain("BatchBuild"));
            Assert.That(summary, Does.Contain("Filter"));
            Assert.That(summary, Does.Contain("Scratch"));
            Assert.That(rendererStats.LastCullingSourceDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingFilteredDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingUsedFilteredLayout, Is.False);
            Assert.That(rendererStats.LastCullingUsedPickingFilter, Is.False);
            Assert.That(rendererStats.LastCullingFilterPassCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingFilterCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.ActiveCullingScratchSlotCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingScratchSplitCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingScratchPacketCount, Is.EqualTo(0));
            Assert.That(rendererStats.CullingScratchFilteredCommandCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.CullingScratchFilteredRangeCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.CullingPageBoundsCapacity, Is.GreaterThan(0));
            Assert.That(rendererStats.LastCullingRecordBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.HasPendingCullingRecordBuild, Is.False);
        }

        [Test]
        public void RendererInspectorNotices_ReturnNone_ForDefaultBillboardRenderer()
        {
            VividParticleSystem system = CreateSystem();

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            Assert.That(
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer),
                Is.EqualTo(VividParticleRendererInspectorNotice.None));
        }

        [Test]
        public void RendererInspectorNotices_ReportNonRenderingModes()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = false;
            system.rendererModule.renderMode = VividParticleRenderMode.None;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.RendererDisabled) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.RenderModeNone) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReportMeshModeWithoutMesh()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.mesh = null;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshMissing) != 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_UseMeshList_WhenPrimaryMeshIsMissing()
        {
            Mesh mesh = new Mesh { name = "Vivid Particle Editor Mesh List Test" };
            try
            {
                VividParticleSystem system = CreateSystem();
                system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
                system.rendererModule.mesh = null;
                system.rendererModule.meshes = new[] { mesh };

                using var serializedSystem = new SerializedObject(system);
                SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

                VividParticleRendererInspectorNotice notices =
                    VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RendererInspectorNotices_ReportMultiMeshDrawCommandHint()
        {
            Mesh firstMesh = new Mesh { name = "Vivid Particle Editor Multi Mesh First" };
            Mesh secondMesh = new Mesh { name = "Vivid Particle Editor Multi Mesh Second" };
            try
            {
                VividParticleSystem system = CreateSystem();
                system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
                system.rendererModule.SetMeshes(new[] { firstMesh, secondMesh });

                using var serializedSystem = new SerializedObject(system);
                SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

                VividParticleRendererInspectorNotice notices =
                    VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MultiMeshSplitsDrawCommands) != 0,
                    Is.True);
                Assert.That(
                    (notices & VividParticleRendererInspectorNotice.MeshMissing) == 0,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(firstMesh);
                Object.DestroyImmediate(secondMesh);
            }
        }

        [Test]
        public void RendererInspectorNotices_ReportUploadAndSortingCostHints()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            system.rendererModule.customData1Enabled = true;
            system.rendererModule.meshIndexDataEnabled = true;
            system.rendererModule.sortMode = VividParticleSortMode.ByDistance;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.StretchUsesPerParticleVelocity) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MeshIndexRequiresCustomShader) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.PerParticleGpuDataIncreasesUpload) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.SortingAllocatesPositions) != 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReportShadowsOnlyViewHint()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.ShadowsOnlySkipsRegularViews) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.RendererDisabled) == 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReportDrawOutputFilterHints()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            system.rendererModule.staticShadowCaster = true;

            using var serializedSystem = new SerializedObject(system);
            SerializedProperty renderer = serializedSystem.FindProperty("m_Renderer");

            VividParticleRendererInspectorNotice notices =
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(renderer);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.MotionVectorsAffectDrawOutput) != 0,
                Is.True);
            Assert.That(
                (notices & VividParticleRendererInspectorNotice.StaticShadowCasterAffectsDrawOutput) != 0,
                Is.True);
        }

        [Test]
        public void RendererInspectorNotices_ReturnNone_WhenRendererModuleIsMissing()
        {
            Assert.That(
                VividParticleSystemEditorUtility.GetRendererInspectorNotices(null),
                Is.EqualTo(VividParticleRendererInspectorNotice.None));
        }

        private static Gradient CreateGradient(Color start, Color end)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(start, 0.0f),
                    new GradientColorKey(end, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(start.a, 0.0f),
                    new GradientAlphaKey(end.a, 1.0f),
                });
            return gradient;
        }

        private VividParticleSystem CreateSystem()
        {
            m_GameObject = new GameObject("Vivid Particle System Editor Test");
            return m_GameObject.AddComponent<VividParticleSystem>();
        }
    }
}
