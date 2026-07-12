using UnityEngine;

namespace VividRP.Runtime.Particle
{
    [CreateAssetMenu(menuName = "VividRP/Particles/Vivid Particle System", fileName = "New Vivid Particle System")]
    public sealed class VividParticleSystemAsset : ScriptableObject
    {
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
        private VividParticleExternalForcesModule m_ExternalForces =
            VividParticleExternalForcesModule.CreateDefault();

        [SerializeField]
        private VividParticleCollisionModule m_Collision =
            VividParticleCollisionModule.CreateDefault();

        [SerializeField]
        private VividParticleTriggerModule m_Trigger =
            VividParticleTriggerModule.CreateDefault();

        [SerializeField]
        private VividParticleVelocityOverLifetimeModule m_VelocityOverLifetime =
            VividParticleVelocityOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleInheritVelocityModule m_InheritVelocity =
            VividParticleInheritVelocityModule.CreateDefault();

        [SerializeField]
        private VividParticleLifetimeByEmitterSpeedModule m_LifetimeByEmitterSpeed =
            VividParticleLifetimeByEmitterSpeedModule.CreateDefault();

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
        private VividParticleCustomDataModule m_CustomData =
            VividParticleCustomDataModule.CreateDefault();

        [SerializeField]
        private VividParticleTextureSheetAnimationModule m_TextureSheetAnimation =
            VividParticleTextureSheetAnimationModule.CreateDefault();

        [SerializeField]
        private VividParticleLightsModule m_Lights = VividParticleLightsModule.CreateDefault();

        [SerializeField]
        private VividParticleRendererModule m_Renderer = VividParticleRendererModule.CreateDefault();

        public VividParticleMainModule main => m_Main ??= VividParticleMainModule.CreateDefault();

        public VividParticleEmissionModule emission => m_Emission ??= VividParticleEmissionModule.CreateDefault();

        public VividParticleShapeModule shape => m_Shape ??= VividParticleShapeModule.CreateDefault();

        public VividParticleForceOverLifetimeModule forceOverLifetime =>
            m_ForceOverLifetime ??= VividParticleForceOverLifetimeModule.CreateDefault();

        public VividParticleExternalForcesModule externalForces =>
            m_ExternalForces ??= VividParticleExternalForcesModule.CreateDefault();

        public VividParticleCollisionModule collision =>
            m_Collision ??= VividParticleCollisionModule.CreateDefault();

        public VividParticleTriggerModule trigger =>
            m_Trigger ??= VividParticleTriggerModule.CreateDefault();

        public VividParticleVelocityOverLifetimeModule velocityOverLifetime =>
            m_VelocityOverLifetime ??= VividParticleVelocityOverLifetimeModule.CreateDefault();

        public VividParticleInheritVelocityModule inheritVelocity =>
            m_InheritVelocity ??= VividParticleInheritVelocityModule.CreateDefault();

        public VividParticleLifetimeByEmitterSpeedModule lifetimeByEmitterSpeed =>
            m_LifetimeByEmitterSpeed ??= VividParticleLifetimeByEmitterSpeedModule.CreateDefault();

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

        public VividParticleCustomDataModule customData =>
            m_CustomData ??= VividParticleCustomDataModule.CreateDefault();

        public VividParticleTextureSheetAnimationModule textureSheetAnimation =>
            m_TextureSheetAnimation ??= VividParticleTextureSheetAnimationModule.CreateDefault();

        public VividParticleLightsModule lights =>
            m_Lights ??= VividParticleLightsModule.CreateDefault();

        public VividParticleRendererModule rendererModule => m_Renderer ??= VividParticleRendererModule.CreateDefault();

        internal void CopyModulesTo(
            VividParticleMainModule targetMain,
            VividParticleEmissionModule targetEmission,
            VividParticleShapeModule targetShape,
            VividParticleForceOverLifetimeModule targetForceOverLifetime,
            VividParticleExternalForcesModule targetExternalForces,
            VividParticleCollisionModule targetCollision,
            VividParticleTriggerModule targetTrigger,
            VividParticleVelocityOverLifetimeModule targetVelocityOverLifetime,
            VividParticleInheritVelocityModule targetInheritVelocity,
            VividParticleLifetimeByEmitterSpeedModule targetLifetimeByEmitterSpeed,
            VividParticleLimitVelocityOverLifetimeModule targetLimitVelocityOverLifetime,
            VividParticleColorOverLifetimeModule targetColorOverLifetime,
            VividParticleColorBySpeedModule targetColorBySpeed,
            VividParticleSizeOverLifetimeModule targetSizeOverLifetime,
            VividParticleSizeBySpeedModule targetSizeBySpeed,
            VividParticleRotationOverLifetimeModule targetRotationOverLifetime,
            VividParticleRotationBySpeedModule targetRotationBySpeed,
            VividParticleNoiseModule targetNoise,
            VividParticleCustomDataModule targetCustomData,
            VividParticleTextureSheetAnimationModule targetTextureSheetAnimation,
            VividParticleLightsModule targetLights,
            VividParticleRendererModule targetRenderer)
        {
            targetMain?.CopyFrom(main);
            targetEmission?.CopyFrom(emission);
            targetShape?.CopyFrom(shape);
            targetForceOverLifetime?.CopyFrom(forceOverLifetime);
            targetExternalForces?.CopyFrom(externalForces);
            targetCollision?.CopyFrom(collision);
            targetTrigger?.CopyFrom(trigger);
            targetVelocityOverLifetime?.CopyFrom(velocityOverLifetime);
            targetInheritVelocity?.CopyFrom(inheritVelocity);
            targetLifetimeByEmitterSpeed?.CopyFrom(lifetimeByEmitterSpeed);
            targetLimitVelocityOverLifetime?.CopyFrom(limitVelocityOverLifetime);
            targetColorOverLifetime?.CopyFrom(colorOverLifetime);
            targetColorBySpeed?.CopyFrom(colorBySpeed);
            targetSizeOverLifetime?.CopyFrom(sizeOverLifetime);
            targetSizeBySpeed?.CopyFrom(sizeBySpeed);
            targetRotationOverLifetime?.CopyFrom(rotationOverLifetime);
            targetRotationBySpeed?.CopyFrom(rotationBySpeed);
            targetNoise?.CopyFrom(noise);
            targetCustomData?.CopyFrom(customData);
            targetTextureSheetAnimation?.CopyFrom(textureSheetAnimation);
            targetLights?.CopyFrom(lights);
            targetRenderer?.CopyFrom(rendererModule);
        }

        private void OnValidate()
        {
            Validate();
        }

        internal void Validate()
        {
            main.Validate();
            emission.Validate();
            shape.Validate();
            forceOverLifetime.Validate();
            externalForces.Validate();
            collision.Validate();
            trigger.Validate();
            velocityOverLifetime.Validate();
            inheritVelocity.Validate();
            lifetimeByEmitterSpeed.Validate();
            limitVelocityOverLifetime.Validate();
            colorOverLifetime.Validate();
            colorBySpeed.Validate();
            sizeOverLifetime.Validate();
            sizeBySpeed.Validate();
            rotationOverLifetime.Validate();
            rotationBySpeed.Validate();
            noise.Validate();
            customData.Validate();
            textureSheetAnimation.Validate();
            lights.Validate();
            rendererModule.Validate();
        }
    }
}
