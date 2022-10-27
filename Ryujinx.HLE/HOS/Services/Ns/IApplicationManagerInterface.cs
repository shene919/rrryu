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
    }
}