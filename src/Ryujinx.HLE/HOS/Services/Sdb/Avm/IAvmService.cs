namespace Ryujinx.HLE.HOS.Services.Sdb.Avm
{
    [Service("avm")] // 6.0.0+
    class IAvmService : IpcService
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public IAvmService(ServiceCtx context) { }
#pragma warning restore IDE0060
    }
}