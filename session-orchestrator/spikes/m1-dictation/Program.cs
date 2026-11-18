// Spike M-1: a test page that records the voice, and a POST endpoint that changes the audio into text
// with a local model. The page sends 16 kHz mono float32 samples, so the server does no audio decode.
using System.Diagnostics;
using SherpaOnnx;

var models = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "orch", "models");
var dir = Environment.GetEnvironmentVariable("M1_MODEL_DIR")
    ?? Directory.GetDirectories(models, "*parakeet-tdt-0.6b-v3*").FirstOrDefault()
    ?? throw new InvalidOperationException($"No Parakeet model folder in {models}");
var port = Environment.GetEnvironmentVariable("M1_PORT") ?? "7830";
var threads = int.Parse(Environment.GetEnvironmentVariable("M1_THREADS") ?? "8");

string File(string pattern) => Directory.GetFiles(dir, pattern).Single();

var config = new OfflineRecognizerConfig();
config.ModelConfig.Transducer.Encoder = File("encoder*.onnx");
config.ModelConfig.Transducer.Decoder = File("decoder*.onnx");
config.ModelConfig.Transducer.Joiner = File("joiner*.onnx");
config.ModelConfig.Tokens = File("tokens.txt");
config.ModelConfig.ModelType = "nemo_transducer";
config.ModelConfig.NumThreads = threads;
config.ModelConfig.Provider = "cpu";

var load = Stopwatch.StartNew();
var recognizer = new OfflineRecognizer(config);
Console.WriteLine($"model {Path.GetFileName(dir)} loaded in {load.ElapsedMilliseconds} ms, {threads} threads");

// One decode at a time: the recognizer is shared.
var gate = new SemaphoreSlim(1);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/transcribe", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    var bytes = buffer.ToArray();
    var samples = new float[bytes.Length / 4];
    Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
    var seconds = samples.Length / 16000.0;

    await gate.WaitAsync();
    try
    {
        var watch = Stopwatch.StartNew();
        var text = Decode(samples);
        var ms = watch.ElapsedMilliseconds;
        Console.WriteLine($"{seconds:F1} s of audio in {ms} ms: {text}");
        return Results.Json(new { text, audioSeconds = Math.Round(seconds, 1), decodeMs = ms });
    }
    finally { gate.Release(); }
});

app.Run();

// Parakeet decodes up to a few minutes in one pass. Longer audio goes in parts of 60 s.
string Decode(float[] samples)
{
    const int part = 16000 * 60;
    var texts = new List<string>();
    for (var start = 0; start < samples.Length; start += part)
    {
        var length = Math.Min(part, samples.Length - start);
        var stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples.AsSpan(start, length).ToArray());
        recognizer.Decode(stream);
        texts.Add(stream.Result.Text.Trim());
    }
    return string.Join(" ", texts.Where(t => t.Length > 0));
}
