using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Plug Is Off")]
    [ExportMetadata("Description", "True as long as the selected plug is off.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PlugIsOffCondition : PlugConditionBase {
        [ImportingConstructor]
        public PlugIsOffCondition(IPlugRegistryService registry) : base(registry) {
        }

        public PlugIsOffCondition(PlugIsOffCondition copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) => SelectedPlug?.IsOn == false;

        public override object Clone() => new PlugIsOffCondition(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(PlugIsOffCondition)}, Plug: {SelectedPlug?.Alias}";
    }
}
