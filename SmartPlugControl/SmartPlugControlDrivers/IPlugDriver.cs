using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Control surface for a single controllable socket. Two implementations, chosen by the device's
    /// own protocol rather than user choice (see CLAUDE.md "Architecture history"): legacy-protocol Kasa
    /// devices ("IOT.*") go through the TP-Link cloud relay (KasaCloudPlugDriver, see
    /// KasaCloudPassthroughClient) since that protocol has no local authentication; Tapo and
    /// newer-generation Kasa devices ("SMART.*", KLAP/securePassthrough) are controlled directly over
    /// the local network (KlapPlugDriver), since that protocol's handshake is itself gated on the real
    /// TP-Link account credentials stored on the device.
    /// </summary>
    public interface IPlugDriver : IAsyncDisposable {
        /// <summary>Stable identifier: the cloud DeviceId, suffixed with the socket index for multi-socket devices.</summary>
        string PlugId { get; }

        /// <summary>
        /// The user-facing name for this specific socket - e.g. each outlet on a Kasa power strip has
        /// its own alias, distinct from the strip's own (usually unnamed) device-level alias.
        /// </summary>
        string Alias { get; }

        Task<bool> IsOnAsync(CancellationToken token = default);
        Task TurnOnAsync(CancellationToken token = default);
        Task TurnOffAsync(CancellationToken token = default);

        Task<bool> SupportsLedAsync(CancellationToken token = default);
        Task SetLedAsync(bool on, CancellationToken token = default);

        /// <summary>Null if the LED isn't supported, or its current state can't be determined (e.g. some power-strip sockets only report LED state at the whole-strip level).</summary>
        Task<bool?> IsLedOnAsync(CancellationToken token = default);

        /// <summary>Returns null if the device/socket does not support energy monitoring.</summary>
        Task<PlugPowerReading> GetPowerAsync(CancellationToken token = default);
    }
}
