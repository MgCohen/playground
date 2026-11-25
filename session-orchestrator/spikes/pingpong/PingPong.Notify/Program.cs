using Microsoft.Extensions.DependencyInjection;
using PingPong.Core;
using PingPong.Ui;
using PingPong.Win;

namespace PingPong.Notify;

/// <summary>A small window that shows without focus.</summary>
sealed class ToastForm(IServiceProvider services) : PageForm(services, typeof(Toast))
{
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x00000008; // WS_EX_NOACTIVATE, WS_EX_TOPMOST
            return p;
        }
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var services = WinHead.Services("notify", new Head { OpenMain = WinHead.OpenMain }).BuildServiceProvider();
        var client = services.GetRequiredService<GameClient>();
        var form = new ToastForm(services)
        {
            Text = "Ping pong notice (spike)",
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
        };
        var s = form.DeviceDpi / 96.0;
        form.Bounds = new Rectangle(40, 980, (int)(380 * s), (int)(92 * s));
        var hide = new System.Windows.Forms.Timer { Interval = 6000 };
        hide.Tick += (_, _) => { hide.Stop(); form.Hide(); };
        client.Arrived += _ => form.BeginInvoke(() => { hide.Stop(); form.Show(); hide.Start(); });
        form.HandleCreated += (_, _) => WinHead.Serve("pingpong-notify", form, line => Probe.Run(line, form, form.Web));
        form.Shown += (_, _) => hide.Start();
        Application.Run(form);
    }
}
