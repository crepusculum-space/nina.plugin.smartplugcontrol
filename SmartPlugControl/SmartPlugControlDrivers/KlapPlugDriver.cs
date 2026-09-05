using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Dto;
using TapoConnect.Exceptions;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a Tapo/newer-generation Kasa plug ("SMART.*" device family, KLAP/securePassthrough
    /// protocol) via a direct local-network connection to the device's own IP - these devices have no
    /// cloud-relay path at all (see CLAUDE.md "Architecture history" for why this is still safe for the
    /// multi-tenant threat model: the KLAP handshake itself is gated on the real TP-Link account
    /// credentials stored on the device since pairing).
    ///
    /// Also represents a single outlet of a multi-outlet Tapo power strip (e.g. P300/P316M), when
    /// constructed with a <paramref name="childDeviceId"/> - see <see cref="KlapPlugDriverFactory"/>,
    /// which is what actually decides whether a device needs one instance or several. TapoConnect
    /// itself has no concept of child outlets at all (confirmed by reading its DeviceGetInfoResult DTO
    /// - no children field of any kind); PowerStripKlapDeviceClient adds the missing
    /// get_child_device_list/control_child commands this class needs for that case.
    /// </summary>
    public class KlapPlugDriver : IPlugDriver {
        private readonly ITapoDeviceClient client;
        private readonly PowerStripKlapDeviceClient powerStripClient = new PowerStripKlapDeviceClient();
        private readonly string deviceIp;
        private readonly string username;
        private readonly string password;
        private readonly string childDeviceId;
        private TapoDeviceKey deviceKey;
        private bool? energyMonitoringSupported;

        public string PlugId { get; }
        public string Alias { get; }

        /// <summary>Exposed so PlugRegistryService can tell whether a device's resolved local IP changed since a cached driver was built for it.</summary>
        public string DeviceIp => deviceIp;

        /// <param name="childDeviceId">
        /// Null for a single-outlet device (the common case). For one outlet of a multi-outlet power
        /// strip, the child's own device_id (from get_child_device_list) - every operation is then
        /// wrapped in a control_child envelope targeting this specific outlet, and IsOnAsync reads this
        /// outlet's own entry from the child list rather than the whole device's state.
        /// </param>
        public KlapPlugDriver(string plugId, string alias, string deviceIp, string username, string password, string childDeviceId = null) {
            PlugId = plugId;
            Alias = alias;
            this.deviceIp = deviceIp;
            this.username = username;
            this.password = password;
            this.childDeviceId = childDeviceId;
            client = new TapoDeviceClient();
        }

        private async Task<TapoDeviceKey> GetDeviceKeyAsync(CancellationToken token) {
            if (deviceKey == null) {
                deviceKey = await client.LoginByIpAsync(deviceIp, username, password);
            }
            return deviceKey;
        }

        /// <summary>
        /// Lets KlapPlugDriverFactory hand a child driver the parent's already-established session
        /// instead of making it log in separately - a power strip's outlets all share one physical
        /// device/one KLAP session, so logging in per-outlet would be exactly the kind of redundant
        /// re-authentication CLAUDE.md's gotchas warn about.
        /// </summary>
        internal void AdoptSessionFrom(KlapPlugDriver parent) {
            deviceKey = parent.deviceKey;
        }

        /// <summary>
        /// Probes whether this device has child outlets at all (a power strip) - null/empty for an
        /// ordinary single-outlet device. Only meaningful on a driver constructed without a
        /// childDeviceId (i.e. representing the whole physical device); used by
        /// KlapPlugDriverFactory to decide whether to build one driver or several.
        /// </summary>
        internal Task<List<DeviceGetInfoResult>> GetChildDevicesAsync(CancellationToken token) =>
            WithRetryAsync(k => powerStripClient.GetChildDeviceListAsync(k), token);

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
            if (childDeviceId != null) {
                var children = await WithRetryAsync(k => powerStripClient.GetChildDeviceListAsync(k), token);
                return children.FirstOrDefault(c => c.DeviceId == childDeviceId)?.DeviceOn ?? false;
            }
            var info = await WithRetryAsync(k => client.GetDeviceInfoAsync(k), token);
            return info.DeviceOn;
        }

        public Task TurnOnAsync(CancellationToken token = default) =>
            childDeviceId != null
                ? WithRetryAsync(k => powerStripClient.SetChildPowerAsync(k, childDeviceId, true), token)
                : WithRetryAsync(k => client.SetPowerAsync(k, true), token);

        public Task TurnOffAsync(CancellationToken token = default) =>
            childDeviceId != null
                ? WithRetryAsync(k => powerStripClient.SetChildPowerAsync(k, childDeviceId, false), token)
                : WithRetryAsync(k => client.SetPowerAsync(k, false), token);

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
                // Always attempt a reading, per-outlet if this is a child driver, exactly like
                // KasaCloudPlugDriver already does for legacy Kasa (GetRealtimeEnergyAsync is called
                // with childId regardless of whether the device turns out to support it) - confirmed on
                // a real P316M capture that individual outlets can meter their own consumption
                // separately (each declares its own "energy_monitoring" component), so assuming
                // otherwise here would have been wrong for that model, even though it holds for the
                // base P300 (no energy capability at all, at either the strip or outlet level).
                double watts;
                if (childDeviceId != null) {
                    var usage = await WithRetryAsync(k => powerStripClient.GetChildEnergyUsageAsync(k, childDeviceId), token);
                    watts = usage.CurrentPower / 1000.0;
                    if (watts == 0) {
                        // get_energy_usage's current_power is known to be omitted/unreliable on some
                        // power-strip outlets of this generation - confirmed in python-kasa's own
                        // energy.py: "get_current_power is only a lower precision fallback used by
                        // devices such as P304M whose get_energy_usage omits current_power" (the P304M
                        // and P316M are the same device generation). Fall back to that separate command
                        // (already in whole Watts, no /1000 needed) rather than reporting a flat 0 that
                        // doesn't reflect the real load.
                        try {
                            watts = await WithRetryAsync(k => powerStripClient.GetChildCurrentPowerAsync(k, childDeviceId), token);
                        } catch (TapoException) {
                            // Leave watts at 0 from the get_energy_usage reading - this fallback isn't
                            // supported on this outlet either.
                        }
                    }
                } else {
                    var usage = await WithRetryAsync(k => client.GetEnergyUsageAsync(k), token);
                    watts = usage.CurrentPower / 1000.0;
                }
                energyMonitoringSupported = true;
                return new PlugPowerReading {
                    Watts = watts,
                    Volts = 0,
                    Amps = 0
                };
            } catch (TapoException) {
                // Not every KLAP-family model/outlet has an energy meter (e.g. the P115 does, some
                // others don't, and the base P300's outlets don't either).
                energyMonitoringSupported = false;
                return null;
            }
        }

        public ValueTask DisposeAsync() => default;
    }
}
