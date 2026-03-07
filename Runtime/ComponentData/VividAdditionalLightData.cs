using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways] // NOTE: This is required to get calls to OnDestroy() always. Graphics resources are released in OnDestroy().
    public class VividAdditionalLightData: MonoBehaviour, IAdditionalData
    {
        
    }
}