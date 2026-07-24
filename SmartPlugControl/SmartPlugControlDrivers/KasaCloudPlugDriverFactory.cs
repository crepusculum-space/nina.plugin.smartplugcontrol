using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// A Kasa cloud device can be a single-socket outlet or a multi-socket power strip; this is only
    /// knowable by asking the device (via cloud relay) for its system info. This factory does that
    /// probe once and returns one <see cref="IPlugDriver"/> per physical socket.
    /// </summary>
    public static class KasaCloudPlugDriverFactory {
        public static async Task<IReadOnlyList<IPlugDriver>> CreateAsync(KasaCloudPassthroughClient client, CloudPlugDeviceInfo device, string cloudToken, CancellationToken token = default) {
            var sysInfo = await client.GetSysInfoAsync(device.AppServerUrl, cloudToken, device.DeviceId, null, token);

            if (sysInfo["children"] is JArray children && children.Count > 0) {
                var drivers = new List<IPlugDriver>(children.Count);
                foreach (var child in children) {
                    string childId = child.Value<string>("id");
                    string childAlias = child.Value<string>("alias") ?? $"{device.Alias} #{childId}";
                    drivers.Add(new KasaCloudPlugDriver($"{device.DeviceId}:{childId}", childAlias, client, device.AppServerUrl, cloudToken, device.DeviceId, childId));
                }
                return drivers;
            }

            return new List<IPlugDriver> {
                new KasaCloudPlugDriver(device.DeviceId, device.Alias, client, device.AppServerUrl, cloudToken, device.DeviceId)
            };
        }
    }
}
