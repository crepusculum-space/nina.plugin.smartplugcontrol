using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartPlugControlTests.Fakes {
    /// <summary>Minimal IPlugRegistryService fake for testing - only tracks how many times RefreshAsync was called.</summary>
    public class FakePlugRegistryService : IPlugRegistryService {
        public int RefreshCallCount { get; private set; }

        public IReadOnlyList<PlugViewModel> Plugs { get; } = new List<PlugViewModel>();
        public IReadOnlyList<PlugViewModel> AllPlugs { get; } = new List<PlugViewModel>();

        public Task RefreshAsync(CancellationToken token = default) {
            RefreshCallCount++;
            return Task.CompletedTask;
        }

        public void SetEquipmentName(string plugId, string equipmentName) { }
        public void SetProtected(string plugId, bool isProtected) { }
        public void SetMaxAmpsAt12V(string plugId, double amps) { }
        public void SetPsuEfficiencyPercent(string plugId, int percent) { }
        public void SetVisibleInNina(string plugId, bool visible) { }

        public Task TurnOnAsync(string plugId, CancellationToken token = default) => Task.CompletedTask;
        public Task TurnOffAsync(string plugId, CancellationToken token = default) => Task.CompletedTask;
        public Task SetLedAsync(string plugId, bool on, CancellationToken token = default) => Task.CompletedTask;

        public Task TurnOnAllAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task TurnOffAllAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task SetAllLedsAsync(bool on, CancellationToken token = default) => Task.CompletedTask;
    }
}
