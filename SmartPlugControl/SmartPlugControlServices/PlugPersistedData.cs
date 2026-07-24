namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// The subset of plug data that is user-configured and profile-scoped (as opposed to reported by
    /// the cloud/device). Serialized as JSON into the plugin's per-profile settings blob.
    /// </summary>
    public class PlugPersistedData {
        public string PlugId { get; set; }
        public string EquipmentName { get; set; } = string.Empty;
        public bool IsProtected { get; set; }
    }
}
