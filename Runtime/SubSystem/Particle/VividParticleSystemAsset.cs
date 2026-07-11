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
        private VividParticleRendererModule m_Renderer = VividParticleRendererModule.CreateDefault();

        public VividParticleMainModule main => m_Main ??= VividParticleMainModule.CreateDefault();

        public VividParticleEmissionModule emission => m_Emission ??= VividParticleEmissionModule.CreateDefault();

        public VividParticleShapeModule shape => m_Shape ??= VividParticleShapeModule.CreateDefault();

        public VividParticleForceOverLifetimeModule forceOverLifetime =>
            m_ForceOverLifetime ??= VividParticleForceOverLifetimeModule.CreateDefault();

        public VividParticleRendererModule rendererModule => m_Renderer ??= VividParticleRendererModule.CreateDefault();

        internal void CopyModulesTo(
            VividParticleMainModule targetMain,
            VividParticleEmissionModule targetEmission,
            VividParticleShapeModule targetShape,
            VividParticleForceOverLifetimeModule targetForceOverLifetime,
            VividParticleRendererModule targetRenderer)
        {
            targetMain?.CopyFrom(main);
            targetEmission?.CopyFrom(emission);
            targetShape?.CopyFrom(shape);
            targetForceOverLifetime?.CopyFrom(forceOverLifetime);
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
            rendererModule.Validate();
        }
    }
}
