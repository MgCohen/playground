using PatternEngine.Features.Catalog.Domain;
using System.Security.Cryptography;
using System.Text;

namespace PatternEngine.Features.Catalog.Domain.Shared;

/// <summary>The patterns of one domain, by id, in file order. The hash names the version of the full catalog.</summary>
public sealed class PatternCatalog
{
    private readonly Dictionary<string, Pattern> byId;

    public PatternCatalog(IEnumerable<Pattern> patterns)
    {
        All = patterns.OrderBy(p => p.File, StringComparer.Ordinal).ToList();
        byId = new Dictionary<string, Pattern>(StringComparer.Ordinal);
        foreach (var p in All)
        {
            byId[p.Id] = p;
        }

        var joined = string.Join("\n", All.Select(p => $"{p.Id}:{p.Hash}"));
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..12];
    }

    public IReadOnlyList<Pattern> All { get; }

    public string Hash { get; }

    public IEnumerable<Pattern> Active => All.Where(p => p.Status != PatternStatus.Proposed);

    public Pattern? Get(string id) => byId.GetValueOrDefault(id);

    public bool Has(string id) => byId.ContainsKey(id);

    public static PatternCatalog Load(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return new PatternCatalog([]);
        }

        var files = Directory.GetFiles(folder, "*.md").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
        return new PatternCatalog(files.Select(f => PatternParser.Parse(File.ReadAllText(f), Path.GetFileName(f))));
    }

    public PatternCatalog With(Pattern pattern) => new(All.Where(p => p.Id != pattern.Id).Append(pattern));

    /// <summary>The ids of the non-leaf child patterns of a pattern.</summary>
    public IReadOnlyList<string> ChildPatterns(Pattern p) =>
        p.Parts.Select(pt => pt.Ref).Where(r => Get(r) is { Kind: not PatternKind.Leaf }).Distinct().ToList();
}
