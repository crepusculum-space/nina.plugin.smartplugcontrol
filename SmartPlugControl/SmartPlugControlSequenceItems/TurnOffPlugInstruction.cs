using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn Plug Off")]
    [ExportMetadata("Description", "Turns off a specific smart plug. Blocked for plugs marked as protected.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnOffPlugInstruction : PlugPickerInstructionBase {
        [ImportingConstructor]
        public TurnOffPlugInstruction(IPlugRegistryService registry) : base(registry) {
        }

        public TurnOffPlugInstruction(TurnOffPlugInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
        }

        // Hard block: unlike the equipment page (which allows turning off a protected plug after a
        // human confirms twice), the sequencer must refuse outright - there is no one watching to
        // confirm mid-sequence.
        public override bool Validate() {
            base.Validate();
            if (SelectedPlug != null && SelectedPlug.IsProtected) {
                Issues.Add($"'{SelectedPlug.Alias}' is marked as protected - turning it off from the sequencer is blocked. Use the equipment page if you really need to.");
            }
            return Issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Defense in depth - Validate() should already have kept this from running.
            if (SelectedPlug != null && SelectedPlug.IsProtected) {
                throw new InvalidOperationException($"Refusing to turn off protected plug '{SelectedPlug.Alias}'.");
            }
            await registry.TurnOffAsync(SelectedPlugId, token);
        }

        public override object Clone() => new TurnOffPlugInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnOffPlugInstruction)}, Plug: {SelectedPlug?.Alias}";
    }
}
