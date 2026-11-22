using System;
using System.IO;
using System.Text;

namespace MochiDock.Services;

/// <summary>
/// User name and password for a shared GOWA server (GOWA's --basic-auth). Sealed with Windows DPAPI for the current user in
/// %LocalAppData%\MochiDock\gowa-remote.dat; never written to the settings JSON, exports or the log.
/// </summary>
public static class GowaCredentialStore
{
    private static string FilePath => Path.Combine(AppDataPaths.Local, "gowa-remote.dat");
    private static readonly ISecretProtector Protector = new DpapiProtector();

    public static (string User, string Password)? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var text = Encoding.UTF8.GetString(Protector.Unprotect(File.ReadAllBytes(FilePath)));
            int split = text.IndexOf('\n');
            if (split <= 0) return null;
            return (text[..split], text[(split + 1)..]);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Gowa", ex);
            return null;
        }
    }

    public static void Save(string user, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(user)) { Clear(); return; }
            DpapiProtector.WriteAtomicBytes(FilePath, Protector.Protect(Encoding.UTF8.GetBytes(user.Trim() + "\n" + password)));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Gowa", ex);
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception ex) { AppLog.Warn("Gowa", ex); }
    }
}
