using System.Security.Cryptography;
using System.Text;

namespace LocalAgentBroker.Shared;

/// <summary>Creates a stable per-state identity for the tray companion process.</summary>
public static class BrokerUiInstanceName
{
    public static string Create(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string normalized = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"Local\\LocalAgentBroker.Tray.{Convert.ToHexString(hash.AsSpan(0, 12))}";
    }
}
