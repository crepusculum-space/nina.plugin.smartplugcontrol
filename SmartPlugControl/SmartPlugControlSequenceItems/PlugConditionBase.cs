using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Validations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    /// <summary>
    /// Shared base for sequencer conditions that check a single plug picked from a ComboBox. Mirrors
    /// PlugPickerInstructionBase - duplicated rather than shared, since C# doesn't allow inheriting
    /// both SequenceItem and SequenceCondition.
    /// </summary>
    public abstract class PlugConditionBase : SequenceCondition, IValidatable {
        protected readonly IPlugRegistryService registry;

        protected PlugConditionBase(IPlugRegistryService registry) {
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

        protected void CopyPickerMetaData(PlugConditionBase copyMe) {
            SelectedPlugId = copyMe.SelectedPlugId;
        }
    }
}
