using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn Plug On")]
    [ExportMetadata("Description", "Turns on a specific smart plug, with an optional delay afterwards.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnOnPlugInstruction : PlugPickerInstructionBase {
        [ImportingConstructor]
        public TurnOnPlugInstruction(IPlugRegistryService registry) : base(registry) {
        }

        public TurnOnPlugInstruction(TurnOnPlugInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
            DelayAfterSeconds = copyMe.DelayAfterSeconds;
        }

        [JsonProperty]
        public int DelayAfterSeconds { get; set; }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await registry.TurnOnAsync(SelectedPlugId, token);
            if (DelayAfterSeconds > 0) {
                await Task.Delay(TimeSpan.FromSeconds(DelayAfterSeconds), token);
            }
        }

        public override object Clone() => new TurnOnPlugInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnOnPlugInstruction)}, Plug: {SelectedPlug?.Alias}, Delay: {DelayAfterSeconds}s";
    }
}
