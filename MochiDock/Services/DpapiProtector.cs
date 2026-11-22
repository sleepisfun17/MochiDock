using System.IO;
using System.Security.Cryptography;

namespace MochiDock.Services;

/// <summary>Windows DPAPI (current user) implementation of <see cref="ISecretProtector"/>.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] sealedBytes) => ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser);

    /// <summary>Writes sealed credential bytes via a flushed temp file and atomic replace.</summary>
    internal static void WriteAtomicBytes(string path, byte[] data)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(data, 0, data.Length);
                file.Flush(true);
            }
            if (File.Exists(fullPath)) File.Replace(temp, fullPath, destinationBackupFileName: null);
            else File.Move(temp, fullPath);
        }
        finally
        {
            if (File.Exists(temp)) try { File.Delete(temp); } catch { }
        }
    }
}
