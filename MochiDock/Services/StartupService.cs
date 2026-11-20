using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace MochiDock.Services;

public static class StartupService
{
    private static readonly MochiDock.Core.StartupRegistration Registration = new(
        () => Task.Run(ReadEnabled), RunScriptAsync);

    public static Task<bool> RefreshAsync() => Registration.ReadAsync();
    public static Task<bool> ToggleAsync() => Registration.ToggleAsync();

    private static bool ReadEnabled()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder(@"\");
            try
            {
                return MochiDock.Core.StartupRegistration.ReadTaskEnabled(() =>
                {
                    dynamic task = folder.GetTask("MochiDock-Startup-" + WindowsIdentity.GetCurrent().User!.Value);
                    try { return (bool)task.Enabled; }
                    finally { Marshal.FinalReleaseComObject(task); }
                });
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }

    public static Task<bool> SetEnabledAsync(bool enabled) => Registration.SetAsync(enabled);

    private static async Task RunScriptAsync(bool enabled)
    {
        string script = Path.Combine(AppContext.BaseDirectory, enabled ? "install-startup.ps1" : "uninstall-startup.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Startup helper is missing.", script);
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell is missing.", powershell);
        var info = new System.Diagnostics.ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) info.ArgumentList.Add(argument);
        if (enabled) { info.ArgumentList.Add("-ExecutablePath"); info.ArgumentList.Add(Environment.ProcessPath!); }
        using var process = System.Diagnostics.Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await errors;
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows startup registration failed: " + error);
    }

    public static async Task<bool> InitializeAsync()
    {
        bool enabled = await RefreshAsync();
        bool legacy = await Task.Run(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key?.GetValue("MochiDock") != null) return true;
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                // "AuraIsland.lnk"/"AuraIsland.exe" are legacy pre-rename startup shortcut names still present on some PCs.
                foreach (string name in new[] { "AuraIsland.lnk", "MochiDock.lnk" })
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), name);
                    if (!File.Exists(path)) continue;
                    dynamic shortcut = shell.CreateShortcut(path);
                    try
                    {
                        string target = Path.GetFileName((string)shortcut.TargetPath);
                        if (target is "AuraIsland.exe" or "MochiDock.exe") return true;
                    }
                    finally { Marshal.FinalReleaseComObject(shortcut); }
                }
                return false;
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        });
        if (!enabled && legacy)
        {
            await SetEnabledAsync(true);
            enabled = await RefreshAsync();
        }
        return enabled;
    }
}
