using HDREZKA.App.Services;
using HDREZKA.App.Views;
using HDREZKA.Core.Api;

namespace HDREZKA.App;

public partial class App
{
    private PlayerWindow? _localPlayerWindow;
    private bool TryLaunchLocalFile()
    {
        var args = Environment.GetCommandLineArgs();
        var local = args.FirstOrDefault(a => a.StartsWith("--local-file="))?[13..];
        if (local == null || !File.Exists(local)) return false;
        LocalizationService.Instance.Apply(SettingsService.Instance.Language);
        var details = new MovieDetailed { Id = "local-anime4k-test", Name = Path.GetFileName(local), Favs = "" };
        var voice = new MovieVoiceActing("Original", "0", "0", "0", "0", "0", false, true, null);
        _localPlayerWindow = new PlayerWindow();
        _localPlayerWindow.ShowPlayer(new PlayerLaunch(details, voice, null, null, null, local));
        var report = args.FirstOrDefault(a => a.StartsWith("--smoke-report="))?[15..];
        if (report != null) _ = RunSmokeAsync(report);
        var smoothReport = args.FirstOrDefault(a => a.StartsWith("--smooth-report="))?[16..];
        if (smoothReport != null) _ = RunSmoothSmokeAsync(smoothReport);
        var switchReport = args.FirstOrDefault(a => a.StartsWith("--switch-report="))?[16..];
        if (switchReport != null) _ = RunSwitchStressAsync(switchReport);
        return true;
    }
    private async Task RunSwitchStressAsync(string report)
    {
        try
        {
            await Task.Delay(1500);
            var frame = _localPlayerWindow?.Content as Microsoft.UI.Xaml.Controls.Frame;
            if (frame?.Content is not PlayerPage page) throw new InvalidOperationException("Player page unavailable");
            await page.RunSwitchStressAsync(report);
        }
        catch (Exception ex) { File.WriteAllText(report, ex.ToString()); }
    }
    private async Task RunSmoothSmokeAsync(string report)
    {
        try
        {
            await Task.Delay(1500);
            var frame = _localPlayerWindow?.Content as Microsoft.UI.Xaml.Controls.Frame;
            if (frame?.Content is not PlayerPage page) throw new InvalidOperationException("Player page unavailable");
            await page.RunSmoothSmokeAsync(report);
        }
        catch (Exception ex) { File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { Error = ex.ToString() })); }
    }
    private async Task RunSmokeAsync(string report)
    {
        try
        {
            await Task.Delay(1500);
            var frame = _localPlayerWindow?.Content as Microsoft.UI.Xaml.Controls.Frame;
            if (frame?.Content is not PlayerPage page) throw new InvalidOperationException("Player page unavailable");
            await page.RunSmokeAsync(report);
        }
        catch (Exception ex) { File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { Error = ex.ToString() })); }
    }
}
