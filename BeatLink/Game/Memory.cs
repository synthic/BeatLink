using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BeatLink.Game;

public static class Memory
{
    private const int PROCESS_VM_OPERATION = 0x08;
    private const int PROCESS_VM_WRITE = 0x20;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hProcess);

    public static void ApplyPatches(string newAuthCode)
    {
        var hostname = "localhost";

        int bytesWritten;

        byte[] hostnameBytes = new byte[29];
        Array.Copy(Encoding.UTF8.GetBytes(hostname), hostnameBytes, Math.Min(hostname.Length, 29));

        Process process = Process.GetProcessesByName("MirrorsEdgeCatalyst")[0];
        IntPtr processHandle = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, process.Id);

        // Update redirector hostname
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x141D80890), hostnameBytes, hostnameBytes.Length, out bytesWritten))
            throw new Win32Exception();

        // Disable SSL in ProtoSSL::Connect function
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x142DBE9B0), [0x31], 1, out bytesWritten))
            throw new Win32Exception();

        // Disable LSX authentication code verification
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x14388BCE6), [0x48, 0x90], 2, out bytesWritten))
            throw new Win32Exception();

        // Bypass encryption of authenticated requests
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x1439C0D81), [0xE9, 0xC7, 0x00], 3, out bytesWritten))
            throw new Win32Exception();

        // Overwrite memory address of authentication code
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x14388AED3), [0x15, 0x28, 0x04, 0x3B, 0xFE], 5, out bytesWritten))
            throw new Win32Exception();

        // Write pointer to new data
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x141C3B300), [0x40, 0xB3, 0xC3, 0x41, 0x01], 5, out bytesWritten))
            throw new Win32Exception();

        // Write new authentication code
        if (!WriteProcessMemory(processHandle, unchecked((IntPtr)0x141C3B340), Encoding.UTF8.GetBytes(newAuthCode), Math.Min(newAuthCode.Length, 256), out bytesWritten))
            throw new Win32Exception();

        CloseHandle(processHandle);
    }
}
