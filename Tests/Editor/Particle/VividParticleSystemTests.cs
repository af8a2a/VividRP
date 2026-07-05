using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.Particle;

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

            Assert.That(emission.enabled, Is.True);
            Assert.That(emission.rateOverTime, Is.EqualTo(10.0f));
            Assert.That(emission.bursts, Is.Empty);

            Assert.That(shape.enabled, Is.True);
            Assert.That(shape.shapeType, Is.EqualTo(VividParticleShapeType.Point));
            Assert.That(shape.radius, Is.EqualTo(1.0f));
            Assert.That(shape.boxSize, Is.EqualTo(Vector3.one));
            Assert.That(shape.angle, Is.EqualTo(25.0f));

            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.renderMode, Is.EqualTo(VividParticleRenderMode.Billboard));
            Assert.That(renderer.material, Is.Null);
            Assert.That(renderer.mesh, Is.Null);
            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(renderer.sizeScale, Is.EqualTo(1.0f));
            Assert.That(renderer.stretchLengthScale, Is.EqualTo(2.0f));
            Assert.That(renderer.stretchSpeedScale, Is.EqualTo(0.0f));
            Assert.That(renderer.renderQueueOffset, Is.EqualTo(0));
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            Assert.That(renderer.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.PerParticle));
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
        }

        [Test]
        public void Asset_AssignmentCopiesModules_WithoutSharingRuntimeState()
        {
            VividParticleSystemAsset asset = ScriptableObject.CreateInstance<VividParticleSystemAsset>();
            m_ToDestroy.Add(asset);
            asset.main.startLifetime = 2.5f;
            asset.main.maxParticles = 7;
            asset.emission.rateOverTime = 3.0f;
            asset.emission.bursts = new[] { new VividParticleBurst(0.25f, 2) };
            asset.shape.shapeType = VividParticleShapeType.Sphere;
            asset.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            asset.rendererModule.color = Color.cyan;
            asset.rendererModule.stretchSpeedScale = 0.5f;
            asset.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            asset.rendererModule.customData1Enabled = true;
            asset.rendererModule.sortMode = VividParticleSortMode.ByDistance;

            VividParticleSystem system = CreateActiveSystem();
            system.asset = asset;

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.main.maxParticles, Is.EqualTo(7));
            Assert.That(system.emission.rateOverTime, Is.EqualTo(3.0f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.shape.shapeType, Is.EqualTo(VividParticleShapeType.Sphere));
            Assert.That(system.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
            Assert.That(system.rendererModule.stretchSpeedScale, Is.EqualTo(0.5f));
            Assert.That(system.rendererModule.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(system.rendererModule.customData1Enabled, Is.True);
            Assert.That(system.rendererModule.sortMode, Is.EqualTo(VividParticleSortMode.ByDistance));

            asset.main.startLifetime = 9.0f;
            asset.emission.bursts[0] = new VividParticleBurst(0.25f, 9);
            asset.rendererModule.renderMode = VividParticleRenderMode.Billboard;
            asset.rendererModule.color = Color.red;
            asset.rendererModule.stretchSpeedScale = 3.0f;
            asset.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            asset.rendererModule.customData1Enabled = false;
            asset.rendererModule.sortMode = VividParticleSortMode.None;

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.rendererModule.renderMode, Is.EqualTo(VividParticleRenderMode.Stretch));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
            Assert.That(system.rendererModule.stretchSpeedScale, Is.EqualTo(0.5f));
            Assert.That(system.rendererModule.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(system.rendererModule.customData1Enabled, Is.True);
            Assert.That(system.rendererModule.sortMode, Is.EqualTo(VividParticleSortMode.ByDistance));
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

            VividParticleSystemManager.CompleteAndUploadForTests();

            Assert.That(VividParticleSystemManager.TryGetStats(system, out var completedStats), Is.True);
            Assert.That(completedStats.PendingJobCount, Is.EqualTo(0));
            Assert.That(completedStats.CompletedJobCount, Is.EqualTo(1));
            Assert.That(completedStats.LastCompletedFrame, Is.GreaterThanOrEqualTo(0));

            Vector3 position = system.GetParticleObjectToWorldMatrix(0).GetColumn(3);
            Assert.That(position.y, Is.EqualTo(-2.4525f).Within(0.0001f));
        }

        [Test]
        public void Manager_RegistersParticleSimulationAndRenderJobs_InEcsRegistry()
        {
            Assert.That(VividParticleSystemManager.registeredSimulationJobCount, Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.registeredRenderJobCount, Is.EqualTo(2));

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
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
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
            system.transform.position = new Vector3(4.0f, 5.0f, 6.0f);

            Bounds bounds = system.worldBounds;

            AssertVectorApproximately(new Vector3(4.0f, 5.0f, 6.0f), bounds.center);
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
            MetadataValue metadata = VividParticleSystemManager.CreatePerInstanceMetadata(123, billboardInfos[2].ByteOffset);
            MetadataValue sharedMetadata = VividParticleSystemManager.CreateSharedMetadata(456, billboardInfos[5].ByteOffset);

            uint expectedDefaultPerSharpBits =
                (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.Scale);
            Assert.That(billboardLayout.Count, Is.EqualTo(7));
            Assert.That(billboardLayout.DataPerSharpBits, Is.EqualTo(expectedDefaultPerSharpBits));
            Assert.That(billboardInfos[0].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SharedData));
            Assert.That(billboardInfos[0].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[1].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData));
            Assert.That(billboardInfos[1].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.Span));
            Assert.That(billboardInfos[1].DataInfo.UsesInstanceMetadata, Is.True);
            Assert.That(billboardInfos[2].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.PositionSize));
            Assert.That(billboardInfos[2].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(billboardInfos[3].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor));
            Assert.That(billboardInfos[3].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(billboardInfos[4].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Scale));
            Assert.That(billboardInfos[4].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[5].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(billboardInfos[5].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.Shared));
            Assert.That(billboardInfos[6].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch));
            Assert.That(billboardInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.Shared));

            Assert.That(billboardInfos[0].ByteOffset, Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize));
            Assert.That(billboardInfos[1].ByteOffset, Is.EqualTo(billboardInfos[0].ByteOffset + VividParticleSystemManager.SharedDataByteSize));
            Assert.That(billboardInfos[2].ByteOffset, Is.EqualTo(billboardInfos[1].ByteOffset + VividParticleSystemManager.SpanSharedDataByteSize));
            Assert.That(billboardInfos[3].ByteOffset, Is.EqualTo(billboardInfos[2].ByteOffset + capacity * VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[4].ByteOffset, Is.EqualTo(billboardInfos[3].ByteOffset + capacity * VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[5].ByteOffset, Is.EqualTo(billboardInfos[4].ByteOffset + VividParticleSystemManager.SizeOfFloat4));
            Assert.That(billboardInfos[6].ByteOffset, Is.EqualTo(billboardInfos[5].ByteOffset + VividParticleSystemManager.SizeOfFloat4));
            Assert.That(
                billboardLayout.CalculateByteSize(capacity, sharpCapacity: 1, spanCapacity: 1),
                Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize
                    + VividParticleSystemManager.SharedDataByteSize
                    + VividParticleSystemManager.SpanSharedDataByteSize
                    + capacity * VividParticleSystemManager.SizeOfFloat4 * 2
                    + VividParticleSystemManager.SizeOfFloat4 * 3));
            Assert.That(stretchInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(
                stretchLayout.CalculateByteSize(capacity, sharpCapacity: 1, spanCapacity: 1),
                Is.EqualTo(VividParticleSystemManager.ZeroBlockByteSize
                    + VividParticleSystemManager.SharedDataByteSize
                    + VividParticleSystemManager.SpanSharedDataByteSize
                    + capacity * VividParticleSystemManager.SizeOfFloat4 * 3
                    + VividParticleSystemManager.SizeOfFloat4 * 2));
            for (int index = 0; index < billboardInfos.Length; index++)
                Assert.That(billboardInfos[index].ByteOffset % 16, Is.EqualTo(0));

            for (int index = 0; index < stretchInfos.Length; index++)
                Assert.That(stretchInfos[index].ByteOffset % 16, Is.EqualTo(0));

            Assert.That((metadata.Value & VividParticleSystemManager.PerInstanceMetadataMask) != 0u, Is.True);
            Assert.That(metadata.Value & ~VividParticleSystemManager.PerInstanceMetadataMask, Is.EqualTo((uint)billboardInfos[2].ByteOffset));
            Assert.That((sharedMetadata.Value & VividParticleSystemManager.PerInstanceMetadataMask) == 0u, Is.True);
            Assert.That(sharedMetadata.Value, Is.EqualTo((uint)billboardInfos[4].ByteOffset));
            Assert.That(VividParticleSystemManager.UsesPerInstanceRotationData(VividParticleRenderMode.Mesh), Is.False);
            Assert.That(VividParticleSystemManager.UsesPerInstanceVelocityStretchData(VividParticleRenderMode.Billboard), Is.False);
            Assert.That(VividParticleSystemManager.UsesPerInstanceVelocityStretchData(VividParticleRenderMode.Stretch), Is.True);
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
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Scale, out var scaleInfo), Is.True);
            Assert.That(scaleInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.UV, out var uvInfo), Is.True);
            Assert.That(uvInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData1, out _), Is.True);
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData2, out _), Is.True);
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex, out _), Is.True);
            Assert.That(
                layout.DataPerSharpBits,
                Is.EqualTo((1u << (int)VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.BaseColor)));
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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastLockCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_RendererUpload_IsCompletedAfterSchedule_WhenStatsAreRead()
        {
            VividParticleSystem system = CreateSystem();
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
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastCopyOperationCount,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Billboard).Count + 1));
        }

        [Test]
        public void Manager_Upload_KeepsVelocityStretchColumn_ForStretchRenderMode()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.renderMode = VividParticleRenderMode.Stretch;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.EqualTo(4));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastCopyOperationCount,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataLayout.Create(VividParticleRenderMode.Stretch).Count + 1));
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
            system.rendererModule.mesh = mesh;
            VividParticleSystemManager.UpdateRendering(system);

            Assert.That(system.shouldRender, Is.True);
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
        public void Renderer_DrawOutputDefaults_ProvideSortingAndLayerFiltering()
        {
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(4), Is.EqualTo(12));
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(-1), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.RequiresSortingPositionsByDefault(), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.None), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.ByDistance), Is.True);
            Assert.That(VividParticleSystemManager.IsLayerVisibleInCullingMask(1u << 5, 5), Is.True);
            Assert.That(VividParticleSystemManager.IsLayerVisibleInCullingMask(1u << 5, 4), Is.False);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.Picking), Is.True);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.SelectionOutline), Is.True);
            Assert.That(VividParticleSystemManager.IsPickingOrSelectionView(BatchCullingViewType.Camera), Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(ShadowCastingMode.Off, BatchCullingViewType.Light),
                Is.False);
            Assert.That(
                VividParticleSystemManager.ShouldRenderBatchForView(ShadowCastingMode.On, BatchCullingViewType.Light),
                Is.True);

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
