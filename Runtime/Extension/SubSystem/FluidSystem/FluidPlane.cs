namespace UnityEngine.Rendering.Universal
{
    public abstract class FluidPlane : MonoBehaviour
    {
        [Min(0.01f)]public float areaSizeX = 1f;
        [Min(0.01f)]public float areaSizeY = 1f;

        public Vector2 areaSize => new Vector2(areaSizeX, areaSizeY);
        
    }
}