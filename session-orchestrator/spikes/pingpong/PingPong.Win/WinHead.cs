using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using PingPong.Core;
using PingPong.Ui;

namespace PingPong.Win;

/// <summary>The start of a Windows head: services, the data folder of WebView2, and the link to the main app.</summary>
public static class WinHead
{
    public const string AppPipe = "pingpong-app";

    public static ServiceCollection Services(string head, Head host)
    {
        // Each head has its own WebView2 data folder, unless the test wants to share one.
        var data = Environment.GetEnvironmentVariable("PP_DATA") ?? Path.Combine(Path.GetTempPath(), "pingpong");
        var folder = Environment.GetEnvironmentVariable("PP_SHARE") == "1" ? "shared" : head;
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(data, "webview2-" + folder));
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(new GameClient(Environment.GetEnvironmentVariable("PP_ENGINE") ?? "http://127.0.0.1:7931/", head));
        services.AddSingleton(host);
        return services;
    }

    /// <summary>Opens a page in the main app: through its pipe if it runs, else it starts the app with the route.</summary>
    public static void OpenMain(string route)
    {
        _ = Task.Run(async () =>
        {
            if (await Send(AppPipe, "open " + route) is null)
                Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("PP_APP") ?? "PingPong.App.exe", route) { UseShellExecute = false });
        });
    }

    /// <summary>Sends one line to a pipe and returns the answer, or null if nothing listens.</summary>
    public static async Task<string?> Send(string pipe, string line, int wait = 400)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.None);
            await client.ConnectAsync(wait);
            await using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, leaveOpen: true);
            await writer.WriteLineAsync(line);
            return await reader.ReadLineAsync() ?? "";
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A pipe server for the current user. The main app uses it for its links, and the test uses it for each head.</summary>
    public static void Serve(string pipe, Control ui, Func<string, Task<string>> answer)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                try
                {
                    using var reader = new StreamReader(server, leaveOpen: true);
                    await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                    var line = await reader.ReadLineAsync() ?? "";
                    var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.BeginInvoke(async () =>
                    {
                        try { done.SetResult(await answer(line)); }
                        catch (Exception e) { done.SetResult("error " + e.Message.ReplaceLineEndings(" ")); }
                    });
                    await writer.WriteLineAsync(await done.Task);
                }
                catch (IOException) { }
            }
        });
    }
}
