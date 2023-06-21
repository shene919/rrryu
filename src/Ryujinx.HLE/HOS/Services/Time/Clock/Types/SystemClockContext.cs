using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Time.Clock.Types
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SystemClockContext
    {
        public long                 Offset;
        public SteadyClockTimePoint SteadyTimePoint;
    }
}
