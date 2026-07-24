using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Plug Is On")]
    [ExportMetadata("Description", "True as long as the selected plug is on.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PlugIsOnCondition : PlugConditionBase {
        [ImportingConstructor]
        public PlugIsOnCondition(IPlugRegistryService registry) : base(registry) {
        }

        public PlugIsOnCondition(PlugIsOnCondition copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) => SelectedPlug?.IsOn == true;

        public override object Clone() => new PlugIsOnCondition(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(PlugIsOnCondition)}, Plug: {SelectedPlug?.Alias}";
    }
}
