using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividRenderingData : ContextItem
    {
        public CullingResults cullingResults;
        public ScriptableRenderContext context;

        public override void Reset()
        {
            
        }
    }
}