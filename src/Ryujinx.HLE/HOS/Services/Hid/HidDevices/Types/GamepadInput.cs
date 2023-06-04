using Ryujinx.HLE.HOS.Services.Hid.Types.Npad;

namespace Ryujinx.HLE.HOS.Services.Hid.HidDevices.Types
{
    public struct GamepadInput
    {
        public PlayerIndex      PlayerId;
        public ControllerKeys   Buttons;
        public JoystickPosition LStick;
        public JoystickPosition RStick;
    }
}