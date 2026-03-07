using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways] // NOTE: This is required to get calls to OnDestroy() always. Graphics resources are released in OnDestroy().
    public class VividAdditionalCameraData : MonoBehaviour,  IAdditionalData
    {
        
    }
}