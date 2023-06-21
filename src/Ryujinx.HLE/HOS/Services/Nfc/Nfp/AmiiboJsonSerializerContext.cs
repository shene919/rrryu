using Ryujinx.HLE.HOS.Services.Nfc.Nfp.NfpManager.Types;
using System.Text.Json.Serialization;

namespace Ryujinx.HLE.HOS.Services.Nfc.Nfp
{
    [JsonSerializable(typeof(VirtualAmiiboFile))]
    internal partial class AmiiboJsonSerializerContext : JsonSerializerContext
    {
    }
}
