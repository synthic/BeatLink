namespace BeatLink.Game;

/// <summary>
/// Write ordering class (B3, I6). The game may execute a half-applied set of
/// patches, so data must exist before any pointer references it, and the code
/// that jumps into the new data must be written last.
/// </summary>
public enum PatchKind
{
    /// <summary>Raw data (token buffer, hostname). Written first.</summary>
    Data = 0,

    /// <summary>A pointer cell referencing data. Written second.</summary>
    Pointer = 1,

    /// <summary>An instruction patch that activates the rest. Written last.</summary>
    Code = 2,
}

/// <summary>
/// One memory patch, located by its offset from the module base (I2, B4).
/// <see cref="Expected"/> is the original bytes from an unpatched game and must
/// be captured per build; <see cref="Replacement"/> is what BeatLink writes.
/// Both must be the same length — patches are applied in place.
/// </summary>
public sealed record Patch(
    string Name,
    nint Rva,
    PatchKind Kind,
    byte[] Expected,
    byte[] Replacement)
{
    /// <summary>Region length. Equals <see cref="Replacement"/> length; <see cref="Expected"/>
    /// may be empty when running unverified.</summary>
    public int Length => Replacement.Length;

    /// <summary>True when original bytes were supplied for this site, so it can be verified.</summary>
    public bool CanVerify => Expected.Length > 0;
}
