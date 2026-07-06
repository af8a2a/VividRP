using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
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
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
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
            asset.rendererModule.renderingLayerMask = 0x8u;
            asset.rendererModule.SetMeshes(new[] { primaryMesh, extraMesh });

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
            Assert.That(system.rendererModule.pivot, Is.EqualTo(new Vector3(0.25f, 0.5f, -0.25f)));
            Assert.That(system.rendererModule.minParticleSize, Is.EqualTo(0.1f));
            Assert.That(system.rendererModule.maxParticleSize, Is.EqualTo(2.0f));
            Assert.That(system.rendererModule.flip, Is.EqualTo(new Vector3(1.0f, 0.5f, 0.0f)));
            Assert.That(system.rendererModule.colorDataMode, Is.EqualTo(VividParticleGpuDataMode.Shared));
            Assert.That(system.rendererModule.customData1Enabled, Is.True);
            Assert.That(system.rendererModule.sortMode, Is.EqualTo(VividParticleSortMode.ByDistance));
            Assert.That(system.rendererModule.renderingLayerMask, Is.EqualTo(0x8u));
            Assert.That(system.rendererModule.meshCount, Is.EqualTo(2));
            Assert.That(system.rendererModule.renderMesh, Is.EqualTo(primaryMesh));
            var copiedMeshes = new Mesh[2];
            Assert.That(system.rendererModule.GetMeshes(copiedMeshes), Is.EqualTo(2));
            Assert.That(copiedMeshes[0], Is.EqualTo(primaryMesh));
            Assert.That(copiedMeshes[1], Is.EqualTo(extraMesh));

            asset.main.startLifetime = 9.0f;
            asset.emission.bursts[0] = new VividParticleBurst(0.25f, 9);
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
            asset.rendererModule.renderingLayerMask = 0x10u;
            asset.rendererModule.SetMeshes(System.Array.Empty<Mesh>());

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
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
            Assert.That(VividParticleSystemManager.registeredRenderJobCount, Is.EqualTo(5));

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
                Is.EqualTo((int)VividParticleSystemManager.RenderJobExtraDataUploadFlag));
            Assert.That(
                VividParticleSystemManager.ResolveRenderJobModuleFlagsForUploadColumnMaskForTests(
                    VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnBaseColorMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask),
                Is.EqualTo((int)VividParticleSystemManager.RenderJobAllPageUploadFlags));
            Assert.That(VividParticleSystemManager.CountRenderPageJobModulesForTests(0u), Is.EqualTo(0));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobTransformUploadFlag),
                Is.EqualTo(1));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobAllPageUploadFlags),
                Is.EqualTo(4));
            Assert.That(
                VividParticleSystemManager.CountRenderPageJobModulesForTests(
                    VividParticleSystemManager.RenderJobAllPageUploadFlags
                    | VividParticleSystemManager.RenderJobSharedDataFlag),
                Is.EqualTo(4));
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
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch);
            Assert.That(billboardLayout.Count, Is.EqualTo(7));
            Assert.That(billboardLayout.DataPerSharpBits, Is.EqualTo(expectedDefaultPerSharpBits));
            Assert.That(billboardLayout.PerInstanceElementByteSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4));
            Assert.That(
                billboardLayout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(billboardInfos[0].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SharedData));
            Assert.That(billboardInfos[0].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[1].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData));
            Assert.That(billboardInfos[1].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.Span));
            Assert.That(billboardInfos[1].DataInfo.UsesInstanceMetadata, Is.True);
            Assert.That(billboardInfos[2].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.PositionSize));
            Assert.That(billboardInfos[2].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(billboardInfos[3].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.BaseColor));
            Assert.That(billboardInfos[3].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[4].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Scale));
            Assert.That(billboardInfos[4].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[5].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(billboardInfos[5].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[6].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch));
            Assert.That(billboardInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
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
                billboardInfos[5].CopyDescriptor.DataId,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(
                billboardInfos[5].CopyDescriptor.Frequency,
                Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(
                billboardInfos[5].CopyDescriptor.ColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnRotationMask));
            Assert.That(
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize),
                Is.EqualTo(1u << (int)VividParticleSystemManager.VividParticleGpuDataId.PositionSize));

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
                    + capacity * VividParticleSystemManager.SizeOfFloat4 * 2
                    + VividParticleSystemManager.SizeOfFloat4 * 3));
            Assert.That(stretchLayout.PerInstanceElementByteSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 2));
            Assert.That(
                stretchLayout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask));
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
            Assert.That(
                layout.PerInstanceElementByteSize,
                Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 8));
            Assert.That(
                layout.PerInstanceUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask
                    | VividParticleSystemManager.UploadColumnRotationMask
                    | VividParticleSystemManager.UploadColumnVelocityStretchMask
                    | VividParticleSystemManager.UploadColumnScaleMask
                    | VividParticleSystemManager.UploadColumnUVMask
                    | VividParticleSystemManager.UploadColumnCustomData1Mask
                    | VividParticleSystemManager.UploadColumnCustomData2Mask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
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
                VividParticleSystemManager.HasAnyVisibleCommandLayer(1u << 4, 1u << 4),
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandLayer(1u << 4, (1u << 5) | (1u << 6)),
                Is.False);
            Assert.That(
                VividParticleSystemManager.HasAnyVisibleCommandLayer(0u, 0u),
                Is.True);
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
                    layer: 2,
                    recordCount: 1,
                    maxVisibleCount: 1,
                    ShadowCastingMode.Off,
                    BatchCullingViewType.Light),
                Is.False);
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
        public void Manager_CopyOperations_UseDataInfoFrequencyAndDirtyMasks()
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

            uint scaleBit = VividParticleSystemManager.GetGpuDataBit(
                VividParticleSystemManager.VividParticleGpuDataId.Scale);
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
                    perSharpScale,
                    hasInstanceRange: false,
                    hasSpanData: false,
                    hasSharedData: true,
                    columnMask: 0,
                    scaleBit),
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
            Assert.That(rendererStats.EcsLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.EcsLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastLockCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCopyOperationCount, Is.GreaterThan(0));
        }

        [Test]
        public void Manager_RenderUploadGraph_UsesDirtyQueueForOrdinaryUpdates()
        {
            VividParticleSystem system = CreateSystem();
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
            Assert.That(cleanStats.LastUploadRecordWorkCount, Is.EqualTo(0));
            Assert.That(cleanStats.LastMergedUploadCopyWorkCount, Is.EqualTo(0));

            system.rendererModule.color = Color.red;
            VividParticleSystemManager.RunRendererUpdateForTests();

            VividParticleSystemManager.VividParticleRendererManagerStats dirtyStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(dirtyStats.LastDirtyUploadQueueCount, Is.EqualTo(1));
            Assert.That(dirtyStats.LastInvalidDirtyUploadQueueCount, Is.EqualTo(0));
            Assert.That(dirtyStats.LastUploadRecordWorkCount, Is.EqualTo(1));
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
        }

        [Test]
        public void Manager_RendererUpdate_TracksOnlyActiveRendererSystems()
        {
            VividParticleSystem activeSystem = CreateSystem();
            VividParticleSystem inactiveSystem = CreateSystem();
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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_SplitsMeshBatchesByMeshSet()
        {
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
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
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(2));
            Assert.That(
                rendererStats.LastUploadColumnMask & VividParticleSystemManager.UploadColumnMeshIndexMask,
                Is.EqualTo(0));

            int[] meshVisibleCounts = VividParticleSystemManager.GetMeshVisibleCountsForTests();
            Assert.That(meshVisibleCounts, Is.EqualTo(new[] { 4, 4 }));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(-1, 2), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(0, 2), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(1, 2), Is.EqualTo(1));
            Assert.That(VividParticleSystemManager.ResolveMeshIndexSlot(2, 2), Is.EqualTo(0));
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
        public void Manager_RendererStats_DoesNotCompletePendingUpload_ForRuntimeSnapshot()
        {
            VividParticleSystem system = CreateSystem();
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
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
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
        public void Manager_Upload_AppendingDefaultSharedData_DoesNotRewritePerSharpColumns()
        {
            VividParticleSystem system = CreateSystem();
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
            Assert.That(appendStats.LastUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastTransformUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(
                appendStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(appendStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastUploadCopyWorkCount, Is.EqualTo(2));
            Assert.That(appendStats.LastMergedUploadCopyWorkCount, Is.EqualTo(2));
            Assert.That(appendStats.LastCopyOperationCount, Is.EqualTo(2));
            Assert.That(appendStats.LastSharedDataWorkCount, Is.LessThan(initialStats.LastSharedDataWorkCount));
            Assert.That(appendStats.LastCopyOperationCount, Is.LessThan(initialStats.LastCopyOperationCount));
        }

        [Test]
        public void Manager_Upload_SharedColorChangeDoesNotUploadParticlePages()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.Shared;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem system = CreateSystem();
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
            VividParticleSystem system = CreateSystem();
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
        public void Manager_Upload_PerParticleColorChangeUploadsParticlePages()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
            system.rendererModule.colorDataMode = VividParticleGpuDataMode.PerParticle;
            system.main.maxParticles = 1024;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

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
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(1024);

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

            VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(4));
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
            Assert.That(
                VividParticleSystemManager.GetVisibleInstanceCount(system.rendererModule.renderMode, system.particleCount),
                Is.EqualTo(1024));
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
        public void Renderer_DrawOutputDefaults_ProvideSortingAndLayerFiltering()
        {
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(4), Is.EqualTo(12));
            Assert.That(VividParticleSystemManager.GetSortingPositionFloatCount(-1), Is.EqualTo(0));
            Assert.That(VividParticleSystemManager.RequiresSortingPositionsByDefault(), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.None), Is.False);
            Assert.That(VividParticleSystemManager.RequiresSortingPositions(VividParticleSortMode.ByDistance), Is.True);
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
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Camera, 0b0011, 4),
                Is.EqualTo(0xff));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Light, 0b0101, 4),
                Is.EqualTo(0b0101));
            Assert.That(
                VividParticleSystemManager.ResolveSplitVisibilityMaskForView(BatchCullingViewType.Light, 0, 4),
                Is.EqualTo(0));

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
            float3 pageLocalResolved = VividParticleSystemManager.ResolvePageSortingPosition(
                pageBoundsCenter,
                localPosition,
                localToWorld,
                (int)VividParticleSystemSimulationSpace.Local,
                hasFirstParticle: true);
            Assert.That(pageLocalResolved.x, Is.EqualTo(localResolved.x).Within(0.0001f));
            Assert.That(pageLocalResolved.y, Is.EqualTo(localResolved.y).Within(0.0001f));
            Assert.That(pageLocalResolved.z, Is.EqualTo(localResolved.z).Within(0.0001f));

            float3 pageFallbackResolved = VividParticleSystemManager.ResolvePageSortingPosition(
                pageBoundsCenter,
                localPosition,
                localToWorld,
                (int)VividParticleSystemSimulationSpace.Local,
                hasFirstParticle: false);
            Assert.That(pageFallbackResolved.x, Is.EqualTo(pageBoundsCenter.x).Within(0.0001f));
            Assert.That(pageFallbackResolved.y, Is.EqualTo(pageBoundsCenter.y).Within(0.0001f));
            Assert.That(pageFallbackResolved.z, Is.EqualTo(pageBoundsCenter.z).Within(0.0001f));

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
