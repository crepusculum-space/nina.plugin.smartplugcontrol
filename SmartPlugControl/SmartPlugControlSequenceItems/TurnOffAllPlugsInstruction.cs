using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Turn Off All Plugs")]
    [ExportMetadata("Description", "Turns off every discovered smart plug except those marked as protected.")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TurnOffAllPlugsInstruction : SequenceItem {
        private readonly IPlugRegistryService registry;

        [ImportingConstructor]
        public TurnOffAllPlugsInstruction(IPlugRegistryService registry) {
            this.registry = registry;
        }

        public TurnOffAllPlugsInstruction(TurnOffAllPlugsInstruction copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) =>
            registry.TurnOffAllAsync(token);

        public override object Clone() => new TurnOffAllPlugsInstruction(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(TurnOffAllPlugsInstruction)}";
    }
}
