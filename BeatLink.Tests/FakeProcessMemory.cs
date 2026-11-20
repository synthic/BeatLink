using BeatLink.Game;

namespace BeatLink.Tests;

/// <summary>
/// In-memory <see cref="IProcessMemory"/> for planner/patcher tests. Backed by a
/// flat byte array indexed by (address - BaseAddress).
/// </summary>
public sealed class FakeProcessMemory : IProcessMemory
{
    private readonly byte[] _memory;

    public FakeProcessMemory(nint baseAddress, int size)
    {
        BaseAddress = baseAddress;
        _memory = new byte[size];
    }

    public nint BaseAddress { get; }

    /// <summary>When true, writes only commit half their bytes (to exercise B19).</summary>
    public bool TruncateWrites { get; set; }

    public byte[] Read(nint address, int length)
    {
        int offset = (int)(address - BaseAddress);
        return _memory.AsSpan(offset, length).ToArray();
    }

    public int Write(nint address, byte[] data)
    {
        int offset = (int)(address - BaseAddress);
        int count = TruncateWrites ? data.Length / 2 : data.Length;
        data.AsSpan(0, count).CopyTo(_memory.AsSpan(offset, count));
        return count;
    }

    /// <summary>Seeds bytes at an RVA (helper for arranging a build layout).</summary>
    public void Seed(nint rva, byte[] bytes) => bytes.CopyTo(_memory.AsSpan((int)rva));
}
