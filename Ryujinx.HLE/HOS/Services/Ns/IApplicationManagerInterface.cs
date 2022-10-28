using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ns.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ApplicationId = LibHac.ApplicationId;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:am")]
    class IApplicationManagerInterface : IpcService
    {
        // FIXME: Remove this
        private static byte[] StructToBytes<T>(T structure)
        {
            byte[] array = new byte[Marshal.SizeOf(structure)];
            GCHandle handle = default;
            try
            {
                handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                Marshal.StructureToPtr(structure, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            return array;
        }

        public IApplicationManagerInterface(ServiceCtx context) { }

        [CommandHipc(0)]
        // ListApplicationRecord(s32) -> (s32, buffer<ApplicationRecord[], 6>)
        // entry_offset -> (out_entrycount, ApplicationRecord[])
        public ResultCode ListApplicationRecord(ServiceCtx ctx)
        {
            int entryOffset = ctx.RequestData.ReadInt32();
            ulong position = ctx.Request.ReceiveBuff[0].Position;
            Logger.Info?.PrintMsg(LogClass.ServiceNs, $"ListApplicationRecord: type-0x6 pos: {position}");
            List<ApplicationRecord> records = new();

            foreach (ApplicationId appId in ctx.Device.Configuration.Titles)
            {
                records.Add(new ApplicationRecord()
                {
                    Type = ApplicationRecordType.Installed,
                    AppId = appId,
                    Unknown1 = 0x2,
                    Unknown2 = new byte[6],
                    Unknown3 = 0,
                    Unknown4 = new byte[7]
                });
            }
            // TODO: Confirm this is correct and works
            records.Sort((x, y) => (int)(x.AppId.Value - y.AppId.Value));
            if (entryOffset > 0)
            {
                records = records.Skip(entryOffset - 1).ToList();
            }

            ctx.ResponseData.Write(records.Count);
            foreach (var record in records)
            {
                ctx.Memory.Write(position, StructToBytes(record));
                position += (ulong)Marshal.SizeOf<ApplicationRecord>();
            }

            return ResultCode.Success;
        }

        [CommandHipc(400)]
        // GetApplicationControlData(u8, u64) -> (unknown<4>, buffer<unknown, 6>)
        public ResultCode GetApplicationControlData(ServiceCtx context)
        {
            byte  source  = (byte)context.RequestData.ReadInt64();
            ulong titleId = context.RequestData.ReadUInt64();

            ulong position = context.Request.ReceiveBuff[0].Position;

            byte[] nacpData = context.Device.Application.ControlData.ByteSpan.ToArray();

            context.Memory.Write(position, nacpData);

            return ResultCode.Success;
        }

        [CommandHipc(1701)] // [3.0.0+]
        // GetApplicationView(buffer<ApplicationId[], 5>) -> (buffer<ApplicationView[], 6>
        public ResultCode GetApplicationView(ServiceCtx ctx)
        {
            ulong appIdsPos = ctx.Request.SendBuff[0].Position;
            ulong appIdsSize = ctx.Request.SendBuff[0].Size;
            byte[] appIdBytes = new byte[appIdsSize];

            ctx.Memory.Read(appIdsPos, appIdBytes);

            ulong[] appIds = new ulong[appIdsSize / sizeof(ulong)];

            for (int i = 0; i < appIds.Length; i++)
            {
                appIds[i] = BitConverter.ToUInt64(appIdBytes, i * sizeof(ulong));
            }

            ulong viewsPos = ctx.Request.ReceiveBuff[0].Position;

            // TODO: Figure struct out, esp. the flags
            foreach (var appId in appIds)
            {
                ctx.Memory.Write(viewsPos, StructToBytes(
                    new ApplicationView()
                    {
                        ApplicationId = appId,
                        Flags = 0,
                        Unknown1 = 0,
                        Unknown2 = new byte[0x40]
                    }
                ));
                viewsPos += (ulong)Marshal.SizeOf<ApplicationView>();
            }

            return ResultCode.Success;
        }
    }
}