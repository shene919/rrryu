using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Npns
{
    class NpnsBase
    {
        public static ResultCode Suspend()
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNpns);
            return ResultCode.Success;
        }
    }
}