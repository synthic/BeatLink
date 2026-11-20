namespace BeatLink.Game;

/// <summary>State of a single patch location, read before any write (I3).</summary>
public enum PatchState
{
    /// <summary>Current bytes match <see cref="Patch.Expected"/>: ready to apply.</summary>
    NotApplied,

    /// <summary>Current bytes match <see cref="Patch.Replacement"/>: already done.</summary>
    AlreadyApplied,

    /// <summary>Current bytes match neither: wrong or updated build (B4).</summary>
    Unknown,
}

/// <summary>Overall result of planning a patch set.</summary>
public enum PlanStatus
{
    /// <summary>At least one patch needs applying; <see cref="PatchPlan.Writes"/> is ordered.</summary>
    ReadyToApply,

    /// <summary>Every patch is already applied; nothing to write.</summary>
    AlreadyApplied,

    /// <summary>One or more locations did not match: refuse to write anything (B4).</summary>
    UnknownBuild,

    /// <summary>The patch table itself is invalid (missing expected bytes, length mismatch).</summary>
    InvalidTable,
}

public sealed record PatchInspection(Patch Patch, PatchState State);

/// <summary>
/// The plan produced from inspecting every location. When <see cref="Status"/>
/// is <see cref="PlanStatus.ReadyToApply"/>, <see cref="Writes"/> holds the
/// patches to write, ordered data → pointer → code (B3, I6).
/// </summary>
public sealed record PatchPlan(
    PlanStatus Status,
    IReadOnlyList<PatchInspection> Inspections,
    IReadOnlyList<Patch> Writes,
    string? Message = null);

/// <summary>
/// Reads every patch location, classifies it, and either produces an ordered
/// write list or refuses the whole set (B3, B4, I3). It performs no writes, so
/// it is fully testable against a fake <see cref="IProcessMemory"/> (I16).
/// </summary>
public static class PatchPlanner
{
    public static PatchPlan Plan(IProcessMemory memory, IReadOnlyList<Patch> patches)
    {
        // Validate the table before touching the target (I2). Expected bytes are
        // optional: when a site carries them it is verified; when it does not, it
        // is written unverified (there is a single supported game build).
        foreach (Patch p in patches)
        {
            if (p.CanVerify && p.Expected.Length != p.Replacement.Length)
                return Invalid($"Patch \"{p.Name}\" expected/replacement length mismatch " +
                               $"({p.Expected.Length} vs {p.Replacement.Length}).");
        }

        var inspections = new List<PatchInspection>(patches.Count);
        bool anyUnknown = false;
        bool anyNotApplied = false;

        foreach (Patch p in patches)
        {
            // Unverified site: no original bytes to compare, so write it.
            if (!p.CanVerify)
            {
                anyNotApplied = true;
                inspections.Add(new PatchInspection(p, PatchState.NotApplied));
                continue;
            }

            nint address = memory.BaseAddress + p.Rva;
            byte[] current = memory.Read(address, p.Length);

            PatchState state =
                current.AsSpan().SequenceEqual(p.Expected) ? PatchState.NotApplied
                : current.AsSpan().SequenceEqual(p.Replacement) ? PatchState.AlreadyApplied
                : PatchState.Unknown;

            if (state == PatchState.Unknown) anyUnknown = true;
            if (state == PatchState.NotApplied) anyNotApplied = true;

            inspections.Add(new PatchInspection(p, state));
        }

        if (anyUnknown)
        {
            string names = string.Join(", ",
                inspections.Where(i => i.State == PatchState.Unknown).Select(i => i.Patch.Name));
            return new PatchPlan(PlanStatus.UnknownBuild, inspections, [],
                $"Refusing to patch: unexpected bytes at {names}. This is likely an " +
                "unsupported or updated game build.");
        }

        if (!anyNotApplied)
            return new PatchPlan(PlanStatus.AlreadyApplied, inspections, [],
                "The game is already patched.");

        IReadOnlyList<Patch> writes = inspections
            .Where(i => i.State == PatchState.NotApplied)
            .Select(i => i.Patch)
            .OrderBy(p => (int)p.Kind) // data → pointer → code (B3)
            .ToList();

        return new PatchPlan(PlanStatus.ReadyToApply, inspections, writes);

        static PatchPlan Invalid(string message) =>
            new(PlanStatus.InvalidTable, [], [], message);
    }
}
