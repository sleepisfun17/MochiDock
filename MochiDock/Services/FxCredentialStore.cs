using System.IO;
using System.Runtime.InteropServices;
namespace MochiDock.Services;
public interface IFxCredentialStore { string? Read(); void Save(string key); void Remove(); }
public sealed class FxCredentialStore : IFxCredentialStore
{
    const string Target = "MochiDock/FX/OpenExchangeRates";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Credential { public uint Flags, Type; public string TargetName; public string? Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes; public string? Alias, UserName; }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ReadNative(string target, uint type, uint flags, out IntPtr value);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WriteNative(ref Credential value, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteNative(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr value);
    public string? Read() { if (!ReadNative(Target, 1, 0, out var ptr)) { if (Marshal.GetLastWin32Error() == 1168) return null; throw new IOException("Cannot read FX credential."); } try { var c = Marshal.PtrToStructure<Credential>(ptr); return Marshal.PtrToStringUni(c.Blob, (int)c.BlobSize / 2); } finally { CredFree(ptr); } }
    public void Save(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 512) throw new ArgumentException("Enter a valid App ID."); var ptr = Marshal.StringToCoTaskMemUni(key); try { var c = new Credential { Type = 1, TargetName = Target, BlobSize = (uint)key.Length * 2, Blob = ptr, Persist = 2, UserName = "MochiDock" }; if (!WriteNative(ref c, 0)) throw new IOException("Cannot save FX credential."); } finally { Marshal.ZeroFreeCoTaskMemUnicode(ptr); } }
    public void Remove() { if (!DeleteNative(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new IOException("Cannot remove FX credential."); }
}
