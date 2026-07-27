namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// The subset of plug data that is user-configured and profile-scoped (as opposed to reported by
    /// the cloud/device). Serialized as JSON into the plugin's per-profile settings blob.
    /// </summary>
    public class PlugPersistedData {
        public string PlugId { get; set; }
        public string EquipmentName { get; set; } = string.Empty;
        public bool IsProtected { get; set; }

        /// <summary>
        /// Whether this plug shows up in the equipment page/sequencer at all. Defaults to true so a
        /// newly discovered plug is visible until the user opts it out - the TP-Link account may have
        /// plugs unrelated to the observatory (a home TV, a printer...) that shouldn't clutter NINA.
        /// </summary>
        public bool IsVisibleInNina { get; set; } = true;

        /// <summary>
        /// The rated max draw (in Amps at 12V DC) of whatever's plugged in here (e.g. a Pegasus
        /// Powerbox) - a per-device, essentially invariant property of the equipment, so it's
        /// configured once on the equipment page rather than per sequence item. 0 = not configured.
        /// Only meaningful for plugs that support energy monitoring.
        /// </summary>
        public double MaxAmpsAt12V { get; set; }

        /// <summary>
        /// Estimated PSU efficiency (%) used to convert MaxAmpsAt12V into an equivalent AC-side Watts
        /// figure comparable against the plug's actual measured Watts - always an estimate, since the
        /// plugin has no way to measure a given supply's real efficiency. Same per-device rationale as
        /// MaxAmpsAt12V above.
        /// </summary>
        public int PsuEfficiencyPercent { get; set; } = 85;
    }
}
