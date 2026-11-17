using PatternEngine.Plugins.Settings;

namespace PatternEngine.Features.Compose.Domain;

/// <summary>The limits of one compose run: decider turns, grade requests in one turn and in the run, and the set size.</summary>
public sealed record ComposeLimits(int Turns, int GradesPerTurn, int Grades, int SetSize);

/// <summary>
/// The compose section of the settings. CodeChecks: code answers each rubric with check code, and checks each set (R1).
/// Gaps: a commit can keep a remainder, with a gap that the author step fills (B4).
/// </summary>
public sealed record ComposeSettings(string Strategy, ComposeLimits Limits, bool Cache, int Concurrency, bool CodeChecks = false, bool Gaps = false)
{
    public static ComposeSettings From(Settings settings)
    {
        var compose = settings.Section("compose");
        var limits = compose.Section("limits");
        return new ComposeSettings(
            compose.Text("strategy"),
            new ComposeLimits(limits.Int("turns"), limits.Int("grades_per_turn"), limits.Int("grades"), limits.Int("set_size")),
            compose.Flag("cache"),
            settings.Section("engine").Int("concurrency"),
            compose.Flag("code_checks"),
            compose.Flag("gaps"));
    }
}
