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
            renderer.batchLayer = -1;
            Assert.That(renderer.batchLayer, Is.EqualTo(VividParticleRendererModule.MinimumBatchLayer));
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
        public void Manager_EmissionInitialize_UsesInlinePath_ForSingleWork()
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
            Assert.That(runtimeStats.LastEmissionInitializeInlineWorkCount, Is.EqualTo(1));
            Assert.That(runtimeStats.LastEmissionInitializeScheduledWorkCount, Is.EqualTo(0));
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
            Assert.That(billboardInfos[4].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnScaleMask));
            Assert.That(billboardInfos[4].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(billboardInfos[5].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.Rotation));
            Assert.That(billboardInfos[5].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
            Assert.That(billboardInfos[5].DataInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnRotationMask));
            Assert.That(billboardInfos[5].DataInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag));
            Assert.That(billboardInfos[6].DataInfo.DataId, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch));
            Assert.That(billboardInfos[6].DataInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerSharp));
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
                    + capacity * VividParticleSystemManager.SizeOfFloat4 * 2
                    + VividParticleSystemManager.SizeOfFloat4 * 3));
            Assert.That(stretchLayout.PerInstanceElementByteSize, Is.EqualTo(VividParticleSystemManager.SizeOfFloat4 * 2));
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
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.Scale, out var scaleInfo), Is.True);
            Assert.That(scaleInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.UV, out var uvInfo), Is.True);
            Assert.That(uvInfo.Frequency, Is.EqualTo(VividParticleSystemManager.VividParticleGpuDataFrequency.PerInstance));
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData1, out var customData1Info), Is.True);
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.CustomData2, out var customData2Info), Is.True);
            Assert.That(layout.TryGetDataInfo(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex, out var meshIndexInfo), Is.True);
            Assert.That(uvInfo.UploadColumnMask, Is.EqualTo(VividParticleSystemManager.UploadColumnUVMask));
            Assert.That(uvInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobUVUploadFlag));
            Assert.That(customData1Info.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobCustomDataUploadFlag));
            Assert.That(customData2Info.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobCustomDataUploadFlag));
            Assert.That(meshIndexInfo.RenderJobFlagMask, Is.EqualTo(VividParticleSystemManager.RenderJobMeshIndexUploadFlag));
            Assert.That(
                layout.DataPerSharpBits,
                Is.EqualTo((1u << (int)VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                    | (1u << (int)VividParticleSystemManager.VividParticleGpuDataId.BaseColor)));
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
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(
                    VividParticleSystemManager.VividParticleGpuDataId.BaseColor)));
            Assert.That(
                layout.PerInstanceDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Rotation)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.VelocityStretch)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.Scale)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.UV)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData1)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.CustomData2)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.MeshIndex)));
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
            Assert.That(
                layout.PerInstanceRenderJobFlagMask,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobVelocityStretchUploadFlag
                    | VividParticleSystemManager.RenderJobExtraDataUploadFlag));
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
                Is.EqualTo(VividParticleSystemManager.UploadColumnUVMask
                    | VividParticleSystemManager.UploadColumnCustomData1Mask
                    | VividParticleSystemManager.UploadColumnCustomData2Mask
                    | VividParticleSystemManager.UploadColumnMeshIndexMask));
            Assert.That(
                layout.UVRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnUVMask));
            Assert.That(
                layout.CustomDataRenderJobUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnCustomData1Mask
                    | VividParticleSystemManager.UploadColumnCustomData2Mask));
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
                Is.EqualTo(0u));
            Assert.That(
                perParticleColorLayout.PerInstanceDataBits
                    & VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.BaseColor),
                Is.Not.EqualTo(0u));
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
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
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
        public void Manager_RendererManager_ReusesDrawBatchObjectsAcrossLayoutRebuilds()
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
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LineGroupPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryCreatedCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryReusedCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedDrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedDrawBatchCount, Is.EqualTo(0));

            system.rendererModule.renderingLayerMask = 0x2u;
            VividParticleSystemManager.RunRendererUpdateForTests();

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
            Assert.That(rendererStats.LineGroupPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedLineGroupCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedLineGroupCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastEcsRendererQueryCreatedCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastEcsRendererQueryReusedCount, Is.EqualTo(1));
            Assert.That(rendererStats.DrawBatchPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedDrawBatchCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedDrawBatchCount, Is.EqualTo(1));
        }

        [Test]
        public void Manager_RendererManager_ReusesRenderRecordsAfterSystemBecomesActiveAgain()
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
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(0));

            system.Stop(withChildren: false, VividParticleSystemStopBehavior.StopEmittingAndClear);

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(0));

            system.Emit(1);

            rendererStats = VividParticleSystemManager.GetRendererStatsForTests();
            Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.RenderRecordPoolCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCreatedRenderRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastReusedRenderRecordCount, Is.EqualTo(1));
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
        }

        [Test]
        public void Manager_RenderUploadGraph_UsesBatchDirtyQueueForBatchOnlyUpdates()
        {
            VividParticleSystem system = CreateSystem();
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
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
        }

        [Test]
        public void Manager_RendererManager_SplitsBatchesBySortingPriority()
        {
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
            VividParticleSystem third = CreateSystem();
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
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
        public void Manager_RendererManager_SplitsBatchesByStaticShadowCaster()
        {
            VividParticleSystem first = CreateSystem();
            VividParticleSystem second = CreateSystem();
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
            Assert.That(rendererStats.EcsMatchedLineCount, Is.EqualTo(2));
            Assert.That(rendererStats.EcsSkippedLineCount, Is.EqualTo(0));
            Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(2));
            Assert.That(rendererStats.DrawRangeCount, Is.EqualTo(2));
            Assert.That(
                VividParticleSystemManager.GetRendererDrawRangeRendererPrioritiesForTests(BatchCullingViewType.Camera),
                Is.EqualTo(new[] { 10, 20 }));
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
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(4));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastMeshVisibleCountInlineWorkCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastMeshVisibleCountScheduledWorkCount, Is.EqualTo(0));
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
        public void Renderer_CullingLayout_UsesSceneCullingMasksWithoutSplittingDrawBatch()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            Scene firstScene = default;
            Scene secondScene = default;
            const ulong firstSceneMask = 0b0010UL;
            const ulong secondSceneMask = 0b0100UL;

            try
            {
                firstScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                secondScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
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

                first.Emit(1);
                second.Emit(1);

                VividParticleSystemManager.VividParticleRendererManagerStats rendererStats =
                    VividParticleSystemManager.GetRendererStatsForTests();
                Assert.That(rendererStats.RenderRecordCount, Is.EqualTo(2));
                Assert.That(rendererStats.DrawBatchCount, Is.EqualTo(1));
                Assert.That(rendererStats.CullingRecordCount, Is.EqualTo(2));
                Assert.That(rendererStats.DrawCommandCount, Is.EqualTo(1));
                Assert.That(rendererStats.VisibleInstanceCapacity, Is.EqualTo(2));
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
                    EditorSceneManager.CloseScene(secondScene, removeScene: true);

                if (firstScene.IsValid() && firstScene.isLoaded)
                    EditorSceneManager.CloseScene(firstScene, removeScene: true);
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
        public void Manager_RenderUploadGraph_SplitsExtraDataPageWorksByModule()
        {
            VividParticleSystem system = CreateSystem();
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
            Assert.That(rendererStats.LastUVUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastCustomDataUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastMeshIndexUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(
                rendererStats.LastExtraDataUploadPageWorkCount,
                Is.EqualTo(rendererStats.LastUVUploadPageWorkCount
                    + rendererStats.LastCustomDataUploadPageWorkCount
                    + rendererStats.LastMeshIndexUploadPageWorkCount));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags & VividParticleSystemManager.RenderJobExtraDataUploadFlag,
                Is.EqualTo(VividParticleSystemManager.RenderJobExtraDataUploadFlag));
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
            Assert.That(rendererStats.LastUploadBatchWorkCount, Is.EqualTo(1));
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
            Assert.That(appendStats.LastDirtyUploadBatchQueueCount, Is.EqualTo(0));
            Assert.That(appendStats.LastUploadBatchWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastTransformUploadPageWorkCount, Is.EqualTo(1));
            Assert.That(appendStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(appendStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(
                appendStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(appendStats.LastSharedDataWorkCount, Is.EqualTo(2));
            Assert.That(appendStats.LastUploadCopyWorkCount, Is.EqualTo(3));
            Assert.That(appendStats.LastMergedUploadCopyWorkCount, Is.EqualTo(appendStats.LastCopyOperationCount));
            Assert.That(appendStats.LastCopyOperationCount, Is.LessThanOrEqualTo(3));
            uint expectedUploadBits =
                VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SpanSharedData)
                | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize);
            Assert.That(appendStats.LastUploadDataBits, Is.EqualTo(expectedUploadBits));
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
        public void Manager_Upload_LocalSimulationTransformChangeUploadsPositionColumn()
        {
            VividParticleSystem system = CreateSystem();
            system.rendererModule.enabled = true;
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
            Assert.That(rendererStats.LastUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastTransformUploadPageWorkCount, Is.GreaterThan(0));
            Assert.That(rendererStats.LastColorUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastVelocityStretchUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastExtraDataUploadPageWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastSharedDataWorkCount, Is.EqualTo(1));
            Assert.That(
                rendererStats.LastUploadColumnMask,
                Is.EqualTo(VividParticleSystemManager.UploadColumnPositionSizeMask));
            Assert.That(
                rendererStats.LastUploadDataBits,
                Is.EqualTo(VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.SharedData)
                    | VividParticleSystemManager.GetGpuDataBit(VividParticleSystemManager.VividParticleGpuDataId.PositionSize)));
            Assert.That(
                rendererStats.LastRenderJobModuleFlags,
                Is.EqualTo(VividParticleSystemManager.RenderJobTransformUploadFlag
                    | VividParticleSystemManager.RenderJobSharedDataFlag));
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
            Assert.That(rendererStats.LastCullingSingleMeshCacheRecordCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingMultiMeshCacheRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingMeshFallbackRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingRecordVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastCullingBatchVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(2));
            Assert.That(rendererStats.MeshVisibleCountWorkCount, Is.EqualTo(0));
            Assert.That(rendererStats.MeshVisibleCountOutputCount, Is.EqualTo(0));
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
            Assert.That(rendererStats.LastCullingSingleMeshCacheRecordCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastCullingMultiMeshCacheRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingMeshFallbackRecordCount, Is.EqualTo(0));
            Assert.That(rendererStats.LastCullingRecordVisibleCacheEntryCount, Is.EqualTo(2));
            Assert.That(rendererStats.LastCullingBatchVisibleCacheEntryCount, Is.EqualTo(1));
            Assert.That(rendererStats.LastVisibleInstanceCapacityCacheEntryCount, Is.EqualTo(3));
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
                Is.True);
            Assert.That(
                VividParticleSystemManager.HasParticleMotion(MotionVectorGenerationMode.Object),
                Is.True);
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
