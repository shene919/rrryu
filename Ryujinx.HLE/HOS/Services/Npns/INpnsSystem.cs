using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Npns
{
    [Service("npns:s")]
    class INpnsSystem : IpcService
    {
        public INpnsSystem(ServiceCtx context) { }

        [CommandHipc(101)]
        public ResultCode Suspend(ServiceCtx ctx)
        {
            return NpnsBase.Suspend();
        }
    }
}