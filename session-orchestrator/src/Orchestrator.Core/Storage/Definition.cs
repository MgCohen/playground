using System.Text.RegularExpressions;

namespace Orchestrator.Core.Storage;

/// <summary>
/// The files of one definition: a message template, a mode or a workflow. The key is the path of the file in the
/// definition, with "/" between the parts (for example <c>render/session.md</c>). The keys are in ordinal order, and
/// each text has "\n" line ends, so two reads of the same definition give the same value.
/// </summary>
public sealed class Definition
{
    public Definition(IEnumerable<KeyValuePair<string, string>> files)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in files) sorted[path.Replace('\\', '/')] = text.ReplaceLineEndings("\n");
        this.files = sorted;
    }

    readonly SortedDictionary<string, string> files;

    public IReadOnlyDictionary<string, string> Files => files;

    /// <summary>The text of one file, or null.</summary>
    public string? this[string path] => files.TryGetValue(path, out var text) ? text : null;
}

/// <summary>
/// One catalog of definitions in the template system. Each catalog is a folder under the templates root, and each
/// definition is a folder in it with the <see cref="Marker"/> file. The message templates are at the top of the root.
/// </summary>
public sealed partial record Catalog(string Name, string Folder, string Marker)
{
    /// <summary>The message templates: <c>templates/&lt;kind&gt;/schema.json</c>.</summary>
    public static readonly Catalog Templates = new("templates", "", "schema.json");

    /// <summary>The session modes (S1): <c>templates/modes/&lt;name&gt;/mode.json</c>.</summary>
    public static readonly Catalog Modes = new("modes", "modes", "mode.json");

    /// <summary>The workflow definitions (C1): <c>templates/workflows/&lt;name&gt;/workflow.json</c>.</summary>
    public static readonly Catalog Workflows = new("workflows", "workflows", "workflow.json");

    public static readonly IReadOnlyList<Catalog> All = [Templates, Modes, Workflows];

    /// <summary>
    /// A definition name: lower case, digits and "-", 2 to 32 characters, a letter first. No dot: dots are for the
    /// system kinds (pr.opened). A folder that starts with "_" or "." is never a definition.
    /// </summary>
    public static bool IsName(string name) => NameRule().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$")]
    private static partial Regex NameRule();
}
