namespace PatternEngine.Plugins.Agents;

/// <summary>Finds an agent file by name. The first folder that has the file wins, so a pack can replace an engine agent.</summary>
public sealed class AgentCatalog(IReadOnlyList<string> folders)
{
    private readonly Dictionary<string, AgentFile> cache = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Folders { get; } = folders;

    public AgentFile Get(string name)
    {
        lock (cache)
        {
            if (cache.TryGetValue(name, out var found))
            {
                return found;
            }

            var path = Folders.Select(f => Path.Combine(f, $"{name}.md")).FirstOrDefault(File.Exists)
                ?? throw new AgentFileException($"no agent file \"{name}.md\" in {string.Join(", ", Folders)}");
            var agent = AgentFile.Read(path);
            cache[name] = agent;
            return agent;
        }
    }
}
