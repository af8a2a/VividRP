using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.Particle.ECS;

namespace VividRP.Runtime.Particle
{
    internal struct VividParticleNativeBurst
    {
        public float Time;
        public int Count;
    }

    internal struct VividParticleNativeSimulationConfig
    {
        public float Duration;
        public float StartLifetime;
        public float StartSpeed;
        public float StartSize;
        public float GravityModifier;
        public float RateOverTime;
        public float ShapeRadius;
        public float ShapeAngleRadians;
        public float3 ShapeBoxSize;
        public float3 ForceOverLifetime;
        public float4 StartColor;
        public uint RandomSeed;
        public int MaxParticles;
        public int Loop;
        public int UseAutoRandomSeed;
        public int EmissionEnabled;
        public int ShapeEnabled;
        public int ShapeType;
        public int SimulationSpace;
        public int ForceOverLifetimeEnabled;
        public int ForceOverLifetimeSpace;
        public int BurstOffset;
        public int BurstCount;
        public int Version;
    }

    internal unsafe struct VividParticleNativeRenderModuleConfig
    {
        public const int LifetimeLutResolution = 32;

        public int ColorOverLifetimeEnabled;
        public int ColorBySpeedEnabled;
        public int SizeOverLifetimeEnabled;
        public int SizeBySpeedEnabled;
        public int RotationOverLifetimeEnabled;
        public int RotationBySpeedEnabled;
        public int NoiseEnabled;
        public int NoiseDamping;
        public int NoiseQuality;
        public int NoiseRemapEnabled;
        public int NoiseOctaveCount;
        public int VelocityOverLifetimeEnabled;
        public int VelocityOverLifetimeSpace;
        public int InheritVelocityEnabled;
        public int InheritVelocityMode;
        public int LimitVelocityOverLifetimeEnabled;
        public int LimitVelocitySeparateAxes;
        public int LimitVelocitySpace;
        public int LimitVelocityMultiplyDragByParticleSize;
        public int LimitVelocityMultiplyDragByParticleVelocity;
        public float LimitVelocityDampen;
        public int TextureSheetAnimationEnabled;
        public int CustomData1Mode;
        public int CustomData2Mode;
        public int TextureSheetTilesX;
        public int TextureSheetTilesY;
        public int TextureSheetAnimationType;
        public int TextureSheetRowIndex;
        public float TextureSheetStartFrame;
        public float TextureSheetCycleCount;
        public float2 ColorBySpeedRange;
        public float2 SizeBySpeedRange;
        public float2 RotationBySpeedRange;
        public float NoiseFrequency;
        public float NoiseOctaveMultiplier;
        public float NoiseOctaveScale;
        public float NoiseSizeMaxMultiplier;
        public float SizeOverLifetimeMaxMultiplier;
        public float SizeBySpeedMaxMultiplier;
        public int Version;
        public fixed float ColorOverLifetimeLut[LifetimeLutResolution * 4];
        public fixed float ColorBySpeedLut[LifetimeLutResolution * 4];
        public fixed float SizeOverLifetimeLut[LifetimeLutResolution];
        public fixed float SizeBySpeedLut[LifetimeLutResolution];
        public fixed float RotationOverLifetimeLut[LifetimeLutResolution];
        public fixed float RotationBySpeedLut[LifetimeLutResolution * 3];
        public fixed float NoiseStrengthLut[LifetimeLutResolution * 3];
        public fixed float NoiseScrollSpeedLut[LifetimeLutResolution];
        public fixed float NoiseRemapLut[LifetimeLutResolution * 3];
        public fixed float NoisePositionAmountLut[LifetimeLutResolution];
        public fixed float NoiseRotationAmountLut[LifetimeLutResolution];
        public fixed float NoiseSizeAmountLut[LifetimeLutResolution];
        public fixed float VelocityOverLifetimeLut[LifetimeLutResolution * 3];
        public fixed float InheritVelocityLut[LifetimeLutResolution];
        public fixed float LimitVelocityLut[LifetimeLutResolution * 3];
        public fixed float LimitVelocityDragLut[LifetimeLutResolution];
        public fixed float TextureSheetFrameOverTimeLut[LifetimeLutResolution];
        public fixed float CustomData1Lut[LifetimeLutResolution * 4];
        public fixed float CustomData2Lut[LifetimeLutResolution * 4];
        public fixed float ExternalForcesMultiplierLut[LifetimeLutResolution];
    }

    internal struct VividParticleSimulationPrepareInput
    {
        public int SystemId;
        public int ConfigSlot;
        public int ActiveCount;
        public VividParticleSimulationTimeStep TimeStep;
    }

    internal struct VividParticleSimulationPrepareOutput
    {
        public int SystemId;
        public int ShouldSchedule;
        public float3 Gravity;
        public VividParticleSimulationTimeStep TimeStep;
    }

    internal unsafe struct VividParticleEmissionPlanInput
    {
        public int SystemId;
        public int ConfigSlot;
        public int AllowEmission;
        public float DeltaTime;
        public float Time;
        public float EmissionAccumulator;
        public ulong BurstTriggeredMask;
        public uint RandomState;
        public int CanReserveNative;

        [NativeDisableUnsafePtrRestriction]
        public int* ActiveCountOutput;

        public VividParticleEcsInitializeParticlesWork InitializeTemplate;
    }

    internal unsafe struct VividParticleEmissionPlanOutput
    {
        public int SystemId;
        public int EmitCount;
        public int RequiresManagedFallback;
        public float Time;
        public float EmissionAccumulator;
        public ulong BurstTriggeredMask;
        public uint RandomState;
        public int ReservedCount;
        public VividParticleEcsInitializeParticlesWork InitializeWork;
    }

    internal readonly struct VividParticleSimulationTimeStep
    {
        public readonly float DeltaTime;
        public readonly int IsActiveAndEnabled;
        public readonly int IsPlaying;
        public readonly int IsPaused;
        public readonly int StopEmitting;
        public readonly int Layer;
        public readonly float3 TransformPosition;
        public readonly float4x4 LocalToWorldMatrix;
        public readonly quaternion WorldRotation;

        public VividParticleSimulationTimeStep(
            float deltaTime,
            bool isActiveAndEnabled,
            bool isPlaying,
            bool isPaused,
            bool stopEmitting,
            int layer,
            Vector3 transformPosition,
            Matrix4x4 localToWorldMatrix,
            Quaternion worldRotation)
        {
            DeltaTime = math.max(0.0f, deltaTime);
            IsActiveAndEnabled = isActiveAndEnabled ? 1 : 0;
            IsPlaying = isPlaying ? 1 : 0;
            IsPaused = isPaused ? 1 : 0;
            StopEmitting = stopEmitting ? 1 : 0;
            Layer = layer;
            TransformPosition = new float3(transformPosition.x, transformPosition.y, transformPosition.z);
            LocalToWorldMatrix = ToFloat4x4(localToWorldMatrix);
            WorldRotation = new quaternion(worldRotation.x, worldRotation.y, worldRotation.z, worldRotation.w);
        }

        public bool allowEmission => IsPlaying != 0 && StopEmitting == 0;

        public bool RequiresAutomaticUpdate(int activeCount, bool requireActive)
        {
            return (!requireActive || IsActiveAndEnabled != 0)
                && IsPaused == 0
                && (IsPlaying != 0 || (StopEmitting != 0 && activeCount > 0));
        }

        public Vector3 ToTransformPosition()
        {
            return new Vector3(TransformPosition.x, TransformPosition.y, TransformPosition.z);
        }

        public Matrix4x4 ToMatrix4x4()
        {
            var value = new Matrix4x4();
            value.m00 = LocalToWorldMatrix.c0.x;
            value.m10 = LocalToWorldMatrix.c0.y;
            value.m20 = LocalToWorldMatrix.c0.z;
            value.m30 = LocalToWorldMatrix.c0.w;
            value.m01 = LocalToWorldMatrix.c1.x;
            value.m11 = LocalToWorldMatrix.c1.y;
            value.m21 = LocalToWorldMatrix.c1.z;
            value.m31 = LocalToWorldMatrix.c1.w;
            value.m02 = LocalToWorldMatrix.c2.x;
            value.m12 = LocalToWorldMatrix.c2.y;
            value.m22 = LocalToWorldMatrix.c2.z;
            value.m32 = LocalToWorldMatrix.c2.w;
            value.m03 = LocalToWorldMatrix.c3.x;
            value.m13 = LocalToWorldMatrix.c3.y;
            value.m23 = LocalToWorldMatrix.c3.z;
            value.m33 = LocalToWorldMatrix.c3.w;
            return value;
        }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(WorldRotation.value.x, WorldRotation.value.y, WorldRotation.value.z, WorldRotation.value.w);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }
    }

    internal readonly struct VividParticleSystemFrameSnapshot
    {
        public readonly float DeltaTime;
        public readonly bool IsActiveAndEnabled;
        public readonly bool IsPlaying;
        public readonly bool IsPaused;
        public readonly bool StopEmitting;
        public readonly float Duration;
        public readonly bool Loop;
        public readonly float StartLifetime;
        public readonly float StartSpeed;
        public readonly float StartSize;
        public readonly Color StartColor;
        public readonly float GravityModifier;
        public readonly VividParticleSystemSimulationSpace SimulationSpace;
        public readonly int MaxParticles;
        public readonly uint RandomSeed;
        public readonly bool UseAutoRandomSeed;
        public readonly bool EmissionEnabled;
        public readonly float RateOverTime;
        public readonly VividParticleBurst[] Bursts;
        public readonly bool ShapeEnabled;
        public readonly VividParticleShapeType ShapeType;
        public readonly float ShapeRadius;
        public readonly Vector3 ShapeBoxSize;
        public readonly float ShapeAngle;
        public readonly bool ForceOverLifetimeEnabled;
        public readonly Vector3 ForceOverLifetime;
        public readonly VividParticleForceSpace ForceOverLifetimeSpace;
        public readonly bool RendererEnabled;
        public readonly VividParticleRenderMode RenderMode;
        public readonly Material RendererMaterial;
        public readonly Mesh RendererMesh;
        public readonly int RendererMeshCount;
        public readonly Color RendererColor;
        public readonly float RendererSizeScale;
        public readonly float StretchLengthScale;
        public readonly float StretchSpeedScale;
        public readonly int RenderQueueOffset;
        public readonly int Layer;
        public readonly Vector3 TransformPosition;
        public readonly Matrix4x4 LocalToWorldMatrix;
        public readonly Quaternion WorldRotation;
        public readonly int EntityHash;

        public VividParticleSystemFrameSnapshot(
            float deltaTime,
            bool isActiveAndEnabled,
            bool isPlaying,
            bool isPaused,
            bool stopEmitting,
            float duration,
            bool loop,
            float startLifetime,
            float startSpeed,
            float startSize,
            Color startColor,
            float gravityModifier,
            VividParticleSystemSimulationSpace simulationSpace,
            int maxParticles,
            uint randomSeed,
            bool useAutoRandomSeed,
            bool emissionEnabled,
            float rateOverTime,
            VividParticleBurst[] bursts,
            bool shapeEnabled,
            VividParticleShapeType shapeType,
            float shapeRadius,
            Vector3 shapeBoxSize,
            float shapeAngle,
            bool forceOverLifetimeEnabled,
            Vector3 forceOverLifetime,
            VividParticleForceSpace forceOverLifetimeSpace,
            bool rendererEnabled,
            VividParticleRenderMode renderMode,
            Material rendererMaterial,
            Mesh rendererMesh,
            int rendererMeshCount,
            Color rendererColor,
            float rendererSizeScale,
            float stretchLengthScale,
            float stretchSpeedScale,
            int renderQueueOffset,
            int layer,
            Vector3 transformPosition,
            Matrix4x4 localToWorldMatrix,
            Quaternion worldRotation,
            int entityHash)
        {
            DeltaTime = Mathf.Max(0.0f, deltaTime);
            IsActiveAndEnabled = isActiveAndEnabled;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            StopEmitting = stopEmitting;
            Duration = Mathf.Max(VividParticleMainModule.MinimumDuration, duration);
            Loop = loop;
            StartLifetime = Mathf.Max(VividParticleMainModule.MinimumStartLifetime, startLifetime);
            StartSpeed = startSpeed;
            StartSize = Mathf.Max(VividParticleMainModule.MinimumStartSize, startSize);
            StartColor = startColor;
            GravityModifier = gravityModifier;
            SimulationSpace = simulationSpace;
            MaxParticles = Mathf.Max(VividParticleMainModule.MinimumMaxParticles, maxParticles);
            RandomSeed = randomSeed;
            UseAutoRandomSeed = useAutoRandomSeed;
            EmissionEnabled = emissionEnabled;
            RateOverTime = Mathf.Max(0.0f, rateOverTime);
            Bursts = bursts ?? Array.Empty<VividParticleBurst>();
            ShapeEnabled = shapeEnabled;
            ShapeType = shapeType;
            ShapeRadius = Mathf.Max(VividParticleShapeModule.MinimumRadius, shapeRadius);
            ShapeBoxSize = new Vector3(
                Mathf.Max(0.0f, shapeBoxSize.x),
                Mathf.Max(0.0f, shapeBoxSize.y),
                Mathf.Max(0.0f, shapeBoxSize.z));
            ShapeAngle = Mathf.Clamp(shapeAngle, 0.0f, 89.0f);
            ForceOverLifetimeEnabled = forceOverLifetimeEnabled;
            ForceOverLifetime = forceOverLifetime;
            ForceOverLifetimeSpace = forceOverLifetimeSpace;
            RendererEnabled = rendererEnabled;
            RenderMode = renderMode;
            RendererMaterial = rendererMaterial;
            RendererMesh = rendererMesh;
            RendererMeshCount = Mathf.Max(0, rendererMeshCount);
            RendererColor = rendererColor;
            RendererSizeScale = Mathf.Max(VividParticleRendererModule.MinimumSizeScale, rendererSizeScale);
            StretchLengthScale = Mathf.Max(VividParticleRendererModule.MinimumStretchLengthScale, stretchLengthScale);
            StretchSpeedScale = Mathf.Max(VividParticleRendererModule.MinimumStretchSpeedScale, stretchSpeedScale);
            RenderQueueOffset = renderQueueOffset;
            Layer = layer;
            TransformPosition = transformPosition;
            LocalToWorldMatrix = localToWorldMatrix;
            WorldRotation = worldRotation;
            EntityHash = entityHash;
        }

        public VividParticleSystemFrameSnapshot WithFrameState(
            float deltaTime,
            bool isActiveAndEnabled,
            bool isPlaying,
            bool isPaused,
            bool stopEmitting,
            int layer,
            Vector3 transformPosition,
            Matrix4x4 localToWorldMatrix,
            Quaternion worldRotation)
        {
            return new VividParticleSystemFrameSnapshot(
                deltaTime,
                isActiveAndEnabled,
                isPlaying,
                isPaused,
                stopEmitting,
                Duration,
                Loop,
                StartLifetime,
                StartSpeed,
                StartSize,
                StartColor,
                GravityModifier,
                SimulationSpace,
                MaxParticles,
                RandomSeed,
                UseAutoRandomSeed,
                EmissionEnabled,
                RateOverTime,
                Bursts,
                ShapeEnabled,
                ShapeType,
                ShapeRadius,
                ShapeBoxSize,
                ShapeAngle,
                ForceOverLifetimeEnabled,
                ForceOverLifetime,
                ForceOverLifetimeSpace,
                RendererEnabled,
                RenderMode,
                RendererMaterial,
                RendererMesh,
                RendererMeshCount,
                RendererColor,
                RendererSizeScale,
                StretchLengthScale,
                StretchSpeedScale,
                RenderQueueOffset,
                layer,
                transformPosition,
                localToWorldMatrix,
                worldRotation,
                EntityHash);
        }

        public VividParticleSystemFrameSnapshot WithFrameState(VividParticleSimulationTimeStep timeStep)
        {
            return new VividParticleSystemFrameSnapshot(
                timeStep.DeltaTime,
                timeStep.IsActiveAndEnabled != 0,
                timeStep.IsPlaying != 0,
                timeStep.IsPaused != 0,
                timeStep.StopEmitting != 0,
                Duration,
                Loop,
                StartLifetime,
                StartSpeed,
                StartSize,
                StartColor,
                GravityModifier,
                SimulationSpace,
                MaxParticles,
                RandomSeed,
                UseAutoRandomSeed,
                EmissionEnabled,
                RateOverTime,
                Bursts,
                ShapeEnabled,
                ShapeType,
                ShapeRadius,
                ShapeBoxSize,
                ShapeAngle,
                ForceOverLifetimeEnabled,
                ForceOverLifetime,
                ForceOverLifetimeSpace,
                RendererEnabled,
                RenderMode,
                RendererMaterial,
                RendererMesh,
                RendererMeshCount,
                RendererColor,
                RendererSizeScale,
                StretchLengthScale,
                StretchSpeedScale,
                RenderQueueOffset,
                timeStep.Layer,
                timeStep.ToTransformPosition(),
                timeStep.ToMatrix4x4(),
                timeStep.ToQuaternion(),
                EntityHash);
        }
    }
}
