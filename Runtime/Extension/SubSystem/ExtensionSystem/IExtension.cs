namespace UnityEngine.Rendering.Universal
{
    public interface IExtension
    {
        public void Init();

        public bool Support()
        {
            Debug.LogError(this.GetType().Name + ": Extension support not implemented.");
            return false;
        }
        
        public bool ShutDown()
        {
            Debug.LogWarning(this.GetType().Name + ": do noting on ShutDown().");
            return false;
        }


        public HardwareExtension GetExtension()
        {
            return HardwareExtension.PlaceHolder;
        }

    }
}