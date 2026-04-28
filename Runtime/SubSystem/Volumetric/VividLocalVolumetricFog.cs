using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum VividLocalVolumetricFogBlendingMode
    {
        Overwrite = 0,
        Additive = 1,
        Multiply = 2,
        Min = 3,
        Max = 4
    }

    public enum VividLocalVolumetricFogFalloffMode
    {
        Linear = 0,
        Exponential = 1
    }

    public enum VividLocalVolumetricFogMaskMode
    {
        Texture = 0,
        Material = 1,
        None = 2
    }

    public enum VividLocalVolumetricFogScaleMode
    {
        Transform = 0,
        Size = 1
    }

    [Serializable]
    public struct VividLocalVolumetricFogArtistParameters
    {
        internal const float MinimumFogDistance = 0.05f;

        public Color albedo;
        [Min(MinimumFogDistance)] public float meanFreePath;
        public VividLocalVolumetricFogBlendingMode blendingMode;
        public int priority;
        [Range(-1.0f, 1.0f)] public float anisotropy;
        public Texture3D volumeMask;
        public Material materialMask;
        public Vector3 textureScrollingSpeed;
        public Vector3 textureTiling;
        [Min(0.0f)] public Vector3 positiveFade;
        [Min(0.0f)] public Vector3 negativeFade;
        public VividLocalVolumetricFogScaleMode scaleMode;
        public Vector3 size;
        public bool invertFade;
        [Min(0.0f)] public float distanceFadeStart;
        [Min(0.0f)] public float distanceFadeEnd;
        public Vector3 textureOffset;
        public VividLocalVolumetricFogFalloffMode falloffMode;
        public VividLocalVolumetricFogMaskMode maskMode;

        public static VividLocalVolumetricFogArtistParameters CreateDefault()
        {
            return new VividLocalVolumetricFogArtistParameters
            {
                albedo = Color.white,
                meanFreePath = 50.0f,
                blendingMode = VividLocalVolumetricFogBlendingMode.Additive,
                priority = 0,
                anisotropy = 0.0f,
                volumeMask = null,
                materialMask = null,
                textureScrollingSpeed = Vector3.zero,
                textureTiling = Vector3.one,
                positiveFade = Vector3.one * 0.1f,
                negativeFade = Vector3.one * 0.1f,
                scaleMode = VividLocalVolumetricFogScaleMode.Transform,
                size = Vector3.one,
                invertFade = false,
                distanceFadeStart = 10000.0f,
                distanceFadeEnd = 10000.0f,
                textureOffset = Vector3.zero,
                falloffMode = VividLocalVolumetricFogFalloffMode.Linear,
                maskMode = VividLocalVolumetricFogMaskMode.Texture
            };
        }

        public void Validate()
        {
            albedo.r = Mathf.Clamp01(albedo.r);
            albedo.g = Mathf.Clamp01(albedo.g);
            albedo.b = Mathf.Clamp01(albedo.b);
            albedo.a = 1.0f;
            meanFreePath = Mathf.Max(meanFreePath, MinimumFogDistance);
            anisotropy = Mathf.Clamp(anisotropy, -1.0f, 1.0f);
            positiveFade = Max(positiveFade, Vector3.zero);
            negativeFade = Max(negativeFade, Vector3.zero);
            size = Max(size, new Vector3(0.001f, 0.001f, 0.001f));
            distanceFadeStart = Mathf.Max(distanceFadeStart, 0.0f);
            distanceFadeEnd = Mathf.Max(distanceFadeStart, distanceFadeEnd);
            textureTiling = Max(textureTiling, Vector3.zero);
        }

        internal Vector3 GetScattering(float extinction)
        {
            return new Vector3(albedo.r * extinction, albedo.g * extinction, albedo.b * extinction);
        }

        private static Vector3 Max(Vector3 value, Vector3 minimum)
        {
            return new Vector3(
                Mathf.Max(value.x, minimum.x),
                Mathf.Max(value.y, minimum.y),
                Mathf.Max(value.z, minimum.z));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividLocalVolumetricFogEngineData
    {
        public Vector4 worldToLocalRow0;
        public Vector4 worldToLocalRow1;
        public Vector4 worldToLocalRow2;
        public Vector4 scatteringExtinction;
        public Vector4 positiveFade;
        public Vector4 negativeFade;
        public Vector4 distanceFade;
        public Vector4 parameters;
        public Vector4 textureScaleOffset0;
        public Vector4 textureScaleOffset1;

        internal static int Stride => Marshal.SizeOf<VividLocalVolumetricFogEngineData>();
    }

    [ExecuteAlways]
    [AddComponentMenu("Rendering/VividRP Local Volumetric Fog")]
    public sealed class VividLocalVolumetricFog : MonoBehaviour, IBoundProxyProvider, IBoundProxyWorldDataProvider, ISerializationCallbackReceiver
    {
        private const int CurrentSerializationVersion = 1;
        private const int LegacySerializationVersion = 0;
        private static readonly Vector3 k_MinimumBoxSize = new(0.001f, 0.001f, 0.001f);
        private static readonly int FogVolumeSingleScatteringAlbedoId = Shader.PropertyToID("_FogVolumeSingleScatteringAlbedo");
        private static readonly int FogVolumeFogDistanceId = Shader.PropertyToID("_FogVolumeFogDistanceProperty");
        private static readonly int FogVolumeBlendModeId = Shader.PropertyToID("_FogVolumeBlendMode");
        private static readonly int FogVolumeMaskId = Shader.PropertyToID("_Mask");

        [SerializeField]
        private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

        [SerializeField]
        private int m_SerializationVersion = CurrentSerializationVersion;

        [SerializeField]
        private VividLocalVolumetricFogArtistParameters m_Parameters =
            VividLocalVolumetricFogArtistParameters.CreateDefault();

        public VividLocalVolumetricFogArtistParameters parameters
        {
            get => m_Parameters;
            set
            {
                m_Parameters = value;
                m_Parameters.Validate();
            }
        }

        public bool IsActive()
        {
            return isActiveAndEnabled
                && m_Parameters.meanFreePath > 0.0f
                && GetFogSize(BoundProxyShape).sqrMagnitude > 0.0f;
        }

        public Bounds GetBounds()
        {
            return BoundProxyUtility.CalculateWorldAabb(transform, BoundProxyShape);
        }

        public VividLocalVolumetricFogEngineData ConvertToEngineData(Camera camera)
        {
            m_Parameters.Validate();
            var parameters = GetEffectiveParameters();
            BoundProxyShape shape = BoundProxyShape;
            Vector3 size = GetFogSize(shape);
            Vector3 center = transform.position + transform.rotation * shape.center;
            Matrix4x4 worldToLocal = Matrix4x4.TRS(center, transform.rotation, size).inverse;
            var extinction = 1.0f / Mathf.Max(parameters.meanFreePath, VividLocalVolumetricFogArtistParameters.MinimumFogDistance);
            var scattering = parameters.GetScattering(extinction);
            var positiveFade = ReciprocalFade(parameters.positiveFade);
            var negativeFade = ReciprocalFade(parameters.negativeFade);
            var distanceFade = BuildDistanceFade(parameters);
            var animatedTextureOffset = parameters.textureOffset
                - parameters.textureScrollingSpeed * Time.time;

            return new VividLocalVolumetricFogEngineData
            {
                worldToLocalRow0 = worldToLocal.GetRow(0),
                worldToLocalRow1 = worldToLocal.GetRow(1),
                worldToLocalRow2 = worldToLocal.GetRow(2),
                scatteringExtinction = new Vector4(scattering.x, scattering.y, scattering.z, extinction),
                positiveFade = new Vector4(positiveFade.x, positiveFade.y, positiveFade.z, 0.0f),
                negativeFade = new Vector4(negativeFade.x, negativeFade.y, negativeFade.z, 0.0f),
                distanceFade = distanceFade,
                parameters = new Vector4(
                    parameters.anisotropy,
                    (float)parameters.blendingMode,
                    parameters.invertFade ? 1.0f : 0.0f,
                    0.0f),
                textureScaleOffset0 = new Vector4(
                    parameters.textureTiling.x,
                    parameters.textureTiling.y,
                    parameters.textureTiling.z,
                    0.0f),
                textureScaleOffset1 = new Vector4(
                    animatedTextureOffset.x,
                    animatedTextureOffset.y,
                    animatedTextureOffset.z,
                    (float)parameters.falloffMode)
            };
        }

        public BoundProxyFeature BoundProxyFeature => BoundProxyFeature.LocalVolumetricFog;

        public bool IsBoundProxyActive => isActiveAndEnabled;

        public Transform BoundProxyTransform => transform;

        public BoundProxyShape BoundProxyShape
        {
            get
            {
                BoundProxyShape shape = m_BoundProxy;
                shape.shape = BoundProxyShapeType.Box;
                shape.Sanitize();
                shape.size = Max(shape.GetSanitizedSize(), k_MinimumBoxSize);
                shape.radius = 0.0f;
                return shape;
            }
        }

        internal int priority => m_Parameters.priority;

        internal bool TryGetVolumeMask(out Texture3D volumeMask, out bool alphaOnly)
        {
            alphaOnly = false;
            volumeMask = null;

            if (m_Parameters.maskMode == VividLocalVolumetricFogMaskMode.Texture)
            {
                volumeMask = m_Parameters.volumeMask;
            }
            else if (m_Parameters.maskMode == VividLocalVolumetricFogMaskMode.Material
                && m_Parameters.materialMask != null
                && m_Parameters.materialMask.HasProperty(FogVolumeMaskId))
            {
                volumeMask = m_Parameters.materialMask.GetTexture(FogVolumeMaskId) as Texture3D;
            }

            if (volumeMask == null)
                return false;

            alphaOnly = volumeMask.format == TextureFormat.Alpha8;
            return true;
        }

        private void OnEnable()
        {
            m_Parameters.Validate();
            ValidateBoundProxy();
            VividLocalVolumetricFogManager.Register(this);
        }

        private void OnDisable()
        {
            VividLocalVolumetricFogManager.Unregister(this);
        }

        private void OnValidate()
        {
            m_Parameters.Validate();
            ValidateBoundProxy();
        }

        public bool TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData)
        {
            if (!IsBoundProxyActive)
            {
                worldData = default;
                return false;
            }

            worldData = BoundProxyUtility.CreateWorldData(
                transform,
                BoundProxyFeature,
                BoundProxyShape,
                transform.GetEntityId());
            return true;
        }

        public void OnBeforeSerialize()
        {
            m_SerializationVersion = CurrentSerializationVersion;
        }

        public void OnAfterDeserialize()
        {
            if (m_SerializationVersion > LegacySerializationVersion)
                return;

            var legacyBlendMode = (int)m_Parameters.blendingMode;
            m_Parameters.blendingMode = legacyBlendMode switch
            {
                0 => VividLocalVolumetricFogBlendingMode.Additive,
                1 => VividLocalVolumetricFogBlendingMode.Overwrite,
                _ => m_Parameters.blendingMode
            };

            var legacyMaskMode = (int)m_Parameters.maskMode;
            m_Parameters.maskMode = legacyMaskMode switch
            {
                0 => VividLocalVolumetricFogMaskMode.None,
                1 => VividLocalVolumetricFogMaskMode.Texture,
                _ => m_Parameters.maskMode
            };

            m_SerializationVersion = CurrentSerializationVersion;
        }

        private static BoundProxyShape CreateDefaultBoundProxy()
        {
            return new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = Vector3.one,
            };
        }

        private void ValidateBoundProxy()
        {
            if (m_BoundProxy.size.sqrMagnitude <= 0.0f)
            {
                m_BoundProxy.size = GetLegacyEffectiveSize();
            }

            m_BoundProxy.shape = BoundProxyShapeType.Box;
            m_BoundProxy.Sanitize();
            m_BoundProxy.size = Max(m_BoundProxy.GetSanitizedSize(), k_MinimumBoxSize);
            m_BoundProxy.radius = 0.0f;
        }

        private Vector3 GetLegacyEffectiveSize()
        {
            if (m_Parameters.scaleMode == VividLocalVolumetricFogScaleMode.Size)
                return Max(m_Parameters.size, k_MinimumBoxSize);

            var scale = transform.lossyScale;
            return Max(new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)), k_MinimumBoxSize);
        }

        private static Vector3 GetFogSize(BoundProxyShape shape)
        {
            return Max(shape.GetSanitizedSize(), k_MinimumBoxSize);
        }

        private VividLocalVolumetricFogArtistParameters GetEffectiveParameters()
        {
            var parameters = m_Parameters;
            if (parameters.maskMode != VividLocalVolumetricFogMaskMode.Material || parameters.materialMask == null)
                return parameters;

            var material = parameters.materialMask;
            if (material.HasProperty(FogVolumeSingleScatteringAlbedoId))
                parameters.albedo = material.GetColor(FogVolumeSingleScatteringAlbedoId);

            if (material.HasProperty(FogVolumeFogDistanceId))
                parameters.meanFreePath = material.GetFloat(FogVolumeFogDistanceId);

            if (material.HasProperty(FogVolumeBlendModeId))
                parameters.blendingMode = (VividLocalVolumetricFogBlendingMode)Mathf.RoundToInt(material.GetFloat(FogVolumeBlendModeId));

            parameters.Validate();
            return parameters;
        }

        private static Vector4 BuildDistanceFade(in VividLocalVolumetricFogArtistParameters parameters)
        {
            var start = Mathf.Max(parameters.distanceFadeStart, 0.0f);
            var end = Mathf.Max(start, parameters.distanceFadeEnd);
            var rcpLength = 1.0f / Mathf.Max(end - start, 0.00001526f);

            return new Vector4(rcpLength, end * rcpLength, start, end);
        }

        private static Vector3 ReciprocalFade(Vector3 fade)
        {
            return new Vector3(
                ReciprocalFade(fade.x),
                ReciprocalFade(fade.y),
                ReciprocalFade(fade.z));
        }

        private static float ReciprocalFade(float fade)
        {
            return fade > 0.0f
                ? Mathf.Min(1.0f / fade, float.MaxValue)
                : float.MaxValue;
        }

        private static Vector3 Max(Vector3 value, Vector3 minimum)
        {
            return new Vector3(
                Mathf.Max(value.x, minimum.x),
                Mathf.Max(value.y, minimum.y),
                Mathf.Max(value.z, minimum.z));
        }
    }
}
