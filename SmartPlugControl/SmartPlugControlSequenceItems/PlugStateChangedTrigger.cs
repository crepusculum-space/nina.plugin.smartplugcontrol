using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [ExportMetadata("Name", "Plug State Changed")]
    [ExportMetadata("Description", "Notifies if the selected plug's on/off state changes unexpectedly during the sequence (e.g. switched via the Kasa app or a physical button by someone else).")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PlugStateChangedTrigger : PlugTriggerBase {
        // Runtime-only baseline, deliberately not persisted - each sequence run starts fresh rather
        // than comparing against whatever the plug's state happened to be the last time NINA ran.
        private bool? lastKnownIsOn;

        [ImportingConstructor]
        public PlugStateChangedTrigger(IPlugRegistryService registry) : base(registry) {
        }

        public PlugStateChangedTrigger(PlugStateChangedTrigger copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        // NINA only checks triggers at instruction boundaries - ShouldTrigger before the next
        // instruction runs, ShouldTriggerAfter once the previous one finishes (never during a
        // single long-running instruction, e.g. a multi-minute Wait). The base SequenceTrigger
        // defaults ShouldTriggerAfter to false, so without this override a sequence with only one
        // instruction (or where this trigger is the last one checked) would never see a state
        // change reported at all - ShouldTrigger only ever fires once, before that lone instruction,
        // to establish the baseline.
        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        private bool CheckShouldTrigger() {
            bool? current = SelectedPlug?.IsOn;
            if (current == null) {
                // Offline or not polled yet - nothing to compare against.
                return false;
            }
            if (lastKnownIsOn == null) {
                // First observation this run - establish the baseline, don't fire on startup.
                lastKnownIsOn = current;
                return false;
            }
            return current != lastKnownIsOn;
        }

        public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            bool? current = SelectedPlug?.IsOn;
            Notification.ShowWarning($"'{SelectedPlug?.Alias}' changed state unexpectedly - now {(current == true ? "ON" : "OFF")}.");
            // Re-sync the baseline so this same change isn't reported again next check.
            lastKnownIsOn = current;
            return Task.CompletedTask;
        }

        public override object Clone() => new PlugStateChangedTrigger(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(PlugStateChangedTrigger)}, Plug: {SelectedPlug?.Alias}";
    }
}
