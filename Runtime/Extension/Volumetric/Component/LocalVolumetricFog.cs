using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using static Unity.Mathematics.math;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>Local volumetric fog blending mode.</summary>
    public enum LocalVolumetricFogBlendingMode {
        /// <summary>Replace the current fog, it is similar to disabling the blending.</summary>
        Overwrite = 0,
        /// <summary>Additively blend fog volumes. This is the default behavior.</summary>
        Additive = 1,
        /// <summary>Multiply the fog values when doing the blending. This is useful to make the fog density relative to other fog volumes.</summary>
        Multiply = 2,
        /// <summary>Performs a minimum operation when blending the volumes.</summary>
        Min = 3,
        /// <summary>Performs a maximum operation when blending the volumes.</summary>
        Max = 4,
    }

    public enum LocalVolumetricFogMode {
        Texture,
        Material
    }

    /// <summary>Artist-friendly Local Volumetric Fog parametrization.</summary>
    [Serializable]
    public struct LocalVolumetricFogArtistParameters {
        /// <summary>Single scattering albedo: [0, 1]. Alpha is ignored.</summary>
        [ColorUsage(false)]
        public Color albedo;
        /// <summary>Mean free path, in meters: [1, inf].</summary>
        public float meanFreePath; // Should be chromatic - this is an optimization!

        /// <summary>
        /// Specifies how the fog in the volume will interact with the fog.
        /// </summary>
        //public LocalVolumetricFogBlendingMode blendingMode;
        public LocalVolumetricFogMode fogMode;
        public Material fogMaterial;
        public Texture fogTexture;

        public Vector3 textureScrollingSpeed;
        /// <summary>Tiling rate of the density texture.</summary>
        public Vector3 textureTiling;

        /// <summary>
        /// Rendering priority of the volume, higher priority will be rendered first.
        /// </summary>
        public int priority;

        public Vector3 size;

        public Vector3 positiveFade;
        public Vector3 negativeFade;

        [SerializeField]
        internal float m_EditorUniformFade;

        //internal Vector3 m_TextureOffset;
        //public Vector3 textureOffset => m_TextureOffset;

        /// <summary>Minimum fog distance you can set in the meanFreePath parameter</summary>
        internal const float kMinFogDistance = 0.05f;

        public LocalVolumetricFogArtistParameters(Color color, float meanFreePath_) {
            albedo = color;
            meanFreePath = meanFreePath_;
            //blendingMode = LocalVolumetricFogBlendingMode.Additive;
            priority = 0;

            size = Vector3.one;

            positiveFade = Vector3.zero * 0.1f;
            negativeFade = Vector3.zero * 0.1f;

            fogMode = LocalVolumetricFogMode.Texture;
            fogMaterial = null;
            fogTexture = null;

            textureScrollingSpeed = Vector3.zero;
            textureTiling = Vector3.one;

            //m_TextureOffset = Vector3.zero;

            m_EditorUniformFade = 0.1f;
        }

        //internal void Update(float time) {
        //    if (fogTexture != null) {
        //        m_TextureOffset = -textureScrollingSpeed * time;
        //    }
        //}

        internal void Constrain() {
            albedo.r = Mathf.Clamp01(albedo.r);
            albedo.g = Mathf.Clamp01(albedo.g);
            albedo.b = Mathf.Clamp01(albedo.b);
            albedo.a = 1.0f;

            meanFreePath = Mathf.Max(meanFreePath, kMinFogDistance);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LocalVolumetricFogBuffer {
        public Vector4 _VolumetricFogObbRight;
        public Vector4 _VolumetricFogObbUp;
        public Vector4 _VolumetricFogObbForward;
        public Vector4 _VolumetricFogObbCenter;
        public Vector4 _VolumetricFogObbExtents;
        public Vector4 _VolumetricFogRcpPosFaceFade;
        public Vector4 _VolumetricFogRcpNegFaceFade;
        public Vector4 _VolumetricFogProperty; // (rgb: albedo, a: extinction)
        public uint _VolumetricFogGlobalIndex;
    }

    [ExecuteAlways]
    [AddComponentMenu("Rendering/Local Volumetric Fog")]
    public class LocalVolumetricFog : MonoBehaviour {
        static readonly int _VolumetricFogObbRight = Shader.PropertyToID("_VolumetricFogObbRight");
        static readonly int _VolumetricFogObbUp = Shader.PropertyToID("_VolumetricFogObbUp");
        static readonly int _VolumetricFogObbForward = Shader.PropertyToID("_VolumetricFogObbForward");
        static readonly int _VolumetricFogObbCenter = Shader.PropertyToID("_VolumetricFogObbCenter");
        static readonly int _VolumetricFogObbExtents = Shader.PropertyToID("_VolumetricFogObbExtents");
        static readonly int _VolumetricFogRcpPosFaceFade = Shader.PropertyToID("_VolumetricFogRcpPosFaceFade");
        static readonly int _VolumetricFogRcpNegFaceFade = Shader.PropertyToID("_VolumetricFogRcpNegFaceFade");
        static readonly int _VolumetricFogProperty = Shader.PropertyToID("_VolumetricFogProperty");
        static readonly int _VolumetricFogGlobalIndex = Shader.PropertyToID("_VolumetricFogGlobalIndex");

        static readonly int _FogTex = Shader.PropertyToID("_FogTex");
        static readonly int _FogTexTiling = Shader.PropertyToID("_FogTexTiling");
        static readonly int _FogTexScroll = Shader.PropertyToID("_FogTexScroll");

        public LocalVolumetricFogArtistParameters parameters = new LocalVolumetricFogArtistParameters(Color.white, 10.0f);

        [NonSerialized]
        int m_GlobalIndex;
        [NonSerialized]
        MaterialPropertyBlock m_RenderingProperties;

        internal int globalIndex => m_GlobalIndex;

        private void OnEnable() {
            LocalVolumetricFogManager.manager.RegisterVolume(this);
        }
        private void OnDisable() {
            LocalVolumetricFogManager.manager.DeRegisterVolume(this);
        }

        private void OnValidate() {
            parameters.Constrain();
        }

        internal void PrepareDrawCall(int index) {
            m_GlobalIndex = index;
            if (!LocalVolumetricFogManager.manager.IsInitialized()) return;

            if (null == m_RenderingProperties) {
                m_RenderingProperties = new MaterialPropertyBlock();
            }
            m_RenderingProperties.Clear();

            var tr = transform;
            var position = tr.position;

            var bounds = new OrientedBBox(Matrix4x4.TRS(position, tr.rotation, parameters.size));

            var AABBExtents = abs(bounds.right * bounds.extentX)
                            + abs(bounds.up * bounds.extentY)
                            + abs(bounds.forward * bounds.extentZ);
            var AABB = new Bounds(bounds.center, AABBExtents * 2.0f);


            var data = new LocalVolumetricFogBuffer();
            data._VolumetricFogGlobalIndex = (uint)m_GlobalIndex;
            data._VolumetricFogObbRight = bounds.right;
            data._VolumetricFogObbUp = bounds.up;
            data._VolumetricFogObbForward = bounds.forward;
            data._VolumetricFogObbCenter = bounds.center;
            data._VolumetricFogObbExtents = bounds.extents;

            var positionFade = parameters.positiveFade;
            data._VolumetricFogRcpPosFaceFade.x = Mathf.Min(1.0f / positionFade.x, float.MaxValue);
            data._VolumetricFogRcpPosFaceFade.y = Mathf.Min(1.0f / positionFade.y, float.MaxValue);
            data._VolumetricFogRcpPosFaceFade.z = Mathf.Min(1.0f / positionFade.z, float.MaxValue);
            var negativeFade = parameters.negativeFade;
            data._VolumetricFogRcpNegFaceFade.x = Mathf.Min(1.0f / negativeFade.x, float.MaxValue);
            data._VolumetricFogRcpNegFaceFade.y = Mathf.Min(1.0f / negativeFade.y, float.MaxValue);
            data._VolumetricFogRcpNegFaceFade.z = Mathf.Min(1.0f / negativeFade.z, float.MaxValue);

            var albedo = parameters.albedo.linear;
            data._VolumetricFogProperty = new Vector4(albedo.r, albedo.g, albedo.b, 1.0f / Mathf.Max(0.05f, parameters.meanFreePath));

            data._VolumetricFogGlobalIndex = (uint)m_GlobalIndex;

            //ConstantBuffer.Set<LocalVolumetricFogBuffer>(m_RenderingProperties, Shader.PropertyToID("LocalVolumetricFogBuffer"));
            m_RenderingProperties.SetVector(_VolumetricFogObbRight, data._VolumetricFogObbRight);
            m_RenderingProperties.SetVector(_VolumetricFogObbUp, data._VolumetricFogObbUp);
            m_RenderingProperties.SetVector(_VolumetricFogObbForward, data._VolumetricFogObbForward);
            m_RenderingProperties.SetVector(_VolumetricFogObbCenter, data._VolumetricFogObbCenter);
            m_RenderingProperties.SetVector(_VolumetricFogObbExtents, data._VolumetricFogObbExtents);
            m_RenderingProperties.SetVector(_VolumetricFogRcpPosFaceFade, data._VolumetricFogRcpPosFaceFade);
            m_RenderingProperties.SetVector(_VolumetricFogRcpNegFaceFade, data._VolumetricFogRcpNegFaceFade);
            m_RenderingProperties.SetVector(_VolumetricFogProperty, data._VolumetricFogProperty);
            m_RenderingProperties.SetFloat(_VolumetricFogGlobalIndex, data._VolumetricFogGlobalIndex);

            if (parameters.fogMode == LocalVolumetricFogMode.Texture) {
                if (parameters.fogTexture != null)
                    m_RenderingProperties.SetTexture(_FogTex, parameters.fogTexture);
                m_RenderingProperties.SetVector(_FogTexTiling, parameters.textureTiling);
                m_RenderingProperties.SetVector(_FogTexScroll, parameters.textureScrollingSpeed);
            }

            var fogManager = LocalVolumetricFogManager.manager;

            var fogMaterial = parameters.fogMaterial;
            if (parameters.fogMode == LocalVolumetricFogMode.Texture) {
                fogMaterial = fogManager.textureFogMaterial;
            }
            if (null == fogMaterial) return;
            var renderParams = new RenderParams() {
                layer = gameObject.layer,
                rendererPriority = parameters.priority,
                worldBounds = AABB,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                renderingLayerMask = ~0u,
                matProps = m_RenderingProperties,
                shadowCastingMode = ShadowCastingMode.Off,
                material = fogMaterial,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
            };
            Graphics.RenderPrimitivesIndexedIndirect(in renderParams, MeshTopology.Triangles,
                fogManager.volumeSliceIndexBuffer, fogManager.globalIndirectArgBuffer, 1, m_GlobalIndex);
        }
    }
}