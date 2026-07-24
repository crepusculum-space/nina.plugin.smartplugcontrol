using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    public interface ITpLinkCloudClient {
        /// <summary>
        /// Logs in once with a single TP-Link ID and returns every device linked to the account,
        /// Kasa and Tapo alike - the cloud API serves both brands from the same account/endpoint.
        /// </summary>
        Task<IReadOnlyList<CloudPlugDeviceInfo>> DiscoverDevicesAsync(string username, string password, CancellationToken token = default);
    }
}
