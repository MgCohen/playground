using Microsoft.Web.WebView2.Core;

namespace PingPong.Win;

/// <summary>The test commands of a head. Test code of the spike, not a part of the design.</summary>
public static class Probe
{
    public static async Task<string> Run(string line, Form form, CoreWebView2? web)
    {
        var parts = line.Split(' ', 2);
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "js":
                return web is null ? "no page" : await web.ExecuteScriptAsync(arg);
            case "page":
                if (web is null) return "no page";
                using (var file = File.Create(arg)) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                return "ok";
            case "area":
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(form.Location, Point.Empty, form.Size);
                    bitmap.Save(arg);
                }
                return "ok";
            case "state":
                return $"visible {form.Visible} top {form.TopMost} taskbar {form.ShowInTaskbar} bounds {form.Bounds} active {Form.ActiveForm == form}";
            case "exit":
                form.BeginInvoke(Application.Exit);
                return "ok";
            default:
                return "unknown";
        }
    }
}
