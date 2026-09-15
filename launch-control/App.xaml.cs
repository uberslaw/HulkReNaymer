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

        var releaseExe = LaunchActions.ExePath(root, "Release");
        var debugExe = LaunchActions.ExePath(root, "Debug");

        LaunchControlApp.Run(this, new LaunchControlProfile
        {
            ProductId = "hulkrenaymer",
            ProductName = "HulkReNaymer",
            AppDataFolder = "HulkReNaymer",
            ServiceNames = [],
            ShowVenvUi = false,
            ShowBrowserButton = false,
            ShowStartStopButtons = true,
            ShowRestartButton = true,
            StartButtonText = "Run Release",
            InstallRoot = root,
            LogPaths = [Path.Combine(roaming, "hulkrenaymer.log")],
            CrashLogPath = Path.Combine(local, "logs", "launch-control.log"),
            DefaultColors = new Dictionary<string, string>
            {
                ["ChromeColor"] = "#10210F",
                ["PrimaryActionColor"] = "#7CFF4C"
            },
            MetaText = () => $"Release {releaseExe}   Debug {debugExe}",
            FooterText = "Closing this window does not stop HulkReNaymer.",
            StartupNotes =
            [
                "Closing this window does not stop HulkReNaymer.",
                "The green button is Run Release. Extra actions (Run Release again, Run Debug, Open CLI, Rebuild) stay visible below — they are not Compact-hidden.",
                "From Master Launch Control: Open Launch Control on the HulkReNaymer card (or run scripts\\Register-HulkReNaymer-MLC.ps1 once)."
            ],
            ProcessFallback = new ProcessFallbackSpec
            {
                WorkingDirectory = () => LaunchActions.ExeDirectory(root, "Release"),
                StartInfo = () => (releaseExe, ""),
                DetachedGui = true,
                FindRunningPids = () => LaunchActions.FindRunningPids(root),
                LaunchNotes = () => File.Exists(releaseExe)
                    ? []
                    : [("Release exe is missing. Use Rebuild Release first.", "ERROR")]
            },
            ExtraActionLayout = new ExtraActionLayout
            {
                CompactGroups = [],
                CollapsedGroups = []
            },
            ExtraActions =
            [
                new("Run Release", w => LaunchActions.RunApp(w, root, "Release"), "Run"),
                new("Run Debug", w => LaunchActions.RunApp(w, root, "Debug"), "Run"),
                new("Open CLI", w => LaunchActions.OpenCli(w, root), "Run"),
                new("Rebuild Release", w => LaunchActions.Rebuild(w, root, "Release"), "Build"),
                new("Rebuild Debug", w => LaunchActions.Rebuild(w, root, "Debug"), "Build")
            ]
        });
    }
}
