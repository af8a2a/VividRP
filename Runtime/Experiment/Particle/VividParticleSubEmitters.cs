using System;
using UnityEngine;

namespace VividRP.Runtime.Particle
{
    public enum VividParticleSubEmitterType
    {
        Birth,
        Collision,
        Death,
        Trigger,
        Manual,
    }

    [Flags]
    public enum VividParticleSubEmitterProperties
    {
        InheritNothing = 0,
        InheritColor = 1 << 0,
        InheritSize = 1 << 1,
        InheritRotation = 1 << 2,
        InheritLifetime = 1 << 3,
        InheritVelocity = 1 << 4,
        InheritEverything = InheritColor
            | InheritSize
            | InheritRotation
            | InheritLifetime
            | InheritVelocity,
    }

    [Serializable]
    public struct VividParticleSubEmitter
    {
        [SerializeField]
        private VividParticleSystem m_System;

        [SerializeField]
        private VividParticleSubEmitterType m_Type;

        [SerializeField]
        private VividParticleSubEmitterProperties m_Properties;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        private float m_EmitProbability;

        public VividParticleSubEmitter(
            VividParticleSystem system,
            VividParticleSubEmitterType type,
            VividParticleSubEmitterProperties properties = VividParticleSubEmitterProperties.InheritNothing,
            float emitProbability = 1.0f)
        {
            m_System = system;
            m_Type = type;
            m_Properties = properties;
            m_EmitProbability = Mathf.Clamp01(emitProbability);
        }

        public VividParticleSystem system
        {
            readonly get => m_System;
            set => m_System = value;
        }

        public VividParticleSubEmitterType type
        {
            readonly get => m_Type;
            set => m_Type = value;
        }

        public VividParticleSubEmitterProperties properties
        {
            readonly get => m_Properties;
            set => m_Properties = value & VividParticleSubEmitterProperties.InheritEverything;
        }

        public float emitProbability
        {
            readonly get => m_EmitProbability;
            set => m_EmitProbability = Mathf.Clamp01(value);
        }

        internal void Validate()
        {
            m_Properties &= VividParticleSubEmitterProperties.InheritEverything;
            m_EmitProbability = Mathf.Clamp01(m_EmitProbability);
        }
    }

    [Serializable]
    public sealed class VividParticleSubEmittersModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private VividParticleSubEmitter[] m_SubEmitters = Array.Empty<VividParticleSubEmitter>();

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value)
                    return;

                m_Enabled = value;
                NotifyChanged();
            }
        }

        public int subEmittersCount => m_SubEmitters?.Length ?? 0;

        public VividParticleSubEmitter[] subEmitters
        {
            get => Clone(m_SubEmitters);
            set
            {
                m_SubEmitters = Clone(value);
                Validate();
                NotifyChanged();
            }
        }

        public void AddSubEmitter(
            VividParticleSystem system,
            VividParticleSubEmitterType type,
            VividParticleSubEmitterProperties properties = VividParticleSubEmitterProperties.InheritNothing,
            float emitProbability = 1.0f)
        {
            int count = subEmittersCount;
            Array.Resize(ref m_SubEmitters, count + 1);
            m_SubEmitters[count] = new VividParticleSubEmitter(
                system,
                type,
                properties,
                emitProbability);
            NotifyChanged();
        }

        public void RemoveSubEmitter(int index)
        {
            ValidateIndex(index);
            int count = subEmittersCount;
            for (int sourceIndex = index + 1; sourceIndex < count; sourceIndex++)
                m_SubEmitters[sourceIndex - 1] = m_SubEmitters[sourceIndex];
            Array.Resize(ref m_SubEmitters, count - 1);
            NotifyChanged();
        }

        public VividParticleSystem GetSubEmitterSystem(int index)
        {
            ValidateIndex(index);
            return m_SubEmitters[index].system;
        }

        public VividParticleSubEmitterType GetSubEmitterType(int index)
        {
            ValidateIndex(index);
            return m_SubEmitters[index].type;
        }

        public VividParticleSubEmitterProperties GetSubEmitterProperties(int index)
        {
            ValidateIndex(index);
            return m_SubEmitters[index].properties;
        }

        public float GetSubEmitterEmitProbability(int index)
        {
            ValidateIndex(index);
            return m_SubEmitters[index].emitProbability;
        }

        public void SetSubEmitterSystem(int index, VividParticleSystem system)
        {
            ValidateIndex(index);
            VividParticleSubEmitter entry = m_SubEmitters[index];
            entry.system = system;
            m_SubEmitters[index] = entry;
            NotifyChanged();
        }

        public void SetSubEmitterType(int index, VividParticleSubEmitterType type)
        {
            ValidateIndex(index);
            VividParticleSubEmitter entry = m_SubEmitters[index];
            entry.type = type;
            m_SubEmitters[index] = entry;
            NotifyChanged();
        }

        public void SetSubEmitterProperties(int index, VividParticleSubEmitterProperties properties)
        {
            ValidateIndex(index);
            VividParticleSubEmitter entry = m_SubEmitters[index];
            entry.properties = properties;
            m_SubEmitters[index] = entry;
            NotifyChanged();
        }

        public void SetSubEmitterEmitProbability(int index, float emitProbability)
        {
            ValidateIndex(index);
            VividParticleSubEmitter entry = m_SubEmitters[index];
            entry.emitProbability = emitProbability;
            m_SubEmitters[index] = entry;
            NotifyChanged();
        }

        internal static VividParticleSubEmittersModule CreateDefault()
        {
            return new VividParticleSubEmittersModule();
        }

        internal void CopyFrom(VividParticleSubEmittersModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_SubEmitters = Clone(source.m_SubEmitters);
            Validate();
        }

        internal void Validate()
        {
            m_SubEmitters ??= Array.Empty<VividParticleSubEmitter>();
            for (int index = 0; index < m_SubEmitters.Length; index++)
            {
                VividParticleSubEmitter entry = m_SubEmitters[index];
                entry.Validate();
                m_SubEmitters[index] = entry;
            }
        }

        internal bool HasType(VividParticleSubEmitterType type)
        {
            if (!m_Enabled || m_SubEmitters == null)
                return false;

            for (int index = 0; index < m_SubEmitters.Length; index++)
            {
                if (m_SubEmitters[index].system != null && m_SubEmitters[index].type == type)
                    return true;
            }

            return false;
        }

        internal VividParticleSubEmitter GetEntryUnchecked(int index)
        {
            return m_SubEmitters[index];
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        private void ValidateIndex(int index)
        {
            if ((uint)index >= (uint)subEmittersCount)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static VividParticleSubEmitter[] Clone(VividParticleSubEmitter[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<VividParticleSubEmitter>();

            var clone = new VividParticleSubEmitter[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }
}
