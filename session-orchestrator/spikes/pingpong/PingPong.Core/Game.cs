namespace PingPong.Core;

/// <summary>One hit of the ball: the event of the stream.</summary>
public sealed record Hit(int Id, string Kind, string From, DateTimeOffset At);

/// <summary>The game: pings and pongs alternate. This is the engine library; it has no HTTP and no UI.</summary>
public sealed class Game
{
    readonly Lock gate = new();
    readonly List<Hit> hits = [];

    public event Action<Hit>? Added;

    public Hit Play(string from)
    {
        Hit hit;
        lock (gate)
        {
            hit = new Hit(hits.Count + 1, hits.Count % 2 == 0 ? "ping" : "pong", from, DateTimeOffset.Now);
            hits.Add(hit);
        }
        Added?.Invoke(hit);
        return hit;
    }

    public IReadOnlyList<Hit> All()
    {
        lock (gate) return [.. hits];
    }
}
