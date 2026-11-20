using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    // A snapshot of the resident material pages. Lighting and history are computed separately.
    public sealed class VividCardMaterialPages : ScriptableObject
    {
        [SerializeField] internal Texture2D Albedo, Normal, Emissive, Depth, Opacity;
        [SerializeField, HideInInspector] internal byte[] Cards, MeshCards, PageTable, CardPages;
        [SerializeField] internal int ResidentPages;
    }
}
