using UnityEngine;

namespace VividRP.Runtime
{
    internal static class SkyShaderCompilationUtility
    {
        internal static bool EnsureMaterialPassReady(Material material, int passIndex)
        {
            if (material == null || passIndex < 0)
                return false;

#if UNITY_EDITOR
            if (!UnityEditor.ShaderUtil.IsPassCompiled(material, passIndex))
            {
                UnityEditor.ShaderUtil.CompilePass(material, passIndex);
                return false;
            }
#endif

            return true;
        }
    }
}
