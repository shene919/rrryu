using System;
using System.Runtime.InteropServices;

namespace Ryujinx.Memory
{
    class MemoryProtectionException : Exception
    {
        public MemoryProtectionException()
        {
        }

        public MemoryProtectionException(IntPtr address, ulong size, MemoryPermission permission) : base($"Failed to set memory protection for {address:X} (length: {size}) to \"{permission}\": {Marshal.GetLastPInvokeErrorMessage()}")
        {
        }

        public MemoryProtectionException(string message) : base(message)
        {
        }

        public MemoryProtectionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
