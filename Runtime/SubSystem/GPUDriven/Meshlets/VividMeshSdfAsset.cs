using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    public enum VividMeshSdfSignMode
    {
        Auto,
        Solid,
        ThinShell,
    }

    public sealed class VividMeshSdfAsset : ScriptableObject
    {
        [SerializeField] private Mesh m_SourceMesh;
        [SerializeField] private Texture3D m_DistanceField;
        [SerializeField] private Bounds m_GeometryBounds;
        [SerializeField] private Vector3 m_SampleOrigin;
        [SerializeField] private float m_VoxelSize;
        [SerializeField] private VividMeshSdfSignMode m_SignMode;
        [SerializeField] private float m_ShellThickness;

        public Mesh SourceMesh => m_SourceMesh;
        public Texture3D DistanceField => m_DistanceField;
        public Bounds GeometryBounds => m_GeometryBounds;
        /// <summary>Mesh-local center of texel (0, 0, 0).</summary>
        public Vector3 SampleOrigin => m_SampleOrigin;
        public float VoxelSize => m_VoxelSize;
        public VividMeshSdfSignMode SignMode => m_SignMode;
        public float ShellThickness => m_ShellThickness;

        internal void Initialize(
            Mesh sourceMesh,
            Texture3D distanceField,
            Bounds geometryBounds,
            Vector3 sampleOrigin,
            float voxelSize,
            VividMeshSdfSignMode signMode,
            float shellThickness)
        {
            m_SourceMesh = sourceMesh;
            m_DistanceField = distanceField;
            m_GeometryBounds = geometryBounds;
            m_SampleOrigin = sampleOrigin;
            m_VoxelSize = voxelSize;
            m_SignMode = signMode;
            m_ShellThickness = shellThickness;
        }
    }
}
