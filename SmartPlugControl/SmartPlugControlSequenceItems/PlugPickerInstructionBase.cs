using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    /// <summary>
    /// Shared base for sequence instructions that target a single plug/socket picked from a
    /// ComboBox. Persists only the stable PlugId ([JsonProperty]); the live PlugViewModel list and
    /// selection are re-resolved against IPlugRegistryService on demand ([JsonIgnore]), so there is
    /// no stale-reference-after-deserialize problem to solve.
    /// </summary>
    public abstract class PlugPickerInstructionBase : SequenceItem, IValidatable {
        protected readonly IPlugRegistryService registry;

        protected PlugPickerInstructionBase(IPlugRegistryService registry) {
            this.registry = registry;
        }

        [JsonProperty]
        public string SelectedPlugId { get; set; }

        [JsonIgnore]
        public IReadOnlyList<PlugViewModel> AvailablePlugs => registry.Plugs;

        [JsonIgnore]
        public PlugViewModel SelectedPlug {
            get => AvailablePlugs.FirstOrDefault(p => p.PlugId == SelectedPlugId);
            set {
                SelectedPlugId = value?.PlugId;
                RaisePropertyChanged();
            }
        }

        public IList<string> Issues { get; } = new ObservableCollection<string>();

        public virtual bool Validate() {
            Issues.Clear();
            if (SelectedPlug == null) {
                Issues.Add("No plug selected.");
            }
            return Issues.Count == 0;
        }

        protected void CopyPickerMetaData(PlugPickerInstructionBase copyMe) {
            SelectedPlugId = copyMe.SelectedPlugId;
        }
    }
}
