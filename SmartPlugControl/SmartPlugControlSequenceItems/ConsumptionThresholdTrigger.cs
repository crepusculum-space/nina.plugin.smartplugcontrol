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
    // Mirrors PlugStateChangedTrigger (same baseline/transition/notify-only pattern). Watches a single
    // plug (picked here, via PlugTriggerBase - its Validate() already requires a plug be selected)
    // against a preventive alert percentage (also picked here, since different sections of the same
    // sequence may want a different tolerance for the same plug, e.g. more tolerant during a brief
    // flats routine) of that plug's own max threshold - MaxAmpsAt12V/PsuEfficiencyPercent, configured
    // once on the equipment page since they're essentially invariant properties of whatever's plugged
    // in, not something to re-enter here.
    [ExportMetadata("Name", "Consumption Threshold Changed")]
    [ExportMetadata("Description", "Notifies when the selected plug's power draw crosses its preventive alert percentage of its configured max threshold (set on the equipment page).")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ConsumptionThresholdTrigger : PlugTriggerBase {
        // Runtime-only baseline, deliberately not persisted - each sequence run starts fresh rather
        // than comparing against whatever the reading happened to be the last time NINA ran.
        private bool? wasOverThreshold;

        [JsonProperty]
        public int PreventiveAlertPercent { get; set; } = 80;

        [ImportingConstructor]
        public ConsumptionThresholdTrigger(IPlugRegistryService registry) : base(registry) {
        }

        public ConsumptionThresholdTrigger(ConsumptionThresholdTrigger copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
            CopyPickerMetaData(copyMe);
            PreventiveAlertPercent = copyMe.PreventiveAlertPercent;
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        // See PlugStateChangedTrigger for why both ShouldTrigger and ShouldTriggerAfter are needed -
        // NINA only checks triggers at instruction boundaries, never during a single long-running one.
        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        private bool CheckShouldTrigger() {
            double? preventiveWatts = GetPreventiveWatts();
            if (preventiveWatts == null) {
                // No max threshold configured for this plug (see equipment page) - nothing to compare.
                return false;
            }

            bool isOver = GetCurrentWatts() >= preventiveWatts.Value;

            if (wasOverThreshold == null) {
                // First observation this run - establish the baseline, don't fire on startup.
                wasOverThreshold = isOver;
                return false;
            }

            return isOver != wasOverThreshold;
        }

        private double? GetPreventiveWatts() => SelectedPlug?.MaxThresholdWatts * (PreventiveAlertPercent / 100.0);

        private double GetCurrentWatts() => SelectedPlug?.LastPower?.Watts ?? 0;

        public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var plug = SelectedPlug;
            double maxAmps = plug?.MaxAmpsAt12V ?? 0;
            int psuEfficiencyPercent = plug?.PsuEfficiencyPercent ?? 100;
            double currentWatts = GetCurrentWatts();
            double? preventiveWatts = GetPreventiveWatts();

            // Amps here are always estimates derived from the measured AC-side Watts (see
            // PlugViewModel.MaxThresholdWatts) - the plug itself only ever measures Watts. The
            // preventive Amps figure doesn't need that conversion though: since it's a fixed percentage
            // of maxAmps, the PSU efficiency estimate cancels out of that particular ratio exactly.
            double preventiveAmps = maxAmps * PreventiveAlertPercent / 100.0;
            double currentAmpsEstimate = currentWatts * (psuEfficiencyPercent / 100.0) / 12.0;

            bool isOver = preventiveWatts != null && currentWatts >= preventiveWatts.Value;

            if (isOver) {
                Notification.ShowWarning($"'{plug?.DisplayName}' is drawing an estimated {currentAmpsEstimate:F2} A - at or above your preventive alert level ({preventiveAmps:F2} A, {PreventiveAlertPercent}% of {maxAmps:F1} A).");
            } else {
                Notification.ShowInformation($"'{plug?.DisplayName}' is back down to an estimated {currentAmpsEstimate:F2} A, below the preventive alert level ({preventiveAmps:F2} A).");
            }

            // Re-sync the baseline so this same change isn't reported again next check.
            wasOverThreshold = isOver;
            return Task.CompletedTask;
        }

        public override object Clone() => new ConsumptionThresholdTrigger(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ConsumptionThresholdTrigger)}, SelectedPlugId: {SelectedPlugId}, PreventiveAlertPercent: {PreventiveAlertPercent}";
    }
}
