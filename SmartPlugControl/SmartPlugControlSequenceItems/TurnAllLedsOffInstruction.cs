using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn All LEDs Off")]
    [ExportMetadata("Description", "Turns off the status LED of every plug that supports it.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnAllLedsOffInstruction : SequenceItem {
        private readonly IPlugRegistryService registry;

        [ImportingConstructor]
        public TurnAllLedsOffInstruction(IPlugRegistryService registry) {
            this.registry = registry;
        }

        public TurnAllLedsOffInstruction(TurnAllLedsOffInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) =>
            registry.SetAllLedsAsync(false, token);

        public override object Clone() => new TurnAllLedsOffInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnAllLedsOffInstruction)}";
    }
}
