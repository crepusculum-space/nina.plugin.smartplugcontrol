using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Local-network control surface for a single controllable socket. Kasa and Tapo devices speak
    /// completely different protocols locally, so each brand gets its own implementation behind this
    /// common interface; cloud discovery (see SmartPlugControlCloud) only tells us which brand/model
    /// a device is - actual on/off/LED/power control always happens over the local network.
    /// </summary>
    public interface IPlugDriver : IAsyncDisposable {
        /// <summary>Stable identifier: the cloud DeviceId, suffixed with the socket index for multi-socket devices.</summary>
        string PlugId { get; }

        Task<bool> IsOnAsync(CancellationToken token = default);
        Task TurnOnAsync(CancellationToken token = default);
        Task TurnOffAsync(CancellationToken token = default);

        Task<bool> SupportsLedAsync(CancellationToken token = default);
        Task SetLedAsync(bool on, CancellationToken token = default);

        /// <summary>Returns null if the device/socket does not support energy monitoring.</summary>
        Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default);
    }
}
