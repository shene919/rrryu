using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Hid.HidServer;
using Ryujinx.HLE.HOS.Services.Hid.Types;
using Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad;
using Ryujinx.Horizon.Common;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Hid
{
    [Service("hid")]
    class IHidServer : IpcService
    {
        private readonly KEvent _xpadIdEvent;
        private readonly KEvent _palmaOperationCompleteEvent;

        private int _xpadIdEventHandle;

        private bool _sixAxisSensorFusionEnabled;
        private bool _unintendedHomeButtonInputProtectionEnabled;
        private bool _vibrationPermitted;
        private bool _usbFullKeyControllerEnabled;
        private readonly bool _isFirmwareUpdateAvailableForSixAxisSensor;
        private bool _isSixAxisSensorUnalteredPassthroughEnabled;

        private NpadHandheldActivationMode _npadHandheldActivationMode;
        private GyroscopeZeroDriftMode     _gyroscopeZeroDriftMode;

        private long  _npadCommunicationMode;
        private uint  _accelerometerPlayMode;
#pragma warning disable CS0649
        private readonly long  _vibrationGcErmCommand;
#pragma warning restore CS0649
        private float _sevenSixAxisSensorFusionStrength;

        private SensorFusionParameters  _sensorFusionParams;
        private AccelerometerParameters _accelerometerParams;

        public IHidServer(ServiceCtx context) : base(context.Device.System.HidServer)
        {
            _xpadIdEvent                 = new KEvent(context.Device.System.KernelContext);
            _palmaOperationCompleteEvent = new KEvent(context.Device.System.KernelContext);

            _npadHandheldActivationMode = NpadHandheldActivationMode.Dual;
            _gyroscopeZeroDriftMode     = GyroscopeZeroDriftMode.Standard;

            _isFirmwareUpdateAvailableForSixAxisSensor = false;

            _sensorFusionParams  = new SensorFusionParameters();
            _accelerometerParams = new AccelerometerParameters();

            // TODO: signal event at right place
            _xpadIdEvent.ReadableEvent.Signal();

            _vibrationPermitted = true;
        }

        [CommandHipc(0)]
        // CreateAppletResource(nn::applet::AppletResourceUserId) -> object<nn::hid::IAppletResource>
        public ResultCode CreateAppletResource(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            MakeObject(context, new IAppletResource(context.Device.System.HidSharedMem));

            return ResultCode.Success;
        }

        [CommandHipc(1)]
        // ActivateDebugPad(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateDebugPad(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            // Initialize entries to avoid issues with some games.

            for (int entry = 0; entry < Hid.SharedMemEntryCount; entry++)
            {
                context.Device.Hid.DebugPad.Update();
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(11)]
        // ActivateTouchScreen(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateTouchScreen(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.Device.Hid.Touchscreen.Active = true;

            // Initialize entries to avoid issues with some games.

            for (int entry = 0; entry < Hid.SharedMemEntryCount; entry++)
            {
                context.Device.Hid.Touchscreen.Update();
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(21)]
        // ActivateMouse(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateMouse(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.Device.Hid.Mouse.Active = true;

            // Initialize entries to avoid issues with some games.

            for (int entry = 0; entry < Hid.SharedMemEntryCount; entry++)
            {
                context.Device.Hid.Mouse.Update(0, 0);
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(31)]
        // ActivateKeyboard(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateKeyboard(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.Device.Hid.Keyboard.Active = true;

            // Initialize entries to avoid issues with some games.

            KeyboardInput emptyInput = new()
            {
                Keys = new ulong[4]
            };

            for (int entry = 0; entry < Hid.SharedMemEntryCount; entry++)
            {
                context.Device.Hid.Keyboard.Update(emptyInput);
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(32)]
        // SendKeyboardLockKeyEvent(uint flags, pid)
        public static ResultCode SendKeyboardLockKeyEvent(ServiceCtx context)
        {
            uint flags = context.RequestData.ReadUInt32();

            // NOTE: This signal the keyboard driver about lock events.

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { flags });

            return ResultCode.Success;
        }

        [CommandHipc(40)]
        // AcquireXpadIdEventHandle(ulong XpadId) -> nn::sf::NativeHandle
        public ResultCode AcquireXpadIdEventHandle(ServiceCtx context)
        {
            long xpadId = context.RequestData.ReadInt64();

            if (context.Process.HandleTable.GenerateHandle(_xpadIdEvent.ReadableEvent, out _xpadIdEventHandle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_xpadIdEventHandle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { xpadId });

            return ResultCode.Success;
        }

        [CommandHipc(41)]
        // ReleaseXpadIdEventHandle(ulong XpadId)
        public ResultCode ReleaseXpadIdEventHandle(ServiceCtx context)
        {
            long xpadId = context.RequestData.ReadInt64();

            context.Process.HandleTable.CloseHandle(_xpadIdEventHandle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { xpadId });

            return ResultCode.Success;
        }

        [CommandHipc(51)]
        // ActivateXpad(nn::hid::BasicXpadId, nn::applet::AppletResourceUserId)
        public static ResultCode ActivateXpad(ServiceCtx context)
        {
            int  basicXpadId          = context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, basicXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(55)]
        // GetXpadIds() -> long IdsCount, buffer<array<nn::hid::BasicXpadId>, type: 0xa>
        public static ResultCode GetXpadIds(ServiceCtx context)
        {
            // There is any Xpad, so we return 0 and write nothing inside the type-0xa buffer.
            context.ResponseData.Write(0L);

            Logger.Stub?.PrintStub(LogClass.ServiceHid);

            return ResultCode.Success;
        }

        [CommandHipc(56)]
        // ActivateJoyXpad(nn::hid::JoyXpadId)
        public static ResultCode ActivateJoyXpad(ServiceCtx context)
        {
            int joyXpadId = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { joyXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(58)]
        // GetJoyXpadLifoHandle(nn::hid::JoyXpadId) -> nn::sf::NativeHandle
        public static ResultCode GetJoyXpadLifoHandle(ServiceCtx context)
        {
            int joyXpadId = context.RequestData.ReadInt32();

            int handle = 0;

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { joyXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(59)]
        // GetJoyXpadIds() -> long IdsCount, buffer<array<nn::hid::JoyXpadId>, type: 0xa>
        public static ResultCode GetJoyXpadIds(ServiceCtx context)
        {
            // There is any JoyXpad, so we return 0 and write nothing inside the type-0xa buffer.
            context.ResponseData.Write(0L);

            Logger.Stub?.PrintStub(LogClass.ServiceHid);

            return ResultCode.Success;
        }

        [CommandHipc(60)]
        // ActivateSixAxisSensor(nn::hid::BasicXpadId)
        public static ResultCode ActivateSixAxisSensor(ServiceCtx context)
        {
            int basicXpadId = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { basicXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(61)]
        // DeactivateSixAxisSensor(nn::hid::BasicXpadId)
        public static ResultCode DeactivateSixAxisSensor(ServiceCtx context)
        {
            int basicXpadId = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { basicXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(62)]
        // GetSixAxisSensorLifoHandle(nn::hid::BasicXpadId) -> nn::sf::NativeHandle
        public static ResultCode GetSixAxisSensorLifoHandle(ServiceCtx context)
        {
            int basicXpadId = context.RequestData.ReadInt32();

            int handle = 0;

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { basicXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(63)]
        // ActivateJoySixAxisSensor(nn::hid::JoyXpadId)
        public static ResultCode ActivateJoySixAxisSensor(ServiceCtx context)
        {
            int joyXpadId = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { joyXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(64)]
        // DeactivateJoySixAxisSensor(nn::hid::JoyXpadId)
        public static ResultCode DeactivateJoySixAxisSensor(ServiceCtx context)
        {
            int joyXpadId = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { joyXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(65)]
        // GetJoySixAxisSensorLifoHandle(nn::hid::JoyXpadId) -> nn::sf::NativeHandle
        public static ResultCode GetJoySixAxisSensorLifoHandle(ServiceCtx context)
        {
            int joyXpadId = context.RequestData.ReadInt32();

            int handle = 0;

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { joyXpadId });

            return ResultCode.Success;
        }

        [CommandHipc(66)]
        // StartSixAxisSensor(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public static ResultCode StartSixAxisSensor(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(67)]
        // StopSixAxisSensor(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public static ResultCode StopSixAxisSensor(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(68)]
        // IsSixAxisSensorFusionEnabled(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId) -> bool IsEnabled
        public ResultCode IsSixAxisSensorFusionEnabled(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_sixAxisSensorFusionEnabled);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _sixAxisSensorFusionEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(69)]
        // EnableSixAxisSensorFusion(bool Enabled, nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public ResultCode EnableSixAxisSensorFusion(ServiceCtx context)
        {
            _sixAxisSensorFusionEnabled = context.RequestData.ReadUInt32() != 0;
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _sixAxisSensorFusionEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(70)]
        // SetSixAxisSensorFusionParameters(nn::hid::SixAxisSensorHandle, float RevisePower, float ReviseRange, nn::applet::AppletResourceUserId)
        public ResultCode SetSixAxisSensorFusionParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding

            _sensorFusionParams = new SensorFusionParameters
            {
                RevisePower = context.RequestData.ReadInt32(),
                ReviseRange = context.RequestData.ReadInt32()
            };

            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _sensorFusionParams.RevisePower, _sensorFusionParams.ReviseRange });

            return ResultCode.Success;
        }

        [CommandHipc(71)]
        // GetSixAxisSensorFusionParameters(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId) -> float RevisePower, float ReviseRange)
        public ResultCode GetSixAxisSensorFusionParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_sensorFusionParams.RevisePower);
            context.ResponseData.Write(_sensorFusionParams.ReviseRange);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _sensorFusionParams.RevisePower, _sensorFusionParams.ReviseRange });

            return ResultCode.Success;
        }

        [CommandHipc(72)]
        // ResetSixAxisSensorFusionParameters(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public ResultCode ResetSixAxisSensorFusionParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            _sensorFusionParams.RevisePower = 0;
            _sensorFusionParams.ReviseRange = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _sensorFusionParams.RevisePower, _sensorFusionParams.ReviseRange });

            return ResultCode.Success;
        }

        [CommandHipc(73)]
        // SetAccelerometerParameters(nn::hid::SixAxisSensorHandle, float X, float Y, nn::applet::AppletResourceUserId)
        public ResultCode SetAccelerometerParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding

            _accelerometerParams = new AccelerometerParameters
            {
                X = context.RequestData.ReadInt32(),
                Y = context.RequestData.ReadInt32()
            };

            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerParams.X, _accelerometerParams.Y });

            return ResultCode.Success;
        }

        [CommandHipc(74)]
        // GetAccelerometerParameters(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId) -> float X, float Y
        public ResultCode GetAccelerometerParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_accelerometerParams.X);
            context.ResponseData.Write(_accelerometerParams.Y);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerParams.X, _accelerometerParams.Y });

            return ResultCode.Success;
        }

        [CommandHipc(75)]
        // ResetAccelerometerParameters(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public ResultCode ResetAccelerometerParameters(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            _accelerometerParams.X = 0;
            _accelerometerParams.Y = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerParams.X, _accelerometerParams.Y });

            return ResultCode.Success;
        }

        [CommandHipc(76)]
        // SetAccelerometerPlayMode(nn::hid::SixAxisSensorHandle, uint PlayMode, nn::applet::AppletResourceUserId)
        public ResultCode SetAccelerometerPlayMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            _accelerometerPlayMode = context.RequestData.ReadUInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerPlayMode });

            return ResultCode.Success;
        }

        [CommandHipc(77)]
        // GetAccelerometerPlayMode(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId) -> uint PlayMode
        public ResultCode GetAccelerometerPlayMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_accelerometerPlayMode);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerPlayMode });

            return ResultCode.Success;
        }

        [CommandHipc(78)]
        // ResetAccelerometerPlayMode(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public ResultCode ResetAccelerometerPlayMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            _accelerometerPlayMode = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _accelerometerPlayMode });

            return ResultCode.Success;
        }

        [CommandHipc(79)]
        // SetGyroscopeZeroDriftMode(nn::hid::SixAxisSensorHandle, uint GyroscopeZeroDriftMode, nn::applet::AppletResourceUserId)
        public ResultCode SetGyroscopeZeroDriftMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            _gyroscopeZeroDriftMode = (GyroscopeZeroDriftMode)context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _gyroscopeZeroDriftMode });

            return ResultCode.Success;
        }

        [CommandHipc(80)]
        // GetGyroscopeZeroDriftMode(nn::applet::AppletResourceUserId, nn::hid::SixAxisSensorHandle) -> int GyroscopeZeroDriftMode
        public ResultCode GetGyroscopeZeroDriftMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write((int)_gyroscopeZeroDriftMode);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _gyroscopeZeroDriftMode });

            return ResultCode.Success;
        }

        [CommandHipc(81)]
        // ResetGyroscopeZeroDriftMode(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public ResultCode ResetGyroscopeZeroDriftMode(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            _gyroscopeZeroDriftMode = GyroscopeZeroDriftMode.Standard;

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _gyroscopeZeroDriftMode });

            return ResultCode.Success;
        }

        [CommandHipc(82)]
        // IsSixAxisSensorAtRest(nn::hid::SixAxisSensorHandle, nn::applet::AppletResourceUserId) -> bool IsAsRest
        public static ResultCode IsSixAxisSensorAtRest(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            bool isAtRest = true;

            context.ResponseData.Write(isAtRest);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, isAtRest });

            return ResultCode.Success;
        }

        [CommandHipc(83)] // 6.0.0+
        // IsFirmwareUpdateAvailableForSixAxisSensor(nn::hid::AppletResourceUserId, nn::hid::SixAxisSensorHandle, pid) -> bool UpdateAvailable
        public ResultCode IsFirmwareUpdateAvailableForSixAxisSensor(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_isFirmwareUpdateAvailableForSixAxisSensor);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _isFirmwareUpdateAvailableForSixAxisSensor });

            return ResultCode.Success;
        }

        [CommandHipc(84)] // 13.0.0+
        // EnableSixAxisSensorUnalteredPassthrough(nn::applet::AppletResourceUserId, nn::hid::SixAxisSensorHandle, u8 enabled)
        public ResultCode EnableSixAxisSensorUnalteredPassthrough(ServiceCtx context)
        {
            _isSixAxisSensorUnalteredPassthroughEnabled = context.RequestData.ReadUInt32() != 0;
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle, _isSixAxisSensorUnalteredPassthroughEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(85)] // 13.0.0+
        // IsSixAxisSensorUnalteredPassthroughEnabled(nn::applet::AppletResourceUserId, nn::hid::SixAxisSensorHandle) -> u8 enabled
        public ResultCode IsSixAxisSensorUnalteredPassthroughEnabled(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_isSixAxisSensorUnalteredPassthroughEnabled);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(87)] // 13.0.0+
        // LoadSixAxisSensorCalibrationParameter(nn::applet::AppletResourceUserId, nn::hid::SixAxisSensorHandle, u64 unknown)
        public static ResultCode LoadSixAxisSensorCalibrationParameter(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            // TODO: CalibrationParameter have to be determined.

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(88)] // 13.0.0+
        // GetSixAxisSensorIcInformation(nn::applet::AppletResourceUserId, nn::hid::SixAxisSensorHandle) -> u64 unknown
        public static ResultCode GetSixAxisSensorIcInformation(ServiceCtx context)
        {
            int sixAxisSensorHandle = context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            // TODO: IcInformation have to be determined.

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, sixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(91)]
        // ActivateGesture(nn::applet::AppletResourceUserId, int Unknown0)
        public static ResultCode ActivateGesture(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();
            int  unknown0             = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, unknown0 });

            return ResultCode.Success;
        }

        [CommandHipc(100)]
        // SetSupportedNpadStyleSet(pid, nn::applet::AppletResourceUserId, nn::hid::NpadStyleTag)
        public static ResultCode SetSupportedNpadStyleSet(ServiceCtx context)
        {
            ulong pid = context.Request.HandleDesc.PId;
            ControllerType type = (ControllerType)context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { pid, appletResourceUserId, type });

            context.Device.Hid.Npads.SupportedStyleSets = type;

            return ResultCode.Success;
        }

        [CommandHipc(101)]
        // GetSupportedNpadStyleSet(pid, nn::applet::AppletResourceUserId) -> uint nn::hid::NpadStyleTag
        public static ResultCode GetSupportedNpadStyleSet(ServiceCtx context)
        {
#pragma warning disable IDE0059
            ulong pid                  = context.Request.HandleDesc.PId;
#pragma warning restore IDE0059
            long  appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write((int)context.Device.Hid.Npads.SupportedStyleSets);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, context.Device.Hid.Npads.SupportedStyleSets });

            return ResultCode.Success;
        }

        [CommandHipc(102)]
        // SetSupportedNpadIdType(nn::applet::AppletResourceUserId, array<NpadIdType, 9>)
        public static ResultCode SetSupportedNpadIdType(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long  appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059
            ulong arrayPosition        = context.Request.PtrBuff[0].Position;
            ulong arraySize            = context.Request.PtrBuff[0].Size;

            ReadOnlySpan<NpadIdType> supportedPlayerIds = MemoryMarshal.Cast<byte, NpadIdType>(context.Memory.GetSpan(arrayPosition, (int)arraySize));

            context.Device.Hid.Npads.ClearSupportedPlayers();

            for (int i = 0; i < supportedPlayerIds.Length; ++i)
            {
                if (HidUtils.IsValidNpadIdType(supportedPlayerIds[i]))
                {
                    context.Device.Hid.Npads.SetSupportedPlayer(HidUtils.GetIndexFromNpadIdType(supportedPlayerIds[i]));
                }
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, $"{supportedPlayerIds.Length} Players: " + string.Join(",", supportedPlayerIds.ToArray()));

            return ResultCode.Success;
        }

        [CommandHipc(103)]
        // ActivateNpad(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateNpad(ServiceCtx context)
        {
            return ActiveNpadImpl(context);
        }

        [CommandHipc(104)]
        // DeactivateNpad(nn::applet::AppletResourceUserId)
        public static ResultCode DeactivateNpad(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.Device.Hid.Npads.Active = false;
            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(106)]
        // AcquireNpadStyleSetUpdateEventHandle(nn::applet::AppletResourceUserId, uint, ulong) -> nn::sf::NativeHandle
        public static ResultCode AcquireNpadStyleSetUpdateEventHandle(ServiceCtx context)
        {
            PlayerIndex npadId               = HidUtils.GetIndexFromNpadIdType((NpadIdType)context.RequestData.ReadInt32());
            long        appletResourceUserId = context.RequestData.ReadInt64();
            long        npadStyleSet         = context.RequestData.ReadInt64();

            KEvent evnt = context.Device.Hid.Npads.GetStyleSetUpdateEvent(npadId);
            if (context.Process.HandleTable.GenerateHandle(evnt.ReadableEvent, out int handle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            // Games expect this event to be signaled after calling this function
            evnt.ReadableEvent.Signal();

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, npadId, npadStyleSet });

            return ResultCode.Success;
        }

        [CommandHipc(107)]
        // DisconnectNpad(nn::applet::AppletResourceUserId, uint NpadIdType)
        public static ResultCode DisconnectNpad(ServiceCtx context)
        {
            NpadIdType npadIdType           = (NpadIdType)context.RequestData.ReadInt32();
            long       appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, npadIdType });

            return ResultCode.Success;
        }

        [CommandHipc(108)]
        // GetPlayerLedPattern(u32 npad_id) -> u64 led_pattern
        public static ResultCode GetPlayerLedPattern(ServiceCtx context)
        {
            NpadIdType npadId = (NpadIdType)context.RequestData.ReadUInt32();

            ulong ledPattern = npadId switch
            {
                NpadIdType.Player1  => 0b0001,
                NpadIdType.Player2  => 0b0011,
                NpadIdType.Player3  => 0b0111,
                NpadIdType.Player4  => 0b1111,
                NpadIdType.Player5  => 0b1001,
                NpadIdType.Player6  => 0b0101,
                NpadIdType.Player7  => 0b1101,
                NpadIdType.Player8  => 0b0110,
                NpadIdType.Unknown  => 0b0000,
                NpadIdType.Handheld => 0b0000,
#pragma warning disable CA2208
                _ => throw new ArgumentOutOfRangeException(nameof(npadId), npadId, null)
#pragma warning restore CA2208
            };

            context.ResponseData.Write(ledPattern);

            return ResultCode.Success;
        }

        [CommandHipc(109)] // 5.0.0+
        // ActivateNpadWithRevision(nn::applet::AppletResourceUserId, ulong revision)
        public static ResultCode ActivateNpadWithRevision(ServiceCtx context)
        {
            ulong revision = context.RequestData.ReadUInt64();

            return ActiveNpadImpl(context, revision);
        }

        private static ResultCode ActiveNpadImpl(ServiceCtx context, ulong revision = 0)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.Device.Hid.Npads.Active = true;

            // Initialize entries to avoid issues with some games.

            List<GamepadInput> emptyGamepadInputs = new();
            List<SixAxisInput> emptySixAxisInputs = new();

            for (int player = 0; player < NpadDevices.MaxControllers; player++)
            {
                GamepadInput gamepadInput = new();
                SixAxisInput sixaxisInput = new();

                gamepadInput.PlayerId = (PlayerIndex)player;
                sixaxisInput.PlayerId = (PlayerIndex)player;

                sixaxisInput.Orientation = new float[9];

                emptyGamepadInputs.Add(gamepadInput);
                emptySixAxisInputs.Add(sixaxisInput);
            }

            for (int entry = 0; entry < Hid.SharedMemEntryCount; entry++)
            {
                context.Device.Hid.Npads.Update(emptyGamepadInputs);
                context.Device.Hid.Npads.UpdateSixAxis(emptySixAxisInputs);
            }

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, revision });

            return ResultCode.Success;
        }

        [CommandHipc(120)]
        // SetNpadJoyHoldType(nn::applet::AppletResourceUserId, ulong NpadJoyHoldType)
        public static ResultCode SetNpadJoyHoldType(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            NpadJoyHoldType npadJoyHoldType = (NpadJoyHoldType)context.RequestData.ReadUInt64();

            if (npadJoyHoldType > NpadJoyHoldType.Horizontal)
            {
#pragma warning disable CA2208
                throw new ArgumentOutOfRangeException(nameof(npadJoyHoldType), npadJoyHoldType, null);
#pragma warning restore CA2208
            }

            foreach (PlayerIndex playerIndex in context.Device.Hid.Npads.GetSupportedPlayers())
            {
                if (HidUtils.GetNpadIdTypeFromIndex(playerIndex) > NpadIdType.Handheld)
                {
                    return ResultCode.InvalidNpadIdType;
                }
            }

            context.Device.Hid.Npads.JoyHold = npadJoyHoldType;

            return ResultCode.Success;
        }

        [CommandHipc(121)]
        // GetNpadJoyHoldType(nn::applet::AppletResourceUserId) -> ulong NpadJoyHoldType
        public static ResultCode GetNpadJoyHoldType(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            foreach (PlayerIndex playerIndex in context.Device.Hid.Npads.GetSupportedPlayers())
            {
                if (HidUtils.GetNpadIdTypeFromIndex(playerIndex) > NpadIdType.Handheld)
                {
                    return ResultCode.InvalidNpadIdType;
                }
            }

            context.ResponseData.Write((ulong)context.Device.Hid.Npads.JoyHold);

            return ResultCode.Success;
        }

        [CommandHipc(122)]
        // SetNpadJoyAssignmentModeSingleByDefault(uint HidControllerId, nn::applet::AppletResourceUserId)
        public static ResultCode SetNpadJoyAssignmentModeSingleByDefault(ServiceCtx context)
        {
            NpadIdType npadIdType = (NpadIdType)context.RequestData.ReadUInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            if (HidUtils.IsValidNpadIdType(npadIdType))
            {
                context.Device.Hid.SharedMemory.Npads[(int)HidUtils.GetIndexFromNpadIdType(npadIdType)].InternalState.JoyAssignmentMode = NpadJoyAssignmentMode.Single;
            }

            return ResultCode.Success;
        }

        [CommandHipc(123)]
        // SetNpadJoyAssignmentModeSingle(uint npadIdType, nn::applet::AppletResourceUserId, uint npadJoyDeviceType)
        public static ResultCode SetNpadJoyAssignmentModeSingle(ServiceCtx context)
        {
            NpadIdType npadIdType = (NpadIdType)context.RequestData.ReadUInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();
            NpadJoyDeviceType npadJoyDeviceType = (NpadJoyDeviceType)context.RequestData.ReadUInt32();

            if (HidUtils.IsValidNpadIdType(npadIdType))
            {
                SetNpadJoyAssignmentModeSingleWithDestinationImpl(context, npadIdType, appletResourceUserId, npadJoyDeviceType, out _, out _);
            }

            return ResultCode.Success;
        }

        [CommandHipc(124)]
        // SetNpadJoyAssignmentModeDual(uint npadIdType, nn::applet::AppletResourceUserId)
        public static ResultCode SetNpadJoyAssignmentModeDual(ServiceCtx context)
        {
            NpadIdType npadIdType = (NpadIdType)context.RequestData.ReadUInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            if (HidUtils.IsValidNpadIdType(npadIdType))
            {
                context.Device.Hid.SharedMemory.Npads[(int)HidUtils.GetIndexFromNpadIdType(npadIdType)].InternalState.JoyAssignmentMode = NpadJoyAssignmentMode.Dual;
            }

            return ResultCode.Success;
        }

        [CommandHipc(125)]
        // MergeSingleJoyAsDualJoy(uint npadIdType0, uint npadIdType1, nn::applet::AppletResourceUserId)
        public static ResultCode MergeSingleJoyAsDualJoy(ServiceCtx context)
        {
            NpadIdType npadIdType0          = (NpadIdType)context.RequestData.ReadUInt32();
            NpadIdType npadIdType1          = (NpadIdType)context.RequestData.ReadUInt32();
            long       appletResourceUserId = context.RequestData.ReadInt64();

            if (HidUtils.IsValidNpadIdType(npadIdType0) && HidUtils.IsValidNpadIdType(npadIdType1))
            {
                Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, npadIdType0, npadIdType1 });
            }

            return ResultCode.Success;
        }

        [CommandHipc(126)]
        // StartLrAssignmentMode(nn::applet::AppletResourceUserId)
        public static ResultCode StartLrAssignmentMode(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(127)]
        // StopLrAssignmentMode(nn::applet::AppletResourceUserId)
        public static ResultCode StopLrAssignmentMode(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(128)]
        // SetNpadHandheldActivationMode(nn::applet::AppletResourceUserId, long HidNpadHandheldActivationMode)
        public ResultCode SetNpadHandheldActivationMode(ServiceCtx context)
        {
            long appletResourceUserId   = context.RequestData.ReadInt64();
            _npadHandheldActivationMode = (NpadHandheldActivationMode)context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, _npadHandheldActivationMode });

            return ResultCode.Success;
        }

        [CommandHipc(129)]
        // GetNpadHandheldActivationMode(nn::applet::AppletResourceUserId) -> long HidNpadHandheldActivationMode
        public ResultCode GetNpadHandheldActivationMode(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write((long)_npadHandheldActivationMode);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, _npadHandheldActivationMode });

            return ResultCode.Success;
        }

        [CommandHipc(130)]
        // SwapNpadAssignment(uint OldNpadAssignment, uint NewNpadAssignment, nn::applet::AppletResourceUserId)
        public static ResultCode SwapNpadAssignment(ServiceCtx context)
        {
            int  oldNpadAssignment    = context.RequestData.ReadInt32();
            int  newNpadAssignment    = context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, oldNpadAssignment, newNpadAssignment });

            return ResultCode.Success;
        }

        [CommandHipc(131)]
        // IsUnintendedHomeButtonInputProtectionEnabled(uint Unknown0, nn::applet::AppletResourceUserId) ->  bool IsEnabled
        public ResultCode IsUnintendedHomeButtonInputProtectionEnabled(ServiceCtx context)
        {
            uint unknown0             = context.RequestData.ReadUInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_unintendedHomeButtonInputProtectionEnabled);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, unknown0, _unintendedHomeButtonInputProtectionEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(132)]
        // EnableUnintendedHomeButtonInputProtection(bool Enable, uint Unknown0, nn::applet::AppletResourceUserId)
        public ResultCode EnableUnintendedHomeButtonInputProtection(ServiceCtx context)
        {
            _unintendedHomeButtonInputProtectionEnabled = context.RequestData.ReadBoolean();
            uint unknown0                               = context.RequestData.ReadUInt32();
            long appletResourceUserId                   = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, unknown0, _unintendedHomeButtonInputProtectionEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(133)] // 5.0.0+
        // SetNpadJoyAssignmentModeSingleWithDestination(uint npadIdType, uint npadJoyDeviceType, nn::applet::AppletResourceUserId) -> bool npadIdTypeIsSet, uint npadIdTypeSet
        public static ResultCode SetNpadJoyAssignmentModeSingleWithDestination(ServiceCtx context)
        {
            NpadIdType        npadIdType        = (NpadIdType)context.RequestData.ReadInt32();
            NpadJoyDeviceType npadJoyDeviceType = (NpadJoyDeviceType)context.RequestData.ReadInt32();
            context.RequestData.BaseStream.Position += 4; // Padding
            long appletResourceUserId = context.RequestData.ReadInt64();

            if (HidUtils.IsValidNpadIdType(npadIdType))
            {
                SetNpadJoyAssignmentModeSingleWithDestinationImpl(context, npadIdType, appletResourceUserId, npadJoyDeviceType, out NpadIdType npadIdTypeSet, out bool npadIdTypeIsSet);

                if (npadIdTypeIsSet)
                {
                    context.ResponseData.Write(npadIdTypeIsSet);
                    context.ResponseData.Write((uint)npadIdTypeSet);
                }
            }

            return ResultCode.Success;
        }

#pragma warning disable IDE0060
        private static void SetNpadJoyAssignmentModeSingleWithDestinationImpl(ServiceCtx context, NpadIdType npadIdType, long appletResourceUserId, NpadJoyDeviceType npadJoyDeviceType, out NpadIdType npadIdTypeSet, out bool npadIdTypeIsSet)
        {
            npadIdTypeSet   = default;
            npadIdTypeIsSet = false;

            context.Device.Hid.SharedMemory.Npads[(int)HidUtils.GetIndexFromNpadIdType(npadIdType)].InternalState.JoyAssignmentMode = NpadJoyAssignmentMode.Single;

            // TODO: Service seems to use the npadJoyDeviceType to find the nearest other Npad available and merge them to dual.
            //       If one is found, it returns the npadIdType of the other Npad and a bool.
            //       If not, it returns nothing.
        }
#pragma warning restore IDE0060

        [CommandHipc(200)]
        // GetVibrationDeviceInfo(nn::hid::VibrationDeviceHandle) -> nn::hid::VibrationDeviceInfo
        public static ResultCode GetVibrationDeviceInfo(ServiceCtx context)
        {
            VibrationDeviceHandle deviceHandle = context.RequestData.ReadStruct<VibrationDeviceHandle>();
            NpadStyleIndex        deviceType   = (NpadStyleIndex)deviceHandle.DeviceType;
            NpadIdType            npadIdType   = (NpadIdType)deviceHandle.PlayerId;

            if (deviceType < NpadStyleIndex.System || deviceType >= NpadStyleIndex.FullKey)
            {
                if (!HidUtils.IsValidNpadIdType(npadIdType))
                {
                    return ResultCode.InvalidNpadIdType;
                }

                if (deviceHandle.Position > 1)
                {
                    return ResultCode.InvalidDeviceIndex;
                }

                VibrationDeviceType vibrationDeviceType = VibrationDeviceType.None;

                if (Enum.IsDefined(deviceType))
                {
                    vibrationDeviceType = VibrationDeviceType.LinearResonantActuator;
                }
                else if ((uint)deviceType == 8)
                {
                    vibrationDeviceType = VibrationDeviceType.GcErm;
                }

                VibrationDevicePosition vibrationDevicePosition = VibrationDevicePosition.None;

                if (vibrationDeviceType == VibrationDeviceType.LinearResonantActuator)
                {
                    if (deviceHandle.Position == 0)
                    {
                        vibrationDevicePosition = VibrationDevicePosition.Left;
                    }
                    else if (deviceHandle.Position == 1)
                    {
                        vibrationDevicePosition = VibrationDevicePosition.Right;
                    }
                    else
                    {
#pragma warning disable CA2208
                        throw new ArgumentOutOfRangeException(nameof(deviceHandle.Position), deviceHandle.Position, null);
#pragma warning restore CA2208
                    }
                }

                VibrationDeviceValue deviceInfo = new()
                {
                    DeviceType = vibrationDeviceType,
                    Position = vibrationDevicePosition
                };

                context.ResponseData.WriteStruct(deviceInfo);

                return ResultCode.Success;
            }

            return ResultCode.InvalidNpadDeviceType;
        }

        [CommandHipc(201)]
        // SendVibrationValue(nn::hid::VibrationDeviceHandle, nn::hid::VibrationValue, nn::applet::AppletResourceUserId)
        public static ResultCode SendVibrationValue(ServiceCtx context)
        {
            VibrationDeviceHandle deviceHandle = new()
            {
                DeviceType = context.RequestData.ReadByte(),
                PlayerId   = context.RequestData.ReadByte(),
                Position   = context.RequestData.ReadByte(),
                Reserved   = context.RequestData.ReadByte()
            };

            VibrationValue vibrationValue = new()
            {
                AmplitudeLow  = context.RequestData.ReadSingle(),
                FrequencyLow  = context.RequestData.ReadSingle(),
                AmplitudeHigh = context.RequestData.ReadSingle(),
                FrequencyHigh = context.RequestData.ReadSingle()
            };

#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            Dictionary<byte, VibrationValue> dualVibrationValues = new()
            {
                [deviceHandle.Position] = vibrationValue
            };

            context.Device.Hid.Npads.UpdateRumbleQueue((PlayerIndex)deviceHandle.PlayerId, dualVibrationValues);

            return ResultCode.Success;
        }

        [CommandHipc(202)]
        // GetActualVibrationValue(nn::hid::VibrationDeviceHandle, nn::applet::AppletResourceUserId) -> nn::hid::VibrationValue
        public static ResultCode GetActualVibrationValue(ServiceCtx context)
        {
            VibrationDeviceHandle deviceHandle = new()
            {
                DeviceType = context.RequestData.ReadByte(),
                PlayerId   = context.RequestData.ReadByte(),
                Position   = context.RequestData.ReadByte(),
                Reserved   = context.RequestData.ReadByte()
            };

#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            VibrationValue vibrationValue = context.Device.Hid.Npads.GetLastVibrationValue((PlayerIndex)deviceHandle.PlayerId, deviceHandle.Position);

            context.ResponseData.Write(vibrationValue.AmplitudeLow);
            context.ResponseData.Write(vibrationValue.FrequencyLow);
            context.ResponseData.Write(vibrationValue.AmplitudeHigh);
            context.ResponseData.Write(vibrationValue.FrequencyHigh);

            return ResultCode.Success;
        }

        [CommandHipc(203)]
        // CreateActiveVibrationDeviceList() -> object<nn::hid::IActiveVibrationDeviceList>
        public ResultCode CreateActiveVibrationDeviceList(ServiceCtx context)
        {
            MakeObject(context, new IActiveApplicationDeviceList());

            return ResultCode.Success;
        }

        [CommandHipc(204)]
        // PermitVibration(bool Enable)
        public ResultCode PermitVibration(ServiceCtx context)
        {
            _vibrationPermitted = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { _vibrationPermitted });

            return ResultCode.Success;
        }

        [CommandHipc(205)]
        // IsVibrationPermitted() -> bool IsEnabled
        public ResultCode IsVibrationPermitted(ServiceCtx context)
        {
            context.ResponseData.Write(_vibrationPermitted);

            return ResultCode.Success;
        }

        [CommandHipc(206)]
        // SendVibrationValues(nn::applet::AppletResourceUserId, buffer<array<nn::hid::VibrationDeviceHandle>, type: 9>, buffer<array<nn::hid::VibrationValue>, type: 9>)
        public static ResultCode SendVibrationValues(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long appletResourceUserId = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            byte[] vibrationDeviceHandleBuffer = new byte[context.Request.PtrBuff[0].Size];

            context.Memory.Read(context.Request.PtrBuff[0].Position, vibrationDeviceHandleBuffer);

            byte[] vibrationValueBuffer = new byte[context.Request.PtrBuff[1].Size];

            context.Memory.Read(context.Request.PtrBuff[1].Position, vibrationValueBuffer);

            Span<VibrationDeviceHandle> deviceHandles   = MemoryMarshal.Cast<byte, VibrationDeviceHandle>(vibrationDeviceHandleBuffer);
            Span<VibrationValue>        vibrationValues = MemoryMarshal.Cast<byte, VibrationValue>(vibrationValueBuffer);

            if (!deviceHandles.IsEmpty && vibrationValues.Length == deviceHandles.Length)
            {
                Dictionary<byte, VibrationValue> dualVibrationValues = new();
                PlayerIndex currentIndex = (PlayerIndex)deviceHandles[0].PlayerId;

                for (int deviceCounter = 0; deviceCounter < deviceHandles.Length; deviceCounter++)
                {
                    PlayerIndex index = (PlayerIndex)deviceHandles[deviceCounter].PlayerId;
                    byte position = deviceHandles[deviceCounter].Position;

                    if (index != currentIndex || dualVibrationValues.Count == 2)
                    {
                        context.Device.Hid.Npads.UpdateRumbleQueue(currentIndex, dualVibrationValues);
                        dualVibrationValues = new Dictionary<byte, VibrationValue>();
                    }

                    dualVibrationValues[position] = vibrationValues[deviceCounter];
                    currentIndex = index;
                }

                context.Device.Hid.Npads.UpdateRumbleQueue(currentIndex, dualVibrationValues);
            }

            return ResultCode.Success;
        }

        [CommandHipc(207)] // 4.0.0+
        // SendVibrationGcErmCommand(nn::hid::VibrationDeviceHandle, nn::hid::VibrationGcErmCommand, nn::applet::AppletResourceUserId)
        public static ResultCode SendVibrationGcErmCommand(ServiceCtx context)
        {
            int  vibrationDeviceHandle = context.RequestData.ReadInt32();
            long vibrationGcErmCommand = context.RequestData.ReadInt64();
            long appletResourceUserId  = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, vibrationDeviceHandle, vibrationGcErmCommand });

            return ResultCode.Success;
        }

        [CommandHipc(208)] // 4.0.0+
        // GetActualVibrationGcErmCommand(nn::hid::VibrationDeviceHandle, nn::applet::AppletResourceUserId) -> nn::hid::VibrationGcErmCommand
        public ResultCode GetActualVibrationGcErmCommand(ServiceCtx context)
        {
            int  vibrationDeviceHandle = context.RequestData.ReadInt32();
            long appletResourceUserId  = context.RequestData.ReadInt64();

            context.ResponseData.Write(_vibrationGcErmCommand);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, vibrationDeviceHandle, _vibrationGcErmCommand });

            return ResultCode.Success;
        }

        [CommandHipc(209)] // 4.0.0+
        // BeginPermitVibrationSession(nn::applet::AppletResourceUserId)
        public static ResultCode BeginPermitVibrationSession(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(210)] // 4.0.0+
        // EndPermitVibrationSession()
#pragma warning disable IDE0060
        public static ResultCode EndPermitVibrationSession(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceHid);

            return ResultCode.Success;
        }
#pragma warning restore IDE0060

        [CommandHipc(211)] // 7.0.0+
        // IsVibrationDeviceMounted(nn::hid::VibrationDeviceHandle, nn::applet::AppletResourceUserId)
        public static ResultCode IsVibrationDeviceMounted(ServiceCtx context)
        {
#pragma warning disable IDE0059
            int  vibrationDeviceHandle = context.RequestData.ReadInt32();
            long appletResourceUserId  = context.RequestData.ReadInt64();
#pragma warning restore IDE0059

            // NOTE: Service use vibrationDeviceHandle to get the PlayerIndex.
            //       And return false if (npadIdType >= (NpadIdType)8 && npadIdType != NpadIdType.Handheld && npadIdType != NpadIdType.Unknown)

            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandHipc(300)]
        // ActivateConsoleSixAxisSensor(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateConsoleSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(301)]
        // StartConsoleSixAxisSensor(nn::hid::ConsoleSixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public static ResultCode StartConsoleSixAxisSensor(ServiceCtx context)
        {
            int  consoleSixAxisSensorHandle = context.RequestData.ReadInt32();
            long appletResourceUserId       = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, consoleSixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(302)]
        // StopConsoleSixAxisSensor(nn::hid::ConsoleSixAxisSensorHandle, nn::applet::AppletResourceUserId)
        public static ResultCode StopConsoleSixAxisSensor(ServiceCtx context)
        {
            int  consoleSixAxisSensorHandle = context.RequestData.ReadInt32();
            long appletResourceUserId       = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, consoleSixAxisSensorHandle });

            return ResultCode.Success;
        }

        [CommandHipc(303)] // 5.0.0+
        // ActivateSevenSixAxisSensor(nn::applet::AppletResourceUserId)
        public static ResultCode ActivateSevenSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(304)] // 5.0.0+
        // StartSevenSixAxisSensor(nn::applet::AppletResourceUserId)
        public static ResultCode StartSevenSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(305)] // 5.0.0+
        // StopSevenSixAxisSensor(nn::applet::AppletResourceUserId)
        public static ResultCode StopSevenSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(306)] // 5.0.0+
        // InitializeSevenSixAxisSensor(array<nn::sf::NativeHandle>, ulong Counter0, array<nn::sf::NativeHandle>, ulong Counter1, nn::applet::AppletResourceUserId)
        public static ResultCode InitializeSevenSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();
            long counter0             = context.RequestData.ReadInt64();
            long counter1             = context.RequestData.ReadInt64();

            // TODO: Determine if array<nn::sf::NativeHandle> is a buffer or not...

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, counter0, counter1 });

            return ResultCode.Success;
        }

        [CommandHipc(307)] // 5.0.0+
        // FinalizeSevenSixAxisSensor(nn::applet::AppletResourceUserId)
        public static ResultCode FinalizeSevenSixAxisSensor(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(308)] // 5.0.0+
        // SetSevenSixAxisSensorFusionStrength(float Strength, nn::applet::AppletResourceUserId)
        public ResultCode SetSevenSixAxisSensorFusionStrength(ServiceCtx context)
        {
                 _sevenSixAxisSensorFusionStrength = context.RequestData.ReadSingle();
            long appletResourceUserId              = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, _sevenSixAxisSensorFusionStrength });

            return ResultCode.Success;
        }

        [CommandHipc(309)] // 5.0.0+
        // GetSevenSixAxisSensorFusionStrength(nn::applet::AppletResourceUserId) -> float Strength
        public ResultCode GetSevenSixAxisSensorFusionStrength(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            context.ResponseData.Write(_sevenSixAxisSensorFusionStrength);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, _sevenSixAxisSensorFusionStrength });

            return ResultCode.Success;
        }

        [CommandHipc(310)] // 6.0.0+
        // ResetSevenSixAxisSensorTimestamp(pid, nn::applet::AppletResourceUserId)
        public static ResultCode ResetSevenSixAxisSensorTimestamp(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId });

            return ResultCode.Success;
        }

        [CommandHipc(400)]
        // IsUsbFullKeyControllerEnabled() -> bool IsEnabled
        public ResultCode IsUsbFullKeyControllerEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(_usbFullKeyControllerEnabled);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { _usbFullKeyControllerEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(401)]
        // EnableUsbFullKeyController(bool Enable)
        public ResultCode EnableUsbFullKeyController(ServiceCtx context)
        {
            _usbFullKeyControllerEnabled = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { _usbFullKeyControllerEnabled });

            return ResultCode.Success;
        }

        [CommandHipc(402)]
        // IsUsbFullKeyControllerConnected(uint Unknown0) -> bool Connected
        public static ResultCode IsUsbFullKeyControllerConnected(ServiceCtx context)
        {
            int unknown0 = context.RequestData.ReadInt32();

            context.ResponseData.Write(true); //FullKeyController is always connected ?

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { unknown0, Connected = true });

            return ResultCode.Success;
        }

        [CommandHipc(403)] // 4.0.0+
        // HasBattery(uint NpadId) -> bool HasBattery
        public static ResultCode HasBattery(ServiceCtx context)
        {
            int npadId = context.RequestData.ReadInt32();

            context.ResponseData.Write(true); //Npad always got a battery ?

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { npadId, HasBattery = true });

            return ResultCode.Success;
        }

        [CommandHipc(404)] // 4.0.0+
        // HasLeftRightBattery(uint NpadId) -> bool HasLeftBattery, bool HasRightBattery
        public static ResultCode HasLeftRightBattery(ServiceCtx context)
        {
            int npadId = context.RequestData.ReadInt32();

            context.ResponseData.Write(true); //Npad always got a left battery ?
            context.ResponseData.Write(true); //Npad always got a right battery ?

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { npadId, HasLeftBattery = true, HasRightBattery = true });

            return ResultCode.Success;
        }

        [CommandHipc(405)] // 4.0.0+
        // GetNpadInterfaceType(uint NpadId) -> uchar InterfaceType
        public static ResultCode GetNpadInterfaceType(ServiceCtx context)
        {
            int npadId = context.RequestData.ReadInt32();

            context.ResponseData.Write((byte)0);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { npadId, NpadInterfaceType = 0 });

            return ResultCode.Success;
        }

        [CommandHipc(406)] // 4.0.0+
        // GetNpadLeftRightInterfaceType(uint NpadId) -> uchar LeftInterfaceType, uchar RightInterfaceType
        public static ResultCode GetNpadLeftRightInterfaceType(ServiceCtx context)
        {
            int npadId = context.RequestData.ReadInt32();

            context.ResponseData.Write((byte)0);
            context.ResponseData.Write((byte)0);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { npadId, LeftInterfaceType = 0, RightInterfaceType = 0 });

            return ResultCode.Success;
        }

        [CommandHipc(500)] // 5.0.0+
        // GetPalmaConnectionHandle(uint Unknown0, nn::applet::AppletResourceUserId) -> nn::hid::PalmaConnectionHandle
        public static ResultCode GetPalmaConnectionHandle(ServiceCtx context)
        {
            int  unknown0             = context.RequestData.ReadInt32();
            long appletResourceUserId = context.RequestData.ReadInt64();

            int palmaConnectionHandle = 0;

            context.ResponseData.Write(palmaConnectionHandle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId , unknown0, palmaConnectionHandle });

            return ResultCode.Success;
        }

        [CommandHipc(501)] // 5.0.0+
        // InitializePalma(nn::hid::PalmaConnectionHandle)
        public ResultCode InitializePalma(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(502)] // 5.0.0+
        // AcquirePalmaOperationCompleteEvent(nn::hid::PalmaConnectionHandle) -> nn::sf::NativeHandle
        public ResultCode AcquirePalmaOperationCompleteEvent(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            if (context.Process.HandleTable.GenerateHandle(_palmaOperationCompleteEvent.ReadableEvent, out int handle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            return ResultCode.Success;
        }

        [CommandHipc(503)] // 5.0.0+
        // GetPalmaOperationInfo(nn::hid::PalmaConnectionHandle) -> long Unknown0, buffer<Unknown>
        public static ResultCode GetPalmaOperationInfo(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            long unknown0 = 0; //Counter?

            context.ResponseData.Write(unknown0);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, unknown0 });

            return ResultCode.Success;
        }

        [CommandHipc(504)] // 5.0.0+
        // PlayPalmaActivity(nn::hid::PalmaConnectionHandle, ulong Unknown0)
        public ResultCode PlayPalmaActivity(ServiceCtx context)
        {
            int  palmaConnectionHandle = context.RequestData.ReadInt32();
            long unknown0              = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, unknown0 });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(505)] // 5.0.0+
        // SetPalmaFrModeType(nn::hid::PalmaConnectionHandle, ulong FrModeType)
        public ResultCode SetPalmaFrModeType(ServiceCtx context)
        {
            int  palmaConnectionHandle = context.RequestData.ReadInt32();
            long frModeType            = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, frModeType });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(506)] // 5.0.0+
        // ReadPalmaStep(nn::hid::PalmaConnectionHandle)
        public static ResultCode ReadPalmaStep(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            return ResultCode.Success;
        }

        [CommandHipc(507)] // 5.0.0+
        // EnablePalmaStep(nn::hid::PalmaConnectionHandle, bool Enable)
        public ResultCode EnablePalmaStep(ServiceCtx context)
        {
            int  palmaConnectionHandle = context.RequestData.ReadInt32();
            bool enabledPalmaStep      = context.RequestData.ReadBoolean();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, enabledPalmaStep });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(508)] // 5.0.0+
        // ResetPalmaStep(nn::hid::PalmaConnectionHandle)
        public ResultCode ResetPalmaStep(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(509)] // 5.0.0+
        // ReadPalmaApplicationSection(nn::hid::PalmaConnectionHandle, ulong Unknown0, ulong Unknown1)
        public static ResultCode ReadPalmaApplicationSection(ServiceCtx context)
        {
            int  palmaConnectionHandle = context.RequestData.ReadInt32();
            long unknown0              = context.RequestData.ReadInt64();
            long unknown1              = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, unknown0, unknown1 });

            return ResultCode.Success;
        }

        [CommandHipc(510)] // 5.0.0+
        // WritePalmaApplicationSection(nn::hid::PalmaConnectionHandle, ulong Unknown0, ulong Unknown1, nn::hid::PalmaApplicationSectionAccessBuffer)
        public ResultCode WritePalmaApplicationSection(ServiceCtx context)
        {
            int  palmaConnectionHandle = context.RequestData.ReadInt32();
            long unknown0              = context.RequestData.ReadInt64();
            long unknown1              = context.RequestData.ReadInt64();
            // nn::hid::PalmaApplicationSectionAccessBuffer cast is unknown

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle, unknown0, unknown1 });

            _palmaOperationCompleteEvent.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandHipc(511)] // 5.0.0+
        // ReadPalmaUniqueCode(nn::hid::PalmaConnectionHandle)
        public static ResultCode ReadPalmaUniqueCode(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            return ResultCode.Success;
        }

        [CommandHipc(512)] // 5.0.0+
        // SetPalmaUniqueCodeInvalid(nn::hid::PalmaConnectionHandle)
        public static ResultCode SetPalmaUniqueCodeInvalid(ServiceCtx context)
        {
            int palmaConnectionHandle = context.RequestData.ReadInt32();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { palmaConnectionHandle });

            return ResultCode.Success;
        }

        [CommandHipc(522)] // 5.1.0+
        // SetIsPalmaAllConnectable(nn::applet::AppletResourceUserId, bool, pid)
        public static ResultCode SetIsPalmaAllConnectable(ServiceCtx context)
        {
            long appletResourceUserId = context.RequestData.ReadInt64();
            long unknownBool          = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, unknownBool });

            return ResultCode.Success;
        }

        [CommandHipc(525)] // 5.1.0+
        // SetPalmaBoostMode(bool)
#pragma warning disable IDE0060
        public static ResultCode SetPalmaBoostMode(ServiceCtx context)
        {
            // NOTE: Stubbed in system module.

            return ResultCode.Success;
        }
#pragma warning restore IDE0060

        [CommandHipc(1000)]
        // SetNpadCommunicationMode(long CommunicationMode, nn::applet::AppletResourceUserId)
        public ResultCode SetNpadCommunicationMode(ServiceCtx context)
        {
                 _npadCommunicationMode = context.RequestData.ReadInt64();
            long appletResourceUserId   = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, _npadCommunicationMode });

            return ResultCode.Success;
        }

        [CommandHipc(1001)]
        // GetNpadCommunicationMode() -> long CommunicationMode
        public ResultCode GetNpadCommunicationMode(ServiceCtx context)
        {
            context.ResponseData.Write(_npadCommunicationMode);

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { _npadCommunicationMode });

            return ResultCode.Success;
        }

        [CommandHipc(1002)] // 9.0.0+
        // SetTouchScreenConfiguration(nn::hid::TouchScreenConfigurationForNx, nn::applet::AppletResourceUserId)
        public static ResultCode SetTouchScreenConfiguration(ServiceCtx context)
        {
            long touchScreenConfigurationForNx = context.RequestData.ReadInt64();
            long appletResourceUserId          = context.RequestData.ReadInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { appletResourceUserId, touchScreenConfigurationForNx });

            return ResultCode.Success;
        }
    }
}