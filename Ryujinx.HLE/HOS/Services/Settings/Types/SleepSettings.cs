using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Settings.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 0x12)]
    struct SleepSettings
    {
        public SleepFlag Flags;
        public HandheldSleepPlan HandheldPlan;
        public ConsoleSleepPlan ConsolePlan;
    }
}