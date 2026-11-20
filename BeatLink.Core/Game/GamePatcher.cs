namespace BeatLink.Game;

/// <summary>Outcome of an apply attempt.</summary>
public enum ApplyStatus
{
    Applied,
    AlreadyApplied,
    UnknownBuild,
    InvalidTable,
    WriteFailed,
}

public sealed record ApplyResult(ApplyStatus Status, string? Message = null)
{
    public bool Ok => Status is ApplyStatus.Applied or ApplyStatus.AlreadyApplied;
}

/// <summary>
/// Plans and applies a patch set against an <see cref="IProcessMemory"/>. All
/// logic here is platform-neutral and testable with a fake memory (I16); the
/// Windows-specific concerns (finding the process, opening a handle, suspending
/// threads for I6) live in the app's implementation of the interface and its
/// caller.
///
/// Fixes: B3/B4/I3 via <see cref="PatchPlanner"/> (ordered, verified, refuses an
/// unknown build with zero writes); B19 (every write's byte count is checked).
/// </summary>
public static class GamePatcher
{
    public static ApplyResult Apply(IProcessMemory memory, IReadOnlyList<Patch> patches)
    {
        PatchPlan plan = PatchPlanner.Plan(memory, patches);

        switch (plan.Status)
        {
            case PlanStatus.AlreadyApplied:
                return new ApplyResult(ApplyStatus.AlreadyApplied, plan.Message);
            case PlanStatus.UnknownBuild:
                return new ApplyResult(ApplyStatus.UnknownBuild, plan.Message);
            case PlanStatus.InvalidTable:
                return new ApplyResult(ApplyStatus.InvalidTable, plan.Message);
        }

        // ReadyToApply: writes are ordered data → pointer → code (B3).
        foreach (Patch p in plan.Writes)
        {
            nint address = memory.BaseAddress + p.Rva;
            int written = memory.Write(address, p.Replacement);
            if (written != p.Replacement.Length) // B19
                return new ApplyResult(ApplyStatus.WriteFailed,
                    $"Partial write at \"{p.Name}\": {written} of {p.Replacement.Length} bytes. " +
                    "The game may be in an inconsistent state.");
        }

        return new ApplyResult(ApplyStatus.Applied);
    }

    /// <summary>
    /// Reads the current bytes at every capture location and records them as the
    /// expected-bytes set for this build. Run once against an unpatched game.
    /// </summary>
    public static ExpectedBytes Capture(IProcessMemory memory, BeatLinkOptions options, string build)
    {
        var result = new ExpectedBytes { Build = build };
        foreach ((string name, nint rva, int length) in Patches.CaptureLayout(options))
        {
            byte[] bytes = memory.Read(memory.BaseAddress + rva, length);
            result.Hex[name] = Convert.ToHexString(bytes);
        }
        return result;
    }
}
