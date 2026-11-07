#if UNITY_EDITOR
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    // Ownership marker for temporary bake geometry, including recovery after an interrupted bake.
    [AddComponentMenu("")]
    internal sealed class MeshletAPVBakeProxy : MonoBehaviour
    {
        [SerializeField] internal MeshletRenderer owner;
    }
}
#endif
