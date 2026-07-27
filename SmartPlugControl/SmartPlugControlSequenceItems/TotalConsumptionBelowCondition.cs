using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Consumption Below")]
    [ExportMetadata("Description", "True as long as the selected plug's power draw stays below its preventive alert percentage of its configured max threshold (set on the equipment page).")]
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

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            double? preventiveWatts = GetPreventiveWatts();
            return preventiveWatts != null && GetWatts() < preventiveWatts.Value;
        }

        public override object Clone() => new TotalConsumptionBelowCondition(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TotalConsumptionBelowCondition)}, SelectedPlugId: {SelectedPlugId}, PreventiveAlertPercent: {PreventiveAlertPercent}";
    }
}
