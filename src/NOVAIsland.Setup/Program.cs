using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NOVAIsland.Setup;

internal sealed record InstallOptions(
    string InstallDir,
    bool RegisterBrowsers,
    bool CreateDesktopShortcut,
    bool LaunchOnStartup,
    bool LaunchAfterInstall,
    bool Silent);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase)
                                || a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase)
                                || a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        var noLaunch = args.Any(a => a.Equals("/NOLAUNCH", StringComparison.OrdinalIgnoreCase)
                                  || a.Equals("--no-launch", StringComparison.OrdinalIgnoreCase));
        var dirArg = args.FirstOrDefault(a => a.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase));
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "DOOMIsland");
        var installDir = dirArg is not null
            ? dirArg[5..].Trim('"')
            : defaultDir;

        if (silent)
        {
            var opts = new InstallOptions(
                InstallDir: installDir,
                RegisterBrowsers: true,
                CreateDesktopShortcut: false,
                LaunchOnStartup: false,
                LaunchAfterInstall: !noLaunch,
                Silent: true);
            InstallerEngine.Install(opts, _ => { }, (_, _) => { });
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(installDir));
        return 0;
    }
}

internal static class InstallerEngine
{
    private const string AppName = "DOOM Island";
    private const string AppVersion = "2.3.0";
    private const string NativeHostName = "com.doomisland.host";
    private const string DefaultExtensionId = "amplhmapoeandecddkchambamhafdcgp";

    public static void Install(
        InstallOptions options,
        Action<string> onStatus,
        Action<int, int> onProgress)
    {
        onStatus("Stopping any running DOOM Island processes...");
        StopExistingProcesses();

        var installDir = Path.GetFullPath(options.InstallDir);
        Directory.CreateDirectory(installDir);

        onStatus("Extracting DOOM Island files...");
        ExtractPayload(installDir, onProgress);

        // Clean up legacy v1.0.0 executable name if upgrading in place
        var legacyExe = Path.Combine(installDir, "NOVAIsland.App.exe");
        if (File.Exists(legacyExe))
        {
            try { File.Delete(legacyExe); } catch { }
        }

        onStatus("Creating Start Menu shortcut...");
        var appExe = Path.Combine(installDir, "DOOMIsland.exe");
        var startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, $"{AppName}.lnk"), appExe, installDir, "DOOM Island — Native Windows Dynamic Island");

        if (options.CreateDesktopShortcut)
        {
            onStatus("Creating Desktop shortcut...");
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktopDir, $"{AppName}.lnk"), appExe, installDir, "DOOM Island — Native Windows Dynamic Island");
        }

        if (options.RegisterBrowsers)
        {
            onStatus("Configuring Chrome, Edge & Opera media bridge...");
            RegisterNativeMessagingHosts(installDir);
        }

        if (options.LaunchOnStartup)
        {
            onStatus("Enabling Windows startup launch...");
            using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            runKey?.SetValue("DOOMIsland", $"\"{appExe}\" --minimized");
        }

        onStatus("Registering uninstaller...");
        RegisterAddRemovePrograms(installDir, appExe);

        if (options.LaunchAfterInstall && File.Exists(appExe))
        {
            onStatus("Launching DOOM Island...");
            Process.Start(new ProcessStartInfo(appExe)
            {
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }

        onStatus("Installation completed.");
    }

    private static void StopExistingProcesses()
    {
        foreach (var procName in new[] { "DOOMIsland", "NOVAIsland.App", "NOVAIsland.BrowserHost" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch
            {
                // Ignore processes that cannot be accessed or already exited
            }
        }
    }

    private static void ExtractPayload(string installDir, Action<int, int> onProgress)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("DOOMIsland.Payload.zip")
            ?? throw new InvalidOperationException("Embedded payload DOOMIsland.Payload.zip is missing. Build using Package-Release.ps1.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var total = Math.Max(1, entries.Count);
        var count = 0;

        foreach (var entry in entries)
        {
            var destPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
            if (!destPath.StartsWith(installDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !destPath.Equals(installDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destPath, overwrite: true);
            count++;
            onProgress(count, total);
        }
    }

    private static void RegisterNativeMessagingHosts(string installDir)
    {
        var hostExe = Path.Combine(installDir, "BrowserHost", "NOVAIsland.BrowserHost.exe");
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DOOMIsland",
            "NativeMessaging");
        Directory.CreateDirectory(manifestDir);

        var manifest = new
        {
            name = NativeHostName,
            description = "DOOM Island Native Messaging Host for Chrome, Edge, and Opera media detection and control",
            path = hostExe,
            type = "stdio",
            allowed_origins = new[]
            {
                $"chrome-extension://{DefaultExtensionId}/"
            }
        };

        var manifestPath = Path.Combine(manifestDir, $"{NativeHostName}.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json, Encoding.UTF8);

        // Chrome & Opera both use HKCU\Software\Google\Chrome\NativeMessagingHosts on Windows; Edge uses Microsoft\Edge.
        foreach (var keyPath in new[]
        {
            $@"Software\Google\Chrome\NativeMessagingHosts\{NativeHostName}",
            $@"Software\Microsoft\Edge\NativeMessagingHosts\{NativeHostName}"
        })
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
            key?.SetValue(null, manifestPath);
        }
    }

    private static void RegisterAddRemovePrograms(string installDir, string appExe)
    {
        var uninstallScript = Path.Combine(installDir, "Uninstall-DOOMIsland.ps1");
        var cmdPath = Path.Combine(installDir, "Uninstall-DOOMIsland.cmd");
        File.WriteAllText(
            cmdPath,
            $"@echo off\r\npowershell -ExecutionPolicy Bypass -File \"{uninstallScript}\" -InstallDir \"{installDir}\"\r\n",
            Encoding.ASCII);

        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DOOMIsland", true);
        if (key is null) return;
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", appExe);
        key.SetValue("UninstallString", $"\"{cmdPath}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) return;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
