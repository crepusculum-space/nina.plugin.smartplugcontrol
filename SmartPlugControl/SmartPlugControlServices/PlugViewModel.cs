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

        /// <summary>Null when the local IP could not be resolved or the device hasn't been polled yet.</summary>
        public bool? IsOn { get; set; }

        public PlugPowerReading LastPower { get; set; }

        /// <summary>Null when the local address of the device is unknown - see PlugRegistryService.</summary>
        public string LocalIpAddress { get; set; }
    }
}
