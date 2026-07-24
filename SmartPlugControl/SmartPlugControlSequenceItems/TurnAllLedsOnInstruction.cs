using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn All LEDs On")]
    [ExportMetadata("Description", "Turns on the status LED of every plug that supports it.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnAllLedsOnInstruction : SequenceItem {
        private readonly IPlugRegistryService registry;

        [ImportingConstructor]
        public TurnAllLedsOnInstruction(IPlugRegistryService registry) {
            this.registry = registry;
        }

        public TurnAllLedsOnInstruction(TurnAllLedsOnInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) =>
            registry.SetAllLedsAsync(true, token);

        public override object Clone() => new TurnAllLedsOnInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnAllLedsOnInstruction)}";
    }
}
