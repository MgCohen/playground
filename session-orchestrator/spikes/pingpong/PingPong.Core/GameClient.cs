using System.Net.Http.Json;
using System.Text.Json;

namespace PingPong.Core;

/// <summary>The connection of a head to the engine process: HTTP for an act, server-sent events for the stream.</summary>
public sealed class GameClient(string baseUrl, string head) : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient http = new() { BaseAddress = new Uri(baseUrl), Timeout = Timeout.InfiniteTimeSpan };
    readonly CancellationTokenSource stop = new();
    readonly Lock gate = new();
    List<Hit> hits = [];
    int started;

    public string Head => head;
    public bool Connected { get; private set; }
    public event Action? Changed;
    public event Action<Hit>? Arrived;

    public IReadOnlyList<Hit> Hits
    {
        get { lock (gate) return [.. hits]; }
    }

    public Hit? Find(int id) => Hits.FirstOrDefault(h => h.Id == id);

    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(Listen);
    }

    public Task Play() => http.PostAsync($"api/hits?from={Uri.EscapeDataString(head)}", null, stop.Token);

    async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                // The stream opens first, so no hit is lost between the list and the stream.
                using var stream = await http.GetStreamAsync("api/live", stop.Token);
                using var reader = new StreamReader(stream);
                await reader.ReadLineAsync(stop.Token); // ": open"
                var all = await http.GetFromJsonAsync<List<Hit>>("api/hits", Json, stop.Token) ?? [];
                lock (gate) hits = all;
                Connected = true;
                Changed?.Invoke();
                while (await reader.ReadLineAsync(stop.Token) is { } line)
                {
                    if (!line.StartsWith("data: ")) continue;
                    var hit = JsonSerializer.Deserialize<Hit>(line[6..], Json)!;
                    lock (gate)
                    {
                        if (hits.Any(h => h.Id == hit.Id)) continue;
                        hits.Add(hit);
                    }
                    Arrived?.Invoke(hit);
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            // Each platform has its own exception types for a lost connection, so this catch takes all of them.
            catch (Exception e) { Console.WriteLine($"pingpong {head}: {e.GetType().FullName}: {e.Message}"); }
            Connected = false;
            Changed?.Invoke();
            try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        http.Dispose();
    }
}
