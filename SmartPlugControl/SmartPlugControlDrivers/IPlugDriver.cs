using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Cloud-relayed control surface for a single controllable socket - commands always go through
    /// TP-Link's cloud API (see KasaCloudPassthroughClient), never a direct local-network connection
    /// to the device (see CLAUDE.md for why). Only legacy-protocol Kasa devices (KasaCloudPlugDriver)
    /// have an implementation: Tapo and newer-generation "SMART.*"-protocol Kasa devices use a
    /// different protocol (KLAP/securePassthrough) that has no cloud-relay path at all - only direct
    /// local-IP access - so they will never get an implementation of this interface.
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
