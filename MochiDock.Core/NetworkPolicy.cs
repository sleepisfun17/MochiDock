using System.Net.NetworkInformation;

namespace MochiDock.Core;

public readonly record struct NetworkAdapterCandidate(
    string Id,
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    long Speed,
    bool HasIpv4Gateway,
    bool HasPhysicalMac
);

public static class NetworkPolicy
{
    public static int ScoreAdapter(NetworkAdapterCandidate adapter)
    {
        int score = 0;

        // 1. Definite negative score for virtual / software adapters based on name / description
        string name = adapter.Name ?? "";
        string desc = adapter.Description ?? "";
        bool isKnownVirtual = name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase) ||
                              desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                              desc.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                              desc.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                              desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

        if (isKnownVirtual)
        {
            score -= 500;
        }

        // 2. Gateway presence is the strongest indicator of the active internet uplink
        if (adapter.HasIpv4Gateway)
        {
            score += 1000;
        }

        // 3. Physical hardware MAC address presence (virtual adapters like Tailscale often have empty MACs)
        if (adapter.HasPhysicalMac)
        {
            score += 500;
        }

        // 4. Standard physical interface types
        if (adapter.InterfaceType == NetworkInterfaceType.Ethernet ||
            adapter.InterfaceType == NetworkInterfaceType.GigabitEthernet)
        {
            score += 100;
        }
        else if (adapter.InterfaceType == NetworkInterfaceType.Wireless80211)
        {
            score += 90;
        }

        return score;
    }

    public static string? SelectPrimaryId(IEnumerable<NetworkAdapterCandidate> candidates)
    {
        return candidates
            .OrderByDescending(ScoreAdapter)
            .ThenByDescending(c => c.Speed)
            .Select(c => c.Id)
            .FirstOrDefault();
    }
}

