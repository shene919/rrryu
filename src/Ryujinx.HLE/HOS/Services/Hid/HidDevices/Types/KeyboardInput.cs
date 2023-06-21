namespace Ryujinx.HLE.HOS.Services.Hid.HidDevices.Types
{
    public struct KeyboardInput
    {
        public int Modifier;
        public ulong[] Keys;
    }
}
