using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Nv.NvDrvServices.NvMap.Types
{
    [StructLayout(LayoutKind.Sequential)]
    struct NvMapFromId
    {
        public int Id;
        public int Handle;
    }
}
