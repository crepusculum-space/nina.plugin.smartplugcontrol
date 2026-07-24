namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// A single device as reported by the TP-Link cloud account (one entry per outlet/power-strip device,
    /// not per individual socket - power strips expose their sockets locally via the brand-specific driver).
    /// </summary>
    public class CloudPlugDeviceInfo {
        public string DeviceId { get; set; }
        public string Alias { get; set; }
        public string DeviceType { get; set; }
        public string DeviceModel { get; set; }
        public string DeviceMac { get; set; }
        public PlugBrand Brand { get; set; }
        public bool IsOnline { get; set; }

        /// <summary>The device's own cloud relay endpoint - control commands are POSTed here, never to a local IP.</summary>
        public string AppServerUrl { get; set; }
    }
}
