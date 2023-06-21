namespace Ryujinx.HLE.HOS.Services.Hid.HidServer.Types.Vibration
{
    public struct VibrationDeviceHandle
    {
        public byte DeviceType;
        public byte PlayerId;
        public byte Position;
        public byte Reserved;
    }
}
