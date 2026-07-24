using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a Kasa plug/socket entirely through the TP-Link cloud relay (see
    /// KasaCloudPassthroughClient) - no local network connection to the device is ever made.
    /// </summary>
    public class KasaCloudPlugDriver : IPlugDriver {
        private readonly KasaCloudPassthroughClient client;
        private readonly string appServerUrl;
        private readonly string cloudToken;
        private readonly string deviceId;
        private readonly string childId;
        private bool? energyMonitoringSupported;

        public string PlugId { get; }
        public string Alias { get; }

        public KasaCloudPlugDriver(string plugId, string alias, KasaCloudPassthroughClient client, string appServerUrl, string cloudToken, string deviceId, string childId = null) {
            PlugId = plugId;
            Alias = alias;
            this.client = client;
            this.appServerUrl = appServerUrl;
            this.cloudToken = cloudToken;
            this.deviceId = deviceId;
            this.childId = childId;
        }

        public async Task<bool> IsOnAsync(CancellationToken token = default) {
            var sysInfo = await client.GetSysInfoAsync(appServerUrl, cloudToken, deviceId, childId, token);
            // Power-strip children report "state" (0/1); single-socket devices report "relay_state".
            int? state = sysInfo.Value<int?>("state") ?? sysInfo.Value<int?>("relay_state");
            return state == 1;
        }

        public Task TurnOnAsync(CancellationToken token = default) =>
            client.SetRelayStateAsync(appServerUrl, cloudToken, deviceId, childId, true, token);

        public Task TurnOffAsync(CancellationToken token = default) =>
            client.SetRelayStateAsync(appServerUrl, cloudToken, deviceId, childId, false, token);

        public Task<bool> SupportsLedAsync(CancellationToken token = default) => Task.FromResult(true);

        public Task SetLedAsync(bool on, CancellationToken token = default) =>
            client.SetLedOffAsync(appServerUrl, cloudToken, deviceId, childId, on, token);

        public async Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default) {
            if (energyMonitoringSupported == false) {
                return null;
            }
            try {
                var usage = await client.GetRealtimeEnergyAsync(appServerUrl, cloudToken, deviceId, childId, token);
                energyMonitoringSupported = true;
                return new PlugPowerReading {
                    Watts = ReadScaled(usage, "power_mw", "power"),
                    Volts = ReadScaled(usage, "voltage_mv", "voltage"),
                    Amps = ReadScaled(usage, "current_ma", "current")
                };
            } catch (KasaCloudException) {
                // Older/cheaper models (e.g. HS103, KP303) have no energy meter at all.
                energyMonitoringSupported = false;
                return null;
            }
        }

        /// <summary>
        /// Newer Kasa firmware reports milli-units (power_mw/voltage_mv/current_ma); older firmware
        /// (e.g. HS110) reports the same readings already in whole units under the unsuffixed keys.
        /// </summary>
        private static double ReadScaled(JObject usage, string milliUnitKey, string wholeUnitKey) {
            double? milliValue = usage.Value<double?>(milliUnitKey);
            if (milliValue.HasValue) {
                return milliValue.Value / 1000.0;
            }
            return usage.Value<double?>(wholeUnitKey) ?? 0;
        }

        public ValueTask DisposeAsync() => default;
    }
}
