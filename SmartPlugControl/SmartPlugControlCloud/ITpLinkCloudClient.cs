using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    public interface ITpLinkCloudClient {
        /// <summary>Logs in with a single TP-Link ID and returns the cloud session token, needed separately for device control.</summary>
        Task<string> LoginAsync(string username, string password, CancellationToken token = default);

        /// <summary>Returns every device linked to the account, Kasa and Tapo alike - the cloud API serves both brands from the same account/endpoint.</summary>
        Task<IReadOnlyList<CloudPlugDeviceInfo>> ListDevicesAsync(string cloudToken, CancellationToken token = default);
    }
}
