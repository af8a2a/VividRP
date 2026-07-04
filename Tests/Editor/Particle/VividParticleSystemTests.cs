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
            Assert.That(renderer.material, Is.Null);
            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(renderer.sizeScale, Is.EqualTo(1.0f));
            Assert.That(renderer.renderQueueOffset, Is.EqualTo(0));
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
            asset.rendererModule.color = Color.cyan;

            VividParticleSystem system = CreateSystem();
            system.asset = asset;

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.main.maxParticles, Is.EqualTo(7));
            Assert.That(system.emission.rateOverTime, Is.EqualTo(3.0f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.shape.shapeType, Is.EqualTo(VividParticleShapeType.Sphere));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));

            asset.main.startLifetime = 9.0f;
            asset.emission.bursts[0] = new VividParticleBurst(0.25f, 9);
            asset.rendererModule.color = Color.red;

            Assert.That(system.main.startLifetime, Is.EqualTo(2.5f));
            Assert.That(system.emission.bursts[0].count, Is.EqualTo(2));
            Assert.That(system.rendererModule.color, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void Emit_ClampsToMaxParticles_WhenCapacityIsExceeded()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 3;
            system.emission.enabled = false;

            system.Emit(10);

            Assert.That(system.particleCount, Is.EqualTo(3));
        }

        [Test]
        public void Storage_UsesFixedPageCapacity_AndClampsActiveCountWhenMaxParticlesShrinks()
        {
            VividParticleSystem system = CreateSystem();
            system.main.maxParticles = 3;
            system.main.startLifetime = 10.0f;
            system.emission.enabled = false;
            system.shape.enabled = false;

            system.Emit(10);

            Assert.That(system.particleStoragePageSize, Is.EqualTo(256));
            Assert.That(system.particleStorageCapacity, Is.EqualTo(256));
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(3));
            Assert.That(system.particleCount, Is.EqualTo(3));

            system.main.maxParticles = 300;
            system.Emit(10);

            Assert.That(system.particleStorageCapacity, Is.EqualTo(512));
            Assert.That(system.particleCount, Is.EqualTo(13));

            system.main.maxParticles = 2;
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(2));
            Assert.That(system.particleCount, Is.EqualTo(2));
            Assert.That(system.GetParticleRenderColor(2), Is.EqualTo(Color.clear));

            system.Simulate(0.01f, withChildren: false, restart: false, fixedTimeStep: false);

            Assert.That(system.particleStorageCapacity, Is.EqualTo(256));
            Assert.That(system.particleStorageActiveCount, Is.EqualTo(2));
            Assert.That(system.particleCount, Is.EqualTo(2));
        }

        [Test]
        public void PlayPauseStop_UpdateExpectedStateAndClearParticles()
        {
            VividParticleSystem system = CreateSystem();
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

            gameObject.SetActive(false);

            Assert.That(system.particleCount, Is.EqualTo(0));
            Assert.That(system.particleStorageCapacity, Is.EqualTo(0));
            Assert.That(system.isPlaying, Is.False);
            Assert.That(system.isPaused, Is.False);
        }

        [Test]
        public void Manager_MetadataOffsets_AreAlignedAndTaggedAsPerInstance()
        {
            int capacity = 17;
            int objectToWorld = VividParticleSystemManager.ObjectToWorldByteAddress(capacity);
            int worldToObject = VividParticleSystemManager.WorldToObjectByteAddress(capacity);
            int baseColor = VividParticleSystemManager.BaseColorByteAddress(capacity);
            MetadataValue metadata = VividParticleSystemManager.CreatePerInstanceMetadata(123, baseColor);

            Assert.That(objectToWorld % 16, Is.EqualTo(0));
            Assert.That(worldToObject % 16, Is.EqualTo(0));
            Assert.That(baseColor % 16, Is.EqualTo(0));
            Assert.That(worldToObject, Is.EqualTo(objectToWorld + capacity * VividParticleSystemManager.SizeOfPackedMatrix));
            Assert.That(baseColor, Is.EqualTo(worldToObject + capacity * VividParticleSystemManager.SizeOfPackedMatrix));
            Assert.That((metadata.Value & VividParticleSystemManager.PerInstanceMetadataMask) != 0u, Is.True);
            Assert.That(metadata.Value & ~VividParticleSystemManager.PerInstanceMetadataMask, Is.EqualTo((uint)baseColor));
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
