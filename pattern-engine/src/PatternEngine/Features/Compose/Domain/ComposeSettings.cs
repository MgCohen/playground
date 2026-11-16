using PatternEngine.Plugins.Settings;

namespace PatternEngine.Features.Compose.Domain;

/// <summary>The limits of one compose run: decider turns, grade requests in one turn and in the run, and the set size.</summary>
public sealed record ComposeLimits(int Turns, int GradesPerTurn, int Grades, int SetSize);

/// <summary>The compose section of the settings.</summary>
public sealed record ComposeSettings(string Strategy, ComposeLimits Limits, bool Cache, int Concurrency)
{
    public static ComposeSettings From(Settings settings)
    {
        var compose = settings.Section("compose");
        var limits = compose.Section("limits");
        return new ComposeSettings(
            compose.Text("strategy"),
            new ComposeLimits(limits.Int("turns"), limits.Int("grades_per_turn"), limits.Int("grades"), limits.Int("party_size")),
            compose.Flag("cache"),
            settings.Section("engine").Int("concurrency"));
    }
}
