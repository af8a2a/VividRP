using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Constant buffer data for world light cluster shader access.
    /// </summary>
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    internal struct ShaderVariablesWorldLightCluster
    {
        public float3 _WorldLightGridMin;
        public int _WorldLightGridResolution;
        
        public float _WorldLightGridCellSize;
        public float _WorldLightGridInvCellSize;
        public int _WorldLightCount;
        public int _WorldLightMaxPerCell;
    }
}
