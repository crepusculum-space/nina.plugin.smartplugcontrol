using System.Threading;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Exceptions;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a single Tapo plug (e.g. P100, P110) over the local network via the KLAP/secure-passthrough
    /// protocol. TapoConnect does not currently support power strips or LED indicator control for plugs.
    /// </summary>
    public class TapoPlugDriver : IPlugDriver {
        private readonly ITapoDeviceClient deviceClient;
        private readonly string ipAddress;
        private readonly string username;
        private readonly string password;

        private TapoDeviceKey key;
        private bool? energyMonitoringSupported;

        public string PlugId { get; }

        public TapoPlugDriver(string plugId, string ipAddress, string username, string password)
            : this(plugId, ipAddress, username, password, new TapoDeviceClient()) {
        }

        internal TapoPlugDriver(string plugId, string ipAddress, string username, string password, ITapoDeviceClient deviceClient) {
            PlugId = plugId;
            this.ipAddress = ipAddress;
            this.username = username;
            this.password = password;
            this.deviceClient = deviceClient;
        }

        private async Task<TapoDeviceKey> GetKeyAsync() {
            if (key == null) {
                key = await deviceClient.LoginByIpAsync(ipAddress, username, password);
            }
            return key;
        }

        private async Task<T> WithRetryAsync<T>(System.Func<TapoDeviceKey, Task<T>> action) {
            try {
                return await action(await GetKeyAsync());
            } catch (TapoDeviceTokenExpiredOrInvalidException) {
                key = await deviceClient.LoginByIpAsync(ipAddress, username, password);
                return await action(key);
            }
        }

        public async Task<bool> IsOnAsync(CancellationToken token = default) {
            var info = await WithRetryAsync(deviceClient.GetDeviceInfoAsync);
            return info.DeviceOn;
        }

        public Task TurnOnAsync(CancellationToken token = default) => WithRetryAsync(async k => { await deviceClient.SetPowerAsync(k, true); return true; });

        public Task TurnOffAsync(CancellationToken token = default) => WithRetryAsync(async k => { await deviceClient.SetPowerAsync(k, false); return true; });

        public Task<bool> SupportsLedAsync(CancellationToken token = default) => Task.FromResult(false);

        public Task SetLedAsync(bool on, CancellationToken token = default) =>
            throw new System.NotSupportedException("LED control is not supported for Tapo devices by this driver.");

        public async Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default) {
            if (energyMonitoringSupported == false) {
                return null;
            }

            try {
                var usage = await WithRetryAsync(deviceClient.GetEnergyUsageAsync);
                energyMonitoringSupported = true;
                return new PlugPowerReading {
                    Watts = usage.CurrentPower / 1000.0,
                    Volts = 0,
                    Amps = 0
                };
            } catch (TapoException) {
                // Not every Tapo model reports energy usage (e.g. plain P100); treat any protocol-level
                // rejection of the request as "unsupported" rather than retrying forever.
                energyMonitoringSupported = false;
                return null;
            }
        }

        public ValueTask DisposeAsync() => default;
    }
}
