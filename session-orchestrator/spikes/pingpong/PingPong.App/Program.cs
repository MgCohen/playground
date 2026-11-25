using Microsoft.Extensions.DependencyInjection;
using PingPong.Ui;
using PingPong.Win;

namespace PingPong.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // One main app: a second start gives its route to the first one.
        using var mutex = new Mutex(true, @"Local\pingpong-app", out var first);
        if (!first)
        {
            WinHead.Send(WinHead.AppPipe, "open " + (args.FirstOrDefault() ?? ""), 2000).Wait();
            return;
        }
        ApplicationConfiguration.Initialize();
        var host = new Head();
        if (args.FirstOrDefault() is { } route) host.Show(route);
        var form = new PageForm(WinHead.Services("app", host).BuildServiceProvider(), typeof(Routes))
        {
            Text = "Ping pong main app (spike)",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(40, 60, 620, 900),
        };
        form.HandleCreated += (_, _) => WinHead.Serve(WinHead.AppPipe, form, line =>
        {
            if (!line.StartsWith("open ")) return Probe.Run(line, form, form.Web);
            host.Show(line[5..]);
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            return Task.FromResult("ok");
        });
        Application.Run(form);
    }
}
