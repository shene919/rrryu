﻿using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.Horizon.Common;
using System;
using System.Net;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator
{
    class IUserLocalCommunicationService : IpcService
    {
        // TODO(Ac_K): Determine what the hardcoded unknown value is.
        private const int UnknownValue = 90;

        private readonly NetworkInterface _networkInterface;

        private int _stateChangeEventHandle = 0;

        public IUserLocalCommunicationService(ServiceCtx context)
        {
            _networkInterface = new NetworkInterface(context.Device.System);
        }

        [CommandHipc(0)]
        // GetState() -> s32 state
        public ResultCode GetState(ServiceCtx context)
        {
            if (_networkInterface.NifmState != ResultCode.Success)
            {
                context.ResponseData.Write((int)NetworkState.Error);

                return ResultCode.Success;
            }

            ResultCode result = _networkInterface.GetState(out NetworkState state);

            if (result == ResultCode.Success)
            {
                context.ResponseData.Write((int)state);
            }

            return result;
        }

        [CommandHipc(100)]
        // AttachStateChangeEvent() -> handle<copy>
        public ResultCode AttachStateChangeEvent(ServiceCtx context)
        {
            if (_stateChangeEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_networkInterface.StateChangeEvent.ReadableEvent, out _stateChangeEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_stateChangeEventHandle);

            // Return ResultCode.InvalidArgument if handle is null, doesn't occur in our case since we already throw an Exception.

            return ResultCode.Success;
        }

        [CommandHipc(400)]
        // InitializeOld(u64, pid)
#pragma warning disable IDE0060
        public static ResultCode InitializeOld(ServiceCtx context)
        {
            return NetworkInterface.Initialize(UnknownValue, 0, null, null);
        }
#pragma warning restore IDE0060

        [CommandHipc(401)]
        // Finalize()
#pragma warning disable IDE0060
        public ResultCode Finalize(ServiceCtx context)
        {
            return _networkInterface.Finalize();
        }
#pragma warning restore IDE0060

        [CommandHipc(402)] // 7.0.0+
        // Initialize(u64 ip_addresses, u64, pid)
        public static ResultCode Initialize(ServiceCtx context)
        {
            // TODO(Ac_K): Determine what addresses are.
            IPAddress unknownAddress1 = new(context.RequestData.ReadUInt32());
            IPAddress unknownAddress2 = new(context.RequestData.ReadUInt32());

            return NetworkInterface.Initialize(UnknownValue, version: 1, unknownAddress1, unknownAddress2);
        }
    }
}