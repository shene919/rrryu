using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.Types
{
    [Flags]
    enum LibraryAppletMode : uint
    {
        AllForeground,
        PartialForeground,
        NoUi,
        PartialForegroundWithIndirectDisplay,
        AllForegroundInitiallyHidden
    }
}
