using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    public interface IPlugRegistryService {
        /// <summary>Only plugs the user has left visible (see SetVisibleInNina) - what the equipment page/sequencer see.</summary>
        IReadOnlyList<PlugViewModel> Plugs { get; }

        /// <summary>Every discovered plug regardless of visibility - for the plug-visibility management UI only.</summary>
        IReadOnlyList<PlugViewModel> AllPlugs { get; }

        /// <summary>Re-runs cloud discovery, re-resolves local drivers, and polls live state. Always
        /// makes a network attempt, regardless of past failures - use this for an explicit user
        /// action (e.g. a "Refresh Plug List" button).</summary>
        Task RefreshAsync(CancellationToken token = default);

        /// <summary>
        /// Same as RefreshAsync, but with isBackgroundPoll=true: after a login failure, backs off for
        /// a cooldown before automatically retrying, instead of retrying every call. Use this for an
        /// automatic poll loop, so it doesn't hammer a failing login every cycle - both to avoid
        /// spamming an error notification repeatedly and to avoid tripping TP-Link's own login rate
        /// limit from the repeated attempts. isBackgroundPoll=false (the parameterless overload) is
        /// never subject to this cooldown - always attempts immediately.
        /// </summary>
        Task RefreshAsync(bool isBackgroundPoll, CancellationToken token = default);

        void SetEquipmentName(string plugId, string equipmentName);
        void SetProtected(string plugId, bool isProtected);

        /// <summary>Per-device consumption-alert configuration (see PlugPersistedData) - invariant properties of whatever's plugged in, so set once here rather than per sequence item.</summary>
        void SetMaxAmpsAt12V(string plugId, double amps);
        void SetPsuEfficiencyPercent(string plugId, int percent);

        /// <summary>Opts a plug in/out of the equipment page/sequencer (the TP-Link account may have plugs unrelated to the observatory).</summary>
        void SetVisibleInNina(string plugId, bool visible);

        /// <summary>
        /// Single-plug actions. TurnOffAsync deliberately does NOT check IsProtected itself - callers
        /// with a human in the loop (the equipment page) are expected to confirm first; a sequencer
        /// instruction (later phase) should refuse to call this at all for a protected plug instead.
        /// </summary>
        Task TurnOnAsync(string plugId, CancellationToken token = default);
        Task TurnOffAsync(string plugId, CancellationToken token = default);
        Task SetLedAsync(string plugId, bool on, CancellationToken token = default);

        /// <summary>Bulk actions. TurnOffAllAsync silently skips protected plugs.</summary>
        Task TurnOnAllAsync(CancellationToken token = default);
        Task TurnOffAllAsync(CancellationToken token = default);
        Task SetAllLedsAsync(bool on, CancellationToken token = default);
    }
}
