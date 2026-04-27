using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum VividLocalVolumetricFogBlendingMode
    {
        Additive = 0,
        Overwrite = 1
    }

    public enum VividLocalVolumetricFogFalloffMode
    {
        Linear = 0,
        Exponential = 1
    }

    public enum VividLocalVolumetricFogMaskMode
    {
        None = 0,
        Texture = 1
    }

    public enum VividLocalVolumetricFogScaleMode
    {
        Transform = 0,
        Size = 1
    }

    [Serializable]
    public struct VividLocalVolumetricFogArtistParameters
    {
        public Color albedo;
        [Min(0.001f)] public float meanFreePath;
        public VividLocalVolumetricFogBlendingMode blendingMode;
        public int priority;
        [Range(-0.95f, 0.95f)] public float anisotropy;
        public Texture3D volumeMask;
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
                textureScrollingSpeed = Vector3.zero,
                textureTiling = Vector3.one,
                positiveFade = Vector3.zero,
                negativeFade = Vector3.zero,
                scaleMode = VividLocalVolumetricFogScaleMode.Transform,
                size = Vector3.one,
                invertFade = false,
                distanceFadeStart = 0.0f,
                distanceFadeEnd = 0.0f,
                textureOffset = Vector3.zero,
                falloffMode = VividLocalVolumetricFogFalloffMode.Linear,
                maskMode = VividLocalVolumetricFogMaskMode.None
            };
        }

        public void Validate()
        {
            meanFreePath = Mathf.Max(meanFreePath, 0.001f);
            anisotropy = Mathf.Clamp(anisotropy, -0.95f, 0.95f);
            positiveFade = Max(positiveFade, Vector3.zero);
            negativeFade = Max(negativeFade, Vector3.zero);
            size = Max(size, new Vector3(0.001f, 0.001f, 0.001f));
            distanceFadeStart = Mathf.Max(distanceFadeStart, 0.0f);
            distanceFadeEnd = Mathf.Max(distanceFadeEnd, 0.0f);
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
    public sealed class VividLocalVolumetricFog : MonoBehaviour, IBoundProxyProvider, IBoundProxyWorldDataProvider
    {
        private static readonly Vector3 k_MinimumBoxSize = new(0.001f, 0.001f, 0.001f);

        [SerializeField]
        private BoundProxyShape m_BoundProxy = CreateDefaultBoundProxy();

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
            BoundProxyShape shape = BoundProxyShape;
            Vector3 size = GetFogSize(shape);
            Vector3 center = transform.position + transform.rotation * shape.center;
            Matrix4x4 worldToLocal = Matrix4x4.TRS(center, transform.rotation, size).inverse;
            var extinction = 1.0f / Mathf.Max(m_Parameters.meanFreePath, 0.001f);
            var scattering = m_Parameters.GetScattering(extinction);
            var positiveFade = NormalizeFade(m_Parameters.positiveFade, size);
            var negativeFade = NormalizeFade(m_Parameters.negativeFade, size);
            var distanceFade = BuildDistanceFade(camera);
            var animatedTextureOffset = m_Parameters.textureOffset
                + m_Parameters.textureScrollingSpeed * Time.time;

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
                    m_Parameters.anisotropy,
                    (float)m_Parameters.blendingMode,
                    m_Parameters.invertFade ? 1.0f : 0.0f,
                    0.0f),
                textureScaleOffset0 = new Vector4(
                    m_Parameters.textureTiling.x,
                    m_Parameters.textureTiling.y,
                    m_Parameters.textureTiling.z,
                    0.0f),
                textureScaleOffset1 = new Vector4(
                    animatedTextureOffset.x,
                    animatedTextureOffset.y,
                    animatedTextureOffset.z,
                    (float)m_Parameters.falloffMode)
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

        internal bool TryGetVolumeMask(out Texture3D volumeMask)
        {
            volumeMask = m_Parameters.volumeMask;
            return m_Parameters.maskMode == VividLocalVolumetricFogMaskMode.Texture && volumeMask != null;
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

        private Vector4 BuildDistanceFade(Camera camera)
        {
            var start = Mathf.Max(m_Parameters.distanceFadeStart, 0.0f);
            var end = Mathf.Max(m_Parameters.distanceFadeEnd, 0.0f);
            if (camera == null || end <= start)
                return Vector4.zero;

            return new Vector4(start, end, 1.0f / Mathf.Max(end - start, 0.0001f), 1.0f);
        }

        private static Vector3 NormalizeFade(Vector3 fade, Vector3 size)
        {
            return new Vector3(
                Mathf.Clamp01(fade.x / Mathf.Max(size.x, 0.001f)),
                Mathf.Clamp01(fade.y / Mathf.Max(size.y, 0.001f)),
                Mathf.Clamp01(fade.z / Mathf.Max(size.z, 0.001f)));
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
