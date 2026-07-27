using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// Determines whether cloud-known devices are physically present on this PC's local network(s), by
    /// warming the OS ARP cache (a ping sweep - even devices that don't answer ICMP still trigger the
    /// underlying ARP resolution needed to attempt routing to them) and then resolving each device's
    /// known MAC address against that cache (see TapoConnect.Util.TapoUtils.TryGetIpAddressByMacAddress,
    /// which reads "arp -a"). This is what lets a NINA instance at one physical site only ever see/control
    /// the plugs actually at that site, even if the same TP-Link account has plugs elsewhere (a different
    /// observatory, home) - see CLAUDE.md "Architecture history" for the full reasoning.
    /// </summary>
    public class LocalPresenceResolver {
        private const int PingTimeoutMs = 250;
        private const int MaxHostsPerSubnet = 254;

        public async Task WarmArpCacheAsync(CancellationToken token = default) {
            var subnets = GetLocalIPv4Subnets();
            var pingTasks = new List<Task>();

            foreach (var (networkAddress, hostCount) in subnets) {
                // hostCount includes the network and broadcast addresses (e.g. 256 for a /24), so
                // compare the actual usable-host count against the cap, not the raw value - otherwise
                // an ordinary /24 (254 usable hosts) is incorrectly skipped entirely.
                if (hostCount < 2 || hostCount - 2 > MaxHostsPerSubnet) {
                    // Avoid an impractically long scan on a misconfigured/overly-broad subnet.
                    continue;
                }
                for (uint i = 1; i < hostCount - 1; i++) {
                    var host = ToIPAddress(ToUInt32(networkAddress) + i);
                    pingTasks.Add(PingQuietlyAsync(host, token));
                }
            }

            await Task.WhenAll(pingTasks);
        }

        private static async Task PingQuietlyAsync(IPAddress address, CancellationToken token) {
            try {
                using var ping = new Ping();
                await ping.SendPingAsync(address, PingTimeoutMs);
            } catch (Exception ex) when (!(ex is OperationCanceledException)) {
                // Irrelevant whether the ping itself succeeds - only the ARP resolution it triggers matters.
                Logger.Debug($"SmartPlugControl: ARP warm-up ping to {address} failed (expected/harmless): {ex.Message}");
            }
        }

        private static IEnumerable<(IPAddress networkAddress, uint hostCount)> GetLocalIPv4Subnets() {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                    continue;
                }
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses) {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) {
                        continue;
                    }
                    var mask = unicast.IPv4Mask;
                    if (mask == null || mask.Equals(IPAddress.Any)) {
                        continue;
                    }
                    uint addr = ToUInt32(unicast.Address);
                    uint maskBits = ToUInt32(mask);
                    uint network = addr & maskBits;
                    uint hostCount = ~maskBits + 1;
                    yield return (ToIPAddress(network), hostCount);
                }
            }
        }

        private static uint ToUInt32(IPAddress address) {
            var bytes = address.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private static IPAddress ToIPAddress(uint value) {
            var bytes = new byte[] {
                (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
            };
            return new IPAddress(bytes);
        }
    }
}
