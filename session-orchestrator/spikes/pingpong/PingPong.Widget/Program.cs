using Microsoft.Extensions.DependencyInjection;
using PingPong.Ui;
using PingPong.Win;

namespace PingPong.Widget;

/// <summary>The page tells the window which part it uses.</summary>
public sealed class WidgetArea
{
    public Action<bool>? Set { get; set; }
}

static class Program
{
    const int W = 360, H = 560;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var area = new WidgetArea();
        var services = WinHead.Services("widget", new Head { OpenMain = WinHead.OpenMain });
        services.AddSingleton(area);
        var form = new ClearForm(services.BuildServiceProvider(), typeof(Widget)) { Text = "Ping pong widget (spike)" };
        var s = form.DeviceDpi / 96.0;
        form.Bounds = new Rectangle(720, 60, (int)(W * s), (int)(H * s));
        area.Set = open => form.SetArea(open ? new Rectangle(0, 0, W, H) : new Rectangle(W - 104, H - 104, 104, 104));

        // The test puts an own striped window behind the widget, so a picture shows no other program.
        Form? back = null;
        if (args.Contains("--backdrop"))
        {
            back = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = form.Bounds, ShowInTaskbar = false, TopMost = true };
            back.Paint += (_, e) =>
            {
                using var brush = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.LargeCheckerBoard, Color.MediumOrchid, Color.Plum);
                e.Graphics.FillRectangle(brush, back.ClientRectangle);
            };
            var clicks = 0;
            back.MouseDown += (_, _) => back.Text = $"clicks {++clicks}";
            back.Show();
        }

        using var icon = new NotifyIcon { Icon = SystemIcons.Information, Text = "Ping pong spike", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        icon.ContextMenuStrip.Items.Add("Open the main app", null, (_, _) => WinHead.OpenMain(""));
        icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());
        form.HandleCreated += (_, _) => WinHead.Serve("pingpong-widget", form, line =>
            line == "back" ? Task.FromResult(back?.Text ?? "") : Probe.Run(line, form, form.Web));
        Application.Run(form);
    }
}
