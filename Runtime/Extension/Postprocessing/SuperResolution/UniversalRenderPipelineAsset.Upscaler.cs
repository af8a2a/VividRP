using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public class DLSSPreset
    {
        /// <summary> Specifies the DLSS Render Preset to use for the Quality performance quality setting.</summary>
        public uint DLSSRenderPresetForQuality = 0;

        /// <summary> Specifies the DLSS Render Preset to use for the Balanced performance quality setting.</summary>
        public uint DLSSRenderPresetForBalanced = 0;

        /// <summary> Specifies the DLSS Render Preset to use for the Performance performance quality setting.</summary>
        public uint DLSSRenderPresetForPerformance = 0;

        /// <summary> Specifies the DLSS Render Preset to use for the UltraPerformance performance quality setting.</summary>
        public uint DLSSRenderPresetForUltraPerformance = 0;

        /// <summary> Specifies the DLSS Render Preset to use for the DLAA performance quality setting.</summary>
        public uint DLSSRenderPresetForDLAA = 0;
    }


    partial class UniversalRenderPipelineAsset
    {
        [SerializeField] internal DLSSPreset m_DLSSPreset = new DLSSPreset();
        
    }
}