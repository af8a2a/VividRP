using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VividRP.Runtime.Particle
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Vivid Particle System")]
    public sealed class VividParticleSystem : MonoBehaviour
    {
        internal const float FixedSimulationStep = 1.0f / 60.0f;
        private const float GravityAcceleration = 9.81f;
        private const float MinimumSimulationStep = 0.000001f;
#if UNITY_EDITOR
        private const float MaximumEditorSimulationStep = 0.1f;
#endif

        [SerializeField]
        private VividParticleSystemAsset m_Asset;

        [SerializeField]
        private VividParticleMainModule m_Main = VividParticleMainModule.CreateDefault();

        [SerializeField]
        private VividParticleEmissionModule m_Emission = VividParticleEmissionModule.CreateDefault();

        [SerializeField]
        private VividParticleShapeModule m_Shape = VividParticleShapeModule.CreateDefault();

        [SerializeField]
        private VividParticleRendererModule m_Renderer = VividParticleRendererModule.CreateDefault();

        private VividParticleStorage m_Particles = new();
        private bool[] m_BurstTriggered = Array.Empty<bool>();
        private System.Random m_Random;
        private float m_Time;
        private float m_EmissionAccumulator;
        private bool m_IsPlaying;
        private bool m_IsPaused;
        private bool m_StopEmitting;
#if UNITY_EDITOR
        private double m_LastEditorUpdateTime;
#endif

        public VividParticleSystemAsset asset
        {
            get => m_Asset;
            set
            {
                if (m_Asset == value)
                    return;

                m_Asset = value;
                CopySettingsFromAsset();
                VividParticleSystemManager.MarkRendererDirty(this);
            }
        }

        public VividParticleMainModule main => m_Main ??= VividParticleMainModule.CreateDefault();

        public VividParticleEmissionModule emission => m_Emission ??= VividParticleEmissionModule.CreateDefault();

        public VividParticleShapeModule shape => m_Shape ??= VividParticleShapeModule.CreateDefault();

        public VividParticleRendererModule rendererModule => m_Renderer ??= VividParticleRendererModule.CreateDefault();

        public int particleCount => validParticleCount;

        public bool isPlaying => m_IsPlaying;

        public bool isPaused => m_IsPaused;

        public float time => m_Time;

        public void Play(bool withChildren = true)
        {
            ApplyToHierarchy(withChildren, system => system.PlaySelf());
        }

        public void Stop(
            bool withChildren = true,
            VividParticleSystemStopBehavior stopBehavior = VividParticleSystemStopBehavior.StopEmitting)
        {
            ApplyToHierarchy(withChildren, system => system.StopSelf(stopBehavior));
        }

        public void Pause(bool withChildren = true)
        {
            ApplyToHierarchy(withChildren, system => system.PauseSelf());
        }

        public void Simulate(
            float t,
            bool withChildren = true,
            bool restart = true,
            bool fixedTimeStep = true)
        {
            ApplyToHierarchy(withChildren, system => system.SimulateSelf(t, restart, fixedTimeStep));
        }

        public void Emit(int count)
        {
            EmitInternal(count);
            VividParticleSystemManager.UpdateRendering(this);
            RequestEditorRenderUpdate();
        }

        internal int aliveParticleCount => validParticleCount;

        internal Bounds worldBounds => CalculateWorldBounds();

        internal bool shouldRender => isActiveAndEnabled && rendererModule.enabled && validParticleCount > 0;

        internal bool requiresAutomaticUpdate => !m_IsPaused
            && (m_IsPlaying || (m_StopEmitting && validParticleCount > 0));

        internal int maxParticles => main.maxParticles;

        internal int particleStoragePageSize => VividParticleStorage.PageSize;

        internal int particleStorageCapacity => m_Particles?.capacity ?? 0;

        internal int particleStorageActiveCount => validParticleCount;

        private int validParticleCount => Mathf.Min(m_Particles?.activeCount ?? 0, main.maxParticles);

        internal Matrix4x4 GetParticleObjectToWorldMatrix(int particleIndex)
        {
            if (particleIndex < 0
                || particleIndex >= validParticleCount
                || m_Particles == null
                || !m_Particles.IsValidIndex(particleIndex))
            {
                return Matrix4x4.identity;
            }

            Vector3 position = GetParticleWorldPosition(m_Particles.GetPosition(particleIndex));
            float size = Mathf.Max(
                VividParticleMainModule.MinimumStartSize,
                m_Particles.GetSize(particleIndex) * rendererModule.sizeScale);

            return Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * size);
        }

        internal Color GetParticleRenderColor(int particleIndex)
        {
            if (particleIndex < 0
                || particleIndex >= validParticleCount
                || m_Particles == null
                || !m_Particles.IsValidIndex(particleIndex))
            {
                return Color.clear;
            }

            float startLifetime = m_Particles.GetStartLifetime(particleIndex);
            float lifetimeRatio = startLifetime > 0.0f
                ? Mathf.Clamp01(m_Particles.GetRemainingLifetime(particleIndex) / startLifetime)
                : 0.0f;
            Color color = m_Particles.GetColor(particleIndex) * rendererModule.color;
            color.a *= lifetimeRatio;
            return color;
        }

        internal void UpdateAutomatic(float deltaTime)
        {
            if (m_IsPaused)
                return;

            bool ageOnly = m_StopEmitting && particleCount > 0;
            if (!m_IsPlaying && !ageOnly)
                return;

            SimulateDelta(deltaTime, allowEmission: m_IsPlaying && !m_StopEmitting);
        }

        internal void EmitInternal(int count)
        {
            if (count <= 0)
                return;

            EnsureModules();
            EnsureRuntimeStorage();
            EnsureRandom();

            int available = Mathf.Max(0, main.maxParticles - particleCount);
            int spawnCount = Mathf.Min(count, available);
            for (int index = 0; index < spawnCount; index++)
                SpawnParticle();
        }

        internal void SimulateDelta(float deltaTime, bool allowEmission)
        {
            if (deltaTime <= 0.0f)
                return;

            EnsureModules();
            EnsureRuntimeStorage();
            IntegrateParticles(deltaTime);
            AdvanceEmission(deltaTime, allowEmission);

            if (m_StopEmitting && particleCount == 0)
                m_StopEmitting = false;
        }

        internal static void SampleShape(
            VividParticleShapeModule shape,
            System.Random random,
            out Vector3 localPosition,
            out Vector3 localDirection)
        {
            random ??= new System.Random(1);
            if (shape == null || !shape.enabled)
            {
                localPosition = Vector3.zero;
                localDirection = Vector3.forward;
                return;
            }

            switch (shape.shapeType)
            {
                case VividParticleShapeType.Sphere:
                    localPosition = SampleInsideUnitSphere(random) * shape.radius;
                    localDirection = localPosition.sqrMagnitude > 0.000001f
                        ? localPosition.normalized
                        : SampleUnitVector(random);
                    break;
                case VividParticleShapeType.Box:
                    localPosition = new Vector3(
                        RandomRange(random, -shape.boxSize.x * 0.5f, shape.boxSize.x * 0.5f),
                        RandomRange(random, -shape.boxSize.y * 0.5f, shape.boxSize.y * 0.5f),
                        RandomRange(random, -shape.boxSize.z * 0.5f, shape.boxSize.z * 0.5f));
                    localDirection = Vector3.forward;
                    break;
                case VividParticleShapeType.Cone:
                    float diskRadius = Mathf.Max(0.0f, shape.radius);
                    Vector2 disk = SampleInsideUnitCircle(random) * diskRadius;
                    localPosition = new Vector3(disk.x, disk.y, 0.0f);
                    localDirection = SampleConeDirection(random, shape.angle);
                    break;
                default:
                    localPosition = Vector3.zero;
                    localDirection = Vector3.forward;
                    break;
            }
        }

        private void Reset()
        {
            CopySettingsFromAsset();
        }

        private void OnEnable()
        {
            EnsureModules();
            ValidateModules();
            EnsureRuntimeStorage();
            VividParticleSystemManager.Register(this);
#if UNITY_EDITOR
            RegisterEditorUpdate();
            ResetEditorUpdateTime();
#endif

            if (main.playOnAwake)
                Play(withChildren: false);
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                VividParticleSystemManager.UpdateSystem(this);
                return;
            }

            VividParticleSystemManager.UpdateRendering(this);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnregisterEditorUpdate();
#endif
            VividParticleSystemManager.Unregister(this);
            ReleaseRuntimeStorage();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnregisterEditorUpdate();
#endif
            VividParticleSystemManager.Unregister(this);
            ReleaseRuntimeStorage();
        }

        private void OnValidate()
        {
            EnsureModules();
            ValidateModules();
            if (m_Particles != null && m_Particles.isCreated)
                EnsureRuntimeStorage();
            else
                EnsureBurstState();
            VividParticleSystemManager.MarkRendererDirty(this);
        }

#if UNITY_EDITOR
        private void RegisterEditorUpdate()
        {
            if (Application.isPlaying)
                return;

            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        private void UnregisterEditorUpdate()
        {
            EditorApplication.update -= EditorUpdate;
        }

        private void EditorUpdate()
        {
            if (Application.isPlaying || !isActiveAndEnabled)
                return;

            float deltaTime = ConsumeEditorDeltaTime();
            if (!requiresAutomaticUpdate)
                return;

            VividParticleSystemManager.UpdateSystem(this, deltaTime);
            RequestEditorRenderUpdate();
        }

        private float ConsumeEditorDeltaTime()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (m_LastEditorUpdateTime <= 0.0)
            {
                m_LastEditorUpdateTime = currentTime;
                return 0.0f;
            }

            float deltaTime = (float)(currentTime - m_LastEditorUpdateTime);
            m_LastEditorUpdateTime = currentTime;
            return Mathf.Clamp(deltaTime, 0.0f, MaximumEditorSimulationStep);
        }

        private void ResetEditorUpdateTime()
        {
            if (!Application.isPlaying)
                m_LastEditorUpdateTime = EditorApplication.timeSinceStartup;
        }

        private static void RequestEditorRenderUpdate()
        {
            if (Application.isPlaying)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#else
        private static void ResetEditorUpdateTime()
        {
        }

        private static void RequestEditorRenderUpdate()
        {
        }
#endif

        private void CopySettingsFromAsset()
        {
            EnsureModules();
            m_Asset?.CopyModulesTo(m_Main, m_Emission, m_Shape, m_Renderer);
            ValidateModules();
            EnsureRuntimeStorage();
            ResetBurstState();
        }

        private void PlaySelf()
        {
            EnsureModules();
            ValidateModules();
            EnsureRuntimeStorage();
            EnsureRandom();

            if (!main.loop && m_Time >= main.duration && particleCount == 0)
                ResetSimulation(clearParticles: true);

            m_IsPlaying = true;
            m_IsPaused = false;
            m_StopEmitting = false;
            ResetEditorUpdateTime();
            RequestEditorRenderUpdate();
        }

        private void StopSelf(VividParticleSystemStopBehavior stopBehavior)
        {
            m_IsPlaying = false;
            m_IsPaused = false;

            if (stopBehavior == VividParticleSystemStopBehavior.StopEmittingAndClear)
            {
                m_StopEmitting = false;
                ResetSimulation(clearParticles: true);
                VividParticleSystemManager.UpdateRendering(this);
                RequestEditorRenderUpdate();
                return;
            }

            m_StopEmitting = particleCount > 0;
            ResetEditorUpdateTime();
            RequestEditorRenderUpdate();
        }

        private void PauseSelf()
        {
            m_IsPaused = true;
            m_IsPlaying = false;
            ResetEditorUpdateTime();
            RequestEditorRenderUpdate();
        }

        private void SimulateSelf(float t, bool restart, bool fixedTimeStep)
        {
            if (restart)
                ResetSimulation(clearParticles: true);

            if (t <= 0.0f)
            {
                VividParticleSystemManager.UpdateRendering(this);
                RequestEditorRenderUpdate();
                return;
            }

            float remaining = t;
            if (fixedTimeStep)
            {
                while (remaining > MinimumSimulationStep)
                {
                    float step = Mathf.Min(FixedSimulationStep, remaining);
                    SimulateDelta(step, allowEmission: true);
                    remaining -= step;
                }
            }
            else
            {
                SimulateDelta(remaining, allowEmission: true);
            }

            VividParticleSystemManager.UpdateRendering(this);
            ResetEditorUpdateTime();
            RequestEditorRenderUpdate();
        }

        private void ApplyToHierarchy(bool withChildren, Action<VividParticleSystem> action)
        {
            if (!withChildren)
            {
                action(this);
                return;
            }

            var systems = GetComponentsInChildren<VividParticleSystem>(true);
            for (int index = 0; index < systems.Length; index++)
            {
                if (systems[index] != null)
                    action(systems[index]);
            }
        }

        private void ResetSimulation(bool clearParticles)
        {
            m_Time = 0.0f;
            m_EmissionAccumulator = 0.0f;
            ResetBurstState();
            if (clearParticles)
                m_Particles?.Clear();
            ResetRandom();
        }

        private void EnsureModules()
        {
            m_Main ??= VividParticleMainModule.CreateDefault();
            m_Emission ??= VividParticleEmissionModule.CreateDefault();
            m_Shape ??= VividParticleShapeModule.CreateDefault();
            m_Renderer ??= VividParticleRendererModule.CreateDefault();
        }

        private void ValidateModules()
        {
            main.Validate();
            emission.Validate();
            shape.Validate();
            rendererModule.Validate();
        }

        private void EnsureRuntimeStorage()
        {
            m_Particles ??= new VividParticleStorage();
            m_Particles.EnsureCapacity(main.maxParticles);
            EnsureBurstState();
        }

        private void ReleaseRuntimeStorage()
        {
            m_Particles?.Dispose();
            m_Time = 0.0f;
            m_EmissionAccumulator = 0.0f;
            m_IsPlaying = false;
            m_IsPaused = false;
            m_StopEmitting = false;
            m_Random = null;
            ResetBurstState();
        }

        private void EnsureBurstState()
        {
            VividParticleBurst[] bursts = emission.bursts;
            int burstCount = bursts?.Length ?? 0;
            if (m_BurstTriggered == null || m_BurstTriggered.Length != burstCount)
                m_BurstTriggered = new bool[burstCount];
        }

        private void ResetBurstState()
        {
            EnsureBurstState();
            Array.Clear(m_BurstTriggered, 0, m_BurstTriggered.Length);
        }

        private void EnsureRandom()
        {
            m_Random ??= CreateRandom();
        }

        private void ResetRandom()
        {
            m_Random = CreateRandom();
        }

        private System.Random CreateRandom()
        {
            uint seed = main.useAutoRandomSeed
                ? unchecked((uint)Environment.TickCount ^ (uint)GetEntityId().GetHashCode())
                : main.randomSeed;
            return new System.Random(unchecked((int)seed));
        }

        private void IntegrateParticles(float deltaTime)
        {
            Vector3 gravity = Vector3.down * (GravityAcceleration * main.gravityModifier);
            m_Particles.Integrate(deltaTime, gravity);
        }

        private void AdvanceEmission(float deltaTime, bool allowEmission)
        {
            float remaining = deltaTime;
            float duration = main.duration;

            while (remaining > MinimumSimulationStep)
            {
                float segmentEnd = main.loop
                    ? Mathf.Min(duration, m_Time + remaining)
                    : Mathf.Min(duration, m_Time + remaining);
                float segmentDelta = Mathf.Max(0.0f, segmentEnd - m_Time);

                if (allowEmission && emission.enabled && segmentDelta > 0.0f)
                    EmitForTimeRange(m_Time, segmentEnd, segmentDelta);

                remaining -= segmentDelta;
                m_Time = segmentEnd;

                if (m_Time < duration)
                    break;

                if (!main.loop)
                {
                    m_Time = duration;
                    break;
                }

                m_Time = 0.0f;
                ResetBurstState();

                if (segmentDelta <= 0.0f)
                    break;
            }
        }

        private void EmitForTimeRange(float startTime, float endTime, float deltaTime)
        {
            m_EmissionAccumulator += emission.rateOverTime * deltaTime;
            int continuousCount = Mathf.FloorToInt(m_EmissionAccumulator);
            if (continuousCount > 0)
            {
                m_EmissionAccumulator -= continuousCount;
                EmitInternal(continuousCount);
            }

            VividParticleBurst[] bursts = emission.bursts;
            if (bursts == null || bursts.Length == 0)
                return;

            EnsureBurstState();
            for (int index = 0; index < bursts.Length; index++)
            {
                if (m_BurstTriggered[index])
                    continue;

                VividParticleBurst burst = bursts[index];
                if (burst.time < startTime || burst.time > endTime)
                    continue;

                m_BurstTriggered[index] = true;
                EmitInternal(burst.count);
            }
        }

        private void SpawnParticle()
        {
            SampleShape(shape, m_Random, out Vector3 localPosition, out Vector3 localDirection);
            localDirection = localDirection.sqrMagnitude > 0.000001f
                ? localDirection.normalized
                : Vector3.forward;

            Vector3 position = localPosition;
            Vector3 velocity = localDirection * main.startSpeed;
            if (main.simulationSpace == VividParticleSystemSimulationSpace.World)
            {
                position = transform.TransformPoint(localPosition);
                velocity = transform.TransformDirection(localDirection).normalized * main.startSpeed;
            }

            m_Particles.Add(
                position,
                velocity,
                main.startLifetime,
                main.startLifetime,
                main.startSize,
                main.startColor);
        }

        private Bounds CalculateWorldBounds()
        {
            int particleCount = validParticleCount;
            if (particleCount <= 0 || m_Particles == null || !m_Particles.isCreated || !m_Particles.IsValidIndex(0))
                return new Bounds(transform.position, Vector3.zero);

            Vector3 firstPosition = GetParticleWorldPosition(m_Particles.GetPosition(0));
            float firstExtent = GetParticleWorldExtent(m_Particles.GetSize(0));
            var bounds = new Bounds(firstPosition, Vector3.one * (firstExtent * 2.0f));

            for (int index = 1; index < particleCount; index++)
            {
                Vector3 position = GetParticleWorldPosition(m_Particles.GetPosition(index));
                float extent = GetParticleWorldExtent(m_Particles.GetSize(index));
                bounds.Encapsulate(position + Vector3.one * extent);
                bounds.Encapsulate(position - Vector3.one * extent);
            }

            return bounds;
        }

        private Vector3 GetParticleWorldPosition(Vector3 position)
        {
            return main.simulationSpace == VividParticleSystemSimulationSpace.Local
                ? transform.TransformPoint(position)
                : position;
        }

        private float GetParticleWorldExtent(float size)
        {
            return Mathf.Max(
                VividParticleMainModule.MinimumStartSize,
                size * rendererModule.sizeScale * 0.5f);
        }

        private static Vector3 SampleInsideUnitSphere(System.Random random)
        {
            Vector3 value;
            do
            {
                value = new Vector3(
                    RandomRange(random, -1.0f, 1.0f),
                    RandomRange(random, -1.0f, 1.0f),
                    RandomRange(random, -1.0f, 1.0f));
            }
            while (value.sqrMagnitude > 1.0f);

            return value;
        }

        private static Vector2 SampleInsideUnitCircle(System.Random random)
        {
            Vector2 value;
            do
            {
                value = new Vector2(
                    RandomRange(random, -1.0f, 1.0f),
                    RandomRange(random, -1.0f, 1.0f));
            }
            while (value.sqrMagnitude > 1.0f);

            return value;
        }

        private static Vector3 SampleUnitVector(System.Random random)
        {
            Vector3 value = SampleInsideUnitSphere(random);
            return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.forward;
        }

        private static Vector3 SampleConeDirection(System.Random random, float angle)
        {
            float clampedAngle = Mathf.Clamp(angle, 0.0f, 89.0f) * Mathf.Deg2Rad;
            float cosMin = Mathf.Cos(clampedAngle);
            float cosTheta = RandomRange(random, cosMin, 1.0f);
            float sinTheta = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - cosTheta * cosTheta));
            float phi = RandomRange(random, 0.0f, Mathf.PI * 2.0f);
            return new Vector3(
                Mathf.Cos(phi) * sinTheta,
                Mathf.Sin(phi) * sinTheta,
                cosTheta).normalized;
        }

        private static float RandomRange(System.Random random, float minInclusive, float maxInclusive)
        {
            return minInclusive + (float)random.NextDouble() * (maxInclusive - minInclusive);
        }
    }
}
