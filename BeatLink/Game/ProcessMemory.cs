using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BeatLink.Game;

/// <summary>
/// Windows implementation of <see cref="IProcessMemory"/> over
/// ReadProcessMemory/WriteProcessMemory.
///
/// Fixes: B5 (null-handle check with a real error, access-denied mapped to a
/// "run as administrator" hint), B6 (<see cref="SafeProcessHandle"/> so the
/// handle is always released), B7 (correct x64 signatures — the size and
/// bytes-written parameters are pointer-width), I4 (generated P/Invoke via
/// <c>LibraryImport</c>).
/// </summary>
public sealed partial class ProcessMemory : IProcessMemory, IDisposable
{
    [Flags]
    private enum ProcessAccess
    {
        VmOperation = 0x0008,
        VmRead = 0x0010,
        VmWrite = 0x0020,
    }

    private const int ERROR_ACCESS_DENIED = 5;

    private readonly SafeProcessHandle _handle;

    public nint BaseAddress { get; }

    private ProcessMemory(SafeProcessHandle handle, nint baseAddress)
    {
        _handle = handle;
        BaseAddress = baseAddress;
    }

    /// <summary>
    /// Opens the target process for read/write and records its main-module base
    /// (B4). Throws <see cref="Win32Exception"/> with an actionable message when
    /// the handle cannot be opened (B5).
    /// </summary>
    public static ProcessMemory Open(int processId, nint baseAddress)
    {
        SafeProcessHandle handle = OpenProcess(
            (uint)(ProcessAccess.VmOperation | ProcessAccess.VmWrite | ProcessAccess.VmRead),
            false, processId);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_ACCESS_DENIED)
                throw new Win32Exception(error,
                    "Access to the game process was denied. Try running BeatLink as administrator.");
            throw new Win32Exception(error);
        }

        return new ProcessMemory(handle, baseAddress);
    }

    public byte[] Read(nint address, int length)
    {
        byte[] buffer = new byte[length];
        if (!ReadProcessMemory(_handle, address, buffer, (nuint)length, out nuint read) ||
            read != (nuint)length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return buffer;
    }

    public int Write(nint address, byte[] data)
    {
        if (!WriteProcessMemory(_handle, address, data, (nuint)data.Length, out nuint written))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (int)written;
    }

    public void Dispose() => _handle.Dispose();

    // DllImport (not LibraryImport) for the SafeHandle return: the source
    // generator's support for SafeHandle returns is version-sensitive, whereas
    // the classic marshaller handles it reliably. The signature is still correct
    // and the handle is a SafeProcessHandle, which is what matters (B6, B7).
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        SafeProcessHandle hProcess, nint lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(
        SafeProcessHandle hProcess, nint lpBaseAddress, byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);
}
