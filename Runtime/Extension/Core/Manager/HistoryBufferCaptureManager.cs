using System;

namespace UnityEngine.Rendering.Universal
{
    public class HistoryBufferCaptureManager
    {
        int NeedHistoryPasses = 0;

        static Lazy<HistoryBufferCaptureManager> _instance = new Lazy<HistoryBufferCaptureManager>(() => new HistoryBufferCaptureManager());

        public static HistoryBufferCaptureManager instance => _instance.Value;

        public void AcquireHistoryPasses()
        {
            NeedHistoryPasses++;
        }

        public void ReleaseHistoryPasses()
        {
            NeedHistoryPasses--;
        }

        public bool EnableHistoryPasses()
        {
            return NeedHistoryPasses > 0;
        }
    }
}