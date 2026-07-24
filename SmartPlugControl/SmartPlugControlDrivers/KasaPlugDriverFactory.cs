using Kasa;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// A Kasa cloud device can be a single-socket outlet or a multi-socket power strip; this is only
    /// knowable by asking the device itself. This factory probes the device once and returns one
    /// <see cref="IPlugDriver"/> per physical socket, with a stable PlugId per socket.
    /// </summary>
    public static class KasaPlugDriverFactory {
        public static async Task<IReadOnlyList<IPlugDriver>> CreateAsync(string cloudDeviceId, string hostname, Kasa.Options options = null) {
            using var probe = new KasaOutlet(hostname, options);
            int socketCount = await probe.System.CountSockets();

            if (socketCount <= 1) {
                return new List<IPlugDriver> {
                    new KasaPlugDriver(cloudDeviceId, hostname, options)
                };
            }

            var sharedOutlet = await KasaMultiSocketPlugDriver.ConnectAsync(hostname, options);
            var drivers = new List<IPlugDriver>(socketCount);
            for (int socketId = 0; socketId < socketCount; socketId++) {
                drivers.Add(new KasaMultiSocketPlugDriver($"{cloudDeviceId}:{socketId}", sharedOutlet, socketId, ownsConnection: socketId == 0));
            }
            return drivers;
        }
    }
}
