using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using System.Linq;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    /// <summary>
    /// Shared base for conditions comparing the combined consumption of every plug with a known
    /// power reading against a configurable threshold. Plugs that don't support energy monitoring
    /// (most models) simply don't contribute to the total.
    /// </summary>
    public abstract class ConsumptionThresholdConditionBase : SequenceCondition {
        protected readonly IPlugRegistryService registry;

        protected ConsumptionThresholdConditionBase(IPlugRegistryService registry) {
            this.registry = registry;
        }

        [JsonProperty]
        public double ThresholdWatts { get; set; }

        protected double GetTotalWatts() =>
            registry.Plugs.Where(p => p.LastPower != null).Sum(p => p.LastPower.Watts);

        protected void CopyThresholdMetaData(ConsumptionThresholdConditionBase copyMe) {
            ThresholdWatts = copyMe.ThresholdWatts;
        }
    }
}
