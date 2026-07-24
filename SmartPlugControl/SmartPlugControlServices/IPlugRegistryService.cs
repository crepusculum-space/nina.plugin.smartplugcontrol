using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    public interface IPlugRegistryService {
        IReadOnlyList<PlugViewModel> Plugs { get; }

        /// <summary>Re-runs cloud discovery, re-resolves local drivers, and polls live state.</summary>
        Task RefreshAsync(CancellationToken token = default);

        void SetEquipmentName(string plugId, string equipmentName);
        void SetProtected(string plugId, bool isProtected);
    }
}
