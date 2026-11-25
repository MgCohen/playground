using System.Text.Json;
using System.Threading.Channels;
using PingPong.Core;

// The engine process: it runs the library and gives it to the heads over HTTP and server-sent events.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:" + (Environment.GetEnvironmentVariable("PP_ENGINE_PORT") ?? "7931"));
var app = builder.Build();
var game = new Game();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/api/hits", () => game.All());
app.MapPost("/api/hits", (string from) => game.Play(from));
app.MapGet("/api/live", async (HttpContext c) =>
{
    c.Response.ContentType = "text/event-stream";
    var queue = Channel.CreateUnbounded<Hit>();
    void Add(Hit h) => queue.Writer.TryWrite(h);
    game.Added += Add;
    try
    {
        await c.Response.WriteAsync(": open\n\n", c.RequestAborted);
        await c.Response.Body.FlushAsync(c.RequestAborted);
        await foreach (var hit in queue.Reader.ReadAllAsync(c.RequestAborted))
        {
            await c.Response.WriteAsync($"data: {JsonSerializer.Serialize(hit, json)}\n\n", c.RequestAborted);
            await c.Response.Body.FlushAsync(c.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    finally { game.Added -= Add; }
});
app.Run();
