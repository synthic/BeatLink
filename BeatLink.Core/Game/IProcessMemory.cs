namespace BeatLink.Game;

/// <summary>
/// Abstraction over reading and writing another process's memory (I4). The
/// Windows app backs this with WriteProcessMemory/ReadProcessMemory behind a
/// safe handle; tests back it with an in-memory fake, which is what makes the
/// patch planner testable (I16).
/// </summary>
public interface IProcessMemory
{
    /// <summary>Base address of the target's main module (B4 — addresses are relative to this).</summary>
    nint BaseAddress { get; }

    /// <summary>Reads exactly <paramref name="length"/> bytes at <paramref name="address"/>.</summary>
    byte[] Read(nint address, int length);

    /// <summary>Writes <paramref name="data"/> at <paramref name="address"/>; returns bytes written.</summary>
    int Write(nint address, byte[] data);
}
