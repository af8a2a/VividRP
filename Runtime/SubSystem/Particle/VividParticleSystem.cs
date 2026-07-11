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

        [SerializeField]
        private VividParticleSystemAsset m_Asset;

        [SerializeField]
        private VividParticleMainModule m_Main = VividParticleMainModule.CreateDefault();

        [SerializeField]
        private VividParticleEmissionModule m_Emission = VividParticleEmissionModule.CreateDefault();

        [SerializeField]
        private VividParticleShapeModule m_Shape = VividParticleShapeModule.CreateDefault();

        [SerializeField]
        private VividParticleForceOverLifetimeModule m_ForceOverLifetime =
            VividParticleForceOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleVelocityOverLifetimeModule m_VelocityOverLifetime =
            VividParticleVelocityOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleLimitVelocityOverLifetimeModule m_LimitVelocityOverLifetime =
            VividParticleLimitVelocityOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleColorOverLifetimeModule m_ColorOverLifetime =
            VividParticleColorOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleColorBySpeedModule m_ColorBySpeed =
            VividParticleColorBySpeedModule.CreateDefault();

        [SerializeField]
        private VividParticleSizeOverLifetimeModule m_SizeOverLifetime =
            VividParticleSizeOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleSizeBySpeedModule m_SizeBySpeed =
            VividParticleSizeBySpeedModule.CreateDefault();

        [SerializeField]
        private VividParticleRotationOverLifetimeModule m_RotationOverLifetime =
            VividParticleRotationOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleRotationBySpeedModule m_RotationBySpeed =
            VividParticleRotationBySpeedModule.CreateDefault();

        [SerializeField]
        private VividParticleNoiseModule m_Noise = VividParticleNoiseModule.CreateDefault();

        [SerializeField]
        private VividParticleTextureSheetAnimationModule m_TextureSheetAnimation =
            VividParticleTextureSheetAnimationModule.CreateDefault();

        [SerializeField]
        private VividParticleRendererModule m_Renderer = VividParticleRendererModule.CreateDefault();

        private bool m_IsPlaying;
        private bool m_IsPaused;
        private bool m_StopEmitting;

        public VividParticleSystemAsset asset
        {
            get => m_Asset;
            set
            {
                if (m_Asset == value)
                    return;

                VividParticleSystemManager.Drain(this);
                m_Asset = value;
                CopySettingsFromAsset();
                VividParticleSystemManager.MarkRendererDirty(this);
            }
        }

        public VividParticleMainModule main => m_Main ??= VividParticleMainModule.CreateDefault();

        public VividParticleEmissionModule emission => m_Emission ??= VividParticleEmissionModule.CreateDefault();

        public VividParticleShapeModule shape => m_Shape ??= VividParticleShapeModule.CreateDefault();

        public VividParticleForceOverLifetimeModule forceOverLifetime =>
            m_ForceOverLifetime ??= VividParticleForceOverLifetimeModule.CreateDefault();

        public VividParticleVelocityOverLifetimeModule velocityOverLifetime =>
            m_VelocityOverLifetime ??= VividParticleVelocityOverLifetimeModule.CreateDefault();

        public VividParticleLimitVelocityOverLifetimeModule limitVelocityOverLifetime =>
            m_LimitVelocityOverLifetime ??= VividParticleLimitVelocityOverLifetimeModule.CreateDefault();

        public VividParticleColorOverLifetimeModule colorOverLifetime =>
            m_ColorOverLifetime ??= VividParticleColorOverLifetimeModule.CreateDefault();

        public VividParticleColorBySpeedModule colorBySpeed =>
            m_ColorBySpeed ??= VividParticleColorBySpeedModule.CreateDefault();

        public VividParticleSizeOverLifetimeModule sizeOverLifetime =>
            m_SizeOverLifetime ??= VividParticleSizeOverLifetimeModule.CreateDefault();

        public VividParticleSizeBySpeedModule sizeBySpeed =>
            m_SizeBySpeed ??= VividParticleSizeBySpeedModule.CreateDefault();

        public VividParticleRotationOverLifetimeModule rotationOverLifetime =>
            m_RotationOverLifetime ??= VividParticleRotationOverLifetimeModule.CreateDefault();

        public VividParticleRotationBySpeedModule rotationBySpeed =>
            m_RotationBySpeed ??= VividParticleRotationBySpeedModule.CreateDefault();

        public VividParticleNoiseModule noise => m_Noise ??= VividParticleNoiseModule.CreateDefault();

        public VividParticleTextureSheetAnimationModule textureSheetAnimation =>
            m_TextureSheetAnimation ??= VividParticleTextureSheetAnimationModule.CreateDefault();

        public VividParticleRendererModule rendererModule => m_Renderer ??= VividParticleRendererModule.CreateDefault();

        public int particleCount => VividParticleSystemManager.GetParticleCount(this);

        public bool isPlaying => m_IsPlaying;

        public bool isPaused => m_IsPaused;

        public float time => VividParticleSystemManager.GetTime(this);

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
            RequestEditorRenderUpdate();
        }

        internal int aliveParticleCount => VividParticleSystemManager.GetParticleCount(this);

        internal Bounds worldBounds => VividParticleSystemManager.GetWorldBounds(this);

        internal bool shouldRender => isActiveAndEnabled
            && rendererModule.enabled
            && rendererModule.renderMode != VividParticleRenderMode.None
            && (rendererModule.renderMode != VividParticleRenderMode.Mesh || rendererModule.hasRenderMesh)
            && aliveParticleCount > 0;

        internal bool requiresAutomaticUpdate => !m_IsPaused
            && (m_IsPlaying || (m_StopEmitting && aliveParticleCount > 0));

        internal int maxParticles => main.maxParticles;

        internal int particleStoragePageSize => VividParticleSystemManager.GetParticleStoragePageSize(this);

        internal int particleStorageCapacity => VividParticleSystemManager.GetParticleStorageCapacity(this);

        internal int particleStoragePageCount => VividParticleSystemManager.GetParticleStoragePageCount(this);

        internal int particleStorageActiveCount => VividParticleSystemManager.GetParticleCount(this);

        internal bool usesEcsParticleStorage => VividParticleSystemManager.UsesEcsStorage(this);

        internal bool stopEmitting => m_StopEmitting;

        internal Matrix4x4 localToWorldMatrixSnapshot => transform.localToWorldMatrix;

        internal Quaternion worldRotationSnapshot => transform.rotation;

        internal Matrix4x4 GetParticleObjectToWorldMatrix(int particleIndex)
        {
            return VividParticleSystemManager.GetParticleObjectToWorldMatrix(this, particleIndex);
        }

        internal Color GetParticleRenderColor(int particleIndex)
        {
            return VividParticleSystemManager.GetParticleRenderColor(this, particleIndex);
        }

        internal void UpdateAutomatic(float deltaTime)
        {
            VividParticleSystemManager.UpdateSystem(this, deltaTime);
        }

        internal void EmitInternal(int count)
        {
            VividParticleSystemManager.Emit(this, count);
        }

        internal void SimulateDelta(float deltaTime, bool allowEmission)
        {
            VividParticleSystemManager.SimulateDeltaImmediate(this, deltaTime, allowEmission);
        }

        internal bool CompleteStopEmittingIfEmpty(int aliveCount)
        {
            if (!m_StopEmitting || aliveCount > 0)
                return false;

            m_StopEmitting = false;
            return true;
        }

        internal VividParticleSystemFrameSnapshot CaptureFrameSnapshot(float deltaTime)
        {
            VividParticleBurst[] burstBuffer = Array.Empty<VividParticleBurst>();
            return CaptureFrameSnapshot(deltaTime, ref burstBuffer);
        }

        internal VividParticleSystemFrameSnapshot CaptureFrameSnapshot(
            float deltaTime,
            ref VividParticleBurst[] burstBuffer)
        {
            EnsureModules();
            ValidateModules();

            VividParticleBurst[] bursts = CopyBurstsToSnapshotBuffer(emission.bursts, ref burstBuffer);

            return new VividParticleSystemFrameSnapshot(
                deltaTime,
                isActiveAndEnabled,
                m_IsPlaying,
                m_IsPaused,
                m_StopEmitting,
                main.duration,
                main.loop,
                main.startLifetime,
                main.startSpeed,
                main.startSize,
                main.startColor,
                main.gravityModifier,
                main.simulationSpace,
                main.maxParticles,
                main.randomSeed,
                main.useAutoRandomSeed,
                emission.enabled,
                emission.rateOverTime,
                bursts,
                shape.enabled,
                shape.shapeType,
                shape.radius,
                shape.boxSize,
                shape.angle,
                forceOverLifetime.enabled,
                forceOverLifetime.force,
                forceOverLifetime.space,
                rendererModule.enabled,
                rendererModule.renderMode,
                rendererModule.material,
                rendererModule.renderMesh,
                rendererModule.meshCount,
                rendererModule.color,
                rendererModule.sizeScale,
                rendererModule.stretchLengthScale,
                rendererModule.stretchSpeedScale,
                rendererModule.renderQueueOffset,
                gameObject.layer,
                transform.position,
                transform.localToWorldMatrix,
                transform.rotation,
                GetEntityId().GetHashCode());
        }

        private static VividParticleBurst[] CopyBurstsToSnapshotBuffer(
            VividParticleBurst[] sourceBursts,
            ref VividParticleBurst[] burstBuffer)
        {
            int burstCount = sourceBursts?.Length ?? 0;
            if (burstCount <= 0)
                return Array.Empty<VividParticleBurst>();

            if (burstBuffer == null || burstBuffer.Length != burstCount)
                burstBuffer = new VividParticleBurst[burstCount];

            Array.Copy(sourceBursts, burstBuffer, burstCount);
            return burstBuffer;
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
            VividParticleSystemManager.Register(this);

            if (main.playOnAwake)
                Play(withChildren: false);
        }

        private void OnDisable()
        {
            VividParticleSystemManager.Unregister(this);
            m_IsPlaying = false;
            m_IsPaused = false;
            m_StopEmitting = false;
        }

        private void OnDestroy()
        {
            VividParticleSystemManager.Unregister(this);
        }

        private void OnValidate()
        {
            EnsureModules();
            ValidateModules();
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkRendererDirty(this);
        }

        private void CopySettingsFromAsset()
        {
            EnsureModules();
            m_Asset?.CopyModulesTo(
                m_Main,
                m_Emission,
                m_Shape,
                m_ForceOverLifetime,
                m_VelocityOverLifetime,
                m_LimitVelocityOverLifetime,
                m_ColorOverLifetime,
                m_ColorBySpeed,
                m_SizeOverLifetime,
                m_SizeBySpeed,
                m_RotationOverLifetime,
                m_RotationBySpeed,
                m_Noise,
                m_TextureSheetAnimation,
                m_Renderer);
            ValidateModules();
            VividParticleSystemManager.NotifySettingsChanged(this);
        }

        private void PlaySelf()
        {
            EnsureModules();
            ValidateModules();
            VividParticleSystemManager.Drain(this);

            if (!main.loop && time >= main.duration && particleCount == 0)
                VividParticleSystemManager.ResetSimulation(this, clearParticles: true);

            m_IsPlaying = true;
            m_IsPaused = false;
            m_StopEmitting = false;
            VividParticleSystemManager.NotifySimulationStateChanged(this);
            VividParticleSystemManager.ResetEditorUpdateTime(this);
            RequestEditorRenderUpdate();
        }

        private void StopSelf(VividParticleSystemStopBehavior stopBehavior)
        {
            VividParticleSystemManager.Drain(this);
            m_IsPlaying = false;
            m_IsPaused = false;

            if (stopBehavior == VividParticleSystemStopBehavior.StopEmittingAndClear)
            {
                m_StopEmitting = false;
                VividParticleSystemManager.NotifySimulationStateChanged(this);
                VividParticleSystemManager.ResetSimulation(this, clearParticles: true);
                VividParticleSystemManager.UpdateRendering(this);
                RequestEditorRenderUpdate();
                return;
            }

            m_StopEmitting = particleCount > 0;
            VividParticleSystemManager.NotifySimulationStateChanged(this);
            VividParticleSystemManager.ResetEditorUpdateTime(this);
            RequestEditorRenderUpdate();
        }

        private void PauseSelf()
        {
            VividParticleSystemManager.Drain(this);
            m_IsPaused = true;
            m_IsPlaying = false;
            VividParticleSystemManager.NotifySimulationStateChanged(this);
            VividParticleSystemManager.ResetEditorUpdateTime(this);
            RequestEditorRenderUpdate();
        }

        private void SimulateSelf(float t, bool restart, bool fixedTimeStep)
        {
            VividParticleSystemManager.Simulate(this, t, restart, fixedTimeStep);
            VividParticleSystemManager.ResetEditorUpdateTime(this);
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

        private void EnsureModules()
        {
            m_Main ??= VividParticleMainModule.CreateDefault();
            m_Emission ??= VividParticleEmissionModule.CreateDefault();
            m_Shape ??= VividParticleShapeModule.CreateDefault();
            m_ForceOverLifetime ??= VividParticleForceOverLifetimeModule.CreateDefault();
            m_VelocityOverLifetime ??= VividParticleVelocityOverLifetimeModule.CreateDefault();
            m_LimitVelocityOverLifetime ??= VividParticleLimitVelocityOverLifetimeModule.CreateDefault();
            m_ColorOverLifetime ??= VividParticleColorOverLifetimeModule.CreateDefault();
            m_ColorBySpeed ??= VividParticleColorBySpeedModule.CreateDefault();
            m_SizeOverLifetime ??= VividParticleSizeOverLifetimeModule.CreateDefault();
            m_SizeBySpeed ??= VividParticleSizeBySpeedModule.CreateDefault();
            m_RotationOverLifetime ??= VividParticleRotationOverLifetimeModule.CreateDefault();
            m_RotationBySpeed ??= VividParticleRotationBySpeedModule.CreateDefault();
            m_Noise ??= VividParticleNoiseModule.CreateDefault();
            m_TextureSheetAnimation ??= VividParticleTextureSheetAnimationModule.CreateDefault();
            m_Renderer ??= VividParticleRendererModule.CreateDefault();
            BindModuleCallbacks();
        }

        private void BindModuleCallbacks()
        {
            m_Main.SetChangeCallback(OnSimulationModuleChanged);
            m_Emission.SetChangeCallback(OnSimulationModuleChanged);
            m_Shape.SetChangeCallback(OnSimulationModuleChanged);
            m_ForceOverLifetime.SetChangeCallback(OnSimulationModuleChanged);
            m_VelocityOverLifetime.SetChangeCallback(OnVelocityOverLifetimeModuleChanged);
            m_LimitVelocityOverLifetime.SetChangeCallback(OnSimulationModuleChanged);
            m_ColorOverLifetime.SetChangeCallback(OnColorOverLifetimeModuleChanged);
            m_ColorBySpeed.SetChangeCallback(OnColorOverLifetimeModuleChanged);
            m_SizeOverLifetime.SetChangeCallback(OnSizeOverLifetimeModuleChanged);
            m_SizeBySpeed.SetChangeCallback(OnSizeOverLifetimeModuleChanged);
            m_RotationOverLifetime.SetChangeCallback(OnRotationOverLifetimeModuleChanged);
            m_RotationBySpeed.SetChangeCallback(OnRotationOverLifetimeModuleChanged);
            m_Noise.SetChangeCallback(OnSimulationModuleChanged);
            m_TextureSheetAnimation.SetChangeCallback(OnTextureSheetAnimationModuleChanged);
            m_Renderer.SetChangeCallback(OnRendererModuleChanged);
        }

        private void OnSimulationModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            RequestEditorRenderUpdate();
        }

        private void OnRendererModuleChanged()
        {
            VividParticleSystemManager.MarkRendererModuleDirty(this);
            RequestEditorRenderUpdate();
        }

        private void OnColorOverLifetimeModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkParticleDataDirty(
                this,
                VividParticleSystemManager.UploadColumnBaseColorMask);
            RequestEditorRenderUpdate();
        }

        private void OnVelocityOverLifetimeModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkParticleDataDirty(
                this,
                VividParticleSystemManager.UploadColumnVelocityStretchMask);
            RequestEditorRenderUpdate();
        }

        private void OnSizeOverLifetimeModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkParticleDataDirty(
                this,
                VividParticleSystemManager.UploadColumnPositionSizeMask
                | VividParticleSystemManager.UploadColumnScaleMask);
            RequestEditorRenderUpdate();
        }

        private void OnRotationOverLifetimeModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkParticleDataDirty(
                this,
                VividParticleSystemManager.UploadColumnRotationMask);
            RequestEditorRenderUpdate();
        }

        private void OnTextureSheetAnimationModuleChanged()
        {
            VividParticleSystemManager.NotifySettingsChanged(this);
            VividParticleSystemManager.MarkParticleDataDirty(
                this,
                VividParticleSystemManager.UploadColumnUVMask);
            RequestEditorRenderUpdate();
        }

        private void ValidateModules()
        {
            main.Validate();
            emission.Validate();
            shape.Validate();
            forceOverLifetime.Validate();
            velocityOverLifetime.Validate();
            limitVelocityOverLifetime.Validate();
            colorOverLifetime.Validate();
            colorBySpeed.Validate();
            sizeOverLifetime.Validate();
            sizeBySpeed.Validate();
            rotationOverLifetime.Validate();
            rotationBySpeed.Validate();
            noise.Validate();
            textureSheetAnimation.Validate();
            rendererModule.Validate();
        }

        private static void RequestEditorRenderUpdate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
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
