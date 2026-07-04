using System;
using UnityEngine;

namespace VividRP.Runtime.Particle
{
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
        public readonly bool RendererEnabled;
        public readonly Material RendererMaterial;
        public readonly Color RendererColor;
        public readonly float RendererSizeScale;
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
            bool rendererEnabled,
            Material rendererMaterial,
            Color rendererColor,
            float rendererSizeScale,
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
            RendererEnabled = rendererEnabled;
            RendererMaterial = rendererMaterial;
            RendererColor = rendererColor;
            RendererSizeScale = Mathf.Max(VividParticleRendererModule.MinimumSizeScale, rendererSizeScale);
            RenderQueueOffset = renderQueueOffset;
            Layer = layer;
            TransformPosition = transformPosition;
            LocalToWorldMatrix = localToWorldMatrix;
            WorldRotation = worldRotation;
            EntityHash = entityHash;
        }
    }
}
