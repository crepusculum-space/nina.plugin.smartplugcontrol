using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Consumption Above")]
    [ExportMetadata("Description", "True as long as the selected plug's power draw stays above its preventive alert percentage of its configured max threshold (set on the equipment page).")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TotalConsumptionAboveCondition : ConsumptionThresholdConditionBase {
        [ImportingConstructor]
        public TotalConsumptionAboveCondition(IPlugRegistryService registry) : base(registry) {
        }

        public TotalConsumptionAboveCondition(TotalConsumptionAboveCondition copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyThresholdMetaData(copyMe);
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            double? preventiveWatts = GetPreventiveWatts();
            return preventiveWatts != null && GetWatts() > preventiveWatts.Value;
        }

        public override object Clone() => new TotalConsumptionAboveCondition(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TotalConsumptionAboveCondition)}, SelectedPlugId: {SelectedPlugId}, PreventiveAlertPercent: {PreventiveAlertPercent}";
    }
}
