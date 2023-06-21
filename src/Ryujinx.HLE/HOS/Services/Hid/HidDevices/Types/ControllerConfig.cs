using Ryujinx.HLE.HOS.Services.Hid.Types.Npad;

namespace Ryujinx.HLE.HOS.Services.Hid.HidDevices.Types
{
    public struct ControllerConfig
    {
        public PlayerIndex    Player;
        public ControllerType Type;
    }
}
