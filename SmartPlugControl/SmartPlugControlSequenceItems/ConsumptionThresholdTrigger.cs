using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Settings = Crepusculum.NINA.SmartPlugControl.Properties.Settings;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    // Mirrors PlugStateChangedTrigger exactly (same baseline/transition/notify-only pattern) - no
    // per-instance configuration needed since it reads the global MaxConsumptionThresholdWatts/
    // PreventiveAlertPercent settings (Options page) rather than a plug/threshold picked here.
    [ExportMetadata("Name", "Consumption Threshold Changed")]
    [ExportMetadata("Description", "Notifies when total consumption across all monitored plugs crosses the preventive alert percentage of the configured maximum (see Options).")]
    [ExportMetadata("Icon", "Crepusculum.NINA.SmartPlugControl_SequenceItemSVG")]
    [ExportMetadata("Category", "Smart Plug Control")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ConsumptionThresholdTrigger : SequenceTrigger {
        private readonly IPlugRegistryService registry;

        // Runtime-only baseline, deliberately not persisted - each sequence run starts fresh rather
        // than comparing against whatever the reading happened to be the last time NINA ran.
        private bool? wasOverThreshold;

        [ImportingConstructor]
        public ConsumptionThresholdTrigger(IPlugRegistryService registry) {
            this.registry = registry;
        }

        public ConsumptionThresholdTrigger(ConsumptionThresholdTrigger copyMe) : this(copyMe.registry) {
            CopyMetaData(copyMe);
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        // See PlugStateChangedTrigger for why both ShouldTrigger and ShouldTriggerAfter are needed -
        // NINA only checks triggers at instruction boundaries, never during a single long-running one.
        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            return CheckShouldTrigger();
        }

        private double GetTotalWatts() =>
            registry.Plugs.Where(p => p.LastPower != null).Sum(p => p.LastPower.Watts);

        private bool CheckShouldTrigger() {
            double maxWatts = Settings.Default.MaxConsumptionThresholdWatts;
            if (maxWatts <= 0) {
                // No threshold configured in Options - nothing to compare against.
                return false;
            }

            double preventiveWatts = maxWatts * Settings.Default.PreventiveAlertPercent / 100.0;
            bool isOver = GetTotalWatts() >= preventiveWatts;

            if (wasOverThreshold == null) {
                // First observation this run - establish the baseline, don't fire on startup.
                wasOverThreshold = isOver;
                return false;
            }

            return isOver != wasOverThreshold;
        }

        public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            double totalWatts = GetTotalWatts();
            double maxWatts = Settings.Default.MaxConsumptionThresholdWatts;
            double preventiveWatts = maxWatts * Settings.Default.PreventiveAlertPercent / 100.0;
            bool isOver = totalWatts >= preventiveWatts;

            if (isOver) {
                Notification.ShowWarning($"Total consumption is {totalWatts:F1} W - at or above your preventive alert level ({preventiveWatts:F1} W, {Settings.Default.PreventiveAlertPercent}% of {maxWatts:F1} W).");
            } else {
                Notification.ShowInformation($"Total consumption is back down to {totalWatts:F1} W, below the preventive alert level ({preventiveWatts:F1} W).");
            }

            // Re-sync the baseline so this same change isn't reported again next check.
            wasOverThreshold = isOver;
            return Task.CompletedTask;
        }

        public override object Clone() => new ConsumptionThresholdTrigger(this);

        public override string ToString() => $"Category: {Category}, Item: {nameof(ConsumptionThresholdTrigger)}";
    }
}
