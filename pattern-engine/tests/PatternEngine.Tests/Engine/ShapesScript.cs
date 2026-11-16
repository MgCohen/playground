using System.Globalization;
using System.Text.RegularExpressions;
using PatternEngine.Domains.Shapes;
using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Catalog.Domain.Shared;
using PatternEngine.Features.Engine.Domain;
using PatternEngine.Features.Engine.Domain.Shared;
using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;
using PatternEngine.Plugins.Settings;
using PatternEngine.Tests.Support;

namespace PatternEngine.Tests.Engine;

/// <summary>A scripted judge for the shapes pack: the decider reads keywords, the composer draws the reference object in its slot.</summary>
public static partial class ShapesScript
{
    public static readonly string[] Objects = ["house", "tower", "shed", "tree", "sign"];

    public static Settings Settings => Settings.Load(Repo.Path("pe.settings.yaml"));

    public static PackContext Pack(string? patternsDir = null, string? packDir = null)
    {
        var dir = packDir ?? Repo.Path("src", "PatternEngine", "Domains", "Shapes");
        return new PackContext(new ShapesPack(), dir, Settings, PatternCatalog.Load(patternsDir ?? Path.Combine(dir, "patterns")));
    }

    public static EngineSettings Engine(Func<EngineSettings, EngineSettings>? change = null)
    {
        var s = EngineSettings.From(Settings);
        return change is null ? s : change(s);
    }

    public static async Task<EngineRunResult> RunAsync(string request, ScriptedJudge judge, Func<EngineSettings, EngineSettings>? change = null, IUserChannel? user = null, PackContext? pack = null)
    {
        pack ??= Pack();
        var dir = Path.Combine(Path.GetTempPath(), "pe-tests", Guid.NewGuid().ToString("N")[..8]);
        EngineRun.Prepare(pack, dir);
        return await EngineRun.RunAsync(pack, request, dir, Engine(change), judge, user ?? new NoUser(), CancellationToken.None);
    }

    /// <summary>The decider: a street if the request names one, else the first object word. Colors become door_fill.</summary>
    public static object Decide(string input)
    {
        var request = Request(input);
        if (request.StartsWith("Draw a street", StringComparison.Ordinal))
        {
            var list = request[(request.IndexOf(':') + 1)..].TrimEnd('.').Split(';').Select(x => x.Replace("and ", string.Empty, StringComparison.Ordinal).Trim()).ToArray();
            return new { status = "solved", pattern = "street", variant = "row", children = list, reason = "a street" };
        }

        var p = Objects.FirstOrDefault(o => request.Contains(o, StringComparison.Ordinal)) ?? "house";
        var color = new[] { "red", "blue", "green", "yellow" }.FirstOrDefault(c => request.Contains($"{c} door", StringComparison.Ordinal));
        var variant = request.Contains("chimney", StringComparison.Ordinal) ? "chimney" : "plain";
        return new { status = "solved", pattern = p, variant, @params = color is null ? new Dictionary<string, string>() : new() { ["door_fill"] = color }, reason = "keyword" };
    }

    /// <summary>The composer: the reference drawing of the pattern, placed in the slot of the input.</summary>
    public static object Compose(string input)
    {
        var m = SlotForm().Match(input);
        var x0 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var x1 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var b = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var top = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        var pm = PatternForm().Match(input);
        string p, v;
        Dictionary<string, string>? parameters = null;
        string? rename = null;
        if (pm.Success)
        {
            p = pm.Groups[1].Value;
            v = pm.Groups[2].Value;
            var json = ParamsForm().Match(input);
            parameters = json.Success ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json.Groups[1].Value) : null;
        }
        else
        {
            var nm = NewForm().Match(input);
            rename = nm.Groups[1].Value;
            p = "tower";
            v = "plain";
        }

        var factor = p switch { "tower" => 2.9, "tree" => 1.2, _ => 1.2 };
        var w = Math.Min((x1 - x0) * 0.9, (b - top) / factor);
        var fragment = ShapesReference.Object(new TreeNode { P = p, V = v, Params = parameters }, x0 + (((x1 - x0) - w) / 2), b, w);
        if (rename is not null)
        {
            fragment = fragment.Replace("data-p=\"tower\"", $"data-p=\"{rename}\"", StringComparison.Ordinal);
        }

        return new { fragment };
    }

    public static string Request(string input) => input.Split('\n')[0].Replace("Request: ", string.Empty, StringComparison.Ordinal).Trim();

    public static ScriptedJudge Judge(Func<AgentCall, object?>? extra = null) => new(call =>
        extra?.Invoke(call) ?? call.Agent switch
        {
            "decider" => Decide(call.Input),
            "composer" => Compose(call.Input),
            _ => null,
        });

    [GeneratedRegex(@"Draw inside x from ([\d.]+) to ([\d.]+)\. The bottom of the object is on y = ([\d.]+)\. The top is not above y = ([\d.]+)\.")]
    private static partial Regex SlotForm();

    [GeneratedRegex(@"Pattern: (\w+), variant: (\w+)")]
    private static partial Regex PatternForm();

    [GeneratedRegex(@"params: (\{[^}]*\})\.")]
    private static partial Regex ParamsForm();

    [GeneratedRegex("Make the new pattern \"([\\w-]+)\"")]
    private static partial Regex NewForm();
}
