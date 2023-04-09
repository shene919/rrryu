﻿namespace Ryujinx.Horizon.Sdk.Sf.Hipc
{
    readonly struct HipcBufferDescriptor
    {
#pragma warning disable CS0649
        private readonly uint _sizeLow;
        private readonly uint _addressLow;
        private readonly uint _word2;
#pragma warning restore CS0649

        public ulong Address => _addressLow | (((ulong)_word2 << 4) & 0xf00000000UL) | (((ulong)_word2 << 34) & 0x7000000000UL);
        public ulong Size => _sizeLow | ((ulong)_word2 << 8) & 0xf00000000UL;
        public HipcBufferMode Mode => (HipcBufferMode)(_word2 & 3);
    }
}