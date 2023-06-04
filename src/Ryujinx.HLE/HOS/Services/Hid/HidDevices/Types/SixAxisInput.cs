using Ryujinx.HLE.HOS.Services.Hid.Types.Npad;
using System.Numerics;

namespace Ryujinx.HLE.HOS.Services.Hid.HidDevices.Types
{
    public struct SixAxisInput
    {
        public PlayerIndex PlayerId;
        public Vector3     Accelerometer;
        public Vector3     Gyroscope;
        public Vector3     Rotation;
        public float[]     Orientation;
    }
}