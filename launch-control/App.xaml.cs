using System.IO;
using System.Windows;
using LaunchControl.Standard.Host;

namespace HulkReNaymer.LaunchControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = LaunchActions.FindRoot();
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HulkReNaymer");
        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HulkReNaymer");
        Directory.CreateDirectory(Path.Combine(local, "logs"));

        LaunchControlApp.Run(this, new LaunchControlProfile
        {
            ProductId = "hulkrenaymer",
            ProductName = "HulkReNaymer",
            AppDataFolder = "HulkReNaymer",
            ServiceNames = [],
            ShowVenvUi = false,
            ShowBrowserButton = false,
            ShowStartStopButtons = false,
            ShowRestartButton = false,
            InstallRoot = root,
            LogPaths = [Path.Combine(roaming, "hulkrenaymer.log")],
            CrashLogPath = Path.Combine(local, "logs", "launch-control.log"),
            DefaultColors = new Dictionary<string, string>
            {
                ["ChromeColor"] = "#10210F",
                ["PrimaryActionColor"] = "#7CFF4C"
            },
            MetaText = () => root,
            FooterText = "Open this LC from Master Launch Control (Generic). Rebuild/Run/CLI live here; MLC Open launches the Release exe.",
            StartupNotes =
            [
                "HulkReNaymer is a desktop app (no Windows service).",
                "Rebuild Release / Debug compiles the repo. Run starts that exe. Open CLI drops a prompt at the repo with both bin folders on PATH.",
                "From MLC: Add app or run scripts\\Register-HulkReNaymer-MLC.ps1, then Open Launch Control. MLC Open / Start uses the Release build."
            ],
            ExtraActionLayout = new ExtraActionLayout
            {
                CompactGroups = ["Build", "Run"]
            },
            ExtraActions =
            [
                new("Rebuild Release", w => LaunchActions.Rebuild(w, root, "Release"), "Build"),
                new("Rebuild Debug", w => LaunchActions.Rebuild(w, root, "Debug"), "Build"),
                new("Run Release", w => LaunchActions.RunApp(w, root, "Release"), "Run"),
                new("Run Debug", w => LaunchActions.RunApp(w, root, "Debug"), "Run"),
                new("Open CLI", w => LaunchActions.OpenCli(w, root), "Run")
            ]
        });
    }
}
