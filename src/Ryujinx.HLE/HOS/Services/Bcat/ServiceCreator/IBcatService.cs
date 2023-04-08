using Ryujinx.HLE.HOS.Services.Arp;

namespace Ryujinx.HLE.HOS.Services.Bcat.ServiceCreator
{
    class IBcatService : IpcService
    {
#pragma warning disable IDE0060
        public IBcatService(ApplicationLaunchProperty applicationLaunchProperty) { }
#pragma warning restore IDE0060

        [CommandHipc(10100)]
        // RequestSyncDeliveryCache() -> object<nn::bcat::detail::ipc::IDeliveryCacheProgressService>
        public ResultCode RequestSyncDeliveryCache(ServiceCtx context)
        {
            MakeObject(context, new IDeliveryCacheProgressService(context));

            return ResultCode.Success;
        }
    }
}
