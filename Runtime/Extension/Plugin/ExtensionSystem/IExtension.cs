namespace UnityEngine.Rendering.Universal
{
    public interface IExtension
    {
        public void Init();

        public bool Support()
        {
            return false;
        }

        public HardwareExtension GetExtension()
        {
            return HardwareExtension.PlaceHolder;
        }

    }
}