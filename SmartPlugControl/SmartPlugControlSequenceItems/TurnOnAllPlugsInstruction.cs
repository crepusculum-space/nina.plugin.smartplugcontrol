using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn All Plugs On")]
    [ExportMetadata("Description", "Turns on every discovered smart plug, with an optional delay afterwards.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnOnAllPlugsInstruction : SequenceItem {
        private readonly IPlugRegistryService registry;

        [ImportingConstructor]
        public TurnOnAllPlugsInstruction(IPlugRegistryService registry) {
            this.registry = registry;
        }

        public TurnOnAllPlugsInstruction(TurnOnAllPlugsInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            DelayAfterSeconds = copyMe.DelayAfterSeconds;
        }

        [JsonProperty]
        public int DelayAfterSeconds { get; set; }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await registry.TurnOnAllAsync(token);
            if (DelayAfterSeconds > 0) {
                await Task.Delay(TimeSpan.FromSeconds(DelayAfterSeconds), token);
            }
        }

        public override object Clone() => new TurnOnAllPlugsInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnOnAllPlugsInstruction)}, Delay: {DelayAfterSeconds}s";
    }
}
