namespace Ryujinx.HLE.HOS.Services.Npns
{
    [Service("npns:u")]
    class INpnsUser : IpcService
    {
        public INpnsUser(ServiceCtx context) { }

        [CommandHipc(101)]
        public ResultCode Suspend(ServiceCtx ctx)
        {
            return NpnsBase.Suspend();
        }
    }
}