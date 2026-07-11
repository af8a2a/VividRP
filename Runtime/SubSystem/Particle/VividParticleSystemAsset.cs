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
        private VividParticleVelocityOverLifetimeModule m_VelocityOverLifetime =
            VividParticleVelocityOverLifetimeModule.CreateDefault();

        [SerializeField]
        private VividParticleInheritVelocityModule m_InheritVelocity =
            VividParticleInheritVelocityModule.CreateDefault();

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
        private VividParticleRendererModule m_Renderer = VividParticleRendererModule.CreateDefault();

        public VividParticleMainModule main => m_Main ??= VividParticleMainModule.CreateDefault();

        public VividParticleEmissionModule emission => m_Emission ??= VividParticleEmissionModule.CreateDefault();

        public VividParticleShapeModule shape => m_Shape ??= VividParticleShapeModule.CreateDefault();

        public VividParticleForceOverLifetimeModule forceOverLifetime =>
            m_ForceOverLifetime ??= VividParticleForceOverLifetimeModule.CreateDefault();

        public VividParticleVelocityOverLifetimeModule velocityOverLifetime =>
            m_VelocityOverLifetime ??= VividParticleVelocityOverLifetimeModule.CreateDefault();

        public VividParticleInheritVelocityModule inheritVelocity =>
            m_InheritVelocity ??= VividParticleInheritVelocityModule.CreateDefault();

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

        public VividParticleRendererModule rendererModule => m_Renderer ??= VividParticleRendererModule.CreateDefault();

        internal void CopyModulesTo(
            VividParticleMainModule targetMain,
            VividParticleEmissionModule targetEmission,
            VividParticleShapeModule targetShape,
            VividParticleForceOverLifetimeModule targetForceOverLifetime,
            VividParticleVelocityOverLifetimeModule targetVelocityOverLifetime,
            VividParticleInheritVelocityModule targetInheritVelocity,
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
            VividParticleRendererModule targetRenderer)
        {
            targetMain?.CopyFrom(main);
            targetEmission?.CopyFrom(emission);
            targetShape?.CopyFrom(shape);
            targetForceOverLifetime?.CopyFrom(forceOverLifetime);
            targetVelocityOverLifetime?.CopyFrom(velocityOverLifetime);
            targetInheritVelocity?.CopyFrom(inheritVelocity);
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
            velocityOverLifetime.Validate();
            inheritVelocity.Validate();
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
            rendererModule.Validate();
        }
    }
}
