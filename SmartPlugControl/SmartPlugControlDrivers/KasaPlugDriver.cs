using Kasa;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a single-socket Kasa outlet (e.g. HS103, KP115, EP10) over the local network.
    /// </summary>
    public class KasaPlugDriver : IPlugDriver {
        private readonly IKasaOutlet outlet;
        private bool? energyMeterSupported;

        public string PlugId { get; }

        public KasaPlugDriver(string plugId, string hostname, Kasa.Options options = null) {
            PlugId = plugId;
            outlet = new KasaOutlet(hostname, options);
        }

        /// <summary>Used by tests to inject a fake outlet.</summary>
        internal KasaPlugDriver(string plugId, IKasaOutlet outlet) {
            PlugId = plugId;
            this.outlet = outlet;
        }

        public Task<bool> IsOnAsync(CancellationToken token = default) => outlet.System.IsSocketOn();

        public Task TurnOnAsync(CancellationToken token = default) => outlet.System.SetSocketOn(true);

        public Task TurnOffAsync(CancellationToken token = default) => outlet.System.SetSocketOn(false);

        public Task<bool> SupportsLedAsync(CancellationToken token = default) => Task.FromResult(true);

        public Task SetLedAsync(bool on, CancellationToken token = default) => outlet.System.SetIndicatorLightOn(on);

        public async Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default) {
            if (energyMeterSupported == false) {
                return null;
            }

            if (energyMeterSupported == null) {
                var info = await outlet.System.GetInfo();
                energyMeterSupported = info.Features.Contains(Feature.EnergyMeter);
                if (!energyMeterSupported.Value) {
                    return null;
                }
            }

            var usage = await outlet.EnergyMeter.GetInstantaneousPowerUsage();
            return new PlugPowerReading {
                Watts = usage.Power / 1000.0,
                Volts = usage.Voltage / 1000.0,
                Amps = usage.Current / 1000.0
            };
        }

        public ValueTask DisposeAsync() {
            outlet.Dispose();
            return default;
        }
    }
}
