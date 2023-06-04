using Ryujinx.HLE.HOS.Services.Hid.Types.Npad;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Applets.Controller
{
    public struct ControllerAppletUiArgs
    {
        public int PlayerCountMin;
        public int PlayerCountMax;
        public ControllerType SupportedStyles;
        public IEnumerable<PlayerIndex> SupportedPlayers;
        public bool IsDocked;
    }
}