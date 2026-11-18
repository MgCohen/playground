using System.Text.Json;

namespace Orchestrator.Core.Sessions;

/// <summary>
/// The settings of one session in tiers: tier name, then key, then a JSON value. For example
/// <c>{ "behavior": { "mode": "auto" }, "permissions": { "mode": "auto", "allowDefaults": false } }</c>.
/// A new key or a new tier needs no change of this type: <see cref="SettingTiers"/> lists the known tiers and their defaults.
/// </summary>
public sealed class SessionSettings : Dictionary<string, Dictionary<string, JsonElement>>
{
    public SessionSettings() : base(StringComparer.Ordinal)
    {
    }

    /// <summary>The value of one key, or null when this entry does not set it.</summary>
    public JsonElement? Value(string tier, string key) =>
        TryGetValue(tier, out var values) && values.TryGetValue(key, out var value) ? value : null;

    public string? Text(string tier, string key) => Value(tier, key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public bool? Flag(string tier, string key) => Value(tier, key) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;

    public bool Has(string tier, string key) => Value(tier, key) is not null;

    /// <summary>Each value, in tier and key order.</summary>
    public IEnumerable<(string Tier, string Key, JsonElement Value)> Entries() =>
        this.OrderBy(t => t.Key, StringComparer.Ordinal)
            .SelectMany(t => t.Value.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => (t.Key, k.Key, k.Value)));

    /// <summary>A copy with one value set.</summary>
    public SessionSettings With(string tier, string key, JsonElement value)
    {
        var copy = Copy();
        if (!copy.TryGetValue(tier, out var values)) copy[tier] = values = new(StringComparer.Ordinal);
        values[key] = value.Clone();
        return copy;
    }

    public SessionSettings With(string tier, string key, object? value) => With(tier, key, JsonSerializer.SerializeToElement(value));

    public SessionSettings Copy()
    {
        var copy = new SessionSettings();
        foreach (var (tier, key, value) in Entries())
        {
            if (!copy.TryGetValue(tier, out var values)) copy[tier] = values = new(StringComparer.Ordinal);
            values[key] = value.Clone();
        }
        return copy;
    }

    /// <summary>
    /// The resolution rule: each value of <paramref name="own"/> first, then each value of <paramref name="fallback"/>
    /// that <paramref name="own"/> does not set.
    /// </summary>
    public static SessionSettings Merge(SessionSettings? own, SessionSettings? fallback)
    {
        var merged = own?.Copy() ?? new SessionSettings();
        foreach (var (tier, key, value) in fallback?.Entries() ?? [])
            if (!merged.Has(tier, key)) merged = merged.With(tier, key, value);
        return merged;
    }
}

/// <summary>One tier of settings: its keys with the root defaults, and the modes that its key <c>mode</c> takes.</summary>
/// <param name="Modes">The valid values of the key <c>mode</c>. Null: the names of the mode catalog (<c>templates/modes/</c>).</param>
public sealed record SettingTier(string Name, string Describe, IReadOnlyDictionary<string, object> Defaults, IReadOnlyList<string>? Modes);

/// <summary>
/// The known tiers of the settings. A new behavior mode is a folder in <c>templates/modes/</c> (no code change). A new
/// tier, key or permissions mode is one line here.
/// </summary>
public static class SettingTiers
{
    public const string Behavior = "behavior";
    public const string Permissions = "permissions";

    public const string Mode = "mode";
    public const string Visibility = "visibility";
    public const string AllowDefaults = "allowDefaults";

    public const string Public = "public";
    public const string Private = "private";

    public static readonly IReadOnlyList<SettingTier> All =
    [
        new(Behavior, "How the session works, reports and shows itself: a mode of the mode catalog, and the axes that change one value of it",
            new Dictionary<string, object> { [Mode] = "auto", [Visibility] = Public }, null),
        new(Permissions, "What the session may do without a person: the engine permission mode, and the merge of unapproved defaults",
            new Dictionary<string, object> { [Mode] = "auto", [AllowDefaults] = false }, ["auto", "bypass"]),
    ];

    public static SettingTier? Find(string tier) => All.FirstOrDefault(t => t.Name == tier);

    /// <summary>The settings of a root that sets nothing: the defaults of each tier.</summary>
    public static SessionSettings Defaults()
    {
        var settings = new SessionSettings();
        foreach (var tier in All)
            foreach (var (key, value) in tier.Defaults)
                settings = settings.With(tier.Name, key, value);
        return settings;
    }

    /// <summary>The errors of one value: an unknown tier, or a mode that the tier does not know.</summary>
    public static IReadOnlyList<string> Check(string tier, string key, JsonElement value, Func<string, bool>? modeExists = null)
    {
        if (Find(tier) is not { } t) return [$"settings: unknown tier {tier} (known: {string.Join(", ", All.Select(a => a.Name))})"];
        if (key != Mode) return [];
        if (value.ValueKind != JsonValueKind.String) return [$"settings: {tier}.{Mode} must be a text"];
        var mode = value.GetString()!;
        if (t.Modes is { } modes) return modes.Contains(mode) ? [] : [$"settings: {tier}.{Mode} {mode} is unknown (known: {string.Join(", ", modes)})"];
        return modeExists is null || modeExists(mode) ? [] : [$"settings: {tier}.{Mode} {mode} is not in the mode catalog"];
    }
}
