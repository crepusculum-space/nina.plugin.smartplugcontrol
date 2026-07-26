using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn LED Off")]
    [ExportMetadata("Description", "Turns off the status LED of a specific smart plug.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnOffLedInstruction : PlugPickerInstructionBase {
        [ImportingConstructor]
        public TurnOffLedInstruction(IPlugRegistryService registry) : base(registry) {
        }

        public TurnOffLedInstruction(TurnOffLedInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
        }

        public override bool Validate() {
            base.Validate();
            if (SelectedPlug != null && !SelectedPlug.SupportsLed) {
                Issues.Add($"'{SelectedPlug.Alias}' does not support LED control.");
            }
            return Issues.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) =>
            registry.SetLedAsync(SelectedPlugId, false, token);

        public override object Clone() => new TurnOffLedInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnOffLedInstruction)}, Plug: {SelectedPlug?.Alias}";
    }
}
