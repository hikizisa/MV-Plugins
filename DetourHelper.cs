using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;

namespace MapsetVerifier.Plugin.CustomSnapshots
{
    internal static class DetourHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(IntPtr addr, UIntPtr len, int prot);

        [DllImport("libc")]
        private static extern int getpagesize();

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const int PROT_READ = 1;
        private const int PROT_WRITE = 2;
        private const int PROT_EXEC = 4;

        public static bool TryDetour(MethodInfo targetMethod, MethodInfo hookMethod)
        {
            if (targetMethod == null || hookMethod == null)
            {
                Log.Warning("[DetourHelper] Target or hook method is null.");
                return false;
            }

            try
            {
                // Force JIT compilation of both methods
                RuntimeHelpers.PrepareMethod(targetMethod.MethodHandle);
                RuntimeHelpers.PrepareMethod(hookMethod.MethodHandle);

                IntPtr targetPtr = targetMethod.MethodHandle.GetFunctionPointer();
                IntPtr hookPtr = hookMethod.MethodHandle.GetFunctionPointer();

                if (targetPtr == hookPtr)
                {
                    Log.Warning("[DetourHelper] Target and hook pointers are identical.");
                    return true;
                }

                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    return PatchX64(targetPtr, hookPtr);
                }
                else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    return PatchArm64(targetPtr, hookPtr);
                }
                else
                {
                    Log.Warning("[DetourHelper] Unsupported architecture: {Arch}", RuntimeInformation.ProcessArchitecture);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DetourHelper] Failed to patch method {Target} -> {Hook}", targetMethod.Name, hookMethod.Name);
                return false;
            }
        }

        private static bool SetMemoryWritable(IntPtr address, int size, out uint oldProtect)
        {
            oldProtect = 0;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return VirtualProtect(address, (UIntPtr)size, PAGE_EXECUTE_READWRITE, out oldProtect);
            }
            else
            {
                int pageSize = 4096;
                try { pageSize = getpagesize(); } catch { }
                long addr = address.ToInt64();
                long pageStart = addr & ~(pageSize - 1);
                int length = (int)(addr - pageStart) + size;
                return mprotect((IntPtr)pageStart, (UIntPtr)length, PROT_READ | PROT_WRITE | PROT_EXEC) == 0;
            }
        }

        private static bool RestoreMemoryProtection(IntPtr address, int size, uint oldProtect)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return VirtualProtect(address, (UIntPtr)size, oldProtect, out _);
            }
            return true;
        }

        private static unsafe bool PatchX64(IntPtr targetPtr, IntPtr hookPtr)
        {
            // 14-byte indirect jump on x64:
            // FF 25 00 00 00 00 [8-byte address in little endian]
            // jmp qword ptr [rip + 0]
            int patchSize = 14;
            if (!SetMemoryWritable(targetPtr, patchSize, out uint oldProtect))
            {
                Log.Error("[DetourHelper] Failed to change memory protection for x64 patch.");
                return false;
            }

            byte* ptr = (byte*)targetPtr.ToPointer();

            // Handle precode stub: FF 25 <rel32> (jmp qword ptr [rip + rel32])
            if (ptr[0] == 0xFF && ptr[1] == 0x25)
            {
                int relOffset = *(int*)(ptr + 2);
                IntPtr* slotPtr = (IntPtr*)(ptr + 6 + relOffset);
                Log.Debug("[DetourHelper] Found precode slot at 0x{Slot:X}, pointing to 0x{Target:X}", (long)slotPtr, (long)(*slotPtr));

                SetMemoryWritable((IntPtr)slotPtr, IntPtr.Size, out _);
                IntPtr nativeCode = *slotPtr;
                *slotPtr = hookPtr;

                if (nativeCode != IntPtr.Zero && nativeCode != targetPtr)
                {
                    WriteJump((byte*)nativeCode.ToPointer(), hookPtr);
                }
            }

            // Also follow relative jump stub (E9) if present
            byte* actualTarget = ptr;
            if (ptr[0] == 0xE9)
            {
                int relOffset = *(int*)(ptr + 1);
                actualTarget = ptr + 5 + relOffset;
                Log.Debug("[DetourHelper] Followed E9 jump to: 0x{Addr:X}", (long)actualTarget);
            }

            // Write 14-byte jmp to actualTarget (and ptr if different)
            WriteJump(actualTarget, hookPtr);
            if (actualTarget != ptr)
            {
                WriteJump(ptr, hookPtr);
            }

            RestoreMemoryProtection(targetPtr, patchSize, oldProtect);
            Log.Information("[DetourHelper] Successfully applied x64 detour at 0x{Target:X} -> 0x{Hook:X}", (long)targetPtr, (long)hookPtr);
            return true;
        }

        private static unsafe void WriteJump(byte* dest, IntPtr hookPtr)
        {
            SetMemoryWritable((IntPtr)dest, 14, out _);
            dest[0] = 0xFF;
            dest[1] = 0x25;
            dest[2] = 0x00;
            dest[3] = 0x00;
            dest[4] = 0x00;
            dest[5] = 0x00;
            *(long*)(dest + 6) = hookPtr.ToInt64();
        }

        private static unsafe bool PatchArm64(IntPtr targetPtr, IntPtr hookPtr)
        {
            // 16-byte ARM64 jump:
            // 50 00 00 58 : ldr x16, #8
            // 10 02 1f d6 : br x16
            // [8 bytes hookPtr]
            int patchSize = 16;
            if (!SetMemoryWritable(targetPtr, patchSize, out uint oldProtect))
            {
                Log.Error("[DetourHelper] Failed to change memory protection for ARM64 patch.");
                return false;
            }

            byte* ptr = (byte*)targetPtr.ToPointer();
            *(uint*)(ptr + 0) = 0x58000050; // ldr x16, #8
            *(uint*)(ptr + 4) = 0xd61f0210; // br x16
            *(long*)(ptr + 8) = hookPtr.ToInt64();

            RestoreMemoryProtection(targetPtr, patchSize, oldProtect);
            Log.Information("[DetourHelper] Successfully applied ARM64 detour at 0x{Target:X} -> 0x{Hook:X}", (long)targetPtr, (long)hookPtr);
            return true;
        }
    }
}
