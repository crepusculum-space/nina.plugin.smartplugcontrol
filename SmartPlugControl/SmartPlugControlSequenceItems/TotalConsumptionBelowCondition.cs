using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Total Consumption Below")]
    [ExportMetadata("Description", "True as long as the combined power draw of all monitored plugs stays below the threshold.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TotalConsumptionBelowCondition : ConsumptionThresholdConditionBase {
        [ImportingConstructor]
        public TotalConsumptionBelowCondition(IPlugRegistryService registry) : base(registry) {
        }

        public TotalConsumptionBelowCondition(TotalConsumptionBelowCondition copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyThresholdMetaData(copyMe);
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) => GetTotalWatts() < ThresholdWatts;

        public override object Clone() => new TotalConsumptionBelowCondition(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TotalConsumptionBelowCondition)}, ThresholdWatts: {ThresholdWatts}";
    }
}
