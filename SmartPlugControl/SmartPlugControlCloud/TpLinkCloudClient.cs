using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Util;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// Wraps TapoConnect's TapoCloudClient, which talks to the shared TP-Link cloud endpoint
    /// (wap.tplinkcloud.com) used by both the Kasa and Tapo apps. A single TP-Link ID login
    /// returns every device on the account regardless of brand; devices are told apart
    /// afterwards by DeviceType (TapoUtils.IsTapoDevice - anything else is treated as Kasa).
    /// </summary>
    public class TpLinkCloudClient : ITpLinkCloudClient {
        private readonly ITapoCloudClient cloudClient;

        public TpLinkCloudClient() : this(new TapoCloudClient()) {
        }

        public TpLinkCloudClient(ITapoCloudClient cloudClient) {
            this.cloudClient = cloudClient;
        }

        public async Task<IReadOnlyList<CloudPlugDeviceInfo>> DiscoverDevicesAsync(string username, string password, CancellationToken token = default) {
            var login = await cloudClient.LoginAsync(username, password);
            token.ThrowIfCancellationRequested();

            var devices = await cloudClient.ListDevicesAsync(login.Token);
            token.ThrowIfCancellationRequested();

            return devices.DeviceList
                .Select(d => new CloudPlugDeviceInfo {
                    DeviceId = d.DeviceId,
                    Alias = d.Alias,
                    DeviceType = d.DeviceType,
                    DeviceModel = d.DeviceModel,
                    DeviceMac = d.DeviceMac,
                    Brand = TapoUtils.IsTapoDevice(d.DeviceType) ? PlugBrand.Tapo : PlugBrand.Kasa,
                    IsOnline = d.Status == 1
                })
                .ToList();
        }
    }
}
