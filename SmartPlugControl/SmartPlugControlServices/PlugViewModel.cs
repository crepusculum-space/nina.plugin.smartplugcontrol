using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// The unified, per-socket view of a plug: cloud identity + brand, user-configured (persisted)
    /// data, and the last polled live state. This is what the future dockable/sequencer code will
    /// bind against; it intentionally has no NINA/WPF dependency so it stays easy to unit test.
    /// </summary>
    public class PlugViewModel {
        public string PlugId { get; set; }
        public string Alias { get; set; }
        public PlugBrand Brand { get; set; }

        public string EquipmentName { get; set; } = string.Empty;
        public bool IsProtected { get; set; }
        public bool IsVisibleInNina { get; set; } = true;

        /// <summary>Equipment name when set (e.g. "Mount"), otherwise the plug's own Kasa alias - used
        /// anywhere a plug is picked from a list (sequencer dropdowns) so equipment is identifiable at
        /// a glance instead of by its raw Kasa device name.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(EquipmentName) ? Alias : EquipmentName;

        /// <summary>Null when the device hasn't been polled yet or the cloud relay call failed (offline, no driver, etc).</summary>
        public bool? IsOn { get; set; }

        public bool SupportsLed { get; set; }

        /// <summary>Null when LED state can't be determined even though SupportsLed is true - see IPlugDriver.IsLedOnAsync.</summary>
        public bool? IsLedOn { get; set; }

        public PlugPowerReading LastPower { get; set; }

        /// <summary>
        /// Whether this plug is capable of energy monitoring at all - distinct from LastPower being
        /// non-null on any given refresh (which can also be null from a transient read failure on a
        /// device that does support it). Drives whether the equipment page shows the
        /// MaxAmpsAt12V/PsuEfficiencyPercent configuration fields at all.
        /// </summary>
        public bool SupportsPowerMonitoring { get; set; }

        /// <summary>See PlugPersistedData - configured once on the equipment page, not per sequence item.</summary>
        public double MaxAmpsAt12V { get; set; }

        /// <summary>See PlugPersistedData.</summary>
        public int PsuEfficiencyPercent { get; set; } = 85;

        private const double DcSupplyVolts = 12.0;

        /// <summary>
        /// MaxAmpsAt12V converted to an estimated AC-side Watts figure, comparable against LastPower -
        /// null if MaxAmpsAt12V hasn't been configured (0). See PlugPersistedData.PsuEfficiencyPercent
        /// for why this is always an estimate.
        /// </summary>
        public double? MaxThresholdWatts => MaxAmpsAt12V > 0 ? (MaxAmpsAt12V * DcSupplyVolts) / (PsuEfficiencyPercent / 100.0) : (double?)null;
    }
}
