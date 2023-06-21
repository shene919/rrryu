using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nv.NvDrvServices.NvMap.Types
{
    [StructLayout(LayoutKind.Sequential)]
    struct NvMapParam
    {
        public int              Handle;
        public NvMapHandleParam Param;
        public int              Result;
    }
}
