using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Nim
{
    class IShopServiceAccessServer : IpcService
    {
        public IShopServiceAccessServer() { }

        [CommandCmif(0)]
        // CreateAccessorInterface(u8) -> object<nn::ec::IShopServiceAccessor>
        public ResultCode CreateAccessorInterface(ServiceCtx context)
        {
            MakeObject(context, new IShopServiceAccessor(context.Device.System));

            Logger.Stub?.PrintStub(LogClass.ServiceNim);

            return ResultCode.Success;
        }
    }
}