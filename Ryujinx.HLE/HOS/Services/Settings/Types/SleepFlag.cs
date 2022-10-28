using System;

namespace Ryujinx.HLE.HOS.Services.Settings.Types
{
    [Flags]
    enum SleepFlag
    {
        SleepsWhilePlayingMedia,
        WakesAtPowerStateChange
    }
}