using System;
using System.Threading;
using System.Threading.Tasks;
using Kasa;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Drives a single socket on a multi-socket Kasa power strip (e.g. HS300, EP40). Several instances
    /// share the same underlying <see cref="IMultiSocketKasaOutlet"/> connection, one per socket; only
    /// the instance created with <paramref name="ownsConnection"/> = true disposes the shared outlet.
    /// </summary>
    /// <remarks>
    /// This library version only exposes whole-device (not per-socket) energy monitoring, so individual
    /// sockets on a strip report as not supporting monitoring. A device-level aggregate reading can be
    /// added later if needed.
    /// </remarks>
    public class KasaMultiSocketPlugDriver : IPlugDriver {
        private readonly IMultiSocketKasaOutlet outlet;
        private readonly int socketId;
        private readonly bool ownsConnection;

        public string PlugId { get; }

        public KasaMultiSocketPlugDriver(string plugId, IMultiSocketKasaOutlet outlet, int socketId, bool ownsConnection) {
            PlugId = plugId;
            this.outlet = outlet;
            this.socketId = socketId;
            this.ownsConnection = ownsConnection;
        }

        public static async Task<IMultiSocketKasaOutlet> ConnectAsync(string hostname, Kasa.Options options = null) {
            var outlet = new MultiSocketKasaOutlet(hostname, options);
            await outlet.Connect();
            return outlet;
        }

        public Task<bool> IsOnAsync(CancellationToken token = default) => outlet.System.IsSocketOn(socketId);

        public Task TurnOnAsync(CancellationToken token = default) => outlet.System.SetSocketOn(socketId, true);

        public Task TurnOffAsync(CancellationToken token = default) => outlet.System.SetSocketOn(socketId, false);

        public Task<bool> SupportsLedAsync(CancellationToken token = default) => Task.FromResult(true);

        public Task SetLedAsync(bool on, CancellationToken token = default) => outlet.System.SetIndicatorLightOn(on);

        public Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default) => Task.FromResult<PlugPowerReading>(null);

        public ValueTask DisposeAsync() {
            if (ownsConnection) {
                outlet.Dispose();
            }
            return default;
        }
    }
}
