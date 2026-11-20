using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BeatLink.Game;

/// <summary>Thrown when the game is not running (B15 — replaces catching IndexOutOfRangeException).</summary>
public sealed class GameNotRunningException : Exception
{
    public GameNotRunningException()
        : base("Mirror's Edge™ Catalyst is not running. Please start the game and try again.") { }
}

/// <summary>
/// Locates the running game and provides its module base and a thread
/// suspend/resume scope (I6).
///
/// Fixes: B15 (checks the match count instead of catching
/// <see cref="IndexOutOfRangeException"/>, disposes the processes it does not
/// use, throws a specific exception), S5 (the caller can inspect
/// <see cref="MainModulePath"/>; the expected-bytes check is the primary guard).
/// </summary>
public sealed class GameProcess : IDisposable
{
    private const string ProcessName = "MirrorsEdgeCatalyst";

    private readonly Process _process;

    private GameProcess(Process process) => _process = process;

    public int Id => _process.Id;

    public nint BaseAddress =>
        _process.MainModule?.BaseAddress
        ?? throw new InvalidOperationException("Could not read the game's module base address.");

    public string? MainModulePath => _process.MainModule?.FileName;

    /// <summary>Raised when the game process exits (used to clear the connect tick, B16).</summary>
    public event EventHandler? Exited;

    public static bool IsRunning() =>
        DisposeAll(Process.GetProcessesByName(ProcessName)) > 0;

    /// <summary>
    /// Finds the single game process. If several match, the first is used and
    /// the rest are disposed (B15).
    /// </summary>
    public static GameProcess Find()
    {
        Process[] matches = Process.GetProcessesByName(ProcessName);
        if (matches.Length == 0)
            throw new GameNotRunningException();

        Process chosen = matches[0];
        for (int i = 1; i < matches.Length; i++)
            matches[i].Dispose();

        var game = new GameProcess(chosen);

        try
        {
            chosen.EnableRaisingEvents = true;
            chosen.Exited += (s, e) => game.Exited?.Invoke(game, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Exit notification is best-effort.
        }

        return game;
    }

    /// <summary>
    /// Suspends every thread of the game for the lifetime of the returned scope,
    /// so the game cannot execute a half-applied patch (I6). Threads that cannot
    /// be suspended are skipped rather than failing the whole operation.
    /// </summary>
    public IDisposable SuspendThreads()
    {
        var suspended = new List<nint>();
        foreach (ProcessThread thread in _process.Threads)
        {
            nint handle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
            if (handle == 0)
                continue;
            if (SuspendThread(handle) != unchecked((uint)-1))
                suspended.Add(handle);
            else
                CloseHandle(handle);
        }
        return new ThreadResumer(suspended);
    }

    public void Dispose() => _process.Dispose();

    private static int DisposeAll(Process[] processes)
    {
        int count = processes.Length;
        foreach (Process p in processes)
            p.Dispose();
        return count;
    }

    private sealed class ThreadResumer : IDisposable
    {
        private readonly List<nint> _handles;
        public ThreadResumer(List<nint> handles) => _handles = handles;

        public void Dispose()
        {
            foreach (nint handle in _handles)
            {
                ResumeThread(handle);
                CloseHandle(handle);
            }
            _handles.Clear();
        }
    }

    private const int THREAD_SUSPEND_RESUME = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenThread(int dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(nint hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
