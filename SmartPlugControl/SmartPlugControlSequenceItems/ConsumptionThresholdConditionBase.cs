using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Validations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    /// <summary>
    /// Shared base for conditions comparing a single plug's own consumption against a preventive
    /// alert percentage of that plug's configured max threshold (MaxAmpsAt12V/PsuEfficiencyPercent -
    /// set once on the equipment page, since they're essentially invariant properties of whatever's
    /// plugged in there, not something that should be re-entered per sequence item). Only the plug and
    /// the preventive percentage are per-instance, since different sections of the same sequence may
    /// want a different tolerance for the same plug (e.g. more tolerant during a brief flats routine).
    /// </summary>
    public abstract class ConsumptionThresholdConditionBase : SequenceCondition, IValidatable {
        protected readonly IPlugRegistryService registry;

        protected ConsumptionThresholdConditionBase(IPlugRegistryService registry) {
            this.registry = registry;
        }

        [JsonProperty]
        public string SelectedPlugId { get; set; }

        [JsonProperty]
        public int PreventiveAlertPercent { get; set; } = 80;

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
            } else if (SelectedPlug.MaxThresholdWatts == null) {
                Issues.Add($"'{SelectedPlug.DisplayName}' has no max consumption threshold configured - set it on the equipment page (Imaging tab).");
            }
            return Issues.Count == 0;
        }

        /// <summary>Null if the plug isn't configured with a max threshold on the equipment page.</summary>
        protected double? GetPreventiveWatts() => SelectedPlug?.MaxThresholdWatts * (PreventiveAlertPercent / 100.0);

        /// <summary>0 if the plug hasn't reported a power reading (e.g. offline this cycle).</summary>
        protected double GetWatts() => SelectedPlug?.LastPower?.Watts ?? 0;

        protected void CopyThresholdMetaData(ConsumptionThresholdConditionBase copyMe) {
            SelectedPlugId = copyMe.SelectedPlugId;
            PreventiveAlertPercent = copyMe.PreventiveAlertPercent;
        }
    }
}
