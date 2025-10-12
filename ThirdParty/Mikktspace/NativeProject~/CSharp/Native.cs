using System;
using System.Runtime.InteropServices;

namespace Mikktspace.NET
{
    static class Native
    {
        const string LIBRARY_NAME = "mikktspace";

        [DllImport(LIBRARY_NAME)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool genMikktTangSpace(IntPtr pContext, float angularThreshold);
    }
}
