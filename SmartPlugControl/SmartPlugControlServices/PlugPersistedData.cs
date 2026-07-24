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
    }
}
