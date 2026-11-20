using System.Net.NetworkInformation;
using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class NetworkPolicyTests
{
    [Fact]
    public void PhysicalEthernetWithGateway_ScoresHigherThanTailscale100G()
    {
        var tailscale = new NetworkAdapterCandidate(
            Id: "{TAILSCALE-GUID}",
            Name: "Tailscale",
            Description: "Tailscale Tunnel",
            InterfaceType: (NetworkInterfaceType)53, // IF_TYPE_PROP_VIRTUAL
            Speed: 100_000_000_000L, // 100 Gbps reported by Tailscale driver
            HasIpv4Gateway: false,
            HasPhysicalMac: false
        );

        var physicalEthernet = new NetworkAdapterCandidate(
            Id: "{ETHERNET-GUID}",
            Name: "Ethernet",
            Description: "Intel(R) Ethernet Controller I226-V",
            InterfaceType: NetworkInterfaceType.Ethernet,
            Speed: 1_000_000_000L, // 1 Gbps
            HasIpv4Gateway: true,
            HasPhysicalMac: true
        );

        int tailscaleScore = NetworkPolicy.ScoreAdapter(tailscale);
        int ethernetScore = NetworkPolicy.ScoreAdapter(physicalEthernet);

        Assert.True(ethernetScore > tailscaleScore);
        Assert.True(tailscaleScore < 0); // Should be negative due to virtual/tunnel penalty
        Assert.True(ethernetScore >= 1600); // 1000 (GW) + 500 (MAC) + 100 (Ethernet)

        var selected = NetworkPolicy.SelectPrimaryId(new[] { tailscale, physicalEthernet });
        Assert.Equal("{ETHERNET-GUID}", selected);
    }

    [Fact]
    public void WifiWithGateway_ScoresHigherThanHyperVDefaultSwitch()
    {
        var hyperV = new NetworkAdapterCandidate(
            Id: "{HYPERV-GUID}",
            Name: "vEthernet (Default Switch)",
            Description: "Hyper-V Virtual Ethernet Adapter",
            InterfaceType: NetworkInterfaceType.Ethernet,
            Speed: 10_000_000_000L, // 10 Gbps
            HasIpv4Gateway: false,
            HasPhysicalMac: true
        );

        var wifi = new NetworkAdapterCandidate(
            Id: "{WIFI-GUID}",
            Name: "Wi-Fi",
            Description: "Intel(R) Wi-Fi 6E AX211 160MHz",
            InterfaceType: NetworkInterfaceType.Wireless80211,
            Speed: 866_000_000L,
            HasIpv4Gateway: true,
            HasPhysicalMac: true
        );

        var selected = NetworkPolicy.SelectPrimaryId(new[] { hyperV, wifi });
        Assert.Equal("{WIFI-GUID}", selected);
    }

    [Fact]
    public void WhenNoGatewayPresent_PhysicalAdapterPreferredOverVirtual()
    {
        var virtualTunnel = new NetworkAdapterCandidate(
            Id: "{VIRT-GUID}",
            Name: "Tailscale",
            Description: "Tailscale Tunnel",
            InterfaceType: (NetworkInterfaceType)53,
            Speed: 100_000_000_000L,
            HasIpv4Gateway: false,
            HasPhysicalMac: false
        );

        var offlineEth = new NetworkAdapterCandidate(
            Id: "{ETH-GUID}",
            Name: "Ethernet",
            Description: "Realtek PCIe 2.5GbE Family Controller",
            InterfaceType: NetworkInterfaceType.Ethernet,
            Speed: 2_500_000_000L,
            HasIpv4Gateway: false,
            HasPhysicalMac: true
        );

        var selected = NetworkPolicy.SelectPrimaryId(new[] { virtualTunnel, offlineEth });
        Assert.Equal("{ETH-GUID}", selected);
    }
}

