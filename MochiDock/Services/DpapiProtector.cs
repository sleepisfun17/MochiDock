using System.Security.Cryptography;

namespace MochiDock.Services;

/// <summary>Windows DPAPI (current user) implementation of <see cref="ISecretProtector"/>.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] sealedBytes) => ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser);
}
