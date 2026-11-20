namespace BeatLink.Game;

/// <summary>
/// Windows glue that ties the game process to the platform-neutral
/// <see cref="GamePatcher"/>: it finds the process, opens its memory, suspends
/// its threads for the duration of the writes (I6) and applies the plan.
/// </summary>
public sealed record ConnectOutcome(ApplyResult Result, GameProcess Game);

public static class GameConnection
{
    /// <summary>
    /// Applies the patches for <paramref name="token"/> to the running game. The
    /// returned <see cref="GameProcess"/> stays open so the caller can subscribe
    /// to its <see cref="GameProcess.Exited"/> event (B16); dispose it when done.
    /// </summary>
    /// <exception cref="GameNotRunningException">The game is not running (B15).</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be opened (B5).</exception>
    /// <exception cref="PatchBuildException">The token or host does not fit (B8, B20).</exception>
    public static ConnectOutcome Connect(BeatLinkOptions options, string token, ExpectedBytes expected)
    {
        GameProcess game = GameProcess.Find();
        try
        {
            using var memory = ProcessMemory.Open(game.Id, game.BaseAddress);
            IReadOnlyList<Patch> patches = Patches.Build(options, token, expected);

            // Suspend threads only while planning+writing so the game can never
            // run a half-applied set (I6).
            using (game.SuspendThreads())
            {
                return new ConnectOutcome(GamePatcher.Apply(memory, patches), game);
            }
        }
        catch
        {
            game.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the current bytes at every patch site and saves them as this
    /// machine's expected-bytes set. Run once against an unpatched game.
    /// </summary>
    public static ExpectedBytes Capture(BeatLinkOptions options, string build)
    {
        using GameProcess game = GameProcess.Find();
        using var memory = ProcessMemory.Open(game.Id, game.BaseAddress);
        return GamePatcher.Capture(memory, options, build);
    }
}
