using BeatLink;
using BeatLink.Game;

namespace BeatLink.Tests;

public static class PatchPlannerTests
{
    private static readonly nint Base = unchecked((nint)0x140000000);

    // Build a synthetic expected-bytes set at the real capture lengths, with a
    // distinct original pattern per location, then a fake memory seeded to match.
    private static (BeatLinkOptions options, ExpectedBytes expected, FakeProcessMemory mem, IReadOnlyList<Patch> patches)
        Arrange(string token = "tok", bool seedExpected = true)
    {
        BeatLinkOptions options = BeatLinkOptions.Defaults with { ClientId = "x", ServerHost = "play.example.com" };
        var expected = new ExpectedBytes { Build = "test" };

        foreach ((string name, _, int length) in Patches.CaptureLayout(options))
        {
            byte[] original = new byte[length];
            for (int i = 0; i < length; i++) original[i] = (byte)(name.GetHashCode() + i); // distinct, non-zero-ish
            expected.Hex[name] = Convert.ToHexString(original);
        }

        IReadOnlyList<Patch> patches = Patches.Build(options, token, expected);

        // Size memory to cover the highest RVA + length.
        int size = patches.Max(p => (int)p.Rva + p.Length) + 16;
        var mem = new FakeProcessMemory(Base, size);

        if (seedExpected)
            foreach (Patch p in patches)
                mem.Seed(p.Rva, p.Expected);

        return (options, expected, mem, patches);
    }

    [Test("Clean build plans as ReadyToApply with data<pointer<code ordering (B3)")]
    public static void OrderedPlan()
    {
        (_, _, FakeProcessMemory mem, IReadOnlyList<Patch> patches) = Arrange();
        PatchPlan plan = PatchPlanner.Plan(mem, patches);

        Assert.Equal(PlanStatus.ReadyToApply, plan.Status);

        int lastKind = -1;
        foreach (Patch w in plan.Writes)
        {
            Assert.True((int)w.Kind >= lastKind, "writes not ordered by kind");
            lastKind = (int)w.Kind;
        }
        // First write is Data, last is Code.
        Assert.Equal(PatchKind.Data, plan.Writes[0].Kind);
        Assert.Equal(PatchKind.Code, plan.Writes[^1].Kind);
    }

    [Test("Apply writes all patches; re-apply reports AlreadyApplied (I3)")]
    public static void ApplyThenAlreadyApplied()
    {
        (_, _, FakeProcessMemory mem, IReadOnlyList<Patch> patches) = Arrange();

        ApplyResult first = GamePatcher.Apply(mem, patches);
        Assert.Equal(ApplyStatus.Applied, first.Status);

        // Verify bytes actually changed to the replacements.
        foreach (Patch p in patches)
            Assert.BytesEqual(p.Replacement, mem.Read(mem.BaseAddress + p.Rva, p.Length));

        ApplyResult second = GamePatcher.Apply(mem, patches);
        Assert.Equal(ApplyStatus.AlreadyApplied, second.Status);
    }

    [Test("An unexpected byte at one site refuses the whole set with zero writes (B4)")]
    public static void UnknownBuildAbortsEverything()
    {
        (_, _, FakeProcessMemory mem, IReadOnlyList<Patch> patches) = Arrange();

        // Corrupt one location so it matches neither expected nor replacement.
        Patch victim = patches.First(p => p.Kind == PatchKind.Code);
        mem.Seed(victim.Rva, new byte[victim.Length]); // all zero, distinct from both

        byte[] before = mem.Read(mem.BaseAddress + patches[0].Rva, patches[0].Length);
        ApplyResult result = GamePatcher.Apply(mem, patches);

        Assert.Equal(ApplyStatus.UnknownBuild, result.Status);
        // Nothing was written anywhere (first patch unchanged).
        Assert.BytesEqual(before, mem.Read(mem.BaseAddress + patches[0].Rva, patches[0].Length));
    }

    [Test("A partial write is reported as WriteFailed (B19)")]
    public static void PartialWriteFails()
    {
        (_, _, FakeProcessMemory mem, IReadOnlyList<Patch> patches) = Arrange();
        mem.TruncateWrites = true;
        ApplyResult result = GamePatcher.Apply(mem, patches);
        Assert.Equal(ApplyStatus.WriteFailed, result.Status);
    }

    [Test("Without captured bytes, patches are written unverified (single build)")]
    public static void MissingExpectedWritesAnyway()
    {
        (_, _, FakeProcessMemory mem, IReadOnlyList<Patch> _) = Arrange();
        // A patch set with no expected bytes at all.
        var empty = new ExpectedBytes();
        var options = BeatLinkOptions.Defaults with { ClientId = "x" };
        IReadOnlyList<Patch> patches = Patches.Build(options, "tok", empty);

        int size = patches.Max(p => (int)p.Rva + p.Length) + 16;
        var freshMem = new FakeProcessMemory(mem.BaseAddress, size);

        PatchPlan plan = PatchPlanner.Plan(freshMem, patches);
        Assert.Equal(PlanStatus.ReadyToApply, plan.Status);
        Assert.Equal(patches.Count, plan.Writes.Count);

        // And applying still writes every replacement, ordered.
        ApplyResult result = GamePatcher.Apply(freshMem, patches);
        Assert.Equal(ApplyStatus.Applied, result.Status);
        foreach (Patch p in patches)
            Assert.BytesEqual(p.Replacement, freshMem.Read(freshMem.BaseAddress + p.Rva, p.Length));
    }

    [Test("Token buffer is zero-padded and reserves a terminator (B8)")]
    public static void TokenBufferPadding()
    {
        (_, _, _, IReadOnlyList<Patch> patches) = Arrange(token: "abc");
        Patch buf = patches.First(p => p.Name == Patches.TokenBuffer);
        Assert.Equal(256, buf.Replacement.Length);
        Assert.Equal((byte)'a', buf.Replacement[0]);
        Assert.Equal((byte)0, buf.Replacement[3]); // terminator after "abc"
        Assert.Equal((byte)0, buf.Replacement[255]);
    }

    [Test("Token longer than the buffer is rejected (B8)")]
    public static void OversizeTokenRejected()
    {
        var options = BeatLinkOptions.Defaults with { ClientId = "x" };
        var expected = new ExpectedBytes();
        expected.Hex[Patches.TokenBuffer] = Convert.ToHexString(new byte[256]);
        expected.Hex[Patches.RedirectorHostname] = Convert.ToHexString(new byte[30]);
        Assert.Throws<PatchBuildException>(() => Patches.Build(options, new string('z', 300), expected));
    }

    [Test("Server host that cannot fit the redirector field is rejected (B20)")]
    public static void OversizeHostRejected()
    {
        var options = BeatLinkOptions.Defaults with { ClientId = "x", ServerHost = new string('h', 40) };
        var expected = new ExpectedBytes();
        expected.Hex[Patches.TokenBuffer] = Convert.ToHexString(new byte[256]);
        expected.Hex[Patches.RedirectorHostname] = Convert.ToHexString(new byte[30]);
        Assert.Throws<PatchBuildException>(() => Patches.Build(options, "tok", expected));
    }

    [Test("Pointer and jump replacements are written full width (B9)")]
    public static void FullWidthPointer()
    {
        (_, _, _, IReadOnlyList<Patch> patches) = Arrange();
        Patch ptr = patches.First(p => p.Name == Patches.TokenPointer);
        Assert.Equal(8, ptr.Replacement.Length);
    }
}
