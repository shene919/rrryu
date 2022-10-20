using System;
using UnicornManaged.Const;

namespace Ryujinx.Tests.Unicorn
{
    public class UnicornAArch64
    {
        internal readonly UnicornManaged.Unicorn uc;

        public IndexedProperty<int, ulong> X
        {
            get
            {
                return new IndexedProperty<int, ulong>(
                    (int i)              => GetX(i),
                    (int i, ulong value) => SetX(i, value));
            }
        }

        public IndexedProperty<int, SimdValue> Q
        {
            get
            {
                return new IndexedProperty<int, SimdValue>(
                    (int i)                  => GetQ(i),
                    (int i, SimdValue value) => SetQ(i, value));
            }
        }

        public ulong LR
        {
            get => GetRegister(Arm64.UC_ARM64_REG_LR);
            set => SetRegister(Arm64.UC_ARM64_REG_LR, value);
        }

        public ulong SP
        {
            get => GetRegister(Arm64.UC_ARM64_REG_SP);
            set => SetRegister(Arm64.UC_ARM64_REG_SP, value);
        }

        public ulong PC
        {
            get => GetRegister(Arm64.UC_ARM64_REG_PC);
            set => SetRegister(Arm64.UC_ARM64_REG_PC, value);
        }

        public uint Pstate
        {
            get => (uint)GetRegister(Arm64.UC_ARM64_REG_PSTATE);
            set =>       SetRegister(Arm64.UC_ARM64_REG_PSTATE, (uint)value);
        }

        public int Fpcr
        {
            get => (int)GetRegister(Arm64.UC_ARM64_REG_FPCR);
            set =>      SetRegister(Arm64.UC_ARM64_REG_FPCR, (uint)value);
        }

        public int Fpsr
        {
            get => (int)GetRegister(Arm64.UC_ARM64_REG_FPSR);
            set =>      SetRegister(Arm64.UC_ARM64_REG_FPSR, (uint)value);
        }

        public bool OverflowFlag
        {
            get =>          (Pstate &  0x10000000u) != 0;
            set => Pstate = (Pstate & ~0x10000000u) | (value ? 0x10000000u : 0u);
        }

        public bool CarryFlag
        {
            get =>          (Pstate &  0x20000000u) != 0;
            set => Pstate = (Pstate & ~0x20000000u) | (value ? 0x20000000u : 0u);
        }

        public bool ZeroFlag
        {
            get =>          (Pstate &  0x40000000u) != 0;
            set => Pstate = (Pstate & ~0x40000000u) | (value ? 0x40000000u : 0u);
        }

        public bool NegativeFlag
        {
            get =>          (Pstate &  0x80000000u) != 0;
            set => Pstate = (Pstate & ~0x80000000u) | (value ? 0x80000000u : 0u);
        }

        public UnicornAArch64()
        {
            uc = new UnicornManaged.Unicorn(Common.UC_ARCH_ARM64, Common.UC_MODE_LITTLE_ENDIAN);

            SetRegister(Arm64.UC_ARM64_REG_CPACR_EL1, 0x00300000);
        }

        ~UnicornAArch64()
        {
            uc.Close();
            uc.Dispose();
        }

        public void RunForCount(ulong count)
        {
            uc.EmuStart(this.PC, 0xFFFFFFFFFFFFFFF, 0, count);
        }

        public void Step()
        {
            RunForCount(1);
        }

        private static int[] XRegisters = new int[31]
        {
            Arm64.UC_ARM64_REG_X0,
            Arm64.UC_ARM64_REG_X1,
            Arm64.UC_ARM64_REG_X2,
            Arm64.UC_ARM64_REG_X3,
            Arm64.UC_ARM64_REG_X4,
            Arm64.UC_ARM64_REG_X5,
            Arm64.UC_ARM64_REG_X6,
            Arm64.UC_ARM64_REG_X7,
            Arm64.UC_ARM64_REG_X8,
            Arm64.UC_ARM64_REG_X9,
            Arm64.UC_ARM64_REG_X10,
            Arm64.UC_ARM64_REG_X11,
            Arm64.UC_ARM64_REG_X12,
            Arm64.UC_ARM64_REG_X13,
            Arm64.UC_ARM64_REG_X14,
            Arm64.UC_ARM64_REG_X15,
            Arm64.UC_ARM64_REG_X16,
            Arm64.UC_ARM64_REG_X17,
            Arm64.UC_ARM64_REG_X18,
            Arm64.UC_ARM64_REG_X19,
            Arm64.UC_ARM64_REG_X20,
            Arm64.UC_ARM64_REG_X21,
            Arm64.UC_ARM64_REG_X22,
            Arm64.UC_ARM64_REG_X23,
            Arm64.UC_ARM64_REG_X24,
            Arm64.UC_ARM64_REG_X25,
            Arm64.UC_ARM64_REG_X26,
            Arm64.UC_ARM64_REG_X27,
            Arm64.UC_ARM64_REG_X28,
            Arm64.UC_ARM64_REG_X29,
            Arm64.UC_ARM64_REG_X30,
        };

        private static int[] QRegisters = new int[32]
        {
            Arm64.UC_ARM64_REG_Q0,
            Arm64.UC_ARM64_REG_Q1,
            Arm64.UC_ARM64_REG_Q2,
            Arm64.UC_ARM64_REG_Q3,
            Arm64.UC_ARM64_REG_Q4,
            Arm64.UC_ARM64_REG_Q5,
            Arm64.UC_ARM64_REG_Q6,
            Arm64.UC_ARM64_REG_Q7,
            Arm64.UC_ARM64_REG_Q8,
            Arm64.UC_ARM64_REG_Q9,
            Arm64.UC_ARM64_REG_Q10,
            Arm64.UC_ARM64_REG_Q11,
            Arm64.UC_ARM64_REG_Q12,
            Arm64.UC_ARM64_REG_Q13,
            Arm64.UC_ARM64_REG_Q14,
            Arm64.UC_ARM64_REG_Q15,
            Arm64.UC_ARM64_REG_Q16,
            Arm64.UC_ARM64_REG_Q17,
            Arm64.UC_ARM64_REG_Q18,
            Arm64.UC_ARM64_REG_Q19,
            Arm64.UC_ARM64_REG_Q20,
            Arm64.UC_ARM64_REG_Q21,
            Arm64.UC_ARM64_REG_Q22,
            Arm64.UC_ARM64_REG_Q23,
            Arm64.UC_ARM64_REG_Q24,
            Arm64.UC_ARM64_REG_Q25,
            Arm64.UC_ARM64_REG_Q26,
            Arm64.UC_ARM64_REG_Q27,
            Arm64.UC_ARM64_REG_Q28,
            Arm64.UC_ARM64_REG_Q29,
            Arm64.UC_ARM64_REG_Q30,
            Arm64.UC_ARM64_REG_Q31,
        };

        public ulong GetX(int index)
        {
            if ((uint)index > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return GetRegister(XRegisters[index]);
        }

        public void SetX(int index, ulong value)
        {
            if ((uint)index > 30)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            SetRegister(XRegisters[index], value);
        }

        public SimdValue GetQ(int index)
        {
            if ((uint)index > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return GetVector(QRegisters[index]);
        }

        public void SetQ(int index, SimdValue value)
        {
            if ((uint)index > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            SetVector(QRegisters[index], value);
        }

        private ulong GetRegister(int register)
        {
            byte[] data = new byte[8];

            uc.RegRead(register, data);

            return BitConverter.ToUInt64(data, 0);
        }

        private void SetRegister(int register, ulong value)
        {
            byte[] data = BitConverter.GetBytes(value);

            uc.RegWrite(register, data);
        }

        private SimdValue GetVector(int register)
        {
            byte[] data = new byte[16];

            uc.RegRead(register, data);

            return new SimdValue(data);
        }

        private void SetVector(int register, SimdValue value)
        {
            byte[] data = value.ToArray();

            uc.RegWrite(register, data);
        }

        public byte[] MemoryRead(ulong address, ulong size)
        {
            byte[] value = new byte[size];

            uc.MemRead(address, value);

            return value;
        }

        public byte   MemoryRead8 (ulong address) => MemoryRead(address, 1)[0];
        public UInt16 MemoryRead16(ulong address) => (UInt16)BitConverter.ToInt16(MemoryRead(address, 2), 0);
        public UInt32 MemoryRead32(ulong address) => (UInt32)BitConverter.ToInt32(MemoryRead(address, 4), 0);
        public UInt64 MemoryRead64(ulong address) => (UInt64)BitConverter.ToInt64(MemoryRead(address, 8), 0);

        public void MemoryWrite(ulong address, byte[] value)
        {
            uc.MemWrite(address, value);
        }

        public void MemoryWrite8 (ulong address, byte value)   => MemoryWrite(address, new byte[]{value});
        public void MemoryWrite16(ulong address, Int16 value)  => MemoryWrite(address, BitConverter.GetBytes(value));
        public void MemoryWrite16(ulong address, UInt16 value) => MemoryWrite(address, BitConverter.GetBytes(value));
        public void MemoryWrite32(ulong address, Int32 value)  => MemoryWrite(address, BitConverter.GetBytes(value));
        public void MemoryWrite32(ulong address, UInt32 value) => MemoryWrite(address, BitConverter.GetBytes(value));
        public void MemoryWrite64(ulong address, Int64 value)  => MemoryWrite(address, BitConverter.GetBytes(value));
        public void MemoryWrite64(ulong address, UInt64 value) => MemoryWrite(address, BitConverter.GetBytes(value));

        public void MemoryMap(ulong address, ulong size, MemoryPermission permissions)
        {
            uc.MemMap(address, size, (int)permissions);
        }

        public void MemoryUnmap(ulong address, ulong size)
        {
            uc.MemUnmap(address, size);
        }

        public void MemoryProtect(ulong address, ulong size, MemoryPermission permissions)
        {
            uc.MemProtect(address, size, (int)permissions);
        }

        public static bool IsAvailable()
        {
            try
            {
                new UnicornManaged.Unicorn(Common.UC_ARCH_ARM64, Common.UC_MODE_LITTLE_ENDIAN);

                return true;
            }
            catch (ApplicationException)
            {
                return false;
            }
        }
    }
}