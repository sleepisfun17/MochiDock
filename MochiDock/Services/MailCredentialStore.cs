using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MochiDock.Services;

/// <summary>
/// App passwords for the IMAP mail accounts, keyed by address. Sealed with Windows DPAPI for the current user in
/// %LocalAppData%\MochiDock\mail-imap.dat; never written to the settings JSON, exports or the log.
/// </summary>
public sealed class MailCredentialStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();

    public MailCredentialStore(string? path = null, ISecretProtector? protector = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.Local, "mail-imap.dat");
        _protector = protector ?? new DpapiProtector();
    }

    public static MailCredentialStore Default { get; } = new();

    private Dictionary<string, string> ReadAll()
    {
        if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
        var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_path)));
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? throw new JsonException("Credential store root cannot be null.");
        return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }

    private void WriteAll(Dictionary<string, string> map)
    {
        if (map.Count == 0) { if (File.Exists(_path)) File.Delete(_path); return; }
        DpapiProtector.WriteAtomicBytes(_path, _protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(map))));
    }

    public string? Load(string address)
    {
        lock (_gate)
        {
            try { return ReadAll().TryGetValue(address.Trim(), out var p) ? p : null; }
            catch (Exception ex) { AppLog.Warn("Mail", ex); return null; }
        }
    }

    public void Save(string address, string password)
    {
        lock (_gate)
        {
            try
            {
                var map = ReadAll();
                map[address.Trim()] = password;
                WriteAll(map);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Mail", ex);
                throw;
            }
        }
    }

    public void Remove(string address)
    {
        lock (_gate)
        {
            try
            {
                var map = ReadAll();
                if (map.Remove(address.Trim())) WriteAll(map);
            }
            catch (Exception ex) { AppLog.Warn("Mail", ex); }
        }
    }
}
