using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VividRP.Runtime.Particle;
using VividRP.Runtime.Particle.ECS;

namespace VividRP.Editor.Tests
{
    public sealed class VividParticleSystemTests
    {
        private readonly List<Object> m_ToDestroy = new();

        [SetUp]
        public void SetUp()
        {
            VividParticleSystemManager.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;
            VividParticleSystemManager.RefreshEditorSelectionForTests();
            VividParticleSystemManager.ClearForTests();

            for (int index = m_ToDestroy.Count - 1; index >= 0; index--)
            {
                if (m_ToDestroy[index] != null)
                    Object.DestroyImmediate(m_ToDestroy[index]);
            }

            m_ToDestroy.Clear();
            VividParticleSystemManager.ClearForTests();
        }

        [Test]
        public void Modules_UseExpectedDefaults_WhenCreated()
        {
            VividParticleMainModule main = VividParticleMainModule.CreateDefault();
            VividParticleEmissionModule emission = VividParticleEmissionModule.CreateDefault();
            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            VividParticleForceOverLifetimeModule forceOverLifetime =
                VividParticleForceOverLifetimeModule.CreateDefault();
            VividParticleExternalForcesModule externalForces =
                VividParticleExternalForcesModule.CreateDefault();
            VividParticleCollisionModule collision =
                VividParticleCollisionModule.CreateDefault();
            VividParticleTriggerModule trigger =
                VividParticleTriggerModule.CreateDefault();
            VividParticleVelocityOverLifetimeModule velocityOverLifetime =
                VividParticleVelocityOverLifetimeModule.CreateDefault();
            VividParticleInheritVelocityModule inheritVelocity =
                VividParticleInheritVelocityModule.CreateDefault();
            VividParticleLifetimeByEmitterSpeedModule lifetimeByEmitterSpeed =
                VividParticleLifetimeByEmitterSpeedModule.CreateDefault();
            VividParticleLimitVelocityOverLifetimeModule limitVelocityOverLifetime =
                VividParticleLimitVelocityOverLifetimeModule.CreateDefault();
            VividParticleColorOverLifetimeModule colorOverLifetime =
                VividParticleColorOverLifetimeModule.CreateDefault();
            VividParticleColorBySpeedModule colorBySpeed =
                VividParticleColorBySpeedModule.CreateDefault();
            VividParticleSizeOverLifetimeModule sizeOverLifetime =
                VividParticleSizeOverLifetimeModule.CreateDefault();
            VividParticleSizeBySpeedModule sizeBySpeed =
                VividParticleSizeBySpeedModule.CreateDefault();
            VividParticleRotationOverLifetimeModule rotationOverLifetime =
                VividParticleRotationOverLifetimeModule.CreateDefault();
            VividParticleRotationBySpeedModule rotationBySpeed =
                VividParticleRotationBySpeedModule.CreateDefault();
            VividParticleNoiseModule noise = VividParticleNoiseModule.CreateDefault();
            VividParticleCustomDataModule customData = VividParticleCustomDataModule.CreateDefault();
            VividParticleTextureSheetAnimationModule textureSheetAnimation =
                VividParticleTextureSheetAnimationModule.CreateDefault();
            VividParticleRendererModule renderer = VividParticleRendererModule.CreateDefault();

            Assert.That(main.duration, Is.EqualTo(5.0f));
            Assert.That(main.loop, Is.True);
            Assert.That(main.playOnAwake, Is.True);
            Assert.That(main.startLifetime, Is.EqualTo(5.0f));
            Assert.That(main.startSpeed, Is.EqualTo(1.0f));
            Assert.That(main.startSize, Is.EqualTo(1.0f));
            Assert.That(main.startColor, Is.EqualTo(Color.white));
            Assert.That(main.gravityModifier, Is.EqualTo(0.0f));
            Assert.That(main.simulationSpace, Is.EqualTo(VividParticleSystemSimulationSpace.Local));
            Assert.That(main.maxParticles, Is.EqualTo(1000));
            Assert.That(main.randomSeed, Is.EqualTo(1u));
            Assert.That(main.useAutoRandomSeed, Is.True);
            Assert.That(main.emitterVelocityMode, Is.EqualTo(VividParticleEmitterVelocityMode.Transform));
            Assert.That(main.customEmitterVelocity, Is.EqualTo(Vector3.zero));

            Assert.That(emission.enabled, Is.True);
            Assert.That(emission.rateOverTime, Is.EqualTo(10.0f));
            Assert.That(emission.bursts, Is.Empty);

            Assert.That(shape.enabled, Is.True);
            Assert.That(shape.shapeType, Is.EqualTo(VividParticleShapeType.Point));
            Assert.That(shape.radius, Is.EqualTo(1.0f));
            Assert.That(shape.boxSize, Is.EqualTo(Vector3.one));
            Assert.That(shape.angle, Is.EqualTo(25.0f));

            Assert.That(forceOverLifetime.enabled, Is.False);
            Assert.That(forceOverLifetime.force, Is.EqualTo(Vector3.zero));
            Assert.That(forceOverLifetime.space, Is.EqualTo(VividParticleForceSpace.Local));
            Assert.That(externalForces.enabled, Is.False);
            Assert.That(
                externalForces.influenceFilter,
                Is.EqualTo(VividParticleGameObjectFilter.LayerMask));
            Assert.That(externalForces.influenceCount, Is.EqualTo(0));
            Assert.That(externalForces.EvaluateMultiplier(0.5f), Is.EqualTo(1.0f));
            Assert.That(collision.enabled, Is.False);
            Assert.That(collision.type, Is.EqualTo(VividParticleCollisionType.Planes));
            Assert.That(collision.mode, Is.EqualTo(VividParticleCollisionMode.Collision3D));
            Assert.That(collision.bounce, Is.EqualTo(1.0f));
            Assert.That(collision.radiusScale, Is.EqualTo(1.0f));
            Assert.That(collision.planeCount, Is.EqualTo(0));
            Assert.That(trigger.enabled, Is.False);
            Assert.That(trigger.enter, Is.EqualTo(VividParticleOverlapAction.Ignore));
            Assert.That(trigger.colliderQueryMode, Is.EqualTo(VividParticleColliderQueryMode.One));
            Assert.That(trigger.radiusScale, Is.EqualTo(1.0f));
            Assert.That(trigger.colliderCount, Is.EqualTo(0));
            Assert.That(velocityOverLifetime.enabled, Is.False);
            Assert.That(velocityOverLifetime.Evaluate(0.5f), Is.EqualTo(Vector3.zero));
            Assert.That(velocityOverLifetime.space, Is.EqualTo(VividParticleForceSpace.Local));
            Assert.That(inheritVelocity.enabled, Is.False);
            Assert.That(inheritVelocity.mode, Is.EqualTo(VividParticleInheritVelocityMode.Initial));
            Assert.That(inheritVelocity.Evaluate(0.5f), Is.EqualTo(1.0f));
            Assert.That(lifetimeByEmitterSpeed.enabled, Is.False);
            Assert.That(lifetimeByEmitterSpeed.range, Is.EqualTo(Vector2.up));
            Assert.That(lifetimeByEmitterSpeed.curveMultiplier, Is.EqualTo(1.0f));
            Assert.That(lifetimeByEmitterSpeed.EvaluateMultiplier(0.5f), Is.EqualTo(1.0f));
            Assert.That(limitVelocityOverLifetime.enabled, Is.False);
            Assert.That(limitVelocityOverLifetime.separateAxes, Is.False);
            Assert.That(limitVelocityOverLifetime.EvaluateLimit(0.5f), Is.EqualTo(Vector3.one));
            Assert.That(limitVelocityOverLifetime.dampen, Is.EqualTo(1.0f));
            Assert.That(limitVelocityOverLifetime.space, Is.EqualTo(VividParticleForceSpace.Local));
            Assert.That(limitVelocityOverLifetime.EvaluateDrag(0.5f), Is.EqualTo(0.0f));
            Assert.That(limitVelocityOverLifetime.multiplyDragByParticleSize, Is.False);
            Assert.That(limitVelocityOverLifetime.multiplyDragByParticleVelocity, Is.False);
            Assert.That(colorOverLifetime.enabled, Is.False);
            Assert.That(colorOverLifetime.Evaluate(0.5f), Is.EqualTo(Color.white));
            Assert.That(colorBySpeed.enabled, Is.False);
            Assert.That(colorBySpeed.range, Is.EqualTo(new Vector2(0.0f, 1.0f)));
            Assert.That(colorBySpeed.Evaluate(0.5f), Is.EqualTo(Color.white));
            Assert.That(sizeOverLifetime.enabled, Is.False);
            Assert.That(sizeOverLifetime.Evaluate(0.5f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(sizeBySpeed.enabled, Is.False);
            Assert.That(sizeBySpeed.range, Is.EqualTo(new Vector2(0.0f, 1.0f)));
            Assert.That(sizeBySpeed.Evaluate(0.5f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(rotationOverLifetime.enabled, Is.False);
            Assert.That(rotationOverLifetime.EvaluateAngularVelocity(0.5f), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(rotationBySpeed.enabled, Is.False);
            Assert.That(rotationBySpeed.separateAxes, Is.False);
            Assert.That(rotationBySpeed.range, Is.EqualTo(new Vector2(0.0f, 1.0f)));
            Assert.That(
                rotationBySpeed.EvaluateAngularVelocity(0.5f),
                Is.EqualTo(new Vector3(0.0f, 0.0f, 45.0f)));
            Assert.That(noise.enabled, Is.False);
            Assert.That(noise.separateAxes, Is.False);
            Assert.That(noise.EvaluateStrength(0.5f), Is.EqualTo(Vector3.one));
            Assert.That(noise.frequency, Is.EqualTo(0.5f));
            Assert.That(noise.damping, Is.True);
            Assert.That(noise.quality, Is.EqualTo(VividParticleNoiseQuality.High));
            Assert.That(noise.remapEnabled, Is.False);
            Assert.That(noise.EvaluateRemap(new Vector3(0.25f, 0.5f, 0.75f)),
                Is.EqualTo(new Vector3(-0.5f, 0.0f, 0.5f)));
            Assert.That(noise.octaveCount, Is.EqualTo(1));
            Assert.That(noise.octaveMultiplier, Is.EqualTo(0.5f));
            Assert.That(noise.octaveScale, Is.EqualTo(2.0f));
            Assert.That(noise.EvaluateScrollSpeed(0.5f), Is.EqualTo(0.0f));
            Assert.That(noise.EvaluatePositionAmount(0.5f), Is.EqualTo(1.0f));
            Assert.That(noise.EvaluateRotationAmount(0.5f), Is.EqualTo(0.0f));
            Assert.That(noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.0f));
            Assert.That(noise.hasPositionEffect, Is.True);
            Assert.That(noise.hasRotationEffect, Is.False);
            Assert.That(noise.hasSizeEffect, Is.False);
            Assert.That(customData.enabled, Is.False);
            Assert.That(customData.mode1, Is.EqualTo(VividParticleCustomDataMode.Disabled));
            Assert.That(customData.mode2, Is.EqualTo(VividParticleCustomDataMode.Disabled));
            Assert.That(customData.numberOfComponents1, Is.EqualTo(4));
            Assert.That(
                customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f),
                Is.EqualTo(Vector4.zero));
            Assert.That(textureSheetAnimation.enabled, Is.False);
            Assert.That(textureSheetAnimation.numTilesX, Is.EqualTo(1));
            Assert.That(textureSheetAnimation.numTilesY, Is.EqualTo(1));
            Assert.That(textureSheetAnimation.animation, Is.EqualTo(VividParticleTextureSheetAnimationType.WholeSheet));
            Assert.That(textureSheetAnimation.EvaluateFrame(0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(textureSheetAnimation.startFrame, Is.EqualTo(0.0f));
            Assert.That(textureSheetAnimation.cycleCount, Is.EqualTo(1.0f));
            Assert.That(textureSheetAnimation.rowIndex, Is.EqualTo(0));

            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.renderMode, Is.EqualTo(VividParticleRenderMode.Billboard));
            Assert.That(renderer.material, Is.Null);
            Assert.That(renderer.mesh, Is.Null);
            Assert.That(renderer.meshes, Is.Empty);
            Assert.That(renderer.meshCount, Is.EqualTo(0));
            Assert.That(renderer.hasRenderMesh, Is.False);
            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(renderer.sizeScale, Is.EqualTo(1.0f));
            Assert.That(renderer.stretchLengthScale, Is.EqualTo(2.0f));
            Assert.That(renderer.stretchSpeedScale, Is.EqualTo(0.0f));
            Assert.That(renderer.pivot, Is.EqualTo(Vector3.zero));
            Assert.That(renderer.minParticleSize, Is.EqualTo(0.0f));
            Assert.That(renderer.maxParticleSize, Is.EqualTo(0.0f));
            Assert.That(renderer.flip, Is.EqualTo(Vector3.zero));
            Assert.That(renderer.renderQueueOffset, Is.EqualTo(0));
            Assert.That(renderer.sortingPriority, Is.EqualTo(0));
            Assert.That(renderer.batchLayer, Is.EqualTo(0));
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
            Assert.That(renderer.staticShadowCaster, Is.False);
            Assert.That(renderer.receiveShadows, Is.False);
            Assert.That(renderer.renderingLayerMask, Is.EqualTo(uint.MaxValue));
            Assert.That(renderer.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(renderer.rotationDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(renderer.velocityDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(renderer.sizeDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(renderer.uvDataEnabled, Is.False);
            Assert.That(renderer.customData1Enabled, Is.False);
            Assert.That(renderer.customData2Enabled, Is.False);
            Assert.That(renderer.meshIndexDataEnabled, Is.False);
            Assert.That(renderer.sortMode, Is.EqualTo(VividParticleSortMode.None));
        }

        [Test]
        public void CustomDataModule_EvaluatesVectorAndColorStreamsWithoutSharingInputs()
        {
            VividParticleCustomDataModule customData = VividParticleCustomDataModule.CreateDefault();
            var x = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 2.0f);
            Gradient color = CreateGradient(Color.red, Color.blue);
            customData.mode1 = VividParticleCustomDataMode.Vector;
            customData.numberOfComponents1 = 99;
            customData.SetVector(VividParticleCustomDataStream.Custom1, 0, x);
            customData.mode2 = VividParticleCustomDataMode.Color;
            customData.SetColor(VividParticleCustomDataStream.Custom2, color);

            x.keys = new[] { new Keyframe(0.0f, 9.0f), new Keyframe(1.0f, 9.0f) };
            color.SetKeys(
                new[] { new GradientColorKey(Color.green, 0.0f) },
                new[] { new GradientAlphaKey(1.0f, 0.0f) });

            Assert.That(customData.numberOfComponents1, Is.EqualTo(4));
            Assert.That(
                customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f).x,
                Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(
                customData.Evaluate(VividParticleCustomDataStream.Custom2, 0.0f),
                Is.EqualTo(new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
        }

        [Test]
        public void CustomDataModule_DetectsConstantVectorAndColorStreamsConservatively()
        {
            VividParticleCustomDataModule customData = VividParticleCustomDataModule.CreateDefault();
            customData.mode1 = VividParticleCustomDataMode.Vector;
            customData.numberOfComponents1 = 2;
            customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 2.0f));
            customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                1,
                AnimationCurve.Linear(0.0f, 3.0f, 1.0f, 3.0f));
            customData.mode2 = VividParticleCustomDataMode.Color;
            customData.SetColor(
                VividParticleCustomDataStream.Custom2,
                CreateGradient(Color.green, Color.green));

            Assert.That(customData.IsStreamConstant(VividParticleCustomDataStream.Custom1), Is.True);
            Assert.That(customData.IsStreamConstant(VividParticleCustomDataStream.Custom2), Is.True);
            Assert.That(
                customData.GetConstantValue(VividParticleCustomDataStream.Custom1),
                Is.EqualTo(new Vector4(2.0f, 3.0f, 0.0f, 0.0f)));

            customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Linear(0.0f, 2.0f, 1.0f, 4.0f));
            Assert.That(customData.IsStreamConstant(VividParticleCustomDataStream.Custom1), Is.False);
            Assert.That(
                customData.GetConstantValue(VividParticleCustomDataStream.Custom1),
                Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void Modules_ClampInvalidValues_WhenPropertiesAreAssigned()
        {
            VividParticleMainModule main = VividParticleMainModule.CreateDefault();
            main.duration = -1.0f;
            main.startLifetime = -2.0f;
            main.startSize = 0.0f;
            main.maxParticles = -5;

            VividParticleEmissionModule emission = VividParticleEmissionModule.CreateDefault();
            emission.rateOverTime = -10.0f;
            emission.bursts = null;

            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            shape.radius = -1.0f;
            shape.boxSize = new Vector3(-1.0f, 2.0f, -3.0f);
            shape.angle = 180.0f;

            VividParticleRendererModule renderer = VividParticleRendererModule.CreateDefault();
            renderer.sizeScale = -4.0f;
            renderer.stretchLengthScale = -1.0f;
            renderer.stretchSpeedScale = -2.0f;
            renderer.minParticleSize = -1.0f;
            renderer.maxParticleSize = -2.0f;
            renderer.flip = new Vector3(-1.0f, 0.5f, 2.0f);
            renderer.batchLayer = 99;

            VividParticleTextureSheetAnimationModule textureSheetAnimation =
                VividParticleTextureSheetAnimationModule.CreateDefault();
            textureSheetAnimation.numTilesX = 0;
            textureSheetAnimation.numTilesY = 100;
            textureSheetAnimation.cycleCount = -1.0f;
            textureSheetAnimation.rowIndex = 100;

            VividParticleLimitVelocityOverLifetimeModule limitVelocityOverLifetime =
                VividParticleLimitVelocityOverLifetimeModule.CreateDefault();
            limitVelocityOverLifetime.dampen = -1.0f;
            VividParticleColorBySpeedModule colorBySpeed =
                VividParticleColorBySpeedModule.CreateDefault();
            colorBySpeed.range = new Vector2(4.0f, -2.0f);
            VividParticleSizeBySpeedModule sizeBySpeed =
                VividParticleSizeBySpeedModule.CreateDefault();
            sizeBySpeed.range = new Vector2(float.NaN, float.PositiveInfinity);
            VividParticleRotationBySpeedModule rotationBySpeed =
                VividParticleRotationBySpeedModule.CreateDefault();
            rotationBySpeed.range = new Vector2(6.0f, -1.0f);
            VividParticleNoiseModule noise = VividParticleNoiseModule.CreateDefault();
            noise.frequency = -1.0f;
            noise.octaveCount = 99;
            noise.octaveMultiplier = -2.0f;
            noise.octaveScale = 0.25f;
            noise.quality = (VividParticleNoiseQuality)99;

            Assert.That(main.duration, Is.EqualTo(VividParticleMainModule.MinimumDuration));
            Assert.That(main.startLifetime, Is.EqualTo(VividParticleMainModule.MinimumStartLifetime));
            Assert.That(main.startSize, Is.EqualTo(VividParticleMainModule.MinimumStartSize));
            Assert.That(main.maxParticles, Is.EqualTo(VividParticleMainModule.MinimumMaxParticles));
            Assert.That(emission.rateOverTime, Is.EqualTo(0.0f));
            Assert.That(emission.bursts, Is.Empty);
            Assert.That(shape.radius, Is.EqualTo(0.0f));
            Assert.That(shape.boxSize, Is.EqualTo(new Vector3(0.0f, 2.0f, 0.0f)));
            Assert.That(shape.angle, Is.EqualTo(89.0f));
            Assert.That(renderer.sizeScale, Is.EqualTo(VividParticleRendererModule.MinimumSizeScale));
            Assert.That(renderer.stretchLengthScale, Is.EqualTo(VividParticleRendererModule.MinimumStretchLengthScale));
            Assert.That(renderer.stretchSpeedScale, Is.EqualTo(VividParticleRendererModule.MinimumStretchSpeedScale));
            Assert.That(renderer.minParticleSize, Is.EqualTo(0.0f));
            Assert.That(renderer.maxParticleSize, Is.EqualTo(0.0f));
            Assert.That(renderer.flip, Is.EqualTo(new Vector3(0.0f, 0.5f, 1.0f)));
            Assert.That(renderer.batchLayer, Is.EqualTo(VividParticleRendererModule.MaximumBatchLayer));
            Assert.That(textureSheetAnimation.numTilesX, Is.EqualTo(1));
            Assert.That(textureSheetAnimation.numTilesY, Is.EqualTo(64));
            Assert.That(textureSheetAnimation.cycleCount, Is.EqualTo(0.0f));
            Assert.That(textureSheetAnimation.rowIndex, Is.EqualTo(63));
            Assert.That(limitVelocityOverLifetime.dampen, Is.EqualTo(0.0f));
            limitVelocityOverLifetime.dampen = 2.0f;
            Assert.That(limitVelocityOverLifetime.dampen, Is.EqualTo(1.0f));
            Assert.That(colorBySpeed.range, Is.EqualTo(new Vector2(0.0f, 4.0f)));
            Assert.That(sizeBySpeed.range, Is.EqualTo(Vector2.zero));
            Assert.That(rotationBySpeed.range, Is.EqualTo(new Vector2(0.0f, 6.0f)));
            Assert.That(noise.frequency, Is.EqualTo(0.0f));
            Assert.That(noise.octaveCount, Is.EqualTo(VividParticleNoiseModule.MaximumOctaveCount));
            Assert.That(noise.octaveMultiplier, Is.EqualTo(0.0f));
            Assert.That(noise.octaveScale, Is.EqualTo(1.0f));
            Assert.That(noise.quality, Is.EqualTo(VividParticleNoiseQuality.High));
            renderer.batchLayer = -1;
            Assert.That(renderer.batchLayer, Is.EqualTo(VividParticleRendererModule.MinimumBatchLayer));
        }

        [Test]
        public void LifetimeByEmitterSpeed_ValidatesRangeMultiplierAndCurve()
        {
            VividParticleLifetimeByEmitterSpeedModule module =
                VividParticleLifetimeByEmitterSpeedModule.CreateDefault();
            module.enabled = true;
            module.range = new Vector2(10.0f, 2.0f);
            module.curveMultiplier = float.PositiveInfinity;
            module.curve = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);

            Assert.That(module.range, Is.EqualTo(new Vector2(10.0f, 10.0f)));
            Assert.That(module.curveMultiplier, Is.EqualTo(0.0f));

            module.range = new Vector2(0.0f, 10.0f);
            module.curveMultiplier = 2.0f;
            Assert.That(module.EvaluateMultiplier(5.0f), Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Asset_AssignmentCopiesLifetimeByEmitterSpeedCurve_WithoutSharingState()
        {
            VividParticleSystemAsset asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_ToDestroy.Add(asset);
            asset.lifetimeByEmitterSpeed.enabled = true;
            asset.lifetimeByEmitterSpeed.range = new Vector2(2.0f, 8.0f);
            asset.lifetimeByEmitterSpeed.curveMultiplier = 3.0f;
            asset.lifetimeByEmitterSpeed.curve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.25f);

            VividParticleSystem system = CreateSystem();
            system.asset = asset;

            Assert.That(system.lifetimeByEmitterSpeed.enabled, Is.True);
            Assert.That(system.lifetimeByEmitterSpeed.range, Is.EqualTo(new Vector2(2.0f, 8.0f)));
            Assert.That(system.lifetimeByEmitterSpeed.curveMultiplier, Is.EqualTo(3.0f));
            Assert.That(
                system.lifetimeByEmitterSpeed.curve.Evaluate(1.0f),
                Is.EqualTo(0.25f).Within(0.0001f));

            asset.lifetimeByEmitterSpeed.curve = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
            asset.lifetimeByEmitterSpeed.range = new Vector2(0.0f, 1.0f);
            Assert.That(system.lifetimeByEmitterSpeed.range, Is.EqualTo(new Vector2(2.0f, 8.0f)));
            Assert.That(
                system.lifetimeByEmitterSpeed.curve.Evaluate(1.0f),
                Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Emit_LifetimeByEmitterSpeedScalesStartLifetime_AndUpdatesEcsModuleKey()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startLifetime = 8.0f;
            system.main.maxParticles = 4;
            system.main.emitterVelocityMode = VividParticleEmitterVelocityMode.Custom;
            system.main.customEmitterVelocity = new Vector3(5.0f, 0.0f, 0.0f);
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.lifetimeByEmitterSpeed.enabled = true;
            system.lifetimeByEmitterSpeed.range = new Vector2(0.0f, 10.0f);
            system.lifetimeByEmitterSpeed.curve = AnimationCurve.Constant(0.0f, 1.0f, 0.25f);
            system.lifetimeByEmitterSpeed.curveMultiplier = 2.0f;

            system.Emit(1);

            Assert.That(system.particleCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetParticleStartLifetimeForTests(system, 0),
                Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(
                VividParticleSystemManager.GetModuleFlagsForTests(system)
                & VividParticleModuleFlags.LifetimeByEmitterSpeed,
                Is.EqualTo(VividParticleModuleFlags.LifetimeByEmitterSpeed));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                & VividParticleModuleFlags.LifetimeByEmitterSpeed,
                Is.EqualTo(VividParticleModuleFlags.None));
        }

        [Test]
        public void AutomaticEmission_LifetimeByEmitterSpeedUsesNativeBurstPlanLut()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.startLifetime = 10.0f;
            system.main.maxParticles = 4;
            system.main.emitterVelocityMode = VividParticleEmitterVelocityMode.Custom;
            system.main.customEmitterVelocity = new Vector3(7.5f, 0.0f, 0.0f);
            system.emission.enabled = true;
            system.emission.rateOverTime = 1.0f;
            system.shape.enabled = false;
            system.lifetimeByEmitterSpeed.enabled = true;
            system.lifetimeByEmitterSpeed.range = new Vector2(0.0f, 10.0f);
            system.lifetimeByEmitterSpeed.curve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.0f);
            system.lifetimeByEmitterSpeed.curveMultiplier = 1.0f;

            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(1.0f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanNativeReservationCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetParticleStartLifetimeForTests(system, 0),
                Is.EqualTo(2.5f).Within(0.05f));
        }

        [Test]
        public void Asset_AssignmentCopiesModules_WithoutSharingRuntimeState()
        {
            VividParticleSystemAsset asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_ToDestroy.Add(asset);
            Mesh primaryMesh = CreateTriangleMesh();
            Mesh extraMesh = CreateTriangleMesh();
            m_ToDestroy.Add(primaryMesh);
            m_ToDestroy.Add(extraMesh);
            asset.main.startLifetime = 2.5f;
            asset.main.maxParticles = 7;
            asset.emission.rateOverTime = 3.0f;
            asset.emission.bursts = new[] { new VividParticleBurst(0.25f, 2) };
            asset.shape.shapeType = VividParticleShapeType.Sphere;
            asset.forceOverLifetime.enabled = true;
            asset.forceOverLifetime.force = new Vector3(1.0f, 2.0f, 3.0f);
            asset.forceOverLifetime.space = VividParticleForceSpace.World;
            asset.externalForces.enabled = true;
            asset.externalForces.influenceFilter = VividParticleGameObjectFilter.LayerMask;
            asset.externalForces.influenceMask = 1 << 8;
            asset.externalForces.multiplier = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);
            asset.velocityOverLifetime.enabled = true;
            asset.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 3.0f);
            asset.velocityOverLifetime.space = VividParticleForceSpace.World;
            asset.main.emitterVelocityMode = VividParticleEmitterVelocityMode.Custom;
            asset.main.customEmitterVelocity = new Vector3(2.0f, 3.0f, 4.0f);
            asset.inheritVelocity.enabled = true;
            asset.inheritVelocity.mode = VividParticleInheritVelocityMode.Current;
            asset.inheritVelocity.curve = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
            asset.limitVelocityOverLifetime.enabled = true;
            asset.limitVelocityOverLifetime.separateAxes = true;
            asset.limitVelocityOverLifetime.limitX = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            asset.limitVelocityOverLifetime.limitY = AnimationCurve.Constant(0.0f, 1.0f, 3.0f);
            asset.limitVelocityOverLifetime.limitZ = AnimationCurve.Constant(0.0f, 1.0f, 4.0f);
            asset.limitVelocityOverLifetime.dampen = 0.5f;
            asset.limitVelocityOverLifetime.space = VividParticleForceSpace.World;
            asset.limitVelocityOverLifetime.drag = AnimationCurve.Constant(0.0f, 1.0f, 0.25f);
            asset.limitVelocityOverLifetime.multiplyDragByParticleSize = true;
            asset.colorOverLifetime.enabled = true;
            asset.colorOverLifetime.color = CreateGradient(Color.red, Color.blue);
            asset.sizeOverLifetime.enabled = true;
            asset.sizeOverLifetime.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.25f);
            asset.rotationOverLifetime.enabled = true;
            asset.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 90.0f);
            asset.noise.enabled = true;
            asset.noise.quality = VividParticleNoiseQuality.Medium;
            asset.noise.remapEnabled = true;
            asset.noise.remapX = AnimationCurve.Linear(0.0f, -0.5f, 1.0f, 0.5f);
            asset.noise.positionAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
            asset.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 30.0f);
            asset.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.5f);
            asset.customData.mode1 = VividParticleCustomDataMode.Vector;
            asset.customData.numberOfComponents1 = 2;
            asset.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 2.0f));
            asset.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                1,
                AnimationCurve.Constant(0.0f, 1.0f, 3.0f));
            asset.textureSheetAnimation.enabled = true;
            asset.textureSheetAnimation.numTilesX = 4;
            asset.textureSheetAnimation.numTilesY = 2;
            asset.textureSheetAnimation.animation = VividParticleTextureSheetAnimationType.SingleRow;
            asset.textureSheetAnimation.frameOverTime = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
            asset.textureSheetAnimation.cycleCount = 2.0f;
            asset.textureSheetAnimation.rowIndex = 1;
            asset.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            asset.rendererModule.color = Color.cyan;
            asset.rendererModule.stretchSpeedScale = 0.5f;
            asset.rendererModule.pivot = new Vector3(0.25f, 0.5f, -0.25f);
            asset.rendererModule.minParticleSize = 0.1f;
            asset.rendererModule.maxParticleSize = 2.0f;
            asset.rendererModule.flip = new Vector3(1.0f, 0.5f, 0.0f);
            asset.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            asset.rendererModule.customData1Enabled = true;
            asset.rendererModule.sortMode = VividParticleSortMode.ByDistance;
            asset.rendererModule.sortingPriority = 7;
            asset.rendererModule.batchLayer = 3;
            asset.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            asset.rendererModule.staticShadowCaster = true;
            asset.rendererModule.renderingLayerMask = 0x8u;
            asset.rendererModule.SetMeshes(new[] { primaryMesh, extraMesh });

            VividParticleSystem system = CreateActiveSystem();
            system.asset = asset;

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.main.maxParticles, Is.EqualTo(7));
            Assert.That(system.emission.rateOverTime, Is.EqualTo(3.0f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.shape.shapeType, Is.EqualTo(VividParticleShapeType.Sphere));
            Assert.That(system.forceOverLifetime.enabled, Is.True);
            Assert.That(system.forceOverLifetime.force, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(system.forceOverLifetime.space, Is.EqualTo(VividParticleForceSpace.World));
            Assert.That(system.externalForces.enabled, Is.True);
            Assert.That(system.externalForces.influenceMask.value, Is.EqualTo(1 << 8));
            Assert.That(system.externalForces.EvaluateMultiplier(0.5f), Is.EqualTo(0.5f));
            Assert.That(system.velocityOverLifetime.enabled, Is.True);
            Assert.That(system.velocityOverLifetime.Evaluate(0.5f).x, Is.EqualTo(3.0f));
            Assert.That(system.velocityOverLifetime.space, Is.EqualTo(VividParticleForceSpace.World));
            Assert.That(system.main.emitterVelocityMode, Is.EqualTo(VividParticleEmitterVelocityMode.Custom));
            Assert.That(system.main.customEmitterVelocity, Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(system.inheritVelocity.enabled, Is.True);
            Assert.That(system.inheritVelocity.mode, Is.EqualTo(VividParticleInheritVelocityMode.Current));
            Assert.That(system.inheritVelocity.Evaluate(0.5f), Is.EqualTo(0.75f));
            Assert.That(system.limitVelocityOverLifetime.enabled, Is.True);
            Assert.That(system.limitVelocityOverLifetime.separateAxes, Is.True);
            Assert.That(
                system.limitVelocityOverLifetime.EvaluateLimit(0.5f),
                Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(system.limitVelocityOverLifetime.dampen, Is.EqualTo(0.5f));
            Assert.That(system.limitVelocityOverLifetime.space, Is.EqualTo(VividParticleForceSpace.World));
            Assert.That(system.limitVelocityOverLifetime.EvaluateDrag(0.5f), Is.EqualTo(0.25f));
            Assert.That(system.limitVelocityOverLifetime.multiplyDragByParticleSize, Is.True);
            Assert.That(system.colorOverLifetime.enabled, Is.True);
            Assert.That(system.colorOverLifetime.Evaluate(0.0f), Is.EqualTo(Color.red));
            Assert.That(system.sizeOverLifetime.enabled, Is.True);
            Assert.That(system.sizeOverLifetime.Evaluate(1.0f), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(system.rotationOverLifetime.enabled, Is.True);
            Assert.That(system.rotationOverLifetime.EvaluateAngularVelocity(0.5f), Is.EqualTo(90.0f).Within(0.0001f));
            Assert.That(system.noise.enabled, Is.True);
            Assert.That(system.noise.quality, Is.EqualTo(VividParticleNoiseQuality.Medium));
            Assert.That(system.noise.remapEnabled, Is.True);
            Assert.That(system.noise.EvaluateRemap(Vector3.zero).x, Is.EqualTo(-0.5f));
            Assert.That(system.noise.EvaluatePositionAmount(0.5f), Is.EqualTo(0.75f));
            Assert.That(system.noise.EvaluateRotationAmount(0.5f), Is.EqualTo(30.0f));
            Assert.That(system.noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.5f));
            Assert.That(system.customData.mode1, Is.EqualTo(VividParticleCustomDataMode.Vector));
            Assert.That(system.customData.numberOfComponents1, Is.EqualTo(2));
            Assert.That(
                system.customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f),
                Is.EqualTo(new Vector4(1.0f, 3.0f, 0.0f, 0.0f)));
            Assert.That(system.textureSheetAnimation.enabled, Is.True);
            Assert.That(system.textureSheetAnimation.numTilesX, Is.EqualTo(4));
            Assert.That(system.textureSheetAnimation.numTilesY, Is.EqualTo(2));
            Assert.That(system.textureSheetAnimation.animation, Is.EqualTo(VividParticleTextureSheetAnimationType.SingleRow));
            Assert.That(system.textureSheetAnimation.EvaluateFrame(0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(system.textureSheetAnimation.cycleCount, Is.EqualTo(2.0f));
            Assert.That(system.textureSheetAnimation.rowIndex, Is.EqualTo(1));
            Assert.That(system.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
            Assert.That(system.rendererModule.stretchSpeedScale, Is.EqualTo(0.5f));
            Assert.That(system.rendererModule.pivot, Is.EqualTo(new Vector3(0.25f, 0.5f, -0.25f)));
            Assert.That(system.rendererModule.minParticleSize, Is.EqualTo(0.1f));
            Assert.That(system.rendererModule.maxParticleSize, Is.EqualTo(2.0f));
            Assert.That(system.rendererModule.flip, Is.EqualTo(new Vector3(1.0f, 0.5f, 0.0f)));
            Assert.That(system.rendererModule.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(system.rendererModule.customData1Enabled, Is.True);
            Assert.That(system.rendererModule.sortMode, Is.EqualTo(VividParticleSortMode.ByDistance));
            Assert.That(system.rendererModule.sortingPriority, Is.EqualTo(7));
            Assert.That(system.rendererModule.batchLayer, Is.EqualTo(3));
            Assert.That(system.rendererModule.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.Camera));
            Assert.That(system.rendererModule.staticShadowCaster, Is.True);
            Assert.That(system.rendererModule.renderingLayerMask, Is.EqualTo(0x8u));
            Assert.That(system.rendererModule.meshCount, Is.EqualTo(2));
            Assert.That(system.rendererModule.renderMesh, Is.EqualTo(primaryMesh));
            var copiedMeshes = new Mesh[2];
            Assert.That(system.rendererModule.GetMeshes(copiedMeshes), Is.EqualTo(2));
            Assert.That(copiedMeshes[0], Is.EqualTo(primaryMesh));
            Assert.That(copiedMeshes[1], Is.EqualTo(extraMesh));

            asset.main.startLifetime = 9.0f;
            asset.emission.bursts[0] = new VividParticleBurst(0.25f, 9);
            asset.forceOverLifetime.force = Vector3.down;
            asset.forceOverLifetime.enabled = false;
            asset.externalForces.enabled = false;
            asset.externalForces.multiplier = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            asset.velocityOverLifetime.enabled = false;
            asset.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            asset.main.customEmitterVelocity = Vector3.one * 9.0f;
            asset.inheritVelocity.enabled = false;
            asset.inheritVelocity.curve = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            asset.limitVelocityOverLifetime.enabled = false;
            asset.limitVelocityOverLifetime.limitX = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            asset.limitVelocityOverLifetime.drag = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            asset.colorOverLifetime.color = CreateGradient(Color.green, Color.yellow);
            asset.sizeOverLifetime.size = AnimationCurve.Constant(0.0f, 1.0f, 4.0f);
            asset.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 180.0f);
            asset.noise.positionAmount = AnimationCurve.Constant(0.0f, 1.0f, 9.0f);
            asset.noise.quality = VividParticleNoiseQuality.Low;
            asset.noise.remapX = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            asset.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 90.0f);
            asset.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 3.0f);
            asset.customData.mode1 = VividParticleCustomDataMode.Disabled;
            asset.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 9.0f));
            asset.textureSheetAnimation.enabled = false;
            asset.textureSheetAnimation.numTilesX = 8;
            asset.textureSheetAnimation.frameOverTime = AnimationCurve.Constant(0.0f, 1.0f, 0.25f);
            asset.rendererModule.renderMode = VividParticleRenderMode.Billboard;
            asset.rendererModule.color = Color.red;
            asset.rendererModule.stretchSpeedScale = 3.0f;
            asset.rendererModule.pivot = Vector3.one;
            asset.rendererModule.minParticleSize = 1.0f;
            asset.rendererModule.maxParticleSize = 4.0f;
            asset.rendererModule.flip = Vector3.one;
            asset.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            asset.rendererModule.customData1Enabled = false;
            asset.rendererModule.sortMode = VividParticleSortMode.None;
            asset.rendererModule.sortingPriority = 17;
            asset.rendererModule.batchLayer = 9;
            asset.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            asset.rendererModule.staticShadowCaster = false;
            asset.rendererModule.renderingLayerMask = 0x10u;
            asset.rendererModule.SetMeshes(System.Array.Empty<Mesh>());

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.forceOverLifetime.enabled, Is.True);
            Assert.That(system.forceOverLifetime.force, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(system.externalForces.enabled, Is.True);
            Assert.That(system.externalForces.EvaluateMultiplier(0.5f), Is.EqualTo(0.5f));
            Assert.That(system.velocityOverLifetime.enabled, Is.True);
            Assert.That(system.velocityOverLifetime.Evaluate(0.5f).x, Is.EqualTo(3.0f));
            Assert.That(system.main.customEmitterVelocity, Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(system.inheritVelocity.enabled, Is.True);
            Assert.That(system.inheritVelocity.Evaluate(0.5f), Is.EqualTo(0.75f));
            Assert.That(system.limitVelocityOverLifetime.enabled, Is.True);
            Assert.That(
                system.limitVelocityOverLifetime.EvaluateLimit(0.5f),
                Is.EqualTo(new Vector3(2.0f, 3.0f, 4.0f)));
            Assert.That(system.limitVelocityOverLifetime.EvaluateDrag(0.5f), Is.EqualTo(0.25f));
            Assert.That(system.colorOverLifetime.Evaluate(0.0f), Is.EqualTo(Color.red));
            Assert.That(system.sizeOverLifetime.Evaluate(1.0f), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(system.rotationOverLifetime.EvaluateAngularVelocity(0.5f), Is.EqualTo(90.0f).Within(0.0001f));
            Assert.That(system.noise.EvaluatePositionAmount(0.5f), Is.EqualTo(0.75f));
            Assert.That(system.noise.quality, Is.EqualTo(VividParticleNoiseQuality.Medium));
            Assert.That(system.noise.EvaluateRemap(Vector3.zero).x, Is.EqualTo(-0.5f));
            Assert.That(system.noise.EvaluateRotationAmount(0.5f), Is.EqualTo(30.0f));
            Assert.That(system.noise.EvaluateSizeAmount(0.5f), Is.EqualTo(0.5f));
            Assert.That(system.customData.mode1, Is.EqualTo(VividParticleCustomDataMode.Vector));
            Assert.That(
                system.customData.Evaluate(VividParticleCustomDataStream.Custom1, 0.5f),
                Is.EqualTo(new Vector4(1.0f, 3.0f, 0.0f, 0.0f)));
            Assert.That(system.textureSheetAnimation.enabled, Is.True);
            Assert.That(system.textureSheetAnimation.numTilesX, Is.EqualTo(4));
            Assert.That(system.textureSheetAnimation.EvaluateFrame(0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(system.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
            Assert.That(system.rendererModule.stretchSpeedScale, Is.EqualTo(0.5f));
            Assert.That(system.rendererModule.pivot, Is.EqualTo(new Vector3(0.25f, 0.5f, -0.25f)));
            Assert.That(system.rendererModule.minParticleSize, Is.EqualTo(0.1f));
            Assert.That(system.rendererModule.maxParticleSize, Is.EqualTo(2.0f));
            Assert.That(system.rendererModule.flip, Is.EqualTo(new Vector3(1.0f, 0.5f, 0.0f)));
            Assert.That(system.rendererModule.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(system.rendererModule.customData1Enabled, Is.True);
            Assert.That(system.rendererModule.sortMode, Is.EqualTo(VividParticleSortMode.ByDistance));
            Assert.That(system.rendererModule.sortingPriority, Is.EqualTo(7));
            Assert.That(system.rendererModule.batchLayer, Is.EqualTo(3));
            Assert.That(system.rendererModule.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.Camera));
            Assert.That(system.rendererModule.staticShadowCaster, Is.True);
            Assert.That(system.rendererModule.renderingLayerMask, Is.EqualTo(0x8u));
            Assert.That(system.rendererModule.meshCount, Is.EqualTo(2));
            Assert.That(system.rendererModule.renderMesh, Is.EqualTo(primaryMesh));
        }

        [Test]
        public void Emit_ClampsToMaxParticles_WhenCapacityIsExceeded()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 3;
            system.emission.enabled = false;

            system.Emit(10);

            Assert.That(system.particleCount, Is.EqualTo(3));
        }

        [Test]
        public void Storage_UsesFixedPageCapacity_AndClampsActiveCountWhenMaxParticlesShrinks()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 3;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(10);

            Assert.That(system.usesEcsParticleStorage, Is.True);
            Assert.That(system.particleStoragePageSize, Is.EqualTo(256));
            Assert.That(system.particleStoragePageCount, Is.EqualTo(1));
            Assert.That(system.particleStorageCapacity, Is.EqualTo(256));
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(3));
            Assert.That(system.particleCount, Is.EqualTo(3));

            system.main.maxParticles = 300;
            system.Emit(10);

            Assert.That(system.particleStorageCapacity, Is.EqualTo(512));
            Assert.That(system.particleStoragePageCount, Is.EqualTo(2));
            Assert.That(system.particleCount, Is.EqualTo(13));

            system.main.maxParticles = 2;
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(2));
            Assert.That(system.particleCount, Is.EqualTo(2));
            Assert.That(system.GetParticleRenderColor(2), Is.EqualTo(Color.clear));

            system.Simulate(0.01f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(system.particleStorageCapacity, Is.EqualTo(256));
            Assert.That(system.particleStoragePageCount, Is.EqualTo(1));
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(2));
            Assert.That(system.particleCount, Is.EqualTo(2));
        }

        [Test]
        public void PlayPauseStop_UpdateExpectedStateAndClearParticles()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 8;
            system.emission.enabled = false;

            system.Play(withChildren: false);
            Assert.That(system.isPlaying, Is.True);
            Assert.That(system.isPaused, Is.False);

            system.Emit(2);
            system.Pause(withChildren: false);
            system.UpdateAutomatic(1.0f);

            Assert.That(system.isPlaying, Is.False);
            Assert.That(system.isPaused, Is.True);
            Assert.That(system.particleCount, Is.EqualTo(2));

            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);

            Assert.That(system.isPlaying, Is.False);
            Assert.That(system.isPaused, Is.False);
            Assert.That(system.particleCount, Is.EqualTo(0));
        }

        [Test]
        public void OnEnable_PlayOnAwakeStartsPlaying_WhenInEditMode()
        {
            Assert.That(Application.isPlaying, Is.False);

            var gameObject = new GameObject("Vivid Particle System Test");
            m_ToDestroy.Add(gameObject);

            VividParticleSystem system = gameObject.AddComponent<VividParticleSystem>();

            Assert.That(system.main.playOnAwake, Is.True);
            Assert.That(system.isPlaying, Is.True);
            Assert.That(system.isPaused, Is.False);
        }

        [Test]
        public void Manager_UpdateSystemWithDelta_EmitsContinuously_WhenPlayingInEditMode()
        {
            Assert.That(Application.isPlaying, Is.False);

            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 16;
            system.main.startLifetime = 10.0f;
            system.main.useAutoRandomSeed = false;
            system.emission.rateOverTime = 20.0f;
            system.shape.enabled = false;

            system.Play(withChildren: false);
            VividParticleSystemManager.UpdateSystem(system, 0.25f);

            Assert.That(system.particleCount, Is.EqualTo(5));
            Assert.That(system.time, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Manager_PlayerLoopSchedulesPendingJob_AndBeginCameraCompleteAppliesIntegration()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var scheduledStats), Is.True);
            Assert.That(scheduledStats.PendingJobCount, Is.EqualTo(1));
            Assert.That(scheduledStats.ScheduledJobCount, Is.EqualTo(1));
            Assert.That(scheduledStats.CompletedJobCount, Is.EqualTo(0));
            Assert.That(scheduledStats.LastScheduledFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(VividParticleSystemManager.pendingSimulationPageWorkCountForTests, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastActiveSimulationQueryLineCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastInvalidActiveSimulationQueryLineCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.pendingSimulationSystemCountForTests, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastSimulationPrepareInlineCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastSimulationPrepareScheduledCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastEmissionPlanWorkCount, Is.EqualTo(1));

            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
            Assert.That(completedStats.CompletedJobCount, Is.EqualTo(1));
            Assert.That(completedStats.LastCompletedFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(VividParticleSystemManager.pendingSimulationPageWorkCountForTests, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.pendingSimulationSystemCountForTests, Is.EqualTo(0));

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.y, Is.EqualTo(-2.4525f).Within(0.0001f));
            Assert.That(VividParticleSystemManager.lastEmissionPlanManagedFallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_NativeSimulationConfigAndBursts_FollowRegistrationAndSettingsDirty()
        {
            VividParticleSystem system = CreateActiveSystem();

            Assert.That(VividParticleSystemManager.nativeSimulationConfigCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.nativeRenderModuleConfigCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.nativeSimulationBurstCount, Is.EqualTo(0));
            int configUpdates = VividParticleSystemManager.nativeSimulationConfigUpdateCount;
            int burstRebuilds = VividParticleSystemManager.nativeSimulationBurstRebuildCount;

            system.forceOverLifetime.enabled = true;
            system.forceOverLifetime.force = Vector3.right;

            Assert.That(VividParticleSystemManager.nativeSimulationConfigUpdateCount, Is.EqualTo(configUpdates + 2));
            Assert.That(VividParticleSystemManager.nativeSimulationBurstRebuildCount, Is.EqualTo(burstRebuilds));

            system.emission.bursts = new[]
            {
                new VividParticleBurst(0.1f, 2),
                new VividParticleBurst(0.2f, 3),
            };

            Assert.That(VividParticleSystemManager.nativeSimulationConfigCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.nativeRenderModuleConfigCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.nativeSimulationBurstCount, Is.EqualTo(2));
            Assert.That(VividParticleSystemManager.nativeSimulationConfigUpdateCount, Is.EqualTo(configUpdates + 3));
            Assert.That(VividParticleSystemManager.nativeSimulationBurstRebuildCount, Is.EqualTo(burstRebuilds + 1));

            system.gameObject.SetActive(false);

            Assert.That(VividParticleSystemManager.nativeSimulationConfigCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.nativeRenderModuleConfigCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.nativeSimulationBurstCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_PlayerLoopCollectsActiveSimulationSystems_FromEcsQuery()
        {
            VividParticleSystem first = CreateActiveSystem();
            first.main.startLifetime = 10.0f;
            first.emission.enabled = false;
            first.Emit(1);
            first.Play(withChildren: false);

            VividParticleSystem second = CreateActiveSystem();
            second.main.startLifetime = 10.0f;
            second.emission.enabled = false;
            second.Pause(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);

            Assert.That(VividParticleSystemManager.activeSimulationSystemCountForTests, Is.EqualTo(1));
            VividParticleSystemManager.CompleteAndUploadForTests();

            first.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmitting);
            second.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);

            Assert.That(VividParticleSystemManager.activeSimulationSystemCountForTests, Is.EqualTo(2));
            VividParticleSystemManager.CompleteAndUploadForTests();

            first.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);
            second.Pause(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);

            Assert.That(VividParticleSystemManager.activeSimulationSystemCountForTests, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RendererUpdateCollectsActiveSystems_FromEcsQuery()
        {
            VividParticleSystem first = CreateActiveSystem();
            first.rendererModule.enabled = true;
            first.main.maxParticles = 4;
            first.main.startLifetime = 10.0f;
            first.emission.enabled = false;
            first.shape.enabled = false;

            VividParticleSystem second = CreateActiveSystem();
            second.rendererModule.enabled = true;
            second.main.maxParticles = 4;
            second.main.startLifetime = 10.0f;
            second.emission.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(1));
            VividParticleSystemManager.RunRendererUpdateForTests();
            Assert.That(VividParticleSystemManager.lastActiveRendererQueryLineCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastInvalidActiveRendererQueryLineCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastRendererHandleRecordLookupCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastRendererManagedRecordFallbackCount, Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetRendererStatsForTests().RenderRecordCount,
                Is.EqualTo(1));

            first.rendererModule.enabled = false;
            second.Emit(1);
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastRendererHandleRecordLookupCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastRendererManagedRecordFallbackCount, Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetRendererStatsForTests().RenderRecordCount,
                Is.EqualTo(1));

            second.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastActiveRendererQueryLineCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastInvalidActiveRendererQueryLineCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastRendererHandleRecordLookupCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastRendererManagedRecordFallbackCount, Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetRendererStatsForTests().RenderRecordCount,
                Is.EqualTo(0));
        }

        [Test]
        public void Manager_PageProducer_MapsMultipleSystemPagesToTheirSimulationWorks()
        {
            VividParticleSystem first = CreateActiveSystem();
            first.main.maxParticles = 600;
            first.main.startLifetime = 10.0f;
            first.main.startSpeed = 0.0f;
            first.main.gravityModifier = 1.0f;
            first.emission.enabled = false;
            first.shape.enabled = false;
            first.Emit(300);
            first.Play(withChildren: false);

            VividParticleSystem second = CreateActiveSystem();
            second.main.maxParticles = 600;
            second.main.startLifetime = 10.0f;
            second.main.startSpeed = 0.0f;
            second.main.gravityModifier = 2.0f;
            second.emission.enabled = false;
            second.shape.enabled = false;
            second.Emit(513);
            second.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);

            Assert.That(VividParticleSystemManager.pendingSimulationPageWorkCountForTests, Is.EqualTo(5));
            Assert.That(VividParticleSystemManager.lastActiveSimulationQueryLineCount, Is.EqualTo(2));
            Assert.That(VividParticleSystemManager.lastInvalidActiveSimulationQueryLineCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.pendingSimulationSystemCountForTests, Is.EqualTo(2));

            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(VividParticleSystemManager.pendingSimulationSystemCountForTests, Is.EqualTo(0));
            Assert.That(first.particleCount, Is.EqualTo(300));
            Assert.That(second.particleCount, Is.EqualTo(513));
            Vector3 firstPosition = first.GetParticleObjectToWorldMatrix(299).GetColumn(3);
            Vector3 secondPosition = second.GetParticleObjectToWorldMatrix(512).GetColumn(3);
            Assert.That(firstPosition.y, Is.EqualTo(-0.0981f).Within(0.0001f));
            Assert.That(secondPosition.y, Is.EqualTo(-0.1962f).Within(0.0001f));
        }

        [Test]
        public void Manager_SimulationModuleGroups_SplitBaseAndVelocityPageJobs()
        {
            VividParticleSystem baseSystem = CreateActiveSystem();
            baseSystem.main.maxParticles = 4;
            baseSystem.main.startLifetime = 10.0f;
            baseSystem.main.startSpeed = 0.0f;
            baseSystem.main.gravityModifier = 0.0f;
            baseSystem.emission.enabled = false;
            baseSystem.shape.enabled = false;
            baseSystem.colorOverLifetime.enabled = true;
            baseSystem.Emit(1);
            baseSystem.Play(withChildren: false);

            VividParticleSystem plainSystem = CreateActiveSystem();
            plainSystem.main.maxParticles = 4;
            plainSystem.main.startLifetime = 10.0f;
            plainSystem.main.startSpeed = 0.0f;
            plainSystem.main.gravityModifier = 0.0f;
            plainSystem.emission.enabled = false;
            plainSystem.shape.enabled = false;
            plainSystem.Emit(1);
            plainSystem.Play(withChildren: false);

            VividParticleSystem velocitySystem = CreateActiveSystem();
            velocitySystem.main.maxParticles = 4;
            velocitySystem.main.startLifetime = 10.0f;
            velocitySystem.main.startSpeed = 0.0f;
            velocitySystem.main.gravityModifier = 0.0f;
            velocitySystem.emission.enabled = false;
            velocitySystem.shape.enabled = false;
            velocitySystem.velocityOverLifetime.enabled = true;
            velocitySystem.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            velocitySystem.Emit(1);
            velocitySystem.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.lastActiveSimulationModuleGroupCount, Is.EqualTo(2));
            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupCacheBuildCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupCacheHitCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupSourceScanCount, Is.EqualTo(3));
            Assert.That(VividParticleSystemManager.lastBaseSimulationPageWorkCount, Is.EqualTo(2));
            Assert.That(VividParticleSystemManager.lastVelocitySimulationPageWorkCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.pendingSimulationPageWorkCountForTests, Is.EqualTo(3));

            VividParticleSystemManager.CompleteAndUploadForTests();
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupCacheBuildCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupCacheHitCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastSimulationModuleGroupSourceScanCount, Is.EqualTo(0));

            VividParticleSystemManager.CompleteAndUploadForTests();
            Vector3 velocityPosition = velocitySystem.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(velocityPosition.x, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Manager_LimitVelocityOverLifetime_RunsInModulePageGraph()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 10.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.limitVelocityOverLifetime.enabled = true;
            system.limitVelocityOverLifetime.limit =
                AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            system.limitVelocityOverLifetime.dampen = 1.0f;
            system.Emit(1);
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);

            Assert.That(VividParticleSystemManager.lastBaseSimulationPageWorkCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastVelocitySimulationPageWorkCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system),
                Is.EqualTo(VividParticleModuleFlags.LimitVelocityOverLifetime));

            VividParticleSystemManager.CompleteAndUploadForTests();
            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.z, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Manager_LimitVelocityOverLifetime_AppliesDragInBurstPageJob()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 10.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.limitVelocityOverLifetime.enabled = true;
            system.limitVelocityOverLifetime.limit =
                AnimationCurve.Constant(0.0f, 1.0f, 100.0f);
            system.limitVelocityOverLifetime.drag =
                AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            system.Emit(1);
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.z, Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void Manager_RotationBySpeed_AccumulatesAcrossVelocityChanges()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 10.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.rotationBySpeed.enabled = true;
            system.rotationBySpeed.range = new Vector2(0.0f, 10.0f);
            system.rotationBySpeed.z = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 100.0f);
            system.Emit(1);
            VividParticleSystemManager.VividParticleGpuDataLayout rotationLayout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(
                    VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor.Create(
                        system.rendererModule,
                        requiresPerParticleColor: false,
                        requiresPerParticleSize: false,
                        requiresPerParticleRotation: true));
            Assert.That(
                rotationLayout.PerInstanceUploadColumnMask
                    & VividParticleSystemManager.UploadColumnRotationMask,
                Is.Not.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetRenderKernelFlagsForTests(system)
                    & VividParticleModuleFlags.RotationBySpeed,
                Is.EqualTo(VividParticleModuleFlags.RotationBySpeed));
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            system.limitVelocityOverLifetime.enabled = true;
            system.limitVelocityOverLifetime.limit = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            system.limitVelocityOverLifetime.dampen = 1.0f;
            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Vector3 xAxis = system.GetParticleObjectToWorldMatrix(0).GetColumn(0);
            float angle = Mathf.Atan2(xAxis.y, xAxis.x) * Mathf.Rad2Deg;
            Assert.That(angle, Is.EqualTo(50.0f).Within(0.25f));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                    & VividParticleModuleFlags.RotationBySpeed,
                Is.EqualTo(VividParticleModuleFlags.RotationBySpeed));
        }

        [Test]
        public void Manager_Noise_UsesLazyStateAndDeterministicModulePageSimulation()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            ConfigureNoiseSystem(first);
            ConfigureNoiseSystem(second);

            Assert.That(VividParticleSystemManager.HasNoiseStateColumnForTests(first), Is.True);
            Assert.That(VividParticleSystemManager.HasNoiseStateColumnForTests(second), Is.True);
            first.Emit(1);
            second.Emit(1);
            first.Play(withChildren: false);
            second.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);
            Assert.That(VividParticleSystemManager.lastBaseSimulationPageWorkCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastVelocitySimulationPageWorkCount, Is.EqualTo(2));
            VividParticleSystemManager.CompleteAndUploadForTests();

            Matrix4x4 firstMatrix = first.GetParticleObjectToWorldMatrix(0);
            Matrix4x4 secondMatrix = second.GetParticleObjectToWorldMatrix(0);
            Vector3 firstPosition = firstMatrix.GetColumn(3);
            Vector3 secondPosition = secondMatrix.GetColumn(3);
            Assert.That(firstPosition.sqrMagnitude, Is.GreaterThan(0.000001f));
            Assert.That(Vector3.Distance(firstPosition, secondPosition), Is.LessThan(0.000001f));
            Assert.That(Mathf.Abs(firstMatrix.GetColumn(0).magnitude - 1.0f), Is.GreaterThan(0.000001f));
            float rotationDelta = Mathf.Abs(firstMatrix.m01)
                + Mathf.Abs(firstMatrix.m02)
                + Mathf.Abs(firstMatrix.m10)
                + Mathf.Abs(firstMatrix.m12)
                + Mathf.Abs(firstMatrix.m20)
                + Mathf.Abs(firstMatrix.m21);
            Assert.That(rotationDelta, Is.GreaterThan(0.000001f));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(first)
                    & VividParticleModuleFlags.Noise,
                Is.EqualTo(VividParticleModuleFlags.Noise));
            Assert.That(
                VividParticleSystemManager.GetRenderKernelFlagsForTests(first)
                    & VividParticleModuleFlags.Noise,
                Is.EqualTo(VividParticleModuleFlags.Noise));
        }

        [Test]
        public void Manager_Noise_QualityAndRemapChangeBurstTrajectory()
        {
            VividParticleSystem high = CreateActiveSystem();
            VividParticleSystem low = CreateActiveSystem();
            VividParticleSystem remappedToZero = CreateActiveSystem();
            ConfigureNoiseSystem(high);
            ConfigureNoiseSystem(low);
            ConfigureNoiseSystem(remappedToZero);
            DisableNoiseRenderEffects(high);
            DisableNoiseRenderEffects(low);
            DisableNoiseRenderEffects(remappedToZero);
            high.noise.quality = VividParticleNoiseQuality.High;
            low.noise.quality = VividParticleNoiseQuality.Low;
            remappedToZero.noise.remapEnabled = true;
            remappedToZero.noise.remap = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

            high.Emit(1);
            low.Emit(1);
            remappedToZero.Emit(1);
            high.Play(withChildren: false);
            low.Play(withChildren: false);
            remappedToZero.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Vector3 highPosition = high.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Vector3 lowPosition = low.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Vector3 remappedPosition = remappedToZero.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(highPosition.sqrMagnitude, Is.GreaterThan(0.000001f));
            Assert.That(Vector3.Distance(highPosition, lowPosition), Is.GreaterThan(0.000001f));
            Assert.That(remappedPosition.sqrMagnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void Manager_RegistersParticleSimulationAndRenderJobs_InEcsRegistry()
        {
            Assert.That(VividParticleSystemManager.registeredSimulationJobCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.registeredRenderPageJobDescriptorCount, Is.EqualTo(6));
            Assert.That(
                VividParticleSystemManager.registeredRenderJobCount,
                Is.EqualTo(VividParticleSystemManager.registeredRenderPageJobDescriptorCount + 1));

            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var scheduledStats), Is.True);
            Assert.That(scheduledStats.PendingJobCount, Is.EqualTo(1));
            Assert.That(scheduledStats.ScheduledJobCount, Is.EqualTo(1));

            VividParticleSystemManager.CompleteAndUploadForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
        }

        [Test]
        public void Manager_RenderJobModuleFlags_AreFilteredByActualWork()
        {
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForTests(
                    requestPageUpload: true,
                    requestSharedData: true,
                    hasPageWorks: true,
                    hasSharedDataWorks: true),
                Is.EqualTo((int)(VividParticleSystemManager.RenderJobAllPageUploadFlags
                    | VividParticleSystemManager.RenderJobSharedDataFlag)));

            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForTests(
                    requestPageUpload: true,
                    requestSharedData: true,
                    hasPageWorks: true,
                    hasSharedDataWorks: false),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobAllPageUploadFlags));

            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForTests(
                    requestPageUpload: true,
                    requestSharedData: true,
                    hasPageWorks: false,
                    hasSharedDataWorks: true),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobSharedDataFlag));

            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForTests(
                    requestPageUpload: true,
                    requestSharedData: true,
                    hasPageWorks: false,
                    hasSharedDataWorks: false),
                Is.EqualTo(0));

            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForPageAvailabilityForTests(
                    hasTransformPageWorks: false,
                    hasColorPageWorks: false,
                    hasVelocityStretchPageWorks: false,
                    hasUVPageWorks: true,
                    hasCustomDataPageWorks: false,
                    hasMeshIndexPageWorks: false,
                    hasSharedDataWorks: false,
                    VividParticleSystemManager.RenderJobAllPageUploadFlags
                    | VividParticleSystemManager.RenderJobSharedDataFlag),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobUVUploadFlag));

            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForPageAvailabilityForTests(
                    hasTransformPageWorks: false,
                    hasColorPageWorks: true,
                    hasVelocityStretchPageWorks: false,
                    hasUVPageWorks: false,
                    hasCustomDataPageWorks: false,
                    hasMeshIndexPageWorks: true,
                    hasSharedDataWorks: true,
                    VividParticleSystemManager.RenderJobAllPageUploadFlags
                    | VividParticleSystemManager.RenderJobSharedDataFlag),
                Is.EqualTo((int)(VividParticleSystemManager.RenderJobColorUploadFlag
                    | VividParticleSystemManager.RenderJobMeshIndexUploadFlag
                    | VividParticleSystemManager.RenderJobSharedDataFlag)));
        }

        [Test]
        public void Manager_RenderJobModuleFlags_AreDerivedFromUploadColumnMask()
        {
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnPositionSizeMask),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnBaseColorMask),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobColorUploadFlag));
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnVelocityStretchMask),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobVelocityStretchUploadFlag));
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnMeshIndexMask),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobMeshIndexUploadFlag));
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnBaseColorMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask),
                Is.EqualTo((int)(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobColorUploadFlag
                    | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                    | VividParticleSystemManager.RenderJobMeshIndexUploadFlag)));
            Assert.That(
                VividParticleSystemManager.registeredRenderPageJobDescriptorFlagsForTests,
                Is.EqualTo(VividParticleSystemManager.RenderJobAllPageUploadFlags));
            Assert.That(
                VividParticleSystemManager.registeredRenderPageJobDescriptorColumnMaskForTests,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnBaseColorMask
                    | VividParticleSystemManager.UploadColumnRotationMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnScaleMask
                    | VividParticleSystemManager.UploadColumnUVMask
                    | VividParticleSystemManager.UploadColumnCustomData1Mask
                    | VividParticleSystemManager.UploadColumnCustomData2Mask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(VividParticleSystemManager.CountRenderPageJobModulesForTests(0u), Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobTransformUploadFlag),
                Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobAllPageUploadFlags),
                Is.EqualTo(6));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobAllPageUploadFlags
                    | VividParticleSystemManager.RenderJobSharedDataFlag),
                Is.EqualTo(6));
        }

        [Test]
        public void Manager_PublicParticleCountDrainsPendingJob_BeforeReturning()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var scheduledStats), Is.True);
            Assert.That(scheduledStats.PendingJobCount, Is.EqualTo(1));

            Assert.That(system.particleCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
            Assert.That(completedStats.CompletedJobCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RuntimeStats_DoNotDrainPendingSimulation()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(
                VividParticleSystemManager.TryGetRuntimeStats(
                    system,
                    out VividParticleSystemManager.VividParticleSystemRuntimeStats runtimeStats),
                Is.True);
            Assert.That(runtimeStats.ParticleCount, Is.EqualTo(1));
            Assert.That(runtimeStats.PageSize, Is.EqualTo(VividParticleSystemManager.GetParticleStoragePageSize(system)));
            Assert.That(runtimeStats.StorageCapacity, Is.EqualTo(system.particleStorageCapacity));
            Assert.That(runtimeStats.HasPendingSimulation, Is.True);
            Assert.That(runtimeStats.PendingJobCount, Is.EqualTo(1));

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var scheduledStats), Is.True);
            Assert.That(scheduledStats.PendingJobCount, Is.EqualTo(1));

            Assert.That(system.particleCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_PlayerLoopDrainsLeftoverJob_BeforeSchedulingNextFrame()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var firstStats), Is.True);
            Assert.That(firstStats.PendingJobCount, Is.EqualTo(1));
            Assert.That(firstStats.ScheduledJobCount, Is.EqualTo(1));
            Assert.That(firstStats.CompletedJobCount, Is.EqualTo(0));

            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var secondStats), Is.True);
            Assert.That(secondStats.PendingJobCount, Is.EqualTo(1));
            Assert.That(secondStats.ScheduledJobCount, Is.EqualTo(2));
            Assert.That(secondStats.CompletedJobCount, Is.EqualTo(1));

            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
            Assert.That(completedStats.CompletedJobCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_EmissionInitialize_IsScheduledInsideManagerGraph_ForSingleWork()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.emission.enabled = true;
            system.emission.rateOverTime = 4.0f;
            system.shape.enabled = false;

            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(
                VividParticleSystemManager.TryGetRuntimeStats(
                    system,
                    out VividParticleSystemManager.VividParticleSystemRuntimeStats runtimeStats),
                Is.True);
            Assert.That(runtimeStats.ParticleCount, Is.EqualTo(1));
            Assert.That(runtimeStats.LastEmissionInitializeWorkCount, Is.EqualTo(1));
            Assert.That(runtimeStats.LastEmissionInitializeInlineWorkCount, Is.EqualTo(0));
            Assert.That(runtimeStats.LastEmissionInitializeScheduledWorkCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionInitializePageWorkCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_EmissionInitializeGraph_WritesShapeDataBeforeCompleteReturns()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.useAutoRandomSeed = false;
            system.emission.enabled = true;
            system.emission.rateOverTime = 4.0f;
            system.shape.enabled = true;
            system.shape.shapeType = VividParticleShapeType.Sphere;
            system.shape.radius = 2.0f;

            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(1));
            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.magnitude, Is.LessThanOrEqualTo(2.0001f));
            Assert.That(VividParticleSystemManager.lastEmissionPlanNativeReservationCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_EmissionInitializeGraph_SplitsLargeReservationByPage()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 600;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.useAutoRandomSeed = false;
            system.emission.enabled = true;
            system.emission.rateOverTime = 0.0f;
            system.emission.bursts = new[] { new VividParticleBurst(0.05f, 513) };
            system.shape.enabled = true;
            system.shape.shapeType = VividParticleShapeType.Sphere;
            system.shape.radius = 2.0f;

            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(513));
            Assert.That(VividParticleSystemManager.lastEmissionInitializePageWorkCount, Is.EqualTo(3));
            Assert.That(VividParticleSystemManager.lastEmissionPlanReservedParticleCount, Is.EqualTo(513));
            Vector3 lastPosition = system.GetParticleObjectToWorldMatrix(512).GetColumn(3);
            Assert.That(lastPosition.magnitude, Is.LessThanOrEqualTo(2.0001f));
        }

        [Test]
        public void StopEmittingAndClear_DrainsPendingJob_AndClearsParticles()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var scheduledStats), Is.True);
            Assert.That(scheduledStats.PendingJobCount, Is.EqualTo(1));

            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);

            Assert.That(system.particleCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
            Assert.That(completedStats.CompletedJobCount, Is.EqualTo(1));
        }

        [Test]
        public void Simulate_IntegratesWithBurstJob_AndAppliesGravity()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(-2.4525f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void Simulate_ForceOverLifetime_IsIntegratedByExistingPageJob()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.forceOverLifetime.enabled = true;
            system.forceOverLifetime.force = new Vector3(4.0f, 0.0f, 0.0f);

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void Simulate_VelocityOverLifetime_UsesOptionalEcsColumnAndPageJob()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.velocityOverLifetime.enabled = true;
            system.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void Manager_VelocityOverLifetime_TransformsLocalVelocityForWorldSimulation()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.velocityOverLifetime.enabled = true;
            system.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            system.velocityOverLifetime.space = VividParticleForceSpace.Local;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void Simulate_InheritVelocityInitial_UsesVelocityCapturedAtEmission()
        {
            VividParticleSystem system = CreateSystem();
            ConfigureInheritVelocitySystem(system, VividParticleInheritVelocityMode.Initial);
            system.main.customEmitterVelocity = new Vector3(2.0f, 0.0f, 0.0f);

            system.Emit(1);
            system.main.customEmitterVelocity = new Vector3(8.0f, 0.0f, 0.0f);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VividParticleSystemManager.HasInheritVelocityStateColumnForTests(system), Is.True);
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                    & VividParticleModuleFlags.InheritVelocity,
                Is.EqualTo(VividParticleModuleFlags.InheritVelocity));
        }

        [Test]
        public void Simulate_InheritVelocityCurrent_UsesLatestEmitterVelocity()
        {
            VividParticleSystem system = CreateSystem();
            ConfigureInheritVelocitySystem(system, VividParticleInheritVelocityMode.Current);
            system.main.customEmitterVelocity = new Vector3(2.0f, 0.0f, 0.0f);

            system.Emit(1);
            system.main.customEmitterVelocity = new Vector3(8.0f, 0.0f, 0.0f);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(4.0f).Within(0.0001f));
        }

        [Test]
        public void Simulate_InheritVelocity_IsIgnoredInLocalSimulationSpace()
        {
            VividParticleSystem system = CreateSystem();
            ConfigureInheritVelocitySystem(system, VividParticleInheritVelocityMode.Current);
            system.main.simulationSpace = VividParticleSystemSimulationSpace.Local;
            system.main.customEmitterVelocity = new Vector3(8.0f, 0.0f, 0.0f);

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Manager_ForceOverLifetime_TransformsLocalForceForWorldSimulation()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.transform.rotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.forceOverLifetime.enabled = true;
            system.forceOverLifetime.force = new Vector3(4.0f, 0.0f, 0.0f);
            system.forceOverLifetime.space = VividParticleForceSpace.Local;

            system.Emit(1);
            system.Play(withChildren: false);
            VividParticleSystemManager.RunPlayerLoopForTests(0.5f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(VividParticleSystemManager.pendingSimulationPageWorkCountForTests, Is.EqualTo(0));
        }

        [Test]
        public void Simulate_ExternalForces_AppliesListedDirectionalFieldInBurstPageJob()
        {
            VividParticleForceField field = CreateForceField();
            field.endRange = 10.0f;
            field.directionX = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            VividParticleSystem system = CreateSystem();
            ConfigureExternalForcesSystem(system);
            system.externalForces.influenceFilter = VividParticleGameObjectFilter.List;
            system.externalForces.AddInfluence(field);

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                    & VividParticleModuleFlags.ExternalForces,
                Is.EqualTo(VividParticleModuleFlags.ExternalForces));
        }

        [Test]
        public void ForceFieldRegistry_RebuildsOnlyForRegistrationSettingsOrTransformChanges()
        {
            VividParticleForceField field = CreateForceField();
            Assert.That(VividParticleSystemManager.PrepareForceFieldRegistryForTests(), Is.EqualTo(1));
            int initialVersion = VividParticleSystemManager.GetForceFieldRegistryVersionForTests();

            Assert.That(VividParticleSystemManager.PrepareForceFieldRegistryForTests(), Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetForceFieldRegistryVersionForTests(),
                Is.EqualTo(initialVersion));

            field.transform.position = Vector3.right;
            Assert.That(VividParticleSystemManager.PrepareForceFieldRegistryForTests(), Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetForceFieldRegistryVersionForTests(),
                Is.EqualTo(initialVersion + 1));

            field.enabled = false;
            Assert.That(VividParticleSystemManager.PrepareForceFieldRegistryForTests(), Is.EqualTo(0));
        }

        [Test]
        public void WindZoneRegistry_DiscoversOnceAndUsesIncrementalPropertyUpdates()
        {
            WindZone windZone = CreateWindZone();
            int trackedCount = VividParticleSystemManager.PrepareWindZoneRegistryForTests();
            int initialVersion = VividParticleSystemManager.GetWindZoneRegistryVersionForTests();
            int initialDiscoveryCount = VividParticleSystemManager.GetWindZoneDiscoveryCountForTests();
            Assert.That(trackedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(initialDiscoveryCount, Is.EqualTo(1));

            VividParticleSystemManager.PrepareWindZoneRegistryForTests();
            Assert.That(
                VividParticleSystemManager.GetWindZoneRegistryVersionForTests(),
                Is.EqualTo(initialVersion));
            Assert.That(
                VividParticleSystemManager.GetWindZoneDiscoveryCountForTests(),
                Is.EqualTo(initialDiscoveryCount));

            windZone.windMain += 1.0f;
            VividParticleSystemManager.PrepareWindZoneRegistryForTests();
            Assert.That(
                VividParticleSystemManager.GetWindZoneRegistryVersionForTests(),
                Is.EqualTo(initialVersion + 1));
            Assert.That(
                VividParticleSystemManager.GetWindZoneDiscoveryCountForTests(),
                Is.EqualTo(initialDiscoveryCount));
        }

        [Test]
        public void Simulate_ExternalForces_AppliesDirectionalWindAndListFilterExcludesIt()
        {
            WindZone windZone = CreateWindZone();
            windZone.mode = WindZoneMode.Directional;
            windZone.windMain = 10000.0f;
            windZone.windPulseMagnitude = 0.0f;
            windZone.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            VividParticleSystem affected = CreateSystem();
            VividParticleSystem excluded = CreateSystem();
            ConfigureExternalForcesSystem(affected);
            ConfigureExternalForcesSystem(excluded);
            affected.externalForces.influenceFilter = VividParticleGameObjectFilter.LayerMask;
            excluded.externalForces.influenceFilter = VividParticleGameObjectFilter.List;

            affected.Emit(1);
            excluded.Emit(1);
            affected.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);
            excluded.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 affectedPosition = affected.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Vector3 excludedPosition = excluded.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(affectedPosition.x, Is.GreaterThan(50.0f));
            Assert.That(excludedPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Simulate_ExternalForces_LayerMaskFilterUpdatesWithoutSceneScan()
        {
            VividParticleForceField field = CreateForceField();
            field.gameObject.layer = 8;
            field.endRange = 10.0f;
            field.directionX = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            VividParticleSystem system = CreateSystem();
            ConfigureExternalForcesSystem(system);
            system.externalForces.influenceFilter = VividParticleGameObjectFilter.LayerMask;
            system.externalForces.influenceMask = 1 << 7;

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);
            Vector3 excludedPosition = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(excludedPosition, Is.EqualTo(Vector3.zero));

            system.externalForces.influenceMask = 1 << 8;
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);
            Vector3 includedPosition = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(includedPosition.x, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Simulate_ExternalForces_DragReducesBaseVelocityBeforeMove()
        {
            VividParticleForceField field = CreateForceField();
            field.endRange = 10.0f;
            field.drag = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            VividParticleSystem system = CreateSystem();
            ConfigureExternalForcesSystem(system);
            system.main.startSpeed = 2.0f;
            system.externalForces.influenceFilter = VividParticleGameObjectFilter.List;
            system.externalForces.AddInfluence(field);

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.z, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void Simulate_ExternalForces_SamplesReadableVectorFieldInBurstJob()
        {
            var vectorField = new Texture3D(1, 1, 1, TextureFormat.RGBAFloat, mipChain: false);
            vectorField.SetPixels(new[] { new Color(1.0f, 0.0f, 0.0f, 0.0f) });
            vectorField.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            m_ToDestroy.Add(vectorField);
            VividParticleForceField field = CreateForceField();
            field.endRange = 10.0f;
            field.vectorField = vectorField;
            field.vectorFieldSpeed = AnimationCurve.Constant(0.0f, 1.0f, 2.0f);
            field.vectorFieldAttraction = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            VividParticleSystem system = CreateSystem();
            ConfigureExternalForcesSystem(system);
            system.externalForces.influenceFilter = VividParticleGameObjectFilter.List;
            system.externalForces.AddInfluence(field);

            system.Emit(1);
            system.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.x, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void CollisionModule_CopyFrom_ClonesPlaneListAndClampsValues()
        {
            Transform plane = CreateCollisionPlane();
            VividParticleCollisionModule source = VividParticleCollisionModule.CreateDefault();
            source.enabled = true;
            source.dampen = 2.0f;
            source.bounce = -1.0f;
            source.minKillSpeed = 4.0f;
            source.maxKillSpeed = 2.0f;
            source.AddPlane(plane);

            VividParticleCollisionModule copy = VividParticleCollisionModule.CreateDefault();
            copy.CopyFrom(source);
            source.RemoveAllPlanes();

            Assert.That(copy.enabled, Is.True);
            Assert.That(copy.dampen, Is.EqualTo(1.0f));
            Assert.That(copy.bounce, Is.EqualTo(0.0f));
            Assert.That(copy.maxKillSpeed, Is.EqualTo(copy.minKillSpeed));
            Assert.That(copy.planeCount, Is.EqualTo(1));
            Assert.That(copy.GetPlane(0), Is.SameAs(plane));
        }

        [Test]
        public void Simulate_CollisionPlane_ResolvesAndBouncesInBurstPageJob()
        {
            Transform plane = CreateCollisionPlane();
            VividParticleSystem system = CreateSystem();
            ConfigurePlaneCollisionSystem(system, plane);

            system.Emit(1);
            system.Simulate(1.0f, withChildren: false, restart: false, fixedTimeStep: false);
            Vector3 contactPosition = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(contactPosition.y, Is.EqualTo(0.5f).Within(0.0001f));

            system.Simulate(0.25f, withChildren: false, restart: false, fixedTimeStep: false);
            Vector3 bouncedPosition = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(bouncedPosition.y, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                    & VividParticleModuleFlags.Collision,
                Is.EqualTo(VividParticleModuleFlags.Collision));
        }

        [Test]
        public void Simulate_CollisionPlane_KillsParticleOutsideConfiguredSpeedRange()
        {
            Transform plane = CreateCollisionPlane();
            VividParticleSystem system = CreateSystem();
            ConfigurePlaneCollisionSystem(system, plane);
            system.collision.maxKillSpeed = 0.5f;

            system.Emit(1);
            system.Simulate(1.0f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(system.particleCount, Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Simulate_WorldPrimitiveCollider_ResolvesInSharedBurstRegistry(int shape)
        {
            Collider collider = CreateWorldCollider(shape, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureWorldCollisionSystem(system, 1 << 30);

            system.Emit(1);
            system.Simulate(1.0f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 contactPosition = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(contactPosition.y, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(collider.enabled, Is.True);
            var collisionEvents = new List<VividParticleCollisionEvent>();
            Assert.That(system.GetCollisionEvents(collisionEvents), Is.EqualTo(1));
            Assert.That(collisionEvents[0].colliderComponent, Is.SameAs(collider));
            Assert.That(collisionEvents[0].intersection.y, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(collisionEvents[0].normal.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(collisionEvents[0].velocity.y, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Simulate_HighQualityCollision_SweepsFastParticleAcrossPrimitive(int shape)
        {
            CreateWorldCollider(shape, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureWorldCollisionSystem(system, 1 << 30);
            system.transform.position = Vector3.up * 3.0f;
            system.main.startSpeed = 10.0f;
            system.collision.quality = VividParticleCollisionQuality.High;

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.y, Is.EqualTo(5.0f).Within(0.001f));
        }

        [Test]
        public void Simulate_LowQualityCollision_UsesDiscreteOverlapForFastParticle()
        {
            CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureWorldCollisionSystem(system, 1 << 30);
            system.transform.position = Vector3.up * 3.0f;
            system.main.startSpeed = 10.0f;
            system.collision.quality = VividParticleCollisionQuality.Low;

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.y, Is.EqualTo(-2.0f).Within(0.001f));
        }

        [Test]
        public void Simulate_HighQualityCollision_TransformsSweepForLocalSimulation()
        {
            CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureWorldCollisionSystem(system, 1 << 30);
            system.transform.position = Vector3.up * 3.0f;
            system.main.startSpeed = 10.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.Local;
            system.collision.quality = VividParticleCollisionQuality.High;

            system.Emit(1);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.y, Is.EqualTo(5.0f).Within(0.001f));
        }

        [Test]
        public void ColliderRegistry_RebuildsForShapeChangesWithoutRepeatedDiscovery()
        {
            SphereCollider collider = (SphereCollider)CreateWorldCollider(shape: 0, layer: 30);
            int initialCount = VividParticleSystemManager.PrepareColliderRegistryForTests();
            int initialVersion = VividParticleSystemManager.GetColliderRegistryVersionForTests();
            int initialDiscoveryCount = VividParticleSystemManager.GetColliderDiscoveryCountForTests();
            Assert.That(initialCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(initialDiscoveryCount, Is.EqualTo(1));

            VividParticleSystemManager.PrepareColliderRegistryForTests();
            Assert.That(
                VividParticleSystemManager.GetColliderRegistryVersionForTests(),
                Is.EqualTo(initialVersion));
            Assert.That(
                VividParticleSystemManager.GetColliderDiscoveryCountForTests(),
                Is.EqualTo(initialDiscoveryCount));

            collider.radius = 2.0f;
            VividParticleSystemManager.PrepareColliderRegistryForTests();
            Assert.That(
                VividParticleSystemManager.GetColliderRegistryVersionForTests(),
                Is.EqualTo(initialVersion + 1));
            Assert.That(
                VividParticleSystemManager.GetColliderDiscoveryCountForTests(),
                Is.EqualTo(initialDiscoveryCount));
        }

        [Test]
        public void Simulate_Trigger_ClassifiesOutsideEnterInsideAndExitEvents()
        {
            Collider collider = CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureTriggerSystem(system, collider);
            system.trigger.outside = VividParticleOverlapAction.Callback;
            system.trigger.enter = VividParticleOverlapAction.Callback;
            system.trigger.inside = VividParticleOverlapAction.Callback;
            system.trigger.exit = VividParticleOverlapAction.Callback;
            var events = new List<VividParticleTriggerEvent>();

            system.Emit(1);
            system.Simulate(0.25f, withChildren: false, restart: false, fixedTimeStep: false);
            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Outside, events),
                Is.EqualTo(1));

            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);
            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Enter, events),
                Is.EqualTo(1));
            Assert.That(events[0].collider, Is.SameAs(collider));

            system.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);
            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Inside, events),
                Is.EqualTo(1));

            system.Simulate(3.0f, withChildren: false, restart: false, fixedTimeStep: false);
            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Exit, events),
                Is.EqualTo(1));
            Assert.That(events[0].collider, Is.SameAs(collider));
            Assert.That(
                VividParticleSystemManager.GetSimulationKernelFlagsForTests(system)
                    & VividParticleModuleFlags.Trigger,
                Is.EqualTo(VividParticleModuleFlags.Trigger));
            Assert.That(VividParticleSystemManager.HasTriggerStateColumnForTests(system), Is.True);
        }

        [Test]
        public void Simulate_TriggerEnterKill_RemovesParticleThroughSharedCompactionJob()
        {
            Collider collider = CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureTriggerSystem(system, collider);
            system.trigger.enter = VividParticleOverlapAction.Kill;

            system.Emit(1);
            system.Simulate(0.75f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(system.particleCount, Is.EqualTo(0));
        }

        [Test]
        public void Simulate_TriggerDisabledColliderQuery_OmitsColliderReference()
        {
            Collider collider = CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureTriggerSystem(system, collider);
            system.trigger.enter = VividParticleOverlapAction.Callback;
            system.trigger.colliderQueryMode = VividParticleColliderQueryMode.Disabled;

            system.Emit(1);
            system.Simulate(0.75f, withChildren: false, restart: false, fixedTimeStep: false);
            var events = new List<VividParticleTriggerEvent>();

            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Enter, events),
                Is.EqualTo(1));
            Assert.That(events[0].collider, Is.Null);
        }

        [Test]
        public void Simulate_TriggerAllColliderQuery_ReturnsEveryOverlappingCollider()
        {
            Collider first = CreateWorldCollider(shape: 0, layer: 30);
            Collider second = CreateWorldCollider(shape: 0, layer: 30);
            VividParticleSystem system = CreateSystem();
            ConfigureTriggerSystem(system, first);
            system.trigger.AddCollider(second);
            system.trigger.enter = VividParticleOverlapAction.Callback;
            system.trigger.colliderQueryMode = VividParticleColliderQueryMode.All;

            system.Emit(1);
            system.Simulate(0.75f, withChildren: false, restart: false, fixedTimeStep: false);
            var events = new List<VividParticleTriggerEvent>();

            Assert.That(
                system.GetTriggerEvents(VividParticleTriggerEventType.Enter, events),
                Is.EqualTo(2));
            Assert.That(
                new[] { events[0].collider, events[1].collider },
                Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void RenderModules_ColorAndSizeOverLifetime_EvaluateAtParticleAge()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.startSize = 2.0f;
            system.main.startColor = Color.white;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.colorOverLifetime.enabled = true;
            system.colorOverLifetime.color = CreateGradient(Color.red, Color.blue);
            system.sizeOverLifetime.enabled = true;
            system.sizeOverLifetime.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.5f);

            system.Emit(1);
            system.Simulate(5.0f, withChildren: false, restart: false, fixedTimeStep: false);

            Color color = system.GetParticleRenderColor(0);
            Matrix4x4 matrix = system.GetParticleObjectToWorldMatrix(0);
            Assert.That(color.r, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(color.g, Is.EqualTo(0.0f).Within(0.02f));
            Assert.That(color.b, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(color.a, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(matrix.GetColumn(0).magnitude, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void RenderModule_RotationOverLifetime_IntegratesAngularVelocity()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.rotationOverLifetime.enabled = true;
            system.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 90.0f);
            system.velocityOverLifetime.enabled = true;
            system.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

            system.Emit(1);
            system.Simulate(1.0f, withChildren: false, restart: false, fixedTimeStep: false);

            Vector4 xAxis = system.GetParticleObjectToWorldMatrix(0).GetColumn(0);
            Assert.That(xAxis.x, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(xAxis.y, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void GpuLayout_LifetimeModulesForceDynamicColumnsToPerInstance()
        {
            VividParticleRendererModule renderer = VividParticleRendererModule.CreateDefault();
            renderer.colorDataMode = VividParticleGpuDataMode.Shared;
            renderer.sizeDataMode = VividParticleGpuDataMode.Shared;

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(
                    VividParticleSystemManager.VividParticleGpuDataLayoutDescriptor.Create(
                        renderer,
                        requiresPerParticleColor: true,
                        requiresPerParticleSize: true,
                        requiresPerParticleRotation: true,
                        requiresPerParticleVelocity: true));

            Assert.That(
                layout[3].Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout[4].Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout[5].Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout[6].Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                layout.PerInstanceUploadColumnMask & VividParticleSystemManager.UploadColumnBaseColorMask,
                Is.Not.EqualTo(0));
            Assert.That(
                layout.PerInstanceUploadColumnMask & VividParticleSystemManager.UploadColumnScaleMask,
                Is.Not.EqualTo(0));
            Assert.That(
                layout.PerInstanceUploadColumnMask & VividParticleSystemManager.UploadColumnRotationMask,
                Is.Not.EqualTo(0));
            Assert.That(
                layout.PerInstanceUploadColumnMask & VividParticleSystemManager.UploadColumnVelocityStretchMask,
                Is.Not.EqualTo(0));
        }

        [Test]
        public void TextureSheetAnimation_ResolvesWholeSheetAndSingleRowUVs()
        {
            float4 wholeSheet = VividParticleSystemManager.ResolveTextureSheetAnimationUV(
                4,
                2,
                VividParticleTextureSheetAnimationType.WholeSheet,
                rowIndex: 0,
                startFrame: 0.0f,
                cycleCount: 1.0f,
                frameOverTime: 0.625f);
            Assert.That(wholeSheet.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(wholeSheet.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(wholeSheet.z, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(wholeSheet.w, Is.EqualTo(0.5f).Within(0.0001f));

            float4 singleRow = VividParticleSystemManager.ResolveTextureSheetAnimationUV(
                4,
                3,
                VividParticleTextureSheetAnimationType.SingleRow,
                rowIndex: 2,
                startFrame: 0.0f,
                cycleCount: 1.0f,
                frameOverTime: 0.375f);
            Assert.That(singleRow.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(singleRow.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(singleRow.z, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(singleRow.w, Is.EqualTo(1.0f / 3.0f).Within(0.0001f));
        }

        [Test]
        public void Manager_TextureSheetAnimation_EnablesDynamicUVPageJob()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.uvDataEnabled = false;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.textureSheetAnimation.enabled = true;
            system.textureSheetAnimation.numTilesX = 4;
            system.textureSheetAnimation.numTilesY = 2;
            system.textureSheetAnimation.frameOverTime = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

            system.Emit(2);

            VividParticleSystemManager.VividParticleRendererManagerStats stats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(stats.LastUVUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                stats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnUVMask,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastRenderJobModuleFlags & VividParticleSystemManager.RenderJobUVUploadFlag,
                Is.Not.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.lastRenderKernelFlags
                    & (uint)VividParticleModuleFlags.TextureSheetAnimation,
                Is.Not.EqualTo(0u));
        }

        [Test]
        public void Manager_LifetimeModulesRunInsidePageRenderUploadGraph()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.colorOverLifetime.enabled = true;
            system.colorOverLifetime.color = CreateGradient(Color.red, Color.blue);
            system.sizeOverLifetime.enabled = true;
            system.sizeOverLifetime.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.5f);
            system.rotationOverLifetime.enabled = true;
            system.rotationOverLifetime.angularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 90.0f);
            system.velocityOverLifetime.enabled = true;
            system.velocityOverLifetime.x = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

            system.Emit(2);

            VividParticleSystemManager.VividParticleRendererManagerStats stats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var systemStats), Is.True);
            Assert.That(systemStats.LastUploadedCount, Is.EqualTo(2));
            Assert.That(
                stats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnBaseColorMask,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnScaleMask,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnRotationMask,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastRenderJobModuleFlags & VividParticleSystemManager.RenderJobColorUploadFlag,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastRenderJobModuleFlags & VividParticleSystemManager.RenderJobTransformUploadFlag,
                Is.Not.EqualTo(0));
            Assert.That(
                stats.LastRenderJobModuleFlags & VividParticleSystemManager.RenderJobVelocityStretchUploadFlag,
                Is.Not.EqualTo(0));
            uint expectedRenderKernelFlags = (uint)(
                VividParticleModuleFlags.ColorOverLifetime
                | VividParticleModuleFlags.SizeOverLifetime
                | VividParticleModuleFlags.RotationOverLifetime
                | VividParticleModuleFlags.VelocityOverLifetime);
            Assert.That(
                VividParticleSystemManager.lastRenderKernelFlags & expectedRenderKernelFlags,
                Is.EqualTo(expectedRenderKernelFlags));
            Assert.That(VividParticleSystemManager.lastAnimatedTransformPageWorkCount, Is.GreaterThan(0));
            Assert.That(VividParticleSystemManager.lastAnimatedColorPageWorkCount, Is.GreaterThan(0));
            Assert.That(VividParticleSystemManager.lastAnimatedVelocityPageWorkCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_SpeedModulesRunInsidePageRenderUploadGraph()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 5.0f;
            system.main.startSize = 1.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.colorBySpeed.enabled = true;
            system.colorBySpeed.range = new Vector2(0.0f, 10.0f);
            system.colorBySpeed.color = CreateGradient(Color.white, Color.red);
            system.sizeBySpeed.enabled = true;
            system.sizeBySpeed.range = new Vector2(0.0f, 10.0f);
            system.sizeBySpeed.size = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 3.0f);
            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats stats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(
                VividParticleSystemManager.lastRenderKernelFlags
                    & (uint)(VividParticleModuleFlags.ColorBySpeed | VividParticleModuleFlags.SizeBySpeed),
                Is.EqualTo((uint)(VividParticleModuleFlags.ColorBySpeed | VividParticleModuleFlags.SizeBySpeed)));
            Assert.That(stats.LastColorUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(stats.LastTransformUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                stats.LastUploadColumnMask
                    & (VividParticleSystemManager.UploadColumnBaseColorMask
                        | VividParticleSystemManager.UploadColumnPositionSizeMask
                        | VividParticleSystemManager.UploadColumnScaleMask),
                Is.EqualTo(VividParticleSystemManager.UploadColumnBaseColorMask
                    | VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnScaleMask));

            Color color = system.GetParticleRenderColor(0);
            Assert.That(color.g, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(color.b, Is.EqualTo(0.5f).Within(0.02f));
            Vector3 xAxis = system.GetParticleObjectToWorldMatrix(0).GetColumn(0);
            Assert.That(xAxis.magnitude, Is.EqualTo(2.0f).Within(0.001f));
            Assert.That(
                VividParticleSystemManager.GetWorldBounds(system).extents.magnitude,
                Is.GreaterThan(2.0f));
        }

        [Test]
        public void Simulate_CompactsExpiredParticles_WithSwapBackStorage()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 4;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.main.startLifetime = 0.05f;
            system.Emit(1);
            system.main.startLifetime = 1.0f;
            system.Emit(2);

            system.Simulate(0.1f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(system.particleCount, Is.EqualTo(2));
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(2));
            Assert.That(system.GetParticleRenderColor(0).a, Is.GreaterThan(0.0f));
            Assert.That(system.GetParticleRenderColor(1).a, Is.GreaterThan(0.0f));
        }

        [Test]
        public void Stop_StopEmitting_AgesExistingParticlesUntilClear()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startLifetime = 0.05f;
            system.main.maxParticles = 4;
            system.emission.enabled = false;

            system.Emit(1);
            system.Play(withChildren: false);
            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmitting);
            system.UpdateAutomatic(0.1f);

            Assert.That(system.particleCount, Is.EqualTo(0));
        }

        [Test]
        public void Simulate_DoesNotChangePausedState_AndFixedStepIsRepeatable()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 80;
            system.main.startLifetime = 10.0f;
            system.main.useAutoRandomSeed = false;
            system.emission.rateOverTime = 60.0f;
            system.shape.enabled = false;

            system.Pause(withChildren: false);
            system.Simulate(1.0f, withChildren: false, restart: true, fixedTimeStep: true);
            Matrix4x4 firstRunMatrix = system.GetParticleObjectToWorldMatrix(0);

            Assert.That(system.isPaused, Is.True);
            Assert.That(system.isPlaying, Is.False);
            Assert.That(system.particleCount, Is.EqualTo(60));

            system.Simulate(1.0f, withChildren: false, restart: true, fixedTimeStep: true);

            Assert.That(system.particleCount, Is.EqualTo(60));
            AssertVectorApproximately(firstRunMatrix.GetColumn(3), system.GetParticleObjectToWorldMatrix(0).GetColumn(3));
        }

        [Test]
        public void Emission_BurstTriggers_WhenSimulationCrossesBurstTime()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 16;
            system.main.startLifetime = 10.0f;
            system.emission.rateOverTime = 0.0f;
            system.emission.bursts = new[] { new VividParticleBurst(0.5f, 4) };

            system.Simulate(0.6f, withChildren: false, restart: true, fixedTimeStep: false);

            Assert.That(system.particleCount, Is.EqualTo(4));
        }

        [Test]
        public void Emission_LoopRetriggersBurst_AfterDurationWrap()
        {
            VividParticleSystem system = CreateSystem();
            system.main.duration = 1.0f;
            system.main.loop = true;
            system.main.maxParticles = 16;
            system.main.startLifetime = 10.0f;
            system.emission.rateOverTime = 0.0f;
            system.emission.bursts = new[] { new VividParticleBurst(0.25f, 2) };

            system.Simulate(1.3f, withChildren: false, restart: true, fixedTimeStep: false);

            Assert.That(system.particleCount, Is.EqualTo(4));
        }

        [Test]
        public void Manager_EmissionPlanJob_RetriggersLoopBurstWithoutManagedFallback()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.duration = 1.0f;
            system.main.loop = true;
            system.main.maxParticles = 16;
            system.main.startLifetime = 10.0f;
            system.emission.rateOverTime = 0.0f;
            system.emission.bursts = new[] { new VividParticleBurst(0.25f, 2) };
            system.shape.enabled = false;
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(1.3f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(4));
            Assert.That(system.time, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(VividParticleSystemManager.lastEmissionPlanWorkCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanManagedFallbackCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastEmissionPlanNativeReservationCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanReservedParticleCount, Is.EqualTo(4));
        }

        [Test]
        public void Manager_EmissionPlanJob_FallsBackForMoreThan64Bursts()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 128;
            system.main.startLifetime = 10.0f;
            system.emission.rateOverTime = 0.0f;
            var bursts = new VividParticleBurst[65];
            for (int index = 0; index < bursts.Length; index++)
                bursts[index] = new VividParticleBurst(0.1f, 1);
            system.emission.bursts = bursts;
            system.shape.enabled = false;
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.2f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(65));
            Assert.That(VividParticleSystemManager.lastEmissionPlanWorkCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanManagedFallbackCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanNativeReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_EmissionPlanNativeReservation_UsesCapacityFreedByCompaction()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.main.maxParticles = 4;
            system.main.startLifetime = 0.05f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.Emit(4);

            system.main.startLifetime = 10.0f;
            system.emission.enabled = true;
            system.emission.rateOverTime = 40.0f;
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.1f);
            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(system.particleCount, Is.EqualTo(4));
            Assert.That(VividParticleSystemManager.lastEmissionPlanManagedFallbackCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastEmissionPlanNativeReservationCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastEmissionPlanReservedParticleCount, Is.EqualTo(4));
        }

        [Test]
        public void Emission_NonLoopStopsEmitting_AfterDuration()
        {
            VividParticleSystem system = CreateSystem();
            system.main.duration = 0.5f;
            system.main.loop = false;
            system.main.maxParticles = 16;
            system.main.startLifetime = 10.0f;
            system.emission.rateOverTime = 10.0f;

            system.Simulate(1.0f, withChildren: false, restart: true, fixedTimeStep: false);
            int countAfterFirstSimulation = system.particleCount;
            system.Simulate(1.0f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(countAfterFirstSimulation, Is.EqualTo(5));
            Assert.That(system.particleCount, Is.EqualTo(5));
        }

        [Test]
        public void Shape_PointSamplesOriginAndForwardDirection()
        {
            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            shape.shapeType = VividParticleShapeType.Point;

            VividParticleSystem.SampleShape(shape, new System.Random(1), out Vector3 position, out Vector3 direction);

            Assert.That(position, Is.EqualTo(Vector3.zero));
            Assert.That(direction, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Shape_SphereSamplesInsideRadius()
        {
            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            shape.shapeType = VividParticleShapeType.Sphere;
            shape.radius = 2.0f;

            for (int index = 0; index < 64; index++)
            {
                VividParticleSystem.SampleShape(shape, new System.Random(index + 1), out Vector3 position, out _);
                Assert.That(position.magnitude, Is.LessThanOrEqualTo(2.0001f));
            }
        }

        [Test]
        public void Shape_BoxSamplesInsideHalfExtents()
        {
            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            shape.shapeType = VividParticleShapeType.Box;
            shape.boxSize = new Vector3(2.0f, 4.0f, 6.0f);

            for (int index = 0; index < 64; index++)
            {
                VividParticleSystem.SampleShape(shape, new System.Random(index + 1), out Vector3 position, out _);
                Assert.That(Mathf.Abs(position.x), Is.LessThanOrEqualTo(1.0001f));
                Assert.That(Mathf.Abs(position.y), Is.LessThanOrEqualTo(2.0001f));
                Assert.That(Mathf.Abs(position.z), Is.LessThanOrEqualTo(3.0001f));
            }
        }

        [Test]
        public void Shape_ConeSamplesDirectionWithinAngle()
        {
            VividParticleShapeModule shape = VividParticleShapeModule.CreateDefault();
            shape.shapeType = VividParticleShapeType.Cone;
            shape.angle = 30.0f;

            for (int index = 0; index < 64; index++)
            {
                VividParticleSystem.SampleShape(shape, new System.Random(index + 1), out _, out Vector3 direction);
                Assert.That(Vector3.Angle(Vector3.forward, direction), Is.LessThanOrEqualTo(30.0001f));
            }
        }

        [Test]
        public void Bounds_FollowTransformAndRendererSize_ForLocalParticles()
        {
            VividParticleSystem system = CreateSystem();
            system.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            system.main.startSize = 2.0f;
            system.rendererModule.sizeScale = 3.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            Bounds initialBounds = system.worldBounds;
            AssertVectorApproximately(new Vector3(1.0f, 2.0f, 3.0f), initialBounds.center);
            system.transform.position = new Vector3(4.0f, 5.0f, 6.0f);

            Bounds bounds = system.worldBounds;

            AssertVectorApproximately(new Vector3(4.0f, 5.0f, 6.0f), bounds.center);
            AssertVectorApproximately(Vector3.one * 3.0f, bounds.extents);
        }

        [Test]
        public void Bounds_IncludeMaximumSizeOverLifetimeMultiplier()
        {
            VividParticleSystem system = CreateSystem();
            system.main.startSize = 2.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.sizeOverLifetime.enabled = true;
            system.sizeOverLifetime.size = AnimationCurve.Constant(0.0f, 1.0f, 3.0f);

            system.Emit(1);

            Bounds bounds = system.worldBounds;
            AssertVectorApproximately(Vector3.one * 3.0f, bounds.extents);
        }

        [Test]
        public void Manager_RegisterUnregister_DeduplicatesSystems()
        {
            VividParticleSystem system = CreateSystem();

            VividParticleSystemManager.Register(system);
            VividParticleSystemManager.Register(system);

            Assert.That(VividParticleSystemManager.registeredSystemCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.Contains(system), Is.True);

            VividParticleSystemManager.Unregister(system);
            VividParticleSystemManager.Unregister(system);

            Assert.That(VividParticleSystemManager.registeredSystemCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.Contains(system), Is.False);
        }

        [Test]
        public void Manager_Shutdown_DisposesNativeState_AndSupportsReinitialization()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.Emit(2);

            Assert.That(VividParticleSystemManager.isInitializedForTests, Is.True);
            Assert.That(VividParticleSystemManager.registeredSystemCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.nativeSimulationConfigCount, Is.GreaterThan(0));

            VividParticleSystemManager.ShutdownForTests();

            Assert.That(VividParticleSystemManager.isInitializedForTests, Is.False);
            Assert.That(VividParticleSystemManager.registeredSystemCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.nativeSimulationConfigCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.GetRendererStatsForTests().RenderRecordCount, Is.EqualTo(0));

            VividParticleSystemManager.Register(system);

            Assert.That(VividParticleSystemManager.isInitializedForTests, Is.True);
            Assert.That(VividParticleSystemManager.Contains(system), Is.True);
            Assert.That(VividParticleSystemManager.registeredSystemCount, Is.EqualTo(1));
        }

        [Test]
        public void Disable_ReleasesNativeStorage_AndClearsRuntimeState()
        {
            var gameObject = new GameObject("Vivid Particle System Test");
            m_ToDestroy.Add(gameObject);

            VividParticleSystem system = gameObject.AddComponent<VividParticleSystem>();
            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);
            system.rendererModule.enabled = false;
            system.main.maxParticles = 3;
            system.emission.enabled = false;

            system.Emit(2);
            Assert.That(system.particleCount, Is.EqualTo(2));
            Assert.That(system.particleStorageCapacity, Is.EqualTo(256));
            Assert.That(system.usesEcsParticleStorage, Is.True);

            gameObject.SetActive(false);

            Assert.That(system.particleCount, Is.EqualTo(0));
            Assert.That(system.particleStorageCapacity, Is.EqualTo(0));
            Assert.That(system.particleStoragePageCount, Is.EqualTo(0));
            Assert.That(system.isPlaying, Is.False);
            Assert.That(system.isPaused, Is.False);
        }

        [Test]
        public void Manager_MetadataOffsets_AreAlignedAndTaggedAsPerInstance()
        {
            int capacity = 17;
            VividParticleSystemManager.VividParticleGpuDataLayout billboardLayout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Billboard);
            VividParticleSystemManager.VividParticleGpuBufferDataInfo[] billboardInfos =
                billboardLayout.CreateBufferInfos(capacity, sharpCapacity: 1, spanCapacity: 1);
            VividParticleSystemManager.VividParticleGpuDataLayout stretchLayout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Stretch);
            VividParticleSystemManager.VividParticleGpuBufferDataInfo[] stretchInfos =
                stretchLayout.CreateBufferInfos(capacity, sharpCapacity: 1, spanCapacity: 1);
            var nativeBillboardInfos =
                new NativeList<VividParticleSystemManager.VividParticleGpuBufferDataInfo>(
                    billboardLayout.Count,
                    Allocator.Temp);
            try
            {
                billboardLayout.FillBufferInfos(nativeBillboardInfos, capacity, sharpCapacity: 1, spanCapacity: 1);
                Assert.That(nativeBillboardInfos.Length, Is.EqualTo(billboardInfos.Length));
                for (int index = 0; index < billboardInfos.Length; index++)
                {
                    Assert.That(nativeBillboardInfos[index].DataInfo, Is.EqualTo(billboardInfos[index].DataInfo));
                    Assert.That(nativeBillboardInfos[index].ByteOffset, Is.EqualTo(billboardInfos[index].ByteOffset));
                    Assert.That(nativeBillboardInfos[index].ElementCapacity, Is.EqualTo(billboardInfos[index].ElementCapacity));
                }
            }
            finally
            {
                nativeBillboardInfos.Dispose();
            }

            MetadataValue metadata = VividParticleSystemManager.CreatePerInstanceMetadata(123, billboardInfos[2].ByteOffset);
            MetadataValue sharedMetadata = VividParticleSystemManager.CreateSharedMetadata(456, billboardInfos[5].ByteOffset);

            uint expectedDefaultPerSharpBits =
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch);
            Assert.That(billboardLayout.Count, Is.EqualTo(7));
            Assert.That(billboardLayout.DataPerSharpBits, Is.EqualTo(expectedDefaultPerSharpBits));
            Assert.That(
                billboardLayout.SharedDataBlockBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));
            Assert.That(
                billboardLayout.SpanSharedDataBlockBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData)));
            Assert.That(
                billboardLayout.PerSharpValueBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)));
            Assert.That(
                billboardLayout.PerInstanceDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.PositionSize)));
            Assert.That(billboardLayout.PerInstanceElementByteSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4));
            Assert.That(
                billboardLayout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(
                billboardLayout.PerInstanceRenderJobFlagMask,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(
                billboardLayout.TransformRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(billboardLayout.ColorRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardLayout.VelocityStretchRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardLayout.ExtraDataRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardLayout.UVRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardLayout.CustomDataRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardLayout.MeshIndexRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardInfos[0].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SharedData));
            Assert.That(billboardInfos[0].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[0].DataInfo.CreatesRecordCopyDescriptor, Is.True);
            Assert.That(billboardInfos[0].DataInfo.IsSharedValue, Is.False);
            Assert.That(billboardInfos[0].DataInfo.IsPerSharpValue, Is.False);
            Assert.That(billboardInfos[0].DataInfo.IsSharedDataBlock, Is.True);
            Assert.That(billboardInfos[0].DataInfo.IsSpanSharedDataBlock, Is.False);
            Assert.That(
                billboardInfos[0].DataInfo.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SharedDataBlock));
            Assert.That(
                billboardInfos[0].CopyDescriptor.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SharedDataBlock));
            Assert.That(
                billboardInfos[0].CopyDescriptor.CopyRangeKind,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerSharpSingle));
            Assert.That(billboardInfos[0].DataInfo.UploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardInfos[0].DataInfo.RenderJobFlagMask, Is.EqualTo(0u));
            Assert.That(billboardInfos[1].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData));
            Assert.That(billboardInfos[1].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.Span));
            Assert.That(billboardInfos[1].DataInfo.CreatesRecordCopyDescriptor, Is.True);
            Assert.That(billboardInfos[1].DataInfo.IsSharedValue, Is.False);
            Assert.That(billboardInfos[1].DataInfo.IsPerSharpValue, Is.False);
            Assert.That(billboardInfos[1].DataInfo.IsSharedDataBlock, Is.False);
            Assert.That(billboardInfos[1].DataInfo.IsSpanSharedDataBlock, Is.True);
            Assert.That(
                billboardInfos[1].DataInfo.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SpanSharedDataBlock));
            Assert.That(
                billboardInfos[1].CopyDescriptor.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SpanSharedDataBlock));
            Assert.That(
                billboardInfos[1].CopyDescriptor.CopyRangeKind,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.SpanRange));
            Assert.That(billboardInfos[1].DataInfo.UsesInstanceMetadata, Is.True);
            Assert.That(billboardInfos[1].DataInfo.UploadColumnMask, Is.EqualTo(0));
            Assert.That(billboardInfos[1].DataInfo.RenderJobFlagMask, Is.EqualTo(0u));
            Assert.That(billboardInfos[2].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.PositionSize));
            Assert.That(billboardInfos[2].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(billboardInfos[2].DataInfo.CreatesRecordCopyDescriptor, Is.True);
            Assert.That(billboardInfos[2].DataInfo.IsPerSharpValue, Is.False);
            Assert.That(
                billboardInfos[2].DataInfo.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerInstanceValue));
            Assert.That(
                billboardInfos[2].CopyDescriptor.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerInstanceValue));
            Assert.That(
                billboardInfos[2].CopyDescriptor.CopyRangeKind,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerInstanceRange));
            Assert.That(
                billboardInfos[2].DataInfo.DataBit,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize)));
            Assert.That(billboardInfos[2].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(billboardInfos[2].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(billboardInfos[3].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor));
            Assert.That(billboardInfos[3].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[3].DataInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfPackedColor));
            Assert.That(billboardInfos[3].DataInfo.CreatesRecordCopyDescriptor, Is.True);
            Assert.That(billboardInfos[3].DataInfo.IsPerSharpValue, Is.True);
            Assert.That(
                billboardInfos[3].DataInfo.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerSharpValue));
            Assert.That(
                billboardInfos[3].CopyDescriptor.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerSharpValue));
            Assert.That(
                billboardInfos[3].CopyDescriptor.CopyRangeKind,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerSharpSingle));
            Assert.That(billboardInfos[3].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnBaseColorMask));
            Assert.That(billboardInfos[3].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobColorUploadFlag));
            Assert.That(billboardInfos[4].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Scale));
            Assert.That(billboardInfos[4].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[4].DataInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat3));
            Assert.That(billboardInfos[4].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnScaleMask));
            Assert.That(billboardInfos[4].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(billboardInfos[5].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(billboardInfos[5].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[5].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnRotationMask));
            Assert.That(billboardInfos[5].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(billboardInfos[6].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch));
            Assert.That(billboardInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[6].DataInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat3));
            Assert.That(billboardInfos[6].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(billboardInfos[6].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobVelocityStretchUploadFlag));
            Assert.That(
                billboardInfos[2].CopyDescriptor.DataId,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.PositionSize));
            Assert.That(
                billboardInfos[2].CopyDescriptor.Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                billboardInfos[2].CopyDescriptor.ColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(
                billboardInfos[2].CopyDescriptor.DataBit,
                Is.EqualTo(billboardInfos[2].DataInfo.DataBit));
            Assert.That(
                billboardInfos[5].CopyDescriptor.DataId,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(
                billboardInfos[5].CopyDescriptor.Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                billboardInfos[5].CopyDescriptor.ColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnRotationMask));
            Assert.That(
                billboardInfos[5].CopyDescriptor.DataBit,
                Is.EqualTo(billboardInfos[5].DataInfo.DataBit));
            Assert.That(
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize),
                Is.EqualTo(1u << (int)VividParticleSystemManager.VividParticleGpuDataId.PositionSize));
            var sharedValueInfo = new VividParticleSystemManager.VividParticleGpuDataInfo(
                VividParticleSystemManager.VividParticleGpuDataId.Rotation,
                VividParticleSystemManager.VividParticleGpuDataFrequency.Shared,
                VividParticleSystemManager.SizeOfFloat4,
                VividParticleSystemManager.InstanceUploadSegment.Rotation);
            Assert.That(sharedValueInfo.CreatesRecordCopyDescriptor, Is.False);
            Assert.That(sharedValueInfo.IsSharedValue, Is.True);
            Assert.That(sharedValueInfo.IsPerSharpValue, Is.False);
            Assert.That(
                sharedValueInfo.Role,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SharedValue));

            Assert.That(billboardInfos[0].ByteOffset, Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize));
            Assert.That(billboardInfos[1].ByteOffset, Is.EqualTo(billboardInfos[0].ByteOffset + VividParticleSystemManager.SharedDataByteSize));
            Assert.That(billboardInfos[2].ByteOffset, Is.EqualTo(billboardInfos[1].ByteOffset + VividParticleSystemManager.SpanSharedDataByteSize));
            Assert.That(billboardInfos[3].ByteOffset, Is.EqualTo(billboardInfos[2].ByteOffset + capacity * VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[4].ByteOffset, Is.EqualTo(billboardInfos[3].ByteOffset + VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[5].ByteOffset, Is.EqualTo(billboardInfos[4].ByteOffset + VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[6].ByteOffset, Is.EqualTo(billboardInfos[5].ByteOffset + VividParticleSystemManager.SizeOfFloat4));
            Assert.That(
                billboardLayout.CalculateByteSize(capacity, sharpCapacity: 1, spanCapacity: 1),
                Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize
                    + VividParticleSystemManager.SharedDataByteSize
                    + VividParticleSystemManager.SpanSharedDataByteSize
                    + capacity * VividParticleSystemManager.SizeOfFloat4
                    + VividParticleSystemManager.SizeOfFloat4 * 4));
            Assert.That(stretchInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                stretchLayout.CalculateByteSize(capacity, sharpCapacity: 1, spanCapacity: 1),
                Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize
                    + VividParticleSystemManager.SharedDataByteSize
                    + VividParticleSystemManager.SpanSharedDataByteSize
                    + capacity * VividParticleSystemManager.SizeOfFloat4
                    + ((capacity * VividParticleSystemManager.SizeOfFloat3 + 15) / 16 * 16)
                    + VividParticleSystemManager.SizeOfFloat4 * 3));
            Assert.That(
                stretchLayout.PerInstanceElementByteSize,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 + VividParticleSystemManager.SizeOfFloat3));
            Assert.That(
                stretchLayout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(
                stretchLayout.PerInstanceRenderJobFlagMask,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag));
            Assert.That(
                stretchLayout.TransformRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(stretchLayout.ColorRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(
                stretchLayout.VelocityStretchRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(stretchLayout.ExtraDataRenderJobUploadColumnMask, Is.EqualTo(0));
            for (int index = 0; index < billboardInfos.Length; index++)
                Assert.That(billboardInfos[index].ByteOffset % 16, Is.EqualTo(0));

            for (int index = 0; index < stretchInfos.Length; index++)
                Assert.That(stretchInfos[index].ByteOffset % 16, Is.EqualTo(0));

            Assert.That((metadata.Value & VividParticleSystemManager.PerInstanceMetadataMask) != 0u, Is.True);
            Assert.That(metadata.Value & ~VividParticleSystemManager.PerInstanceMetadataMask, Is.EqualTo((uint)billboardInfos[2].ByteOffset));
            Assert.That((sharedMetadata.Value & VividParticleSystemManager.PerInstanceMetadataMask) == 0u, Is.True);
            Assert.That(sharedMetadata.Value, Is.EqualTo((uint)billboardInfos[5].ByteOffset));
            Assert.That(VividParticleSystemManager.UsesPerInstanceRotationData(VividParticleRenderMode.Mesh), Is.False);
            Assert.That(
                VividParticleSystemManager.UsesPerInstanceRotationData(VividParticleGpuDataMode.Shared),
                Is.False);
            Assert.That(
                VividParticleSystemManager.UsesPerInstanceRotationData(VividParticleGpuDataMode.PerParticle),
                Is.True);
            Assert.That(VividParticleSystemManager.UsesPerInstanceVelocityStretchData(VividParticleRenderMode.Billboard), Is.False);
            Assert.That(VividParticleSystemManager.UsesPerInstanceVelocityStretchData(VividParticleRenderMode.Stretch), Is.True);
            Assert.That(
                VividParticleSystemManager.UsesPerInstanceVelocityStretchData(
                    VividParticleRenderMode.Billboard,
                    VividParticleGpuDataMode.Shared),
                Is.False);
            Assert.That(
                VividParticleSystemManager.UsesPerInstanceVelocityStretchData(
                    VividParticleRenderMode.Billboard,
                    VividParticleGpuDataMode.PerParticle),
                Is.True);
            Assert.That(
                VividParticleSystemManager.UsesPerInstanceVelocityStretchData(
                    VividParticleRenderMode.Stretch,
                    VividParticleGpuDataMode.Shared),
                Is.True);
        }

        [Test]
        public void Manager_GpuDataLayout_IsDerivedFromRendererModule()
        {
            VividParticleRendererModule renderer = VividParticleRendererModule.CreateDefault();
            renderer.colorDataMode = VividParticleGpuDataMode.Shared;
            renderer.rotationDataMode = VividParticleGpuDataMode.PerParticle;
            renderer.velocityDataMode = VividParticleGpuDataMode.PerParticle;
            renderer.sizeDataMode = VividParticleGpuDataMode.PerParticle;
            renderer.uvDataEnabled = true;
            renderer.customData1Enabled = true;
            renderer.customData2Enabled = true;
            renderer.meshIndexDataEnabled = true;

            VividParticleSystemManager.VividParticleGpuDataLayout layout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(renderer);

            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor, out var colorInfo), Is.True);
            Assert.That(colorInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Rotation, out var rotationInfo), Is.True);
            Assert.That(rotationInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch, out var velocityInfo), Is.True);
            Assert.That(velocityInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(velocityInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat3));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Scale, out var scaleInfo), Is.True);
            Assert.That(scaleInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(scaleInfo.ElementSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat3));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.UV, out var uvInfo), Is.True);
            Assert.That(uvInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData1, out var customData1Info), Is.True);
            Assert.That(customData1Info.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData2, out var customData2Info), Is.True);
            Assert.That(customData2Info.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex, out var meshIndexInfo), Is.True);
            Assert.That(uvInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnUVMask));
            Assert.That(uvInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobUVUploadFlag));
            Assert.That(customData1Info.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobCustomDataUploadFlag));
            Assert.That(customData2Info.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobCustomDataUploadFlag));
            Assert.That(meshIndexInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobMeshIndexUploadFlag));
            Assert.That(
                layout.DataPerSharpBits,
                Is.EqualTo((1u << (int)VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.UV)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.CustomData1)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.CustomData2)));
            Assert.That(
                layout.SharedDataBlockBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));
            Assert.That(
                layout.SpanSharedDataBlockBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData)));
            Assert.That(
                layout.PerSharpValueBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.UV)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData1)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData2)));
            Assert.That(
                layout.PerInstanceDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex)));
            Assert.That(
                layout.PerInstanceElementByteSize,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 3
                    + VividParticleSystemManager.SizeOfFloat3 * 2));
            Assert.That(
                layout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnRotationMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnScaleMask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(
                layout.PerInstanceRenderJobFlagMask,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                    | VividParticleSystemManager.RenderJobMeshIndexUploadFlag));
            Assert.That(
                layout.TransformRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnRotationMask
                    | VividParticleSystemManager.UploadColumnScaleMask));
            Assert.That(layout.ColorRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(
                layout.VelocityStretchRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(
                layout.ExtraDataRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(layout.UVRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(layout.CustomDataRenderJobUploadColumnMask, Is.EqualTo(0));
            Assert.That(
                layout.MeshIndexRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnMeshIndexMask));

            renderer.colorDataMode = VividParticleGpuDataMode.PerParticle;
            VividParticleSystemManager.VividParticleGpuDataLayout perParticleColorLayout =
                VividParticleSystemManager.VividParticleGpuDataLayout.Create(renderer);
            Assert.That(
                perParticleColorLayout.ColorRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnBaseColorMask));
            Assert.That(
                perParticleColorLayout.PerSharpValueBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.UV)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData1)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData2)));
            Assert.That(
                perParticleColorLayout.PerInstanceDataBits
                    & VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor),
                Is.Not.EqualTo(0u));
            Assert.That(
                perParticleColorLayout.PerInstanceElementByteSize,
                Is.EqualTo(layout.PerInstanceElementByteSize + VividParticleSystemManager.SizeOfPackedColor));
        }

        [Test]
        public void Manager_PackedParticleColor_UsesClampedRgba8Layout()
        {
            uint packed = VividParticleSystemManager.PackParticleColorForTests(
                new Color(1.5f, 0.5f, -1.0f, 0.25f));

            Assert.That(packed, Is.EqualTo(0x400080ffu));
        }

        [Test]
        public void Manager_UploadColumnMasks_MapInstanceSegments()
        {
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.ZeroBlock),
                Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.PositionSize),
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.BaseColor),
                Is.EqualTo(VividParticleSystemManager.UploadColumnBaseColorMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.Rotation),
                Is.EqualTo(VividParticleSystemManager.UploadColumnRotationMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.VelocityStretch),
                Is.EqualTo(VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.Scale),
                Is.EqualTo(VividParticleSystemManager.UploadColumnScaleMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.UV),
                Is.EqualTo(VividParticleSystemManager.UploadColumnUVMask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.CustomData1),
                Is.EqualTo(VividParticleSystemManager.UploadColumnCustomData1Mask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.CustomData2),
                Is.EqualTo(VividParticleSystemManager.UploadColumnCustomData2Mask));
            Assert.That(
                VividParticleSystemManager.GetUploadColumnMask(VividParticleSystemManager.InstanceUploadSegment.MeshIndex),
                Is.EqualTo(VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(
                VividParticleSystemManager.UploadColumnMaskAffectsBounds(
                    VividParticleSystemManager.UploadColumnBaseColorMask),
                Is.False);
            Assert.That(
                VividParticleSystemManager.UploadColumnMaskAffectsBounds(
                    VividParticleSystemManager.UploadColumnPositionSizeMask),
                Is.True);
            Assert.That(
                VividParticleSystemManager.UploadColumnMaskAffectsBounds(
                    VividParticleSystemManager.UploadColumnVelocityStretchMask),
                Is.True);
            Assert.That(
                VividParticleSystemManager.UploadColumnMaskAffectsBounds(
                    VividParticleSystemManager.UploadColumnScaleMask),
                Is.True);
        }

        [Test]
        public void Manager_CullingLayerMask_DetectsAnyVisibleCommandLayer()
        {
            ulong defaultSceneMask = VividParticleSystemManager.ResolveDefaultSceneCullingMask();
            Assert.That(defaultSceneMask, Is.Not.EqualTo(0UL));
            Assert.That(VividParticleSystemManager.ResolveParticleSceneCullingMask(0UL), Is.EqualTo(defaultSceneMask));
            Assert.That(VividParticleSystemManager.ResolveParticleSceneCullingMask(0b0100UL), Is.EqualTo(0b0100UL));
            Assert.That(VividParticleSystemManager.IsSceneVisibleInCullingMask(0UL, 0UL), Is.True);
            Assert.That(VividParticleSystemManager.IsSceneVisibleInCullingMask(0b0010UL, 0b0010UL), Is.True);
            Assert.That(VividParticleSystemManager.IsSceneVisibleInCullingMask(0b0010UL, 0b0100UL), Is.False);
            Assert.That(
                VividParticleSystemManager.HasAnyLayerVisibleInCullingMaskForTests(1u << 4, 4),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyLayerVisibleInCullingMaskForTests(1u << 4, 0, 1, 4, 5),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyLayerVisibleInCullingMaskForTests(1u << 4, 0, 1, 2, 3),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasAnyLayerVisibleInCullingMaskForTests(0u, 0, 1, 2, 31),
                Is.False);
            Assert.That(VividParticleSystemManager.CanUseUnfilteredDrawLayout(uint.MaxValue), Is.True);
            Assert.That(VividParticleSystemManager.CanUseUnfilteredDrawLayout(1u << 4), Is.False);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredDrawLayout(1u << 4, 1u << 4),
                Is.True);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredDrawLayout(1u << 4, (1u << 4) | (1u << 5)),
                Is.False);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredDrawLayout(0u, 0u),
                Is.True);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredSceneCullingLayout(0UL, 0b0110UL),
                Is.True);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredSceneCullingLayout(0b0110UL, 0b0010UL),
                Is.True);
            Assert.That(
                VividParticleSystemManager.CanUseUnfilteredSceneCullingLayout(0b0010UL, 0b0110UL),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandLayer(1u << 4, 1u << 4),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandLayer(1u << 4, (1u << 5) | (1u << 6)),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandLayer(0u, 0u),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandScene(0UL, 0b0100UL),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandScene(0b0010UL, 0b0110UL),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandScene(0b0001UL, 0b0110UL),
                Is.False);
        }

        [Test]
        public void Manager_FilteredDrawLayoutCounts_CompactInvisibleLayers()
        {
            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                BatchCullingViewType.Camera,
                new[] { 4, 5, 4 },
                new[] { 1, 7, 3 },
                new[] { false, false, false },
                out int commandCount,
                out int rangeCount,
                out int visibleCount,
                out int sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(visibleCount, Is.EqualTo(4));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                BatchCullingViewType.Camera,
                new[] { 4, 4 },
                new[] { 2, 3 },
                new[] { false, true },
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(2));
            Assert.That(visibleCount, Is.EqualTo(5));
            Assert.That(sortingCount, Is.EqualTo(3));

            Assert.That(
                VividParticleSystemManager.ShouldKeepDrawCommandForCulling(
                    1u << 2,
                    cullingSceneMask: 0b0010UL,
                    layer: 2,
                    sceneCullingMask: 0b0100UL,
                    recordCount: 1,
                    maxVisibleCount: 1,
                    ShadowCastingMode.On,
                    BatchCullingViewType.Camera),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldKeepDrawCommandForCulling(
                    1u << 2,
                    cullingSceneMask: 0b0010UL,
                    layer: 2,
                    sceneCullingMask: 0b0010UL,
                    recordCount: 1,
                    maxVisibleCount: 1,
                    ShadowCastingMode.On,
                    BatchCullingViewType.Camera),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldKeepDrawCommandForCulling(
                    1u << 2,
                    layer: 2,
                    recordCount: 1,
                    maxVisibleCount: 1,
                    ShadowCastingMode.Off,
                    BatchCullingViewType.Light),
                Is.False);

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                0b0010UL,
                BatchCullingViewType.Camera,
                new[] { 4, 4, 4 },
                new[] { 0b0010UL, 0b0100UL, 0b0010UL },
                new[] { 1, 7, 3 },
                new[] { false, false, false },
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(visibleCount, Is.EqualTo(4));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                0b0110UL,
                BatchCullingViewType.Camera,
                new[] { 4, 4 },
                new[] { 0b0010UL, 0b0100UL },
                new[] { 1, 1 },
                new[] { false, false },
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(2));
            Assert.That(visibleCount, Is.EqualTo(2));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                0UL,
                BatchCullingViewType.Camera,
                new[] { 4, 4 },
                new ulong[] { 0UL, 0UL },
                new[] { 1, 1 },
                new[] { false, false },
                new[] { MotionVectorGenerationMode.ForceNoMotion, MotionVectorGenerationMode.Camera },
                new[] { false, false },
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(2));
            Assert.That(visibleCount, Is.EqualTo(2));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsForTests(
                1u << 4,
                0UL,
                BatchCullingViewType.Camera,
                new[] { 4, 4 },
                new ulong[] { 0UL, 0UL },
                new[] { 1, 1 },
                new[] { false, false },
                new[] { MotionVectorGenerationMode.ForceNoMotion, MotionVectorGenerationMode.ForceNoMotion },
                new[] { false, true },
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(2));
            Assert.That(visibleCount, Is.EqualTo(2));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsWithPickingFilterForTests(
                1u << 4,
                0UL,
                BatchCullingViewType.Picking,
                new[] { 4, 4, 4 },
                new ulong[] { 0UL, 0UL, 0UL },
                new[] { 2, 3, 5 },
                new[] { false, false, false },
                new ulong[] { 10UL, 20UL, 30UL },
                includeEnabled: true,
                includeRenderers: new[] { 10UL, 20UL },
                includeEntities: System.Array.Empty<ulong>(),
                excludeRenderers: new[] { 20UL },
                excludeEntities: System.Array.Empty<ulong>(),
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(1));
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(visibleCount, Is.EqualTo(2));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsWithPickingFilterForTests(
                1u << 4,
                0UL,
                BatchCullingViewType.Camera,
                new[] { 4, 4, 4 },
                new ulong[] { 0UL, 0UL, 0UL },
                new[] { 2, 3, 5 },
                new[] { false, false, false },
                new ulong[] { 10UL, 20UL, 30UL },
                includeEnabled: true,
                includeRenderers: new[] { 10UL },
                includeEntities: System.Array.Empty<ulong>(),
                excludeRenderers: new[] { 20UL },
                excludeEntities: System.Array.Empty<ulong>(),
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(3));
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(visibleCount, Is.EqualTo(10));
            Assert.That(sortingCount, Is.EqualTo(0));

            VividParticleSystemManager.CalculateFilteredDrawLayoutCountsWithPickingFilterForTests(
                1u << 4,
                0UL,
                BatchCullingViewType.Picking,
                new[] { 4, 4 },
                new ulong[] { 0UL, 0UL },
                new[] { 2, 3 },
                new[] { false, false },
                new ulong[] { 10UL, 20UL },
                includeEnabled: false,
                includeRenderers: System.Array.Empty<ulong>(),
                includeEntities: System.Array.Empty<ulong>(),
                excludeRenderers: System.Array.Empty<ulong>(),
                excludeEntities: System.Array.Empty<ulong>(),
                out commandCount,
                out rangeCount,
                out visibleCount,
                out sortingCount);

            Assert.That(commandCount, Is.EqualTo(2));
            Assert.That(rangeCount, Is.EqualTo(1));
            Assert.That(visibleCount, Is.EqualTo(5));
            Assert.That(sortingCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_CopyOperations_CanMergeOnlyContiguousRanges()
        {
            Assert.That(
                VividParticleSystemManager.CanMergeUploadCopyOperations(
                    previousSrcOffset: 16,
                    previousDstOffset: 32,
                    previousSize: 8,
                    nextSrcOffset: 24,
                    nextDstOffset: 40),
                Is.True);
            Assert.That(
                VividParticleSystemManager.CanMergeUploadCopyOperations(
                    previousSrcOffset: 16,
                    previousDstOffset: 32,
                    previousSize: 8,
                    nextSrcOffset: 28,
                    nextDstOffset: 40),
                Is.False);
            Assert.That(
                VividParticleSystemManager.CanMergeUploadCopyOperations(
                    previousSrcOffset: 16,
                    previousDstOffset: 32,
                    previousSize: 8,
                    nextSrcOffset: 24,
                    nextDstOffset: 44),
                Is.False);
        }

        [Test]
        public void Manager_CopyOperations_UseDataInfoRoleAndDirtyMasks()
        {
            var position = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.PositionSize,
                VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance,
                byteOffset: 16,
                elementSize: 16,
                columnMask: VividParticleSystemManager.UploadColumnPositionSizeMask);
            var color = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.BaseColor,
                VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance,
                byteOffset: 32,
                elementSize: 16,
                columnMask: VividParticleSystemManager.UploadColumnBaseColorMask);
            var span = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData,
                VividParticleSystemManager.VividParticleGpuDataFrequency.Span,
                byteOffset: 48,
                elementSize: 16,
                columnMask: 0);
            var sharedData = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.SharedData,
                VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp,
                byteOffset: 56,
                elementSize: VividParticleSystemManager.SharedDataByteSize,
                columnMask: 0);
            var perSharpScale = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.Scale,
                VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp,
                byteOffset: 64,
                elementSize: 16,
                columnMask: VividParticleSystemManager.UploadColumnScaleMask);
            var sharedRotation = new VividParticleSystemManager.VividParticleGpuDataCopyDescriptor(
                VividParticleSystemManager.VividParticleGpuDataId.Rotation,
                VividParticleSystemManager.VividParticleGpuDataFrequency.Shared,
                byteOffset: 80,
                elementSize: 16,
                columnMask: VividParticleSystemManager.UploadColumnRotationMask);

            Assert.That(
                perSharpScale.DataBit,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.Scale)));
            Assert.That(position.Role, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerInstanceValue));
            Assert.That(span.Role, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SpanSharedDataBlock));
            Assert.That(sharedData.Role, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SharedDataBlock));
            Assert.That(perSharpScale.Role, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.PerSharpValue));
            Assert.That(sharedRotation.Role, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataRole.SharedValue));
            Assert.That(position.CopyRangeKind, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerInstanceRange));
            Assert.That(span.CopyRangeKind, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.SpanRange));
            Assert.That(sharedData.CopyRangeKind, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerSharpSingle));
            Assert.That(perSharpScale.CopyRangeKind, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.PerSharpSingle));
            Assert.That(sharedRotation.CopyRangeKind, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataCopyRangeKind.None));
            Assert.That(
                position.ShouldCopyForUploadWork(
                    hasInstanceRange: true,
                    hasSpanData: false,
                    hasSharedData: false,
                    VividParticleSystemManager.UploadColumnPositionSizeMask,
                    sharedDataBits: 0u),
                Is.True);
            Assert.That(
                color.ShouldCopyForUploadWork(
                    hasInstanceRange: true,
                    hasSpanData: false,
                    hasSharedData: false,
                    VividParticleSystemManager.UploadColumnPositionSizeMask,
                    sharedDataBits: 0u),
                Is.False);
            Assert.That(
                span.ShouldCopyForUploadWork(
                    hasInstanceRange: false,
                    hasSpanData: true,
                    hasSharedData: false,
                    columnMask: 0,
                    sharedDataBits: 0u),
                Is.True);
            Assert.That(
                sharedData.ShouldCopyForUploadWork(
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    sharedData.DataBit),
                Is.True);
            position.ResolveUploadRequestRange(
                instanceStartIndex: 3,
                instanceCount: 5,
                sharedStartIndex: 7,
                sharedCount: 11,
                activeCount: 13,
                out int resolvedStart,
                out int resolvedCount);
            Assert.That(resolvedStart, Is.EqualTo(3));
            Assert.That(resolvedCount, Is.EqualTo(5));
            span.ResolveUploadRequestRange(
                instanceStartIndex: 3,
                instanceCount: 5,
                sharedStartIndex: 7,
                sharedCount: 11,
                activeCount: 13,
                out resolvedStart,
                out resolvedCount);
            Assert.That(resolvedStart, Is.EqualTo(7));
            Assert.That(resolvedCount, Is.EqualTo(11));
            perSharpScale.ResolveUploadRequestRange(
                instanceStartIndex: 3,
                instanceCount: 5,
                sharedStartIndex: 7,
                sharedCount: 11,
                activeCount: 13,
                out resolvedStart,
                out resolvedCount);
            Assert.That(resolvedStart, Is.EqualTo(7));
            Assert.That(resolvedCount, Is.EqualTo(13));
            sharedRotation.ResolveUploadRequestRange(
                instanceStartIndex: 3,
                instanceCount: 5,
                sharedStartIndex: 7,
                sharedCount: 11,
                activeCount: 13,
                out resolvedStart,
                out resolvedCount);
            Assert.That(resolvedStart, Is.EqualTo(0));
            Assert.That(resolvedCount, Is.EqualTo(0));
            Assert.That(
                position.TryResolveElementCopyRange(
                    activeCount: 13,
                    batchBaseIndex: 100,
                    sharpIndex: 200,
                    spanBaseIndex: 300,
                    renderMode: VividParticleRenderMode.Mesh,
                    startIndex: 3,
                    count: 5,
                    out int elementStart,
                    out int elementCount),
                Is.True);
            Assert.That(elementStart, Is.EqualTo(103));
            Assert.That(elementCount, Is.EqualTo(5));
            Assert.That(
                perSharpScale.TryResolveElementCopyRange(
                    activeCount: 13,
                    batchBaseIndex: 100,
                    sharpIndex: 200,
                    spanBaseIndex: 300,
                    renderMode: VividParticleRenderMode.Mesh,
                    startIndex: 7,
                    count: 13,
                    out elementStart,
                    out elementCount),
                Is.True);
            Assert.That(elementStart, Is.EqualTo(200));
            Assert.That(elementCount, Is.EqualTo(1));
            Assert.That(
                span.TryResolveElementCopyRange(
                    activeCount: 32,
                    batchBaseIndex: 100,
                    sharpIndex: 200,
                    spanBaseIndex: 300,
                    renderMode: VividParticleRenderMode.Mesh,
                    startIndex: 7,
                    count: 11,
                    out elementStart,
                    out elementCount),
                Is.True);
            Assert.That(elementStart, Is.EqualTo(307));
            Assert.That(elementCount, Is.EqualTo(11));
            Assert.That(
                span.TryResolveElementCopyRange(
                    activeCount: VividParticleSystemManager.BillboardPageSize * 2,
                    batchBaseIndex: 100,
                    sharpIndex: 200,
                    spanBaseIndex: 300,
                    renderMode: VividParticleRenderMode.Billboard,
                    startIndex: VividParticleSystemManager.BillboardPageSize,
                    count: VividParticleSystemManager.BillboardPageSize,
                    out elementStart,
                    out elementCount),
                Is.True);
            Assert.That(elementStart, Is.EqualTo(301));
            Assert.That(elementCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    position,
                    hasInstanceRange: true,
                    hasSpanData: false,
                    hasSharedData: false,
                    VividParticleSystemManager.UploadColumnPositionSizeMask,
                    sharedDataBits: 0u),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    color,
                    hasInstanceRange: true,
                    hasSpanData: false,
                    hasSharedData: false,
                    VividParticleSystemManager.UploadColumnPositionSizeMask,
                    sharedDataBits: 0u),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    span,
                    hasInstanceRange: false,
                    hasSpanData: true,
                    hasSharedData: false,
                    columnMask: 0,
                    sharedDataBits: 0u),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    sharedData,
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    sharedData.DataBit),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    sharedData,
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    sharedDataBits: 0u),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    perSharpScale,
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    perSharpScale.DataBit),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    perSharpScale,
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    sharedDataBits: 0u),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldCopyGpuDataForUploadWork(
                    sharedRotation,
                    hasInstanceRange: true,
                    hasSpanData: true,
                    hasSharedData: true,
                    VividParticleSystemManager.UploadColumnRotationMask,
                    VividParticleSystemManager.GetGpuDataBit(
                        VividParticleSystemManager.VividParticleGpuDataId.Rotation)),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldQueueUploadRecordWorkForTests(
                    hasInstanceRange: false,
                    hasSharedData: false,
                    hasSpanData: false),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldQueueUploadRecordWorkForTests(
                    hasInstanceRange: false,
                    hasSharedData: false,
                    hasSpanData: true),
                Is.True);
        }

        [Test]
        public void Manager_CopyOperations_SortByByteOffsetBeforeByteCount()
        {
            Assert.That(
                VividParticleSystemManager.CompareUploadCopyOperationsForMerge(
                    leftByteOffset: 16,
                    leftByteCount: 128,
                    rightByteOffset: 32,
                    rightByteCount: 4),
                Is.LessThan(0));
            Assert.That(
                VividParticleSystemManager.CompareUploadCopyOperationsForMerge(
                    leftByteOffset: 32,
                    leftByteCount: 4,
                    rightByteOffset: 16,
                    rightByteCount: 128),
                Is.GreaterThan(0));
            Assert.That(
                VividParticleSystemManager.CompareUploadCopyOperationsForMerge(
                    leftByteOffset: 16,
                    leftByteCount: 4,
                    rightByteOffset: 16,
                    rightByteCount: 128),
                Is.LessThan(0));
        }

        [Test]
        public void Manager_IntersectsCullingPlanes_ReturnsExpectedResults()
        {
            Plane[] planes = CreateUnitCubePlanes();

            Assert.That(
                VividParticleSystemManager.IntersectsCullingPlanes(new Bounds(Vector3.zero, Vector3.one), planes),
                Is.True);
            Assert.That(
                VividParticleSystemManager.IntersectsCullingPlanes(new Bounds(new Vector3(1.1f, 0.0f, 0.0f), Vector3.one * 0.4f), planes),
                Is.True);
            Assert.That(
                VividParticleSystemManager.IntersectsCullingPlanes(new Bounds(new Vector3(3.0f, 0.0f, 0.0f), Vector3.one * 0.5f), planes),
                Is.False);
        }

        [Test]
        public void Manager_UpdateRendering_InitializesFallbackMaterial_WhenMaterialIsNull()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;

            system.Emit(2);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var stats), Is.True);
            Assert.That(stats.IsInitialized, Is.True);
            Assert.That(stats.Capacity, Is.EqualTo(4));
            Assert.That(stats.LastUploadedCount, Is.EqualTo(system.particleCount));
        }

        [Test]
        public void Manager_DeltaUpload_UsesTripleBuffers_AndSkipsCleanUploads()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(2);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var initialStats), Is.True);
            Assert.That(initialStats.InstanceDataBufferCount, Is.EqualTo(3));
            Assert.That(initialStats.LastUploadedCount, Is.EqualTo(2));
            Assert.That(initialStats.LastUploadOperationCount, Is.GreaterThan(0));
            Assert.That(initialStats.LastUploadByteCount, Is.GreaterThan(0));
            int initialUploadByteCount = initialStats.LastUploadByteCount;

            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var cleanStats), Is.True);
            Assert.That(cleanStats.LastUploadedCount, Is.EqualTo(2));
            Assert.That(cleanStats.LastUploadOperationCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastUploadByteCount, Is.EqualTo(0));

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var colorStats), Is.True);
            Assert.That(colorStats.LastUploadedCount, Is.EqualTo(2));
            Assert.That(colorStats.LastUploadOperationCount, Is.GreaterThan(0));
            Assert.That(colorStats.LastUploadByteCount, Is.LessThan(initialUploadByteCount));

            system.Emit(1);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var appendStats), Is.True);
            Assert.That(appendStats.LastUploadedCount, Is.EqualTo(3));
            Assert.That(appendStats.LastUploadOperationCount, Is.GreaterThan(0));
            Assert.That(appendStats.LastUploadByteCount, Is.LessThan(initialUploadByteCount));
        }

        [Test]
        public void Manager_RendererManager_BatchesDefaultMaterialSystems_AndLocksOnce()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.RendererRecordRefCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastInvalidRendererRecordRefCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastGpuBufferInfoCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastRecordCopyDescriptorCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastSharedValueBufferInfoCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastPerSharpValueBufferInfoCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastLockCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUploadBatchWorkCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RendererManager_BatchesSystemsUsingSameSourceMaterialVariant()
        {
            Material material = CreateParticleMaterial("Vivid Particle Shared Source Material");
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.material = material;
            second.rendererModule.material = material;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetRendererNativeBatchRecordRefCountForTests(),
                Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererNativeDynamicRecord_TracksActiveCountAndTransform()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.transform.position = new Vector3(2.0f, 3.0f, 4.0f);

            system.Emit(1);

            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeDynamicRecordForTests(
                    system,
                    out int activeCount,
                    out float4x4 localToWorld,
                    out ulong sceneCullingMask,
                    out bool isEditorSelected),
                Is.True);
            Assert.That(activeCount, Is.EqualTo(1));
            Assert.That(
                math.distance(localToWorld.c3.xyz, new float3(2.0f, 3.0f, 4.0f)),
                Is.LessThan(0.00001f));
            Assert.That(sceneCullingMask, Is.Not.EqualTo(0UL));
            Assert.That(isEditorSelected, Is.False);

            system.transform.position = new Vector3(-1.0f, 5.0f, 7.0f);
            system.Emit(2);
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeDynamicRecordForTests(
                    system,
                    out activeCount,
                    out localToWorld,
                    out _,
                    out _),
                Is.True);
            Assert.That(activeCount, Is.EqualTo(3));
            Assert.That(
                math.distance(localToWorld.c3.xyz, new float3(-1.0f, 5.0f, 7.0f)),
                Is.LessThan(0.00001f));
            Vector3[] cullingBoundsCenters =
                VividParticleSystemManager.GetRendererCullingRecordBoundsCentersForTests();
            Assert.That(cullingBoundsCenters, Is.Not.Empty);
            bool containsUpdatedCenter = false;
            for (int index = 0; index < cullingBoundsCenters.Length; index++)
            {
                containsUpdatedCenter |= Vector3.Distance(
                    cullingBoundsCenters[index],
                    new Vector3(-1.0f, 5.0f, 7.0f)) < 0.0001f;
            }
            Assert.That(containsUpdatedCenter, Is.True);

            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeDynamicRecordForTests(
                    system,
                    out _,
                    out _,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void Manager_RendererNativeStorageView_RefreshesAfterEcsCapacityChange()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeStorageView(
                    system,
                    out int initialCapacity,
                    out bool isRenderable,
                    out bool hasPositions,
                    out _),
                Is.True);
            Assert.That(isRenderable, Is.True);
            Assert.That(hasPositions, Is.True);
            Assert.That(initialCapacity, Is.GreaterThanOrEqualTo(8));

            system.main.maxParticles = 300;
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeStorageView(
                    system,
                    out int resizedCapacity,
                    out isRenderable,
                    out hasPositions,
                    out _),
                Is.True);
            Assert.That(isRenderable, Is.True);
            Assert.That(hasPositions, Is.True);
            Assert.That(resizedCapacity, Is.GreaterThanOrEqualTo(300));
            Assert.That(resizedCapacity, Is.GreaterThan(initialCapacity));
        }

        [Test]
        public void Manager_RendererManager_ReusesDrawBatchObjectsAcrossLayoutRebuilds()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.gameObject.SetActive(true);
            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LineGroupPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCreatedCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryReusedCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCacheBuildCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryCacheHitCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQuerySourceScanCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryCachedLineCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheBuildCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheHitCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheSourceScanCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedDrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedDrawBatchCount, Is.EqualTo(0));

            system.rendererModule.renderingLayerMask = 0x2u;
            VividParticleSystemManager.RunRendererUpdateForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LineGroupPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCreatedCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryReusedCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryCacheBuildCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCacheHitCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQuerySourceScanCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCachedLineCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheBuildCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheHitCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheSourceScanCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedDrawBatchCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedDrawBatchCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RendererLineGroupCache_ReusesGroupsForCapacityOnlyLayoutChanges()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.gameObject.SetActive(true);
            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheBuildCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheHitCount, Is.EqualTo(0));

            system.main.maxParticles = 300;
            VividParticleSystemManager.RunRendererUpdateForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryCacheBuildCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCacheHitCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheBuildCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheHitCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererLineGroupCacheSourceScanCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedDrawBatchCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedDrawBatchCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RendererIdentityCache_ReusesStaticIdentityForOrdinaryFrames()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.gameObject.SetActive(true);
            system.Emit(1);

            _ = VividParticleSystemManager.GetRendererStatsForTests();
            int initialRefreshCount =
                VividParticleSystemManager.GetRenderIdentityRefreshCountForTests(system);
            Assert.That(initialRefreshCount, Is.EqualTo(1));

            system.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            VividParticleSystemManager.RunRendererUpdateForTests();
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(
                VividParticleSystemManager.GetRenderIdentityRefreshCountForTests(system),
                Is.EqualTo(initialRefreshCount));

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(
                VividParticleSystemManager.GetRenderIdentityRefreshCountForTests(system),
                Is.EqualTo(initialRefreshCount + 1));
        }

        [Test]
        public void Manager_RendererManager_ReusesRenderRecordsAfterSystemBecomesActiveAgain()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(0));

            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));

            system.Emit(1);

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RenderUploadGraph_UsesDirtyQueueForOrdinaryUpdates()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(2);
            _ = VividParticleSystemManager.GetRendererStatsForTests();

            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats cleanStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(cleanStats.LastDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastInvalidDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastInvalidDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastUploadRecordWorkCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastUploadBatchWorkCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastMergedUploadCopyWorkCount, Is.EqualTo(0));

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats dirtyStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(dirtyStats.LastDirtyUploadQueueCount, Is.EqualTo(1));
            Assert.That(dirtyStats.LastInvalidDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastInvalidDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastUploadRecordWorkCount, Is.EqualTo(1));
            Assert.That(dirtyStats.LastUploadBatchWorkCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastUploadCopyWorkCount, Is.GreaterThan(0));
            Assert.That(dirtyStats.LastMergedUploadCopyWorkCount, Is.EqualTo(dirtyStats.LastCopyOperationCount));
            Assert.That(dirtyStats.LastMergedUploadCopyWorkCount, Is.LessThanOrEqualTo(dirtyStats.LastUploadCopyWorkCount));
            Assert.That(
                dirtyStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
            Assert.That(dirtyStats.LastTransformUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastRenderPageJobModuleCount, Is.EqualTo(0));
            VividParticleSystemManager.GetRendererUploadCollectPathCountsForTests(
                out int nativeRequestCount,
                out int managedFallbackCount);
            Assert.That(nativeRequestCount, Is.EqualTo(1));
            Assert.That(managedFallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RenderUploadGraph_UsesBatchDirtyQueueForBatchOnlyUpdates()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(2);
            _ = VividParticleSystemManager.GetRendererStatsForTests();

            Assert.That(VividParticleSystemManager.MarkFirstRendererBatchZeroBlockDirtyForTests(), Is.True);
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastInvalidDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastDirtyUploadBatchQueueCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastInvalidDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastUploadRecordWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastUploadBatchWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastUploadCopyWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadCopySortCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCopyOperationCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RendererUpdate_TracksOnlyActiveRendererSystems()
        {
            VividParticleSystem activeSystem = CreateActiveSystem();
            VividParticleSystem inactiveSystem = CreateActiveSystem();
            activeSystem.rendererModule.enabled = true;
            inactiveSystem.rendererModule.enabled = true;
            activeSystem.main.maxParticles = 4;
            inactiveSystem.main.maxParticles = 4;
            activeSystem.main.startLifetime = 10.0f;
            inactiveSystem.main.startLifetime = 10.0f;
            activeSystem.emission.enabled = false;
            inactiveSystem.emission.enabled = false;
            activeSystem.shape.enabled = false;
            inactiveSystem.shape.enabled = false;

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(0));

            activeSystem.Emit(1);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(1));
            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));

            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(1));
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));

            activeSystem.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(0));
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(0));

            inactiveSystem.Emit(1);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(1));
            inactiveSystem.rendererModule.enabled = false;
            VividParticleSystemManager.UpdateRendering(inactiveSystem);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(0));
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RendererUpdate_RemovesInactiveRecordsFromQueue()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.pendingRendererRemoveCountForTests, Is.EqualTo(0));

            VividParticleSystemManager.ResetSimulation(system, clearParticles: true);

            Assert.That(VividParticleSystemManager.activeRendererSystemCountForTests, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.pendingRendererRemoveCountForTests, Is.EqualTo(1));

            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(VividParticleSystemManager.pendingRendererRemoveCountForTests, Is.EqualTo(0));
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesByRenderingLayerMask()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.renderingLayerMask = 0x1u;
            second.rendererModule.renderingLayerMask = 0x2u;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesBySortingPriority()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.sortingPriority = 20;
            second.rendererModule.sortingPriority = 10;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
            Assert.That(
                VividParticleSystemManager.GetRendererDrawRangeRendererPrioritiesForTests(BatchCullingViewType.Camera),
                Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public void Manager_RendererManager_GroupsDrawRangesByFilterBeforeMaterial()
        {
            Material materialA = CreateParticleMaterial("Vivid Particle Test Material A");
            Material materialB = CreateParticleMaterial("Vivid Particle Test Material B");
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            VividParticleSystem third = CreateActiveSystem();
            first.gameObject.layer = 4;
            second.gameObject.layer = 5;
            third.gameObject.layer = 4;
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            third.rendererModule.enabled = true;
            first.rendererModule.material = materialA;
            second.rendererModule.material = materialA;
            third.rendererModule.material = materialB;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            third.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            third.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;
            third.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);
            third.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(3));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(3));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(3));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(3));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
            Assert.That(
                VividParticleSystemManager.GetRendererDrawRangeLayersForTests(BatchCullingViewType.Camera),
                Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesByBatchLayer()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.batchLayer = 1;
            second.rendererModule.batchLayer = 2;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesByMotionVectorMode()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            second.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_NormalizesObjectMotionToCameraBatch()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            second.rendererModule.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesByStaticShadowCaster()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.staticShadowCaster = false;
            second.rendererModule.staticShadowCaster = true;
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_SplitsMeshBatchesByMeshSet()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            Mesh sharedMesh = CreateTriangleMesh();
            Mesh firstExtraMesh = CreateTriangleMesh();
            Mesh secondExtraMesh = CreateTriangleMesh();
            m_ToDestroy.Add(sharedMesh);
            m_ToDestroy.Add(firstExtraMesh);
            m_ToDestroy.Add(secondExtraMesh);

            first.rendererModule.enabled = true;
            second.rendererModule.enabled = true;
            first.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            second.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            first.rendererModule.mesh = sharedMesh;
            second.rendererModule.mesh = sharedMesh;
            first.rendererModule.meshes = new[] { firstExtraMesh };
            second.rendererModule.meshes = new[] { secondExtraMesh };
            Assert.That(
                first.rendererModule.meshSetHash,
                Is.Not.EqualTo(second.rendererModule.meshSetHash));
            first.main.maxParticles = 4;
            second.main.maxParticles = 4;
            first.emission.enabled = false;
            second.emission.enabled = false;
            first.shape.enabled = false;
            second.shape.enabled = false;

            first.Emit(1);
            second.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.GetRendererDrawRangeRendererPrioritiesForTests(BatchCullingViewType.Camera),
                Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Renderer_CullingLayout_EmitsDrawCommandPerMeshInMeshSet()
        {
            VividParticleSystem system = CreateActiveSystem();
            Mesh firstMesh = CreateTriangleMesh();
            Mesh secondMesh = CreateTriangleMesh();
            m_ToDestroy.Add(firstMesh);
            m_ToDestroy.Add(secondMesh);

            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.SetMeshes(new[] { firstMesh, secondMesh });
            system.rendererModule.meshIndexDataEnabled = false;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(8);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(8));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(8));
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(0));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(2));
            Assert.That(rendererStats.MeshBatchVisibleCountOutputCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastMeshVisibleBatchReduceWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastMeshVisibleCountInlineWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastMeshVisibleCountScheduledWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnMeshIndexMask,
                Is.EqualTo(0));

            int[] meshVisibleCounts = VividParticleSystemManager.GetMeshVisibleCountsForTests();
            Assert.That(meshVisibleCounts, Is.EqualTo(new[] { 4, 4 }));
            int[] meshBatchVisibleCounts = VividParticleSystemManager.GetMeshBatchVisibleCountsForTests();
            Assert.That(meshBatchVisibleCounts, Is.EqualTo(new[] { 4, 4 }));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(-1, 2), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(0, 2), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(1, 2), Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(2, 2), Is.EqualTo(0));
        }

        [Test]
        public void Renderer_MultiMeshCounts_ScheduleAsSingleManagerGraph_ForMultipleRecords()
        {
            VividParticleSystem first = CreateActiveSystem();
            VividParticleSystem second = CreateActiveSystem();
            Mesh firstMesh = CreateTriangleMesh();
            Mesh secondMesh = CreateTriangleMesh();
            m_ToDestroy.Add(firstMesh);
            m_ToDestroy.Add(secondMesh);

            VividParticleSystem[] systems = { first, second };
            for (int index = 0; index < systems.Length; index++)
            {
                VividParticleSystem system = systems[index];
                system.rendererModule.enabled = true;
                system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
                system.rendererModule.SetMeshes(new[] { firstMesh, secondMesh });
                system.rendererModule.meshIndexDataEnabled = false;
                system.main.maxParticles = 8;
                system.main.startLifetime = 10.0f;
                system.emission.enabled = false;
                system.shape.enabled = false;
                system.Emit(8);
            }

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(16));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(4));
            Assert.That(rendererStats.MeshBatchVisibleCountOutputCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastMeshVisibleBatchReduceWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastMeshVisibleCountInlineWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastMeshVisibleCountScheduledWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastCullingMultiMeshCacheRecordCount, Is.EqualTo(2));

            int[] meshVisibleCounts = VividParticleSystemManager.GetMeshVisibleCountsForTests();
            Assert.That(meshVisibleCounts, Is.EqualTo(new[] { 4, 4, 4, 4 }));
            int[] meshBatchVisibleCounts = VividParticleSystemManager.GetMeshBatchVisibleCountsForTests();
            Assert.That(meshBatchVisibleCounts, Is.EqualTo(new[] { 8, 8 }));
        }

        [Test]
        public void Renderer_CullingLayout_UsesSceneCullingMasksWithoutSplittingDrawBatch()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            Scene firstScene = default;
            Scene secondScene = default;
            const ulong firstSceneMask = 0b0010UL;
            const ulong secondSceneMask = 0b0100UL;

            try
            {
                firstScene = EditorSceneManager.NewPreviewScene();
                secondScene = EditorSceneManager.NewPreviewScene();
                EditorSceneManager.SetSceneCullingMask(firstScene, firstSceneMask);
                EditorSceneManager.SetSceneCullingMask(secondScene, secondSceneMask);
                if (originalScene.IsValid())
                    SceneManager.SetActiveScene(originalScene);

                VividParticleSystem first = CreateActiveSystem();
                VividParticleSystem second = CreateActiveSystem();
                SceneManager.MoveGameObjectToScene(first.gameObject, firstScene);
                SceneManager.MoveGameObjectToScene(second.gameObject, secondScene);

                first.rendererModule.enabled = true;
                second.rendererModule.enabled = true;
                first.main.maxParticles = 4;
                second.main.maxParticles = 4;
                first.main.startLifetime = 10.0f;
                second.main.startLifetime = 10.0f;
                first.emission.enabled = false;
                second.emission.enabled = false;
                first.shape.enabled = false;
                second.shape.enabled = false;
                first.transform.position = new Vector3(10.0f, 0.0f, 0.0f);
                second.transform.position = new Vector3(-20.0f, 0.0f, 0.0f);

                first.Emit(1);
                second.Emit(1);
                Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.True);

                VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                    VividParticleSystemManager.GetRendererStatsForTests();
                Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
                Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
                Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(2));
                Assert.That(rendererStats.CullingPageBoundsCapacity, Is.EqualTo(2));
                Assert.That(rendererStats.LastCullingRecordBuildWorkCount, Is.EqualTo(2));
                Assert.That(rendererStats.RendererRecordRefCount, Is.EqualTo(2));
                Assert.That(rendererStats.LastInvalidRendererRecordRefCount, Is.EqualTo(0));
                Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.False);
                Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
                Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(2));
                Vector3[] boundsCenters =
                    VividParticleSystemManager.GetRendererCullingRecordBoundsCentersForTests();
                bool containsFirstCenter = false;
                bool containsSecondCenter = false;
                for (int index = 0; index < boundsCenters.Length; index++)
                {
                    containsFirstCenter |= Mathf.Abs(boundsCenters[index].x - 10.0f) < 0.0001f;
                    containsSecondCenter |= Mathf.Abs(boundsCenters[index].x + 20.0f) < 0.0001f;
                }

                Assert.That(containsFirstCenter, Is.True);
                Assert.That(containsSecondCenter, Is.True);
                Assert.That(VividParticleSystemManager.ResolveGameObjectSceneCullingMask(first.gameObject), Is.EqualTo(firstSceneMask));
                Assert.That(VividParticleSystemManager.ResolveGameObjectSceneCullingMask(second.gameObject), Is.EqualTo(secondSceneMask));
                Assert.That(
                    VividParticleSystemManager.GetRendererDrawCommandSceneCullingMaskForTests(BatchCullingViewType.Camera),
                    Is.EqualTo(firstSceneMask | secondSceneMask));
                Assert.That(
                    VividParticleSystemManager.GetRendererDrawCommandSceneCullingMaskForTests(BatchCullingViewType.Picking),
                    Is.EqualTo(firstSceneMask | secondSceneMask));
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                    SceneManager.SetActiveScene(originalScene);

                if (secondScene.IsValid() && secondScene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(secondScene);

                if (firstScene.IsValid() && firstScene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(firstScene);
            }
        }

        [Test]
        public void Renderer_CullingLayout_UsesCurrentMeshCount_WhenMeshSetShrinks()
        {
            VividParticleSystem system = CreateActiveSystem();
            Mesh firstMesh = CreateTriangleMesh();
            Mesh secondMesh = CreateTriangleMesh();
            m_ToDestroy.Add(firstMesh);
            m_ToDestroy.Add(secondMesh);

            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.SetMeshes(new[] { firstMesh, secondMesh });
            system.rendererModule.meshIndexDataEnabled = false;
            system.main.maxParticles = 8;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(8);
            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(2));

            system.rendererModule.SetMeshes(new[] { firstMesh });
            VividParticleSystemManager.RunRendererUpdateForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(8));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_RendererUpload_IsCompletedAfterSchedule_WhenStatsAreRead()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.True);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.False);
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_RenderUploadGraph_MovesStaticExtraDataToPerSharp()
        {
            VividParticleSystem system = CreateActiveSystem();
            Mesh mesh = CreateTriangleMesh();
            m_ToDestroy.Add(mesh);

            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.mesh = mesh;
            system.rendererModule.uvDataEnabled = true;
            system.rendererModule.customData1Enabled = true;
            system.rendererModule.customData2Enabled = true;
            system.rendererModule.meshIndexDataEnabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(2);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();

            Assert.That(rendererStats.LastRecordCopyDescriptorCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUVUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCustomDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastMeshIndexUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastExtraDataUploadPageWorkCount,
                Is.EqualTo(rendererStats.LastMeshIndexUploadPageWorkCount));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags
                    & (VividParticleSystemManager.RenderJobMeshIndexUploadFlag
                        | VividParticleSystemManager.RenderJobSharedDataFlag),
                Is.EqualTo(VividParticleSystemManager.RenderJobMeshIndexUploadFlag
                    | VividParticleSystemManager.RenderJobSharedDataFlag));
        }

        [Test]
        public void Manager_CustomDataModule_UsesPerParticleBurstUploadColumns()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.customData1Enabled = false;
            system.rendererModule.customData2Enabled = false;
            system.main.maxParticles = 4;
            system.main.startLifetime = 1.0f;
            system.main.startSpeed = 0.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.customData.mode1 = VividParticleCustomDataMode.Vector;
            system.customData.numberOfComponents1 = 2;
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 2.0f));
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                1,
                AnimationCurve.Constant(0.0f, 1.0f, 3.0f));

            system.Emit(2);
            system.Simulate(0.5f, withChildren: false, restart: false, fixedTimeStep: false);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastCustomDataUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags
                    & VividParticleSystemManager.RenderJobCustomDataUploadFlag,
                Is.EqualTo(VividParticleSystemManager.RenderJobCustomDataUploadFlag));
            Assert.That(
                VividParticleSystemManager.GetRenderKernelFlagsForTests(system)
                    & VividParticleModuleFlags.CustomData,
                Is.EqualTo(VividParticleModuleFlags.CustomData));
            Vector4 sampledCustomData = VividParticleSystemManager.GetCustomDataLutValueForTests(
                system,
                VividParticleCustomDataStream.Custom1,
                0.5f);
            Assert.That(sampledCustomData.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(sampledCustomData.y, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(sampledCustomData.z, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(sampledCustomData.w, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void Manager_ConstantCustomDataModule_UsesPerSharpUploadWithoutPageWork()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 512;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.customData.mode1 = VividParticleCustomDataMode.Vector;
            system.customData.numberOfComponents1 = 2;
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 2.0f));
            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                1,
                AnimationCurve.Constant(0.0f, 1.0f, 3.0f));

            system.Emit(300);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastCustomDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags
                    & VividParticleSystemManager.RenderJobCustomDataUploadFlag,
                Is.EqualTo(0u));
            Assert.That(
                VividParticleSystemManager.GetPerSharpGpuDataValueForTests(
                    system,
                    VividParticleSystemManager.VividParticleGpuDataId.CustomData1),
                Is.EqualTo(new Vector4(2.0f, 3.0f, 0.0f, 0.0f)));

            system.customData.SetVector(
                VividParticleCustomDataStream.Custom1,
                0,
                AnimationCurve.Constant(0.0f, 1.0f, 5.0f));
            VividParticleSystemManager.RunRendererUpdateForTests();
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();

            Assert.That(rendererStats.LastCustomDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                VividParticleSystemManager.GetPerSharpGpuDataValueForTests(
                    system,
                    VividParticleSystemManager.VividParticleGpuDataId.CustomData1),
                Is.EqualTo(new Vector4(5.0f, 3.0f, 0.0f, 0.0f)));
        }

        [Test]
        public void Manager_RendererStats_DoesNotCompletePendingUpload_ForRuntimeSnapshot()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.True);

            VividParticleSystemManager.GetRendererStats();

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.True);

            VividParticleSystemManager.GetRendererStatsForTests();

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.False);
        }

        [Test]
        public void Manager_RendererUpdateSchedulesUpload_AndCompleteFinalizesIt()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 1.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            VividParticleSystemManager.GetRendererStatsForTests();
            system.Play(withChildren: false);

            VividParticleSystemManager.RunPlayerLoopForTests(0.25f);

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.False);

            VividParticleSystemManager.RunRendererUpdateForTests();

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.True);

            VividParticleSystemManager.CompletePendingRendererUploadForTests();

            Assert.That(VividParticleSystemManager.HasPendingRendererUploadForTests(), Is.False);
            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_Upload_ExpandsDirtyRangeIntoPageWorks()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.gameObject.SetActive(true);
            system.Emit(1024);
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.True);
            VividParticleSystemManager.CompletePendingRendererUploadForTests();
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.True);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.False);
            Assert.That(rendererStats.LastUploadBatchWorkCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.lastRendererUploadPageRangeWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(
                rendererStats.LastUploadCopyWorkCount,
                Is.GreaterThanOrEqualTo(8));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastMergedUploadCopyWorkCount, Is.EqualTo(rendererStats.LastCopyOperationCount));
            Assert.That(rendererStats.LastCopyOperationCount, Is.LessThanOrEqualTo(rendererStats.LastUploadCopyWorkCount));
            Assert.That(
                (rendererStats.LastUploadDataBits
                    & VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize)) != 0u,
                Is.True);
            Assert.That(
                (rendererStats.LastUploadDataBits
                    & VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData)) != 0u,
                Is.True);
        }

        [Test]
        public void Manager_Upload_DefaultSharedColorCopiesLessThanPerParticleColor()
        {
            int sharedColorBytes = UploadBillboardParticlesAndGetCopyByteCount(VividParticleGpuDataMode.Shared);
            int perParticleColorBytes = UploadBillboardParticlesAndGetCopyByteCount(VividParticleGpuDataMode.PerParticle);

            Assert.That(sharedColorBytes, Is.GreaterThan(0));
            Assert.That(perParticleColorBytes, Is.GreaterThan(sharedColorBytes));
        }

        [Test]
        public void Manager_Upload_AppendingDefaultSharedData_UpdatesOnlyPositionSpanAndSharedCount()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(512);

            VividParticleSystemManager.VividParticleRendererManagerStats initialStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(initialStats.LastUploadPageWorkCount, Is.EqualTo(2));
            Assert.That(initialStats.LastSharedDataWorkCount, Is.GreaterThan(0));

            system.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats appendStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(appendStats.LastDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(appendStats.LastUploadBatchWorkCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.lastRendererUploadPageRangeWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastTransformUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(
                appendStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(appendStats.LastSharedDataWorkCount, Is.EqualTo(2));
            Assert.That(appendStats.LastUploadCopyWorkCount, Is.EqualTo(4));
            Assert.That(appendStats.LastMergedUploadCopyWorkCount, Is.EqualTo(appendStats.LastCopyOperationCount));
            Assert.That(appendStats.LastCopyOperationCount, Is.LessThanOrEqualTo(4));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(VividParticleSystemManager.SharedDataActiveCountFloat4Mask));
            uint expectedUploadBits =
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize);
            Assert.That(appendStats.LastUploadDataBits, Is.EqualTo(expectedUploadBits));
            Assert.That(appendStats.LastSharedDataWorkCount, Is.LessThan(initialStats.LastSharedDataWorkCount));
            Assert.That(appendStats.LastCopyByteCount, Is.LessThan(initialStats.LastCopyByteCount));
        }

        [Test]
        public void Manager_Upload_UnalignedAppendSplitsAtEcsPageBoundary()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 512;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.gameObject.SetActive(true);

            system.Emit(255);
            VividParticleSystemManager.GetRendererStatsForTests();
            system.Emit(2);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
        }

        [Test]
        public void Manager_Upload_SharedColorChangeDoesNotUploadParticlePages()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.gameObject.SetActive(true);
            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(rendererStats.LastCopyByteCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(0u));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.BaseColor)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_Upload_RendererSharedParametersDoNotUploadParticlePages()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.pivot = new Vector3(0.25f, -0.5f, 0.0f);
            system.rendererModule.minParticleSize = 0.1f;
            system.rendererModule.maxParticleSize = 4.0f;
            system.rendererModule.flip = new Vector3(1.0f, 0.5f, 0.0f);
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(rendererStats.LastCopyByteCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCopyByteCount, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 3));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(VividParticleSystemManager.SharedDataSizeFloat4Mask
                    | VividParticleSystemManager.SharedDataPivotFloat4Mask
                    | VividParticleSystemManager.SharedDataFlipFloat4Mask));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
        }

        [Test]
        public void Manager_Upload_SharedSizeScaleChangeDoesNotUploadParticlePages()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.Shared;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.sizeScale = 2.0f;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(rendererStats.LastCopyByteCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.Scale)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
        }

        [Test]
        public void Manager_Upload_SharedMainStaticDataChangeDoesNotUploadParticlePages()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.Shared;
            system.rendererModule.velocityDataMode = VividParticleGpuDataMode.Shared;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.main.startColor = Color.cyan;
            system.main.startSize = 2.0f;
            system.main.startSpeed = 3.0f;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(rendererStats.LastCopyByteCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
        }

        [Test]
        public void Manager_Upload_PerParticleSizeScaleChangeUploadsParticlePages()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.sizeDataMode = VividParticleGpuDataMode.PerParticle;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.sizeScale = 2.0f;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnScaleMask));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_Upload_LocalSimulationTransformChangeUploadsOnlySharedTransform()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.Local;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastSharedDataWorkCount,
                Is.EqualTo(1),
                $"dirty={rendererStats.LastDirtyUploadQueueCount}, invalid={rendererStats.LastInvalidDirtyUploadQueueCount}, "
                + $"recordWorks={rendererStats.LastUploadRecordWorkCount}, bits={rendererStats.LastUploadDataBits}, "
                + $"flags={rendererStats.LastRenderJobModuleFlags}, pending={rendererStats.HasPendingCullingRecordBuild}");
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastCopyByteCount,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 4));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(VividParticleSystemManager.SharedDataTransformFloat4Mask));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_Upload_SimulationSpaceChangeRefreshesParticleSpaceColumns()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.Local;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastUploadColumnMask
                    & VividParticleSystemManager.UploadColumnPositionSizeMask,
                Is.Not.EqualTo(0));
            Assert.That(
                rendererStats.LastUploadColumnMask
                    & VividParticleSystemManager.UploadColumnVelocityStretchMask,
                Is.Not.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_Upload_StretchParameterChangeUploadsOnlySharedRendererParameters()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.stretchLengthScale = 3.0f;
            system.rendererModule.stretchSpeedScale = 0.5f;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadColumnMask, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastCopyByteCount,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 2));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(VividParticleSystemManager.SharedDataRendererParametersFloat4Mask
                    | VividParticleSystemManager.SharedDataRuntimeFlagsFloat4Mask));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobSharedDataFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_Upload_PerParticleColorChangeUploadsParticlePages()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.gameObject.SetActive(true);
            system.Emit(1024);
            VividParticleSystemManager.GetRendererStatsForTests();

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(0));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnBaseColorMask));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobColorUploadFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_Upload_KeepsVelocityStretchColumn_ForStretchRenderMode()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.main.playOnAwake = false;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.gameObject.SetActive(true);
            system.Emit(1024);
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.True);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask));
            Assert.That(
                rendererStats.LastUploadCopyWorkCount,
                Is.GreaterThanOrEqualTo(8));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastMergedUploadCopyWorkCount, Is.EqualTo(rendererStats.LastCopyOperationCount));
            Assert.That(rendererStats.LastCopyOperationCount, Is.LessThanOrEqualTo(rendererStats.LastUploadCopyWorkCount));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                    | VividParticleSystemManager.RenderJobSharedDataFlag));
            Assert.That(rendererStats.LastRenderPageJobModuleCount, Is.EqualTo(2));
        }

        [Test]
        public void Renderer_NoneMode_SimulatesButDoesNotInitializeRendering()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.None;
            system.main.maxParticles = 4;
            system.emission.enabled = false;

            system.Emit(2);
            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(system.particleCount, Is.EqualTo(2));
            Assert.That(system.shouldRender, Is.False);
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var stats), Is.True);
            Assert.That(stats.IsInitialized, Is.False);
            Assert.That(stats.LastUploadedCount, Is.EqualTo(0));
        }

        [Test]
        public void Renderer_MeshMode_RequiresMesh_AndInitializesWithCustomMesh()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.main.maxParticles = 4;
            system.emission.enabled = false;

            system.Emit(1);
            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(system.shouldRender, Is.False);
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var missingMeshStats), Is.True);
            Assert.That(missingMeshStats.IsInitialized, Is.False);

            Mesh mesh = CreateTriangleMesh();
            m_ToDestroy.Add(mesh);
            system.rendererModule.meshes = new[] { mesh };
            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(system.shouldRender, Is.True);
            Assert.That(system.rendererModule.meshCount, Is.EqualTo(1));
            Assert.That(system.rendererModule.renderMesh, Is.EqualTo(mesh));
            Assert.That(VividParticleSystemManager.TryGetStats(system, out var meshStats), Is.True);
            Assert.That(meshStats.IsInitialized, Is.True);
            Assert.That(meshStats.LastUploadedCount, Is.EqualTo(1));
        }

        [Test]
        public void Renderer_StretchMode_AlignsMatrixToParticleVelocity()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.rendererModule.stretchLengthScale = 2.0f;
            system.rendererModule.stretchSpeedScale = 0.5f;
            system.main.startSpeed = 2.0f;
            system.main.startSize = 1.0f;
            system.main.maxParticles = 4;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            Matrix4x4 matrix = system.GetParticleObjectToWorldMatrix(0);
            Vector3 stretchedAxis = matrix.GetColumn(1);
            Vector3 thinAxis = matrix.GetColumn(0);

            Assert.That(Vector3.Dot(stretchedAxis.normalized, Vector3.forward), Is.GreaterThan(0.999f));
            Assert.That(stretchedAxis.magnitude, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(thinAxis.magnitude, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Renderer_BillboardVariants_InitializeWithQuadRendering()
        {
            VividParticleRenderMode[] modes =
            {
                VividParticleRenderMode.Billboard,
                VividParticleRenderMode.HorizontalBillboard,
                VividParticleRenderMode.VerticalBillboard,
            };

            for (int index = 0; index < modes.Length; index++)
            {
                VividParticleSystem system = CreateActiveSystem();
                system.rendererModule.enabled = true;
                system.rendererModule.renderMode = modes[index];
                system.main.maxParticles = 4;
                system.emission.enabled = false;

                system.Emit(1);

                Assert.That(system.shouldRender, Is.True);
                Assert.That(VividParticleSystemManager.TryGetStats(system, out var stats), Is.True);
                Assert.That(stats.IsInitialized, Is.True);
                Assert.That(stats.LastUploadedCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Renderer_VisibleInstanceCount_UsesPagesForBillboards_AndParticlesForMesh()
        {
            const int particleCount = 1024;

            Assert.That(VividParticleSystemManager.GetCullingRecordCount(particleCount), Is.EqualTo(4));
            Assert.That(VividParticleSystemManager.GetCullingRecordCount(0), Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(VividParticleRenderMode.Billboard, particleCount),
                Is.EqualTo(4));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(VividParticleRenderMode.Stretch, particleCount),
                Is.EqualTo(4));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(VividParticleRenderMode.HorizontalBillboard, particleCount),
                Is.EqualTo(4));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(VividParticleRenderMode.VerticalBillboard, particleCount),
                Is.EqualTo(4));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(VividParticleRenderMode.Mesh, particleCount),
                Is.EqualTo(1024));
        }

        [Test]
        public void Renderer_CullingRecords_UsePagesForMeshWithoutChangingVisibleInstanceCount()
        {
            VividParticleSystem system = CreateActiveSystem();
            Mesh mesh = CreateTriangleMesh();
            m_ToDestroy.Add(mesh);
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            system.rendererModule.mesh = mesh;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.True);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(4));
            Assert.That(rendererStats.CullingPageBoundsCapacity, Is.EqualTo(4));
            Assert.That(rendererStats.LastCullingRecordBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.RendererRecordRefCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastInvalidRendererRecordRefCount, Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.HasPendingCullingRecordBuildForTests(), Is.False);
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(1024));
            Assert.That(rendererStats.SortingPositionCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.LightDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.LightDrawRangeCount, Is.EqualTo(0));
            Assert.That(rendererStats.LightVisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.PickingDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(1024));
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionDrawRangeCount, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.LastBoundsPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastBoundsRecordWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingSingleMeshCacheRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingMultiMeshCacheRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingMeshFallbackRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingRecordVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingBatchVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(system.rendererModule.renderMode, system.particleCount),
                Is.EqualTo(1024));
        }

        [Test]
        public void Renderer_BoundsJobs_SkipCleanNativeRecords_AndRescheduleTransformChanges()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 512;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(300);

            VividParticleSystemManager.VividParticleRendererManagerStats initialStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(initialStats.LastBoundsPageWorkCount, Is.EqualTo(2));
            Assert.That(initialStats.LastBoundsRecordWorkCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeBoundsCache(
                    system,
                    out int initialBoundsVersion,
                    out int initialCompletedBoundsVersion,
                    out int cachedActiveCount,
                    out bool hasCachedBounds),
                Is.True);
            Assert.That(initialCompletedBoundsVersion, Is.EqualTo(initialBoundsVersion));
            Assert.That(cachedActiveCount, Is.EqualTo(300));
            Assert.That(hasCachedBounds, Is.True);

            VividParticleSystemManager.RunRendererUpdateForTests();
            VividParticleSystemManager.VividParticleRendererManagerStats cleanStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeBoundsCache(
                    system,
                    out int cleanBoundsVersion,
                    out int cleanCompletedBoundsVersion,
                    out cachedActiveCount,
                    out hasCachedBounds),
                Is.True);
            Assert.That(cleanBoundsVersion, Is.EqualTo(initialBoundsVersion));
            Assert.That(cleanCompletedBoundsVersion, Is.EqualTo(cleanBoundsVersion));
            Assert.That(cleanStats.LastBoundsPageWorkCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastBoundsRecordWorkCount, Is.EqualTo(0));

            system.transform.position = new Vector3(4.0f, 5.0f, 6.0f);
            VividParticleSystemManager.RunRendererUpdateForTests();
            VividParticleSystemManager.VividParticleRendererManagerStats movedStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(movedStats.LastBoundsPageWorkCount, Is.EqualTo(2));
            Assert.That(movedStats.LastBoundsRecordWorkCount, Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.TryGetRendererNativeBoundsCache(
                    system,
                    out int movedBoundsVersion,
                    out int movedCompletedBoundsVersion,
                    out cachedActiveCount,
                    out hasCachedBounds),
                Is.True);
            Assert.That(movedBoundsVersion, Is.Not.EqualTo(initialBoundsVersion));
            Assert.That(movedCompletedBoundsVersion, Is.EqualTo(movedBoundsVersion));
            Assert.That(cachedActiveCount, Is.EqualTo(300));
            Assert.That(hasCachedBounds, Is.True);

            Vector3[] centers = VividParticleSystemManager.GetRendererCullingRecordBoundsCentersForTests();
            Assert.That(centers, Has.Length.EqualTo(2));
            for (int index = 0; index < centers.Length; index++)
                AssertVectorApproximately(new Vector3(4.0f, 5.0f, 6.0f), centers[index]);
        }

        [Test]
        public void Renderer_CullingLayout_SplitsPickingAndSelectionCommands()
        {
            VividParticleSystem selectedSystem = CreateActiveSystem();
            selectedSystem.rendererModule.enabled = true;
            selectedSystem.main.maxParticles = 4;
            selectedSystem.main.startLifetime = 10.0f;
            selectedSystem.emission.enabled = false;
            selectedSystem.shape.enabled = false;

            VividParticleSystem unselectedSystem = CreateActiveSystem();
            unselectedSystem.rendererModule.enabled = true;
            unselectedSystem.main.maxParticles = 4;
            unselectedSystem.main.startLifetime = 10.0f;
            unselectedSystem.emission.enabled = false;
            unselectedSystem.shape.enabled = false;

            selectedSystem.Emit(1);
            unselectedSystem.Emit(1);
            Selection.activeGameObject = selectedSystem.gameObject;
            VividParticleSystemManager.RefreshEditorSelectionForTests();
            VividParticleSystemManager.UpdateRendering(selectedSystem);
            VividParticleSystemManager.UpdateRendering(unselectedSystem);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.SortingPositionCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.LightDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.LightDrawRangeCount, Is.EqualTo(0));
            Assert.That(rendererStats.LightVisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.PickingDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.SelectionDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingSingleMeshCacheRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastCullingMultiMeshCacheRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingMeshFallbackRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingRecordVisibleCacheEntryCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingBatchVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(1));
        }

        [Test]
        public void Renderer_EditorSelectionChange_RebuildsSelectionCullingWithoutRecordUpdate()
        {
            VividParticleSystem selectedSystem = CreateActiveSystem();
            selectedSystem.rendererModule.enabled = true;
            selectedSystem.main.maxParticles = 4;
            selectedSystem.main.startLifetime = 10.0f;
            selectedSystem.emission.enabled = false;
            selectedSystem.shape.enabled = false;

            VividParticleSystem unselectedSystem = CreateActiveSystem();
            unselectedSystem.rendererModule.enabled = true;
            unselectedSystem.main.maxParticles = 4;
            unselectedSystem.main.startLifetime = 10.0f;
            unselectedSystem.emission.enabled = false;
            unselectedSystem.shape.enabled = false;

            selectedSystem.Emit(1);
            unselectedSystem.Emit(1);
            VividParticleSystemManager.UpdateRendering(selectedSystem);
            VividParticleSystemManager.UpdateRendering(unselectedSystem);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(0));

            Selection.activeGameObject = selectedSystem.gameObject;
            VividParticleSystemManager.RefreshEditorSelectionForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.SelectionDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(1));

            VividParticleSystemManager.RunRendererUpdateForTests();
            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastDirtyUploadQueueCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadRecordWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastUploadCopyWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCopyByteCount, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4));
            Assert.That(
                VividParticleSystemManager.lastRendererSharedDataFloat4Mask,
                Is.EqualTo(VividParticleSystemManager.SharedDataPickingFloat4Mask));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.SharedData)));

            Selection.activeGameObject = null;
            VividParticleSystemManager.RefreshEditorSelectionForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(0));
        }

        [Test]
        public void Renderer_BatchRendererGroup_UsesParticleUnlitPickingMaterial()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);

            Assert.That(VividParticleSystemManager.HasRendererPickingMaterialForTests(), Is.True);
        }

        [Test]
        public void Renderer_CullingLayout_UsesShadowOnlyLightCommands()
        {
            VividParticleSystem nonShadowSystem = CreateActiveSystem();
            nonShadowSystem.rendererModule.enabled = true;
            nonShadowSystem.rendererModule.shadowCastingMode = ShadowCastingMode.Off;
            nonShadowSystem.main.maxParticles = 4;
            nonShadowSystem.main.startLifetime = 10.0f;
            nonShadowSystem.emission.enabled = false;
            nonShadowSystem.shape.enabled = false;

            VividParticleSystem shadowSystem = CreateActiveSystem();
            shadowSystem.rendererModule.enabled = true;
            shadowSystem.rendererModule.shadowCastingMode = ShadowCastingMode.On;
            shadowSystem.main.maxParticles = 4;
            shadowSystem.main.startLifetime = 10.0f;
            shadowSystem.emission.enabled = false;
            shadowSystem.shape.enabled = false;

            nonShadowSystem.Emit(1);
            shadowSystem.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.SortingPositionCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.LightDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.LightDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.LightVisibleInstanceCapacity, Is.EqualTo(1));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(2));
        }

        [Test]
        public void Renderer_CullingLayout_ShadowOnlyModeSkipsCameraPickingAndSelection()
        {
            VividParticleSystem system = CreateActiveSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1);
            Selection.activeGameObject = system.gameObject;
            VividParticleSystemManager.RefreshEditorSelectionForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(0));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.PickingDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.PickingVisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionDrawCommandCount, Is.EqualTo(0));
            Assert.That(rendererStats.SelectionVisibleInstanceCapacity, Is.EqualTo(0));
            Assert.That(rendererStats.LastPickingDrawBuildWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastBatchDrawBuildWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LightDrawCommandCount, Is.EqualTo(1));
            Assert.That(rendererStats.LightDrawRangeCount, Is.EqualTo(1));
            Assert.That(rendererStats.LightVisibleInstanceCapacity, Is.EqualTo(1));
        }

        [Test]
        public void Renderer_CullingLayout_SplitsSortedAndUnsortedBatches()
        {
            VividParticleSystem unsortedSystem = CreateActiveSystem();
            unsortedSystem.rendererModule.enabled = true;
            unsortedSystem.rendererModule.sortMode = VividParticleSortMode.None;
            unsortedSystem.main.maxParticles = 4;
            unsortedSystem.main.startLifetime = 10.0f;
            unsortedSystem.emission.enabled = false;
            unsortedSystem.shape.enabled = false;

            VividParticleSystem sortedSystem = CreateActiveSystem();
            sortedSystem.rendererModule.enabled = true;
            sortedSystem.rendererModule.sortMode = VividParticleSortMode.ByDistance;
            sortedSystem.main.maxParticles = 4;
            sortedSystem.main.startLifetime = 10.0f;
            sortedSystem.emission.enabled = false;
            sortedSystem.shape.enabled = false;

            unsortedSystem.Emit(1);
            sortedSystem.Emit(1);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(2));
            Assert.That(rendererStats.SortingPositionCapacity, Is.EqualTo(1));
        }

        [Test]
        public void Renderer_CullingLayout_UsesPageSortingForBillboards_AndParticleSortingForMesh()
        {
            VividParticleSystem billboardSystem = CreateActiveSystem();
            billboardSystem.rendererModule.enabled = true;
            billboardSystem.rendererModule.renderMode = VividParticleRenderMode.Billboard;
            billboardSystem.rendererModule.sortMode = VividParticleSortMode.ByDistance;
            billboardSystem.main.maxParticles = 1024;
            billboardSystem.main.startLifetime = 10.0f;
            billboardSystem.emission.enabled = false;
            billboardSystem.shape.enabled = false;

            VividParticleSystem meshSystem = CreateActiveSystem();
            Mesh mesh = CreateTriangleMesh();
            m_ToDestroy.Add(mesh);
            meshSystem.rendererModule.enabled = true;
            meshSystem.rendererModule.renderMode = VividParticleRenderMode.Mesh;
            meshSystem.rendererModule.sortMode = VividParticleSortMode.ByDistance;
            meshSystem.rendererModule.mesh = mesh;
            meshSystem.main.maxParticles = 8;
            meshSystem.main.startLifetime = 10.0f;
            meshSystem.emission.enabled = false;
            meshSystem.shape.enabled = false;

            billboardSystem.Emit(1024);
            meshSystem.Emit(8);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(12));
            Assert.That(rendererStats.SortingPositionCapacity, Is.EqualTo(12));
        }

        [Test]
        public void Renderer_CullingScratchCapacity_GrowsByPowersOfTwo()
        {
            Assert.That(VividParticleSystemManager.ResolvePersistentScratchCapacity(0), Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.ResolvePersistentScratchCapacity(1), Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.ResolvePersistentScratchCapacity(2), Is.EqualTo(2));
            Assert.That(VividParticleSystemManager.ResolvePersistentScratchCapacity(3), Is.EqualTo(4));
            Assert.That(VividParticleSystemManager.ResolvePersistentScratchCapacity(9), Is.EqualTo(16));
        }

        [Test]
        public void Renderer_DrawOutputDefaults_ProvideSortingAndLayerFiltering()
        {
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(4), Is.EqualTo(12));
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(-1), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.RequiresSortingPositionsByDefault(), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.None), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.ByDistance), Is.True);
            Assert.That(VividParticleSystemManager.ShouldRunMeshVisibleCountInline(1), Is.True);
            Assert.That(VividParticleSystemManager.ShouldRunMeshVisibleCountInline(2), Is.False);
            Assert.That(VividParticleSystemManager.ShouldRunEmissionInitializeInline(1), Is.True);
            Assert.That(VividParticleSystemManager.ShouldRunEmissionInitializeInline(2), Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldWriteSortingPositionsForView(BatchCullingViewType.Camera),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldWriteSortingPositionsForView(BatchCullingViewType.Light),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldWriteSortingPositionsForView(BatchCullingViewType.Picking),
                Is.False);
            Assert.That(VividParticleSystemManager.ResolveAllDepthSortedFlag(false), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveAllDepthSortedFlag(true), Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.ResolveRendererPriority(23), Is.EqualTo(23));
            Assert.That(VividParticleSystemManager.ResolveBatchLayer(-1), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveBatchLayer(9), Is.EqualTo(9));
            Assert.That(VividParticleSystemManager.ResolveBatchLayer(99), Is.EqualTo(31));
            Assert.That(VividParticleSystemManager.IsLayerVisibleInCullingMask(1u << 5, 5), Is.True);
            Assert.That(VividParticleSystemManager.IsLayerVisibleInCullingMask(1u << 5, 4), Is.False);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.Picking), Is.True);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.SelectionOutline), Is.True);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.Camera), Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldWritePickingEntityIdsForView(BatchCullingViewType.Picking),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldWritePickingEntityIdsForView(BatchCullingViewType.SelectionOutline),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldWritePickingEntityIdsForView(BatchCullingViewType.Camera),
                Is.False);
            Assert.That(
                VividParticleSystemManager.DoesPickingEntityPassFilterForTests(
                    10,
                    includeEnabled: false,
                    includeRenderers: System.Array.Empty<ulong>(),
                    includeEntities: System.Array.Empty<ulong>(),
                    excludeRenderers: System.Array.Empty<ulong>(),
                    excludeEntities: System.Array.Empty<ulong>()),
                Is.True);
            Assert.That(
                VividParticleSystemManager.DoesPickingEntityPassFilterForTests(
                    10,
                    includeEnabled: true,
                    includeRenderers: new ulong[] { 10 },
                    includeEntities: System.Array.Empty<ulong>(),
                    excludeRenderers: System.Array.Empty<ulong>(),
                    excludeEntities: System.Array.Empty<ulong>()),
                Is.True);
            Assert.That(
                VividParticleSystemManager.DoesPickingEntityPassFilterForTests(
                    10,
                    includeEnabled: true,
                    includeRenderers: System.Array.Empty<ulong>(),
                    includeEntities: System.Array.Empty<ulong>(),
                    excludeRenderers: System.Array.Empty<ulong>(),
                    excludeEntities: System.Array.Empty<ulong>()),
                Is.False);
            Assert.That(
                VividParticleSystemManager.DoesPickingEntityPassFilterForTests(
                    10,
                    includeEnabled: false,
                    includeRenderers: System.Array.Empty<ulong>(),
                    includeEntities: System.Array.Empty<ulong>(),
                    excludeRenderers: new ulong[] { 10 },
                    excludeEntities: System.Array.Empty<ulong>()),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(ShadowCastingMode.Off, BatchCullingViewType.Light),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(ShadowCastingMode.On, BatchCullingViewType.Light),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(
                    ShadowCastingMode.ShadowsOnly,
                    BatchCullingViewType.Light),
                Is.True);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(
                    ShadowCastingMode.ShadowsOnly,
                    BatchCullingViewType.Camera),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(
                    ShadowCastingMode.ShadowsOnly,
                    BatchCullingViewType.Picking),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(
                    ShadowCastingMode.ShadowsOnly,
                    BatchCullingViewType.SelectionOutline),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Camera, 0b0011, 4),
                Is.EqualTo(0xff));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Light, 0b0101, 4),
                Is.EqualTo(0b0101));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Light, 0, 4),
                Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(
                    BatchCullingViewType.Light,
                    splitVisibilityMask: 0b1111,
                    splitCount: 2,
                    splitExclusionMask: 0),
                Is.EqualTo(0b0011));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(
                    BatchCullingViewType.Light,
                    splitVisibilityMask: 0b1111,
                    splitCount: 4,
                    splitExclusionMask: 0b0101),
                Is.EqualTo(0b1010));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(
                    BatchCullingViewType.Light,
                    splitVisibilityMask: 0b0011,
                    splitCount: 2,
                    splitExclusionMask: 0b0011),
                Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.IsBackfacingReceiverPlaneForLight(
                    new float4(0.0f, 0.0f, -1.0f, 0.0f),
                    isOrthographic: true,
                    lightDirection: new float3(0.0f, 0.0f, 1.0f),
                    lightPosition: float3.zero),
                Is.True);
            Assert.That(
                VividParticleSystemManager.IsBackfacingReceiverPlaneForLight(
                    new float4(0.0f, 0.0f, 1.0f, 0.0f),
                    isOrthographic: true,
                    lightDirection: new float3(0.0f, 0.0f, 1.0f),
                    lightPosition: float3.zero),
                Is.False);
            Assert.That(
                VividParticleSystemManager.IsBackfacingReceiverPlaneForLight(
                    new float4(0.0f, 0.0f, 1.0f, -5.0f),
                    isOrthographic: false,
                    lightDirection: float3.zero,
                    lightPosition: new float3(0.0f, 0.0f, 10.0f)),
                Is.True);
            Assert.That(
                VividParticleSystemManager.IsBackfacingReceiverPlaneForLight(
                    new float4(0.0f, 0.0f, 1.0f, -5.0f),
                    isOrthographic: false,
                    lightDirection: float3.zero,
                    lightPosition: new float3(0.0f, 0.0f, 1.0f)),
                Is.False);

            float4x4 localToWorld = float4x4.TRS(
                new float3(10.0f, 20.0f, 30.0f),
                quaternion.identity,
                new float3(2.0f, 3.0f, 4.0f));
            float3 localPosition = new(1.0f, 2.0f, 3.0f);
            float3 localResolved = VividParticleSystemManager.ResolveParticleSortingPosition(
                localPosition,
                localToWorld,
                (int)VividParticleSystemSimulationSpace.Local);
            Assert.That(localResolved.x, Is.EqualTo(12.0f).Within(0.0001f));
            Assert.That(localResolved.y, Is.EqualTo(26.0f).Within(0.0001f));
            Assert.That(localResolved.z, Is.EqualTo(42.0f).Within(0.0001f));

            float3 worldResolved = VividParticleSystemManager.ResolveParticleSortingPosition(
                localPosition,
                localToWorld,
                (int)VividParticleSystemSimulationSpace.World);
            Assert.That(worldResolved.x, Is.EqualTo(localPosition.x).Within(0.0001f));
            Assert.That(worldResolved.y, Is.EqualTo(localPosition.y).Within(0.0001f));
            Assert.That(worldResolved.z, Is.EqualTo(localPosition.z).Within(0.0001f));

            float3 pageBoundsCenter = new(100.0f, 200.0f, 300.0f);
            float3 pageLocalResolved = VividParticleSystemManager.ResolvePageSortingPosition(pageBoundsCenter);
            Assert.That(pageLocalResolved.x, Is.EqualTo(pageBoundsCenter.x).Within(0.0001f));
            Assert.That(pageLocalResolved.y, Is.EqualTo(pageBoundsCenter.y).Within(0.0001f));
            Assert.That(pageLocalResolved.z, Is.EqualTo(pageBoundsCenter.z).Within(0.0001f));

            float3 secondPageBoundsCenter = new(-10.0f, -20.0f, -30.0f);
            float3 secondPageResolved = VividParticleSystemManager.ResolvePageSortingPosition(secondPageBoundsCenter);
            Assert.That(secondPageResolved.x, Is.EqualTo(secondPageBoundsCenter.x).Within(0.0001f));
            Assert.That(secondPageResolved.y, Is.EqualTo(secondPageBoundsCenter.y).Within(0.0001f));
            Assert.That(secondPageResolved.z, Is.EqualTo(secondPageBoundsCenter.z).Within(0.0001f));

            float4x4 rotatedScaledLocalToWorld = float4x4.TRS(
                float3.zero,
                quaternion.RotateY(math.radians(90.0f)),
                new float3(2.0f, 3.0f, 4.0f));
            float4 localSharedVelocity = VividParticleSystemManager.ResolveSharedVelocityStretchData(
                rotatedScaledLocalToWorld,
                VividParticleSystemSimulationSpace.Local,
                startSpeed: 3.0f,
                startSize: 2.0f);
            Assert.That(localSharedVelocity.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(localSharedVelocity.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(localSharedVelocity.z, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(localSharedVelocity.w, Is.EqualTo(2.0f).Within(0.0001f));

            float3 expectedWorldDirection = math.normalizesafe(
                rotatedScaledLocalToWorld.c2.xyz,
                new float3(0.0f, 0.0f, 1.0f));
            float4 worldSharedVelocity = VividParticleSystemManager.ResolveSharedVelocityStretchData(
                rotatedScaledLocalToWorld,
                VividParticleSystemSimulationSpace.World,
                startSpeed: 3.0f,
                startSize: 0.0f);
            Assert.That(worldSharedVelocity.x, Is.EqualTo(expectedWorldDirection.x * 3.0f).Within(0.0001f));
            Assert.That(worldSharedVelocity.y, Is.EqualTo(expectedWorldDirection.y * 3.0f).Within(0.0001f));
            Assert.That(worldSharedVelocity.z, Is.EqualTo(expectedWorldDirection.z * 3.0f).Within(0.0001f));
            Assert.That(worldSharedVelocity.w, Is.EqualTo(VividParticleMainModule.MinimumStartSize).Within(0.0001f));

            BatchDrawCommandFlags flags = VividParticleSystemManager.ResolveParticleDrawCommandFlags(
                hasSortingPosition: true,
                hasMotion: false);
            Assert.That((flags & BatchDrawCommandFlags.HasSortingPosition) != 0, Is.True);
            Assert.That((flags & BatchDrawCommandFlags.HasMotion) == 0, Is.True);

            flags = VividParticleSystemManager.ResolveParticleDrawCommandFlags(
                hasSortingPosition: false,
                hasMotion: false);
            Assert.That((flags & BatchDrawCommandFlags.HasSortingPosition) == 0, Is.True);

            flags = VividParticleSystemManager.ResolveParticleDrawCommandFlags(
                hasSortingPosition: true,
                hasMotion: true);
            Assert.That((flags & BatchDrawCommandFlags.HasMotion) != 0, Is.True);

            Assert.That(
                VividParticleSystemManager.HasParticleMotion(MotionVectorGenerationMode.ForceNoMotion),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasParticleMotion(MotionVectorGenerationMode.Camera),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasParticleMotion(MotionVectorGenerationMode.Object),
                Is.True);
            Assert.That(VividParticleSystemManager.UsesParticleMotionVectorPass(), Is.True);
            Assert.That(
                VividParticleSystemManager.ResolveSupportedParticleMotionMode(
                    MotionVectorGenerationMode.ForceNoMotion),
                Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
            Assert.That(
                VividParticleSystemManager.ResolveSupportedParticleMotionMode(
                    MotionVectorGenerationMode.Camera),
                Is.EqualTo(MotionVectorGenerationMode.Camera));
            Assert.That(
                VividParticleSystemManager.ResolveSupportedParticleMotionMode(
                    MotionVectorGenerationMode.Object),
                Is.EqualTo(MotionVectorGenerationMode.Camera));
        }

        [Test]
        public void Manager_UpdateRendering_WithNoParticlesKeepsDrawCountEmpty()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.main.maxParticles = 4;

            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var stats), Is.True);
            Assert.That(stats.IsInitialized, Is.True);
            Assert.That(stats.LastUploadedCount, Is.EqualTo(0));
            Assert.That(stats.LastDrawCommandCount, Is.EqualTo(0));
        }

        [Test]
        public void Manager_EnableDisable_CanRepeatWithoutThrowing()
        {
            var gameObject = new GameObject("Vivid Particle System Test");
            m_ToDestroy.Add(gameObject);

            Assert.DoesNotThrow(() =>
            {
                var system = gameObject.AddComponent<VividParticleSystem>();
                system.rendererModule.enabled = false;
                gameObject.SetActive(false);
                gameObject.SetActive(true);
                gameObject.SetActive(false);
            });
        }

        [Test]
        public void Shader_FindReturnsDefaultParticleShader()
        {
            Assert.That(Shader.Find(VividParticleSystemManager.DefaultShaderName), Is.Not.Null);
        }

        [Test]
        public void CaptureFrameSnapshot_ReusesBurstBuffer_WhenBurstCountUnchanged()
        {
            VividParticleSystem system = CreateSystem();
            system.emission.bursts = new[]
            {
                new VividParticleBurst(0.1f, 1),
                new VividParticleBurst(0.2f, 2),
            };

            VividParticleBurst[] buffer = null;
            VividParticleSystemFrameSnapshot firstSnapshot = system.CaptureFrameSnapshot(0.0f, ref buffer);
            VividParticleBurst[] firstBuffer = buffer;

            Assert.That(firstSnapshot.Bursts, Is.SameAs(firstBuffer));
            Assert.That(firstBuffer, Is.Not.Null);

            system.emission.bursts[0] = new VividParticleBurst(0.3f, 3);
            VividParticleSystemFrameSnapshot secondSnapshot = system.CaptureFrameSnapshot(0.0f, ref buffer);

            Assert.That(buffer, Is.SameAs(firstBuffer));
            Assert.That(secondSnapshot.Bursts, Is.SameAs(firstBuffer));
            Assert.That(secondSnapshot.Bursts[0].time, Is.EqualTo(0.3f));
            Assert.That(secondSnapshot.Bursts[0].count, Is.EqualTo(3));
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
            var gameObject = new GameObject("Vivid Particle System Test");
            gameObject.SetActive(false);
            m_ToDestroy.Add(gameObject);

            VividParticleSystem system = gameObject.AddComponent<VividParticleSystem>();
            system.rendererModule.enabled = false;
            system.main.useAutoRandomSeed = false;
            return system;
        }

        private static void ConfigureNoiseSystem(VividParticleSystem system)
        {
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.main.useAutoRandomSeed = false;
            system.main.randomSeed = 123u;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.noise.enabled = true;
            system.noise.strength = AnimationCurve.Constant(0.0f, 1.0f, 5.0f);
            system.noise.frequency = 1.0f;
            system.noise.damping = false;
            system.noise.octaveCount = 2;
            system.noise.octaveMultiplier = 0.5f;
            system.noise.octaveScale = 2.0f;
            system.noise.positionAmount = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            system.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 90.0f);
            system.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.75f);
        }

        private static void DisableNoiseRenderEffects(VividParticleSystem system)
        {
            system.noise.rotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            system.noise.sizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
        }

        private static void ConfigureInheritVelocitySystem(
            VividParticleSystem system,
            VividParticleInheritVelocityMode mode)
        {
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.main.emitterVelocityMode = VividParticleEmitterVelocityMode.Custom;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.inheritVelocity.enabled = true;
            system.inheritVelocity.mode = mode;
            system.inheritVelocity.curve = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        }

        private static void ConfigureExternalForcesSystem(VividParticleSystem system)
        {
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 0.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.externalForces.enabled = true;
            system.externalForces.multiplier = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        }

        private static void ConfigurePlaneCollisionSystem(
            VividParticleSystem system,
            Transform plane)
        {
            system.transform.position = Vector3.up;
            system.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 1.0f;
            system.main.startSize = 1.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.collision.enabled = true;
            system.collision.type = VividParticleCollisionType.Planes;
            system.collision.radiusScale = 1.0f;
            system.collision.dampen = 0.0f;
            system.collision.bounce = 1.0f;
            system.collision.AddPlane(plane);
        }

        private static void ConfigureWorldCollisionSystem(
            VividParticleSystem system,
            LayerMask collidesWith)
        {
            system.transform.position = Vector3.up * 2.0f;
            system.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 1.0f;
            system.main.startSize = 1.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.collision.enabled = true;
            system.collision.type = VividParticleCollisionType.World;
            system.collision.collidesWith = collidesWith;
            system.collision.radiusScale = 1.0f;
            system.collision.dampen = 0.0f;
            system.collision.bounce = 1.0f;
            system.collision.sendCollisionMessages = true;
        }

        private static void ConfigureTriggerSystem(
            VividParticleSystem system,
            Collider collider)
        {
            system.transform.position = Vector3.up * 2.0f;
            system.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            system.main.maxParticles = 4;
            system.main.startLifetime = 10.0f;
            system.main.startSpeed = 1.0f;
            system.main.startSize = 1.0f;
            system.main.gravityModifier = 0.0f;
            system.main.simulationSpace = VividParticleSystemSimulationSpace.World;
            system.emission.enabled = false;
            system.shape.enabled = false;
            system.trigger.enabled = true;
            system.trigger.radiusScale = 1.0f;
            system.trigger.colliderQueryMode = VividParticleColliderQueryMode.One;
            system.trigger.AddCollider(collider);
        }

        private Transform CreateCollisionPlane()
        {
            var gameObject = new GameObject("Vivid Particle Collision Plane");
            m_ToDestroy.Add(gameObject);
            return gameObject.transform;
        }

        private Collider CreateWorldCollider(int shape, int layer)
        {
            var gameObject = new GameObject("Vivid Particle World Collider");
            gameObject.layer = layer;
            m_ToDestroy.Add(gameObject);
            return shape switch
            {
                1 => CreateBoxCollider(gameObject),
                2 => CreateCapsuleCollider(gameObject),
                _ => CreateSphereCollider(gameObject),
            };
        }

        private static SphereCollider CreateSphereCollider(GameObject gameObject)
        {
            SphereCollider collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = 1.0f;
            return collider;
        }

        private static BoxCollider CreateBoxCollider(GameObject gameObject)
        {
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 2.0f;
            return collider;
        }

        private static CapsuleCollider CreateCapsuleCollider(GameObject gameObject)
        {
            CapsuleCollider collider = gameObject.AddComponent<CapsuleCollider>();
            collider.radius = 1.0f;
            collider.height = 2.0f;
            return collider;
        }

        private VividParticleForceField CreateForceField()
        {
            var gameObject = new GameObject("Vivid Particle Force Field Test");
            m_ToDestroy.Add(gameObject);
            VividParticleForceField field = gameObject.AddComponent<VividParticleForceField>();
            field.shape = VividParticleForceFieldShape.Sphere;
            field.startRange = 0.0f;
            field.endRange = 1.0f;
            return field;
        }

        private WindZone CreateWindZone()
        {
            var gameObject = new GameObject("Vivid Particle Wind Zone Test");
            m_ToDestroy.Add(gameObject);
            WindZone windZone = gameObject.AddComponent<WindZone>();
            windZone.windMain = 1.0f;
            windZone.windPulseMagnitude = 0.0f;
            windZone.windPulseFrequency = 0.0f;
            return windZone;
        }

        private VividParticleSystem CreateActiveSystem()
        {
            var gameObject = new GameObject("Vivid Particle System Test");
            m_ToDestroy.Add(gameObject);

            VividParticleSystem system = gameObject.AddComponent<VividParticleSystem>();
            system.rendererModule.enabled = false;
            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);
            system.main.useAutoRandomSeed = false;
            return system;
        }

        private Material CreateParticleMaterial(string name)
        {
            Shader shader = Shader.Find(VividParticleSystemManager.DefaultShaderName);
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            m_ToDestroy.Add(material);
            return material;
        }

        private static Mesh CreateTriangleMesh()
        {
            var mesh = new Mesh
            {
                name = "Vivid Particle Test Triangle",
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3(0.0f, 0.5f, 0.0f),
                new Vector3(0.5f, -0.5f, 0.0f),
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(0.5f, 1.0f),
                new Vector2(1.0f, 0.0f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int UploadBillboardParticlesAndGetCopyByteCount(VividParticleGpuDataMode colorDataMode)
        {
            VividParticleSystemManager.ClearForTests();
            var gameObject = new GameObject("Vivid Particle Upload Budget Test");
            try
            {
                VividParticleSystem system = gameObject.AddComponent<VividParticleSystem>();
                system.rendererModule.enabled = true;
                system.rendererModule.colorDataMode = colorDataMode;
                system.main.maxParticles = 1024;
                system.main.startLifetime = 10.0f;
                system.emission.enabled = false;
                system.shape.enabled = false;

                system.Emit(1024);

                return VividParticleSystemManager.GetRendererStatsForTests().LastCopyByteCount;
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                VividParticleSystemManager.ClearForTests();
            }
        }

        private static Plane[] CreateUnitCubePlanes()
        {
            return new[]
            {
                new Plane(Vector3.right, new Vector3(-1.0f, 0.0f, 0.0f)),
                new Plane(Vector3.left, new Vector3(1.0f, 0.0f, 0.0f)),
                new Plane(Vector3.up, new Vector3(0.0f, -1.0f, 0.0f)),
                new Plane(Vector3.down, new Vector3(0.0f, 1.0f, 0.0f)),
                new Plane(Vector3.forward, new Vector3(0.0f, 0.0f, -1.0f)),
                new Plane(Vector3.back, new Vector3(0.0f, 0.0f, 1.0f)),
            };
        }

        private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
