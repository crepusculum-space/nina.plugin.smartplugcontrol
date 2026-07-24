using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    public interface IPlugRegistryService {
        /// <summary>Only plugs the user has left visible (see SetVisibleInNina) - what the equipment page/sequencer see.</summary>
        IReadOnlyList<PlugViewModel> Plugs { get; }

        /// <summary>Every discovered plug regardless of visibility - for the plug-visibility management UI only.</summary>
        IReadOnlyList<PlugViewModel> AllPlugs { get; }

        /// <summary>Re-runs cloud discovery, re-resolves local drivers, and polls live state.</summary>
        Task RefreshAsync(CancellationToken token = default);

        void SetEquipmentName(string plugId, string equipmentName);
        void SetProtected(string plugId, bool isProtected);

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
