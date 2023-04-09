namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class IManagerForSystemService : IpcService
    {
        private readonly ManagerServer _managerServer;

        public IManagerForSystemService(UserId userId)
        {
            _managerServer = new ManagerServer(userId);
        }

        [CommandHipc(0)]
        // CheckAvailability()
        public static ResultCode CheckAvailability(ServiceCtx context)
        {
            return ManagerServer.CheckAvailability(context);
        }

        [CommandHipc(1)]
        // GetAccountId() -> nn::account::NetworkServiceAccountId
        public static ResultCode GetAccountId(ServiceCtx context)
        {
            return ManagerServer.GetAccountId(context);
        }

        [CommandHipc(2)]
        // EnsureIdTokenCacheAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode EnsureIdTokenCacheAsync(ServiceCtx context)
        {
            ResultCode resultCode = _managerServer.EnsureIdTokenCacheAsync(context, out IAsyncContext asyncContext);

            if (resultCode == ResultCode.Success)
            {
                MakeObject(context, asyncContext);
            }

            return resultCode;
        }

        [CommandHipc(3)]
        // LoadIdTokenCache() -> (u32 id_token_cache_size, buffer<bytes, 6>)
        public static ResultCode LoadIdTokenCache(ServiceCtx context)
        {
            return ManagerServer.LoadIdTokenCache(context);
        }
    }
}