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

        /// <summary>Exposed so PlugRegistryService can tell whether a device's resolved local IP changed since a cached driver was built for it.</summary>
        public string DeviceIp => deviceIp;

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

        // A cached KLAP session (deviceKey) is now reused across many refresh cycles instead of being
        // rebuilt every ~10s (see PlugRegistryService/CLAUDE.md), so it can genuinely go stale mid-life
        // - e.g. the device silently rotating/invalidating the session for reasons outside our control
        // (another driver instance re-authenticating against the same physical device, the device's own
        // firmware timing out an idle session, etc). Real hardware testing showed this does NOT always
        // surface as a clean error: a stale session's KLAP cipher is out of sync with the device, so a
        // request can come back encrypted/framed for a session we no longer share - which shows up as
        // TapoConnect trying to decrypt or JSON-parse garbage (CryptographicException: "Padding is
        // invalid", or a JsonException on $.error_code), not just the "named" failure types
        // (TapoDeviceTokenExpiredOrInvalidException, or HttpResponseException Forbidden/BadRequest).
        // So: treat ANY failure of an already-authenticated request (not the login itself, which is
        // outside this try/catch) as a possibly-stale session - drop the cached key and retry once with
        // a fresh login. A genuinely offline/unreachable device just fails the same way again on retry
        // and still surfaces to the caller, at the cost of one extra round-trip.
        private static bool IsStaleSessionError(Exception ex) => !(ex is OperationCanceledException);

        private async Task<T> WithRetryAsync<T>(Func<TapoDeviceKey, Task<T>> action, CancellationToken token) {
            var key = await GetDeviceKeyAsync(token);
            try {
                return await action(key);
            } catch (Exception ex) when (IsStaleSessionError(ex)) {
                deviceKey = null;
                key = await GetDeviceKeyAsync(token);
                return await action(key);
            }
        }

        private async Task WithRetryAsync(Func<TapoDeviceKey, Task> action, CancellationToken token) {
            var key = await GetDeviceKeyAsync(token);
            try {
                await action(key);
            } catch (Exception ex) when (IsStaleSessionError(ex)) {
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
