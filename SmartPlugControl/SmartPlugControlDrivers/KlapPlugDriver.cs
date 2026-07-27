using System;
using System.Threading;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Exceptions;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a Tapo/newer-generation Kasa plug ("SMART.*" device family, KLAP/securePassthrough
    /// protocol) via a direct local-network connection to the device's own IP - these devices have no
    /// cloud-relay path at all (see CLAUDE.md "Architecture history" for why this is still safe for the
    /// multi-tenant threat model: the KLAP handshake itself is gated on the real TP-Link account
    /// credentials stored on the device since pairing).
    /// </summary>
    public class KlapPlugDriver : IPlugDriver {
        private readonly ITapoDeviceClient client;
        private readonly string deviceIp;
        private readonly string username;
        private readonly string password;
        private TapoDeviceKey deviceKey;
        private bool? energyMonitoringSupported;

        public string PlugId { get; }
        public string Alias { get; }

        public KlapPlugDriver(string plugId, string alias, string deviceIp, string username, string password) {
            PlugId = plugId;
            Alias = alias;
            this.deviceIp = deviceIp;
            this.username = username;
            this.password = password;
            client = new TapoDeviceClient();
        }

        private async Task<TapoDeviceKey> GetDeviceKeyAsync(CancellationToken token) {
            if (deviceKey == null) {
                deviceKey = await client.LoginByIpAsync(deviceIp, username, password);
            }
            return deviceKey;
        }

        private async Task<T> WithRetryAsync<T>(Func<TapoDeviceKey, Task<T>> action, CancellationToken token) {
            var key = await GetDeviceKeyAsync(token);
            try {
                return await action(key);
            } catch (TapoDeviceTokenExpiredOrInvalidException) {
                deviceKey = null;
                key = await GetDeviceKeyAsync(token);
                return await action(key);
            }
        }

        private async Task WithRetryAsync(Func<TapoDeviceKey, Task> action, CancellationToken token) {
            var key = await GetDeviceKeyAsync(token);
            try {
                await action(key);
            } catch (TapoDeviceTokenExpiredOrInvalidException) {
                deviceKey = null;
                key = await GetDeviceKeyAsync(token);
                await action(key);
            }
        }

        public async Task<bool> IsOnAsync(CancellationToken token = default) {
            var info = await WithRetryAsync(k => client.GetDeviceInfoAsync(k), token);
            return info.DeviceOn;
        }

        public Task TurnOnAsync(CancellationToken token = default) =>
            WithRetryAsync(k => client.SetPowerAsync(k, true), token);

        public Task TurnOffAsync(CancellationToken token = default) =>
            WithRetryAsync(k => client.SetPowerAsync(k, false), token);

        // No LED-related field found anywhere in DeviceGetInfoResult (the KLAP device-info DTO) as of
        // TapoConnect 3.2.4 - re-verify against the real P115/KP125M JSON once hardware is available,
        // in case a field exists that this library doesn't surface.
        public Task<bool> SupportsLedAsync(CancellationToken token = default) => Task.FromResult(false);

        public Task SetLedAsync(bool on, CancellationToken token = default) =>
            throw new NotSupportedException("LED control is not supported for KLAP-protocol devices.");

        public Task<bool?> IsLedOnAsync(CancellationToken token = default) => Task.FromResult((bool?)null);

        public async Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default) {
            if (energyMonitoringSupported == false) {
                return null;
            }
            try {
                var usage = await WithRetryAsync(k => client.GetEnergyUsageAsync(k), token);
                energyMonitoringSupported = true;
                return new PlugPowerReading {
                    Watts = usage.CurrentPower / 1000.0,
                    Volts = 0,
                    Amps = 0
                };
            } catch (TapoException) {
                // Not every KLAP-family model has an energy meter (e.g. the P115 does, some others don't).
                energyMonitoringSupported = false;
                return null;
            }
        }

        public ValueTask DisposeAsync() => default;
    }
}
