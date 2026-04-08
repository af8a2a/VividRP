using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class DDGIRuntimeData : ContextItem
    {
        internal bool supportsRayTracing;
        internal bool hasActiveVolume;
        internal bool isRuntimeReady;
        internal bool clearProbeTextures;
        internal DDGIVolume activeVolume;
        internal int probesPerPlane;
        internal DDGIProfileId profileId;
        internal DDGIRootConstants rootConstants;
        internal ShaderVariablesDDGI shaderVariables;
        internal GraphicsBuffer volumeConstantsBuffer;
        internal GraphicsBuffer instanceBuffer;
        internal GraphicsBuffer subMeshBuffer;
        internal GraphicsBuffer materialBuffer;
        internal GraphicsBuffer vertexBuffer;
        internal GraphicsBuffer indexBuffer;
        internal GraphicsBuffer directionalLightBuffer;
        internal GraphicsBuffer punctualLightBuffer;

        public override void Reset()
        {
            supportsRayTracing = false;
            hasActiveVolume = false;
            isRuntimeReady = false;
            clearProbeTextures = false;
            activeVolume = null;
            probesPerPlane = 0;
            profileId = DDGIProfileId.Balanced;
            rootConstants = default;
            shaderVariables = ShaderVariablesDDGI.CreateDisabled();
            volumeConstantsBuffer = null;
            instanceBuffer = null;
            subMeshBuffer = null;
            materialBuffer = null;
            vertexBuffer = null;
            indexBuffer = null;
            directionalLightBuffer = null;
            punctualLightBuffer = null;
        }
    }
}
